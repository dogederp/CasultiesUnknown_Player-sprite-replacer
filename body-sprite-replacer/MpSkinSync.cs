using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using CUCoreLib.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Exchanges skin zips over the CUCoreLib bridge. The host stores one package
// for each client, applies it to that client's body, and relays it to the
// other clients. A late client can request the stored set.
internal static class MpSkinSync
{
    private const string FullChannel = "spritereplacer.skin.full";
    private const string RequestChannel = "spritereplacer.skin.request";
    private const string ApplyChannel = "spritereplacer.skin.apply";
    private const string ManifestChannel = "spritereplacer.skin.manifest";
    private const string FetchChannel = "spritereplacer.skin.fetch";

    private static readonly Dictionary<uint, MpSkinPackage> Skins =
        new Dictionary<uint, MpSkinPackage>();
    private static readonly Dictionary<uint, MpSkinPackage> Pending =
        new Dictionary<uint, MpSkinPackage>();
    private static readonly Dictionary<uint, uint> PlayerIds =
        new Dictionary<uint, uint>();
    private static readonly Dictionary<uint, string> LastContent =
        new Dictionary<uint, string>();
    private static readonly HashSet<uint> Waiting = new HashSet<uint>();
    private static readonly MpUploadPolicy UploadPolicy = new MpUploadPolicy();
    private static readonly MpRateLimiter UploadLimiter = new MpRateLimiter(8, 10f);
    private static readonly MpRateLimiter RequestLimiter = new MpRateLimiter(4, 10f);
    private static readonly MpRateLimiter FetchLimiter = new MpRateLimiter(4, 10f);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly MpCooldown Cooldown = new MpCooldown();
    private static MpSkinCache _cache;

    private static bool _handlers;
    private static bool _localSent;
    private static bool _requested;
    private static bool _resend;
    private static string _lastSharedSlot;
    private static string _signature;
    private static float _nextTick;
    private static float _nextReapply;
    private static bool _manualChoice;

    internal static bool IsCoolingDown =>
        MpBody.IsActive && Cooldown.IsActive(Time.unscaledTime);

    internal static int CooldownSeconds =>
        IsCoolingDown ? Cooldown.RemainingSeconds(Time.unscaledTime) : 0;

    // Server handlers can run off the main thread. Do not read Unity time
    // there; use a monotonic clock for the server-side rate limits.
    private static float ServerTime => (float)Clock.Elapsed.TotalSeconds;

    private static MpSkinCache Cache
    {
        get
        {
            if (_cache == null)
            {
                string root = Path.Combine(SpriteReplacerPlugin.SpritesPath, "mp_cache");
                _cache = new MpSkinCache(root,
                    SpriteReplacerPlugin.MaxSkinCacheMB * 1024L * 1024L);
            }
            return _cache;
        }
    }

    internal static int ClearCache()
    {
        try
        {
            return Cache.Clear();
        }
        catch (Exception ex)
        {
            Log("cache clear failed: " + ex.Message);
            return 0;
        }
    }

    internal static long CacheBytes()
    {
        try { return Cache.TotalBytes(); }
        catch { return 0; }
    }

    internal static void OnLocalSkinChanged()
    {
        _signature = null;
        _localSent = false;
        _resend = false;
        if (SkinLock.CanChangeOnce)
            _manualChoice = true;
    }

    internal static void OnLockChanged()
    {
        UploadPolicy.Clear();
        foreach (uint playerId in LastContent.Keys)
            UploadPolicy.MarkSeen(playerId);
    }

    internal static void OnShareChanged()
    {
        _signature = null;
        if (!MpBody.IsActive) return;
        ShareChangedCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShareChangedCore()
    {
        if (SpriteReplacerPlugin.ShareSkins)
        {
            _localSent = false;
            _resend = !string.IsNullOrEmpty(_lastSharedSlot) &&
                      string.Equals(_lastSharedSlot, SpriteReplacerPlugin.ActiveSlot,
                          StringComparison.Ordinal);
            return;
        }

        _localSent = true;
        _resend = false;
        ClearLocalSkin();
    }

    internal static void OnDownloadChanged()
    {
        if (!MpBody.IsActive) return;

        if (!SpriteReplacerPlugin.DownloadSkins)
        {
            ClearRemoteViews();
            return;
        }

        _requested = false;
        ApplyPending();
    }

    private static void ClearLocalSkin()
    {
        if (!MpBody.TryGetLocalIds(out uint network, out uint player)) return;

        if (MpBody.IsHost)
        {
            Remove(network, player);
            BroadcastClear(network, player);
        }
        else
        {
            SendClear(network, player);
        }

        SkinLock.HasLocalSkin = false;
    }

    private static void SendClear(uint network, uint player)
    {
        try
        {
            var payload = new JObject
            {
                ["clientId"] = network,
                ["playerId"] = player,
                ["clear"] = true
            };
            MultiplayerApi.RequestServer(FullChannel, payload, OnUploadResponse, true);
        }
        catch (Exception ex)
        {
            Log("clear request failed: " + ex.Message);
        }
    }

    private static void BroadcastClear(uint network, uint player)
    {
        try
        {
            var payload = new JObject
            {
                ["clientId"] = network,
                ["playerId"] = player,
                ["clear"] = true
            };
            MultiplayerApi.Broadcast(ApplyChannel, payload, false, true);
        }
        catch (Exception ex)
        {
            Log("clear broadcast failed: " + ex.Message);
        }
    }

    internal static void Tick()
    {
        if (!MpBody.IsActive)
        {
            Reset();
            return;
        }

        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 0.5f;

        EnsureHandlers();

        if (!MpBody.TryGetLocalIds(out uint localNetwork, out uint localPlayer)) return;

        string signature = SpriteReplacerPlugin.ActiveSlot + ":" + SpriteReplacerPlugin.LoadedSprites.Count;
        if (!string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            _signature = signature;
            _localSent = false;
        }

        if (_resend && !string.Equals(_lastSharedSlot, SpriteReplacerPlugin.ActiveSlot,
                StringComparison.Ordinal))
            _resend = false;

        if (!_localSent && !IsCoolingDown && SpriteReplacerPlugin.ShareSkins &&
            (SkinLock.CanChangeLocal || _resend))
        {
            MpSkinPackage package = MpSkinPackage.FromSlot(SpriteReplacerPlugin.ActiveSlot);
            if (package != null)
            {
                bool sent;
                if (MpBody.IsHost)
                {
                    Store(localNetwork, localPlayer, package);
                    Broadcast(package, localNetwork, localPlayer);
                    SkinLock.HasLocalSkin = true;
                    sent = true;
                }
                else
                {
                    sent = SendToServer(package, localNetwork, localPlayer);
                    if (sent)
                    {
                        SkinLock.HasLocalSkin = true;
                        if (_manualChoice)
                        {
                            _manualChoice = false;
                            SkinLock.ConsumeChoice();
                        }
                    }
                }

                if (sent)
                {
                    _localSent = true;
                    _resend = false;
                    _lastSharedSlot = SpriteReplacerPlugin.ActiveSlot;
                    SkinLock.HadLocalSkin = true;
                }
                else package.Dispose();
            }
        }

        if (!_requested && !IsCoolingDown && SpriteReplacerPlugin.DownloadSkins && MpBody.IsClient && !MpBody.IsHost)
        {
            _requested = true;
            RequestExistingSkins(localNetwork);
            Log("requested existing skins.");
        }

        ApplyPending();

        if (Time.unscaledTime >= _nextReapply)
        {
            _nextReapply = Time.unscaledTime + 3f;
            if (SpriteReplacerPlugin.DownloadSkins)
            {
                foreach (KeyValuePair<uint, MpSkinPackage> pair in Skins)
                {
                    if (pair.Key == localNetwork) continue;
                    uint player = PlayerIds.TryGetValue(pair.Key, out uint id) ? id : 0;
                    MpBody.ApplyRemoteSkin(pair.Key, player, pair.Value.Sprites);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RequestExistingSkins(uint localNetwork)
    {
        try
        {
            var request = new JObject { ["clientId"] = localNetwork };
            MultiplayerApi.RequestServer(RequestChannel, request, OnSyncResponse, true);
        }
        catch (Exception ex)
        {
            Log("request failed: " + ex.Message);
        }
    }

    private static void EnsureHandlers()
    {
        if (_handlers) return;

        try
        {
            RegisterSenderHandler(FullChannel, OnServerFull);
            RegisterSenderHandler(RequestChannel, OnServerRequest);
            RegisterSenderHandler(FetchChannel, OnServerFetch);
            MultiplayerApi.RegisterClientHandler(ApplyChannel, OnClientApply);
            MultiplayerApi.RegisterClientHandler(ManifestChannel, OnClientManifest);
            MultiplayerApi.RegisterClientHandler(SkinLock.LockChannel, SkinLock.ApplyRemote);
            Log("handlers registered.");
        }
        catch (Exception ex)
        {
            Log("handler registration failed: " + ex);
        }

        _handlers = true;
    }

    // CUCoreLib 1.0.6.1 has an internal sender-aware server handler. Use it
    // when it is there, so the server checks the real network sender. Fall
    // back to the payload client ID on an older or different CUCoreLib.
    private static void RegisterSenderHandler(string channel,
        Func<uint, JToken, JToken> handler)
    {
        try
        {
            MethodInfo method = typeof(MultiplayerBridge).GetMethod(
                "RegisterServerHandler",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(Func<uint, JToken, JToken>) },
                null);
            if (method != null)
            {
                method.Invoke(null, new object[] { channel, handler });
                Log("sender-aware handler registered for " + channel + ".");
                return;
            }
        }
        catch (Exception ex)
        {
            Log("sender handler failed for " + channel + ": " + ex.Message);
        }

        MultiplayerApi.RegisterServerHandler(channel, token => handler(0, token));
        Log("sender-aware handler missing for " + channel + ". Using the payload client ID.");
    }

    private static JToken OnServerFull(uint sender, JToken token)
    {
        try
        {
            var payload = token as JObject;
            if (payload == null) return new JValue("bad");

            uint claimedPlayer = ReadUInt(payload, "playerId");
            if (sender != 0 && claimedPlayer != sender)
            {
                Log("rejected skin: claimed player " + claimedPlayer
                    + ", sender " + sender + ".");
                return new JValue("bad owner");
            }

            uint ownerNetwork = ReadUInt(payload, "clientId");
            uint ownerPlayer = sender != 0 ? sender : claimedPlayer;
            if (ownerNetwork == 0 || ownerPlayer == 0) return new JValue("bad owner");

            if (sender != 0 && !MpBody.PlayerMatchesNetwork(ownerNetwork, ownerPlayer))
            {
                Log("rejected skin: network " + ownerNetwork
                    + " does not match player " + ownerPlayer + ".");
                return new JValue("bad owner");
            }

            payload["clientId"] = ownerNetwork;

            if (payload.Value<bool?>("clear") == true)
            {
                Remove(ownerNetwork, ownerPlayer);
                MultiplayerApi.Broadcast(ApplyChannel, payload, false, true);
                Log("cleared skin from client " + ownerNetwork + ".");
                return SkinLock.Payload(false);
            }

            MpRateLimiter.Result limit = UploadLimiter.Check(ownerPlayer, ServerTime);
            if (!limit.Allowed && !limit.AcceptOnce)
            {
                Log("rate limited upload from player " + ownerPlayer + " for "
                    + limit.RetryIn.ToString("0") + "s, strike " + limit.Strikes + ".");
                return SkinLock.Payload(false, limit.RetryIn, limit.Strikes);
            }
            if (limit.AcceptOnce)
                Log("rate limit tripped for player " + ownerPlayer + " for "
                    + limit.RetryIn.ToString("0") + "s, strike " + limit.Strikes + ".");

            MpSkinPackage package = Parse(payload);
            if (package == null) return new JValue("reject");

            bool sameSkin = !string.IsNullOrEmpty(package.ContentHash) &&
                LastContent.TryGetValue(ownerPlayer, out string lastContent) &&
                string.Equals(lastContent, package.ContentHash, StringComparison.OrdinalIgnoreCase);

            MpUploadPolicy.Decision decision = sameSkin
                ? MpUploadPolicy.Decision.Accept
                : UploadPolicy.Check(ownerPlayer, SkinLock.IsLocked, HasStoredSkin(ownerPlayer));
            if (decision == MpUploadPolicy.Decision.Reject)
            {
                package.Dispose();
                Log("rejected locked skin from player " + ownerPlayer + ".");
                return SkinLock.Payload(false);
            }

            bool allow = SkinLock.IsLocked && !sameSkin &&
                decision == MpUploadPolicy.Decision.Accept;

            if (SpriteReplacerPlugin.DownloadSkins)
                Cache.Store(package.Hash, package.Zip);

            Store(ownerNetwork, ownerPlayer, package);
            if (SpriteReplacerPlugin.DownloadSkins)
                MpBody.ApplyRemoteSkin(ownerNetwork, ownerPlayer, package.Sprites);
            MultiplayerApi.Broadcast(ApplyChannel, payload, false, true);

            Log("stored skin from client " + ownerNetwork + " player " + ownerPlayer
                + " (" + package.SetName + ") allow=" + allow + ".");

            if (limit.AcceptOnce)
            {
                return SkinLock.Payload(allow, limit.RetryIn, limit.Strikes, true);
            }

            return SkinLock.Payload(allow);
        }
        catch (Exception ex)
        {
            Log("server full failed: " + ex);
            return new JValue("error");
        }
    }

    private static JToken OnServerRequest(uint sender, JToken token)
    {
        try
        {
            var payload = token as JObject;
            uint requester = RequesterId(sender, payload);
            if (requester == 0) return new JValue("bad");

            MpRateLimiter.Result limit = RequestLimiter.Check(requester, ServerTime);
            if (!limit.Allowed && !limit.AcceptOnce)
            {
                Log("rate limited request from player " + requester + " for "
                    + limit.RetryIn.ToString("0") + "s, strike " + limit.Strikes + ".");
                return SkinLock.Payload(false, limit.RetryIn, limit.Strikes);
            }

            EnsureLocalPackage();

            var entries = new JArray();
            foreach (KeyValuePair<uint, MpSkinPackage> pair in Skins)
            {
                uint player = PlayerIds.TryGetValue(pair.Key, out uint id) ? id : 0;
                entries.Add(new JObject
                {
                    ["clientId"] = pair.Key,
                    ["playerId"] = player,
                    ["hash"] = pair.Value.Hash,
                    ["setName"] = pair.Value.SetName
                });
            }

            var manifest = new JObject { ["skins"] = entries };
            MultiplayerApi.Broadcast(ManifestChannel, manifest, false, true);

            bool allow = SkinLock.IsLocked && UploadPolicy.HasAllowance(requester);
            Log("broadcast manifest with " + entries.Count + " stored skins for player "
                + requester + " allow=" + allow + ".");

            if (limit.AcceptOnce)
            {
                return SkinLock.Payload(allow, limit.RetryIn, limit.Strikes, true);
            }

            return SkinLock.Payload(allow);
        }
        catch (Exception ex)
        {
            Log("server request failed: " + ex);
            return new JValue("error");
        }
    }

    private static JToken OnServerFetch(uint sender, JToken token)
    {
        try
        {
            var payload = token as JObject;
            uint requester = RequesterId(sender, payload);
            if (requester == 0) return new JValue("bad");

            MpRateLimiter.Result limit = FetchLimiter.Check(requester, ServerTime);
            if (!limit.Allowed && !limit.AcceptOnce)
            {
                Log("rate limited fetch from player " + requester + " for "
                    + limit.RetryIn.ToString("0") + "s, strike " + limit.Strikes + ".");
                return SkinLock.Payload(false, limit.RetryIn, limit.Strikes);
            }

            var hashes = payload["hashes"] as JArray;
            var seen = new HashSet<string>();
            int sent = 0;

            if (hashes != null)
            {
                foreach (JToken item in hashes)
                {
                    string hash = item == null ? null : item.Value<string>();
                    if (!MpSkinCache.IsHash(hash)) continue;

                    string key = hash.ToLowerInvariant();
                    if (!seen.Add(key)) continue;

                    foreach (KeyValuePair<uint, MpSkinPackage> pair in Skins)
                    {
                        if (!string.Equals(pair.Value.Hash, hash, StringComparison.OrdinalIgnoreCase))
                            continue;

                        uint player = PlayerIds.TryGetValue(pair.Key, out uint id) ? id : 0;
                        MultiplayerApi.Broadcast(ApplyChannel,
                            ToPayload(pair.Value, pair.Key, player), false, true);
                        sent++;
                    }
                }
            }

            Log("sent " + sent + " cached skins to player " + requester + ".");
            if (limit.AcceptOnce)
            {
                return SkinLock.Payload(false, limit.RetryIn, limit.Strikes, true);
            }

            return SkinLock.Payload(false);
        }
        catch (Exception ex)
        {
            Log("fetch failed: " + ex);
            return new JValue("error");
        }
    }

    private static void EnsureLocalPackage()
    {
        if (!MultiplayerApi.IsHost) return;
        if (!SpriteReplacerPlugin.ShareSkins) return;
        if (!MpBody.TryGetLocalIds(out uint network, out uint player)) return;
        if (Skins.ContainsKey(network)) return;

        MpSkinPackage package = MpSkinPackage.FromSlot(SpriteReplacerPlugin.ActiveSlot);
        if (package == null) return;

        Skins[network] = package;
        PlayerIds[network] = player;
        LastContent[player] = package.ContentHash;
        Log("stored local skin for late join: network=" + network + " player=" + player + ".");
    }

    private static void OnClientApply(JToken token)
    {
        try
        {
            var payload = token as JObject;
            if (payload == null) return;
            if (!SpriteReplacerPlugin.DownloadSkins) return;

            uint ownerNetwork = ReadUInt(payload, "clientId");
            uint ownerPlayer = ReadUInt(payload, "playerId");
            if (ownerNetwork == MpBody.LocalClientId) return;

            if (payload.Value<bool?>("clear") == true)
            {
                Remove(ownerNetwork, ownerPlayer);
                Log("cleared skin from client " + ownerNetwork + ".");
                return;
            }

            string hash = payload.Value<string>("hash") ?? string.Empty;
            if (MpSkinCache.IsHash(hash) &&
                Skins.TryGetValue(ownerNetwork, out MpSkinPackage existing) &&
                string.Equals(existing.Hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            MpSkinPackage package = Parse(payload);
            if (package == null) return;

            Cache.Store(package.Hash, package.Zip);
            Store(ownerNetwork, ownerPlayer, package);
            ApplyPending();

            Log("received skin from client " + ownerNetwork + " player " + ownerPlayer
                + " (" + package.SetName + ").");
        }
        catch (Exception ex)
        {
            Log("client apply failed: " + ex);
        }
    }

    private static void OnClientManifest(JToken token)
    {
        try
        {
            if (!SpriteReplacerPlugin.DownloadSkins) return;

            var payload = token as JObject;
            var skins = payload == null ? null : payload["skins"] as JArray;
            if (skins == null) return;
            if (!MpBody.TryGetLocalIds(out uint localNetwork, out _)) return;

            var missing = new JArray();
            var seen = new HashSet<string>();
            int cached = 0;

            foreach (JToken entry in skins)
            {
                var item = entry as JObject;
                if (item == null) continue;

                uint owner = ReadUInt(item, "clientId");
                uint player = ReadUInt(item, "playerId");
                string hash = item.Value<string>("hash") ?? string.Empty;
                string setName = item.Value<string>("setName") ?? string.Empty;
                if (owner == 0 || owner == localNetwork || !MpSkinCache.IsHash(hash)) continue;

                if (Skins.TryGetValue(owner, out MpSkinPackage existing) &&
                    string.Equals(existing.Hash, hash, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Cache.TryRead(hash, out byte[] zip))
                {
                    MpSkinPackage package = MpSkinPackage.FromZip(zip, hash, setName);
                    if (package != null)
                    {
                        Store(owner, player, package);
                        cached++;
                        continue;
                    }
                    Cache.Remove(hash);
                }

                string key = hash.ToLowerInvariant();
                if (seen.Add(key) && missing.Count < MpSkinRules.MaxEntries)
                    missing.Add(hash);
            }

            ApplyPending();

            if (missing.Count > 0)
                RequestMissing(missing, localNetwork);

            Log("manifest: cached=" + cached + " missing=" + missing.Count + ".");
        }
        catch (Exception ex)
        {
            Log("client manifest failed: " + ex);
        }
    }

    private static void RequestMissing(JArray hashes, uint localNetwork)
    {
        try
        {
            var request = new JObject
            {
                ["clientId"] = localNetwork,
                ["hashes"] = hashes
            };
            MultiplayerApi.RequestServer(FetchChannel, request, OnFetchResponse, true);
        }
        catch (Exception ex)
        {
            Log("fetch request failed: " + ex.Message);
        }
    }

    private static void OnFetchResponse(JToken token)
    {
        if (token == null) return;
        Log("fetch response: " + token);
    }

    private static void OnSyncResponse(JToken token)
    {
        SkinLock.ApplyRemote(token);
        HandleRateLimit(token, true);
        Log("sync response: " + (token == null ? "null" : token.ToString()));
    }

    private static void OnUploadResponse(JToken token)
    {
        SkinLock.ApplyRemote(token);
        HandleRateLimit(token, false);
        if (token != null && token.Type == JTokenType.String)
            Log("upload response: " + token);
    }

    private static void HandleRateLimit(JToken token, bool retryRequest)
    {
        var payload = token as JObject;
        if (payload == null) return;

        JToken retryToken = payload["retryIn"];
        if (retryToken == null) return;

        float retry = retryToken.Value<float>();
        if (retry <= 0f) return;

        int strikes = payload["strikes"] == null ? 1 : payload["strikes"].Value<int>();
        bool accepted = payload["accepted"] != null && payload["accepted"].Value<bool>();
        SetCooldown(retry, strikes, retryRequest, accepted);
    }

    private static void SetCooldown(float seconds, int strikes, bool retryRequest,
        bool accepted)
    {
        Cooldown.Set(Time.unscaledTime, seconds);
        if (!accepted) _localSent = false;
        if (retryRequest && !accepted) _requested = false;

        int whole = Cooldown.RemainingSeconds(Time.unscaledTime);
        SkinLock.AnnounceRateLimit(whole, strikes, accepted && !retryRequest);
        SkinMenuUI.RefreshLockUi();
        Log("rate limited for " + whole + "s (strike " + strikes
            + ", accepted=" + accepted + ").");
    }

    private static MpSkinPackage Parse(JObject payload)
    {
        string hash = payload.Value<string>("hash") ?? string.Empty;
        string setName = payload.Value<string>("setName") ?? string.Empty;
        string encoded = payload.Value<string>("zip") ?? string.Empty;

        byte[] zip;
        try
        {
            zip = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return null;
        }

        return MpSkinPackage.FromZip(zip, hash, setName);
    }

    private static bool SendToServer(MpSkinPackage package, uint localNetwork, uint localPlayer)
    {
        bool sent = MultiplayerApi.RequestServer(FullChannel,
            ToPayload(package, localNetwork, localPlayer), OnUploadResponse, true);
        Log("sent local skin (" + package.SetName + ") network=" + localNetwork
            + " player=" + localPlayer + " result=" + sent + ".");
        return sent;
    }

    private static void Broadcast(MpSkinPackage package, uint localNetwork, uint localPlayer)
    {
        MultiplayerApi.Broadcast(ApplyChannel,
            ToPayload(package, localNetwork, localPlayer), false, true);
        Log("broadcast local skin (" + package.SetName + ") network=" + localNetwork
            + " player=" + localPlayer + ".");
    }

    private static void ClearRemoteViews()
    {
        foreach (KeyValuePair<uint, MpSkinPackage> pair in Skins)
        {
            if (pair.Key == MpBody.LocalClientId) continue;
            uint player = PlayerIds.TryGetValue(pair.Key, out uint id) ? id : 0;
            MpBody.ClearRemoteSkin(pair.Key, player);
        }

        if (MpBody.IsHost) return;

        var packages = new HashSet<MpSkinPackage>(Skins.Values);
        foreach (MpSkinPackage pending in Pending.Values) packages.Add(pending);
        foreach (MpSkinPackage package in packages) package.Dispose();

        Skins.Clear();
        Pending.Clear();
        PlayerIds.Clear();
        Waiting.Clear();
    }

    private static void Remove(uint ownerNetwork, uint ownerPlayer)
    {
        if (Skins.TryGetValue(ownerNetwork, out MpSkinPackage package))
        {
            package.Dispose();
            Skins.Remove(ownerNetwork);
        }

        if (Pending.TryGetValue(ownerNetwork, out MpSkinPackage pending) &&
            !ReferenceEquals(pending, package))
            pending.Dispose();

        Pending.Remove(ownerNetwork);
        PlayerIds.Remove(ownerNetwork);
        Waiting.Remove(ownerNetwork);

        MpBody.ClearRemoteSkin(ownerNetwork, ownerPlayer);
    }

    private static bool HasStoredSkin(uint playerId)
    {
        foreach (KeyValuePair<uint, uint> pair in PlayerIds)
        {
            if (pair.Value == playerId) return true;
        }

        return false;
    }

    private static void Store(uint ownerNetwork, uint ownerPlayer, MpSkinPackage package)
    {
        if (Skins.TryGetValue(ownerNetwork, out MpSkinPackage old) && !ReferenceEquals(old, package))
            old.Dispose();

        Skins[ownerNetwork] = package;
        PlayerIds[ownerNetwork] = ownerPlayer;
        LastContent[ownerPlayer] = package.ContentHash;
        Pending[ownerNetwork] = package;
    }

    private static void ApplyPending()
    {
        if (Pending.Count == 0 || !SpriteReplacerPlugin.DownloadSkins) return;

        var owners = new List<uint>(Pending.Keys);
        foreach (uint owner in owners)
        {
            if (owner == MpBody.LocalClientId)
            {
                Pending.Remove(owner);
                continue;
            }

            MpSkinPackage package = Pending[owner];
            uint player = PlayerIds.TryGetValue(owner, out uint id) ? id : 0;
            if (MpBody.ApplyRemoteSkin(owner, player, package.Sprites))
            {
                Pending.Remove(owner);
                Waiting.Remove(owner);
            }
            else if (Waiting.Add(owner))
            {
                Log("waiting for body: network=" + owner + " player=" + player);
            }
        }
    }

    private static JObject ToPayload(MpSkinPackage package, uint ownerNetwork, uint ownerPlayer)
    {
        return new JObject
        {
            ["clientId"] = ownerNetwork,
            ["playerId"] = ownerPlayer,
            ["setName"] = package.SetName,
            ["hash"] = package.Hash,
            ["zip"] = Convert.ToBase64String(package.Zip)
        };
    }

    private static uint RequesterId(uint sender, JObject payload)
    {
        return sender != 0 ? sender : (payload == null ? 0 : ReadUInt(payload, "clientId"));
    }

    private static uint ReadUInt(JObject payload, string key)
    {
        JToken token = payload[key];
        return token == null ? 0 : token.Value<uint>();
    }

    private static void Reset()
    {
        _handlers = false;
        _localSent = false;
        _requested = false;
        _resend = false;
        _lastSharedSlot = null;
        _signature = null;
        _manualChoice = false;
        var packages = new HashSet<MpSkinPackage>(Skins.Values);
        foreach (MpSkinPackage package in Pending.Values) packages.Add(package);
        foreach (MpSkinPackage package in packages) package.Dispose();
        Skins.Clear();
        Pending.Clear();
        PlayerIds.Clear();
        LastContent.Clear();
        Waiting.Clear();
        UploadPolicy.Clear();
        UploadLimiter.Clear();
        RequestLimiter.Clear();
        FetchLimiter.Clear();
        Cooldown.Clear();
        SkinLock.Reset();
    }

    private static void Log(string message)
    {
        if (SpriteReplacerPlugin.Log != null)
            SpriteReplacerPlugin.Log.LogInfo("[MP skin] " + message);
    }
}
