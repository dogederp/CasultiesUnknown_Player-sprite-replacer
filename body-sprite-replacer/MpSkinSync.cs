using System;
using System.Collections.Generic;
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

    private static readonly Dictionary<uint, MpSkinPackage> Skins =
        new Dictionary<uint, MpSkinPackage>();
    private static readonly Dictionary<uint, MpSkinPackage> Pending =
        new Dictionary<uint, MpSkinPackage>();
    private static readonly Dictionary<uint, uint> PlayerIds =
        new Dictionary<uint, uint>();
    private static readonly HashSet<uint> Waiting = new HashSet<uint>();
    private static readonly HashSet<uint> LateJoinAllowance = new HashSet<uint>();

    private static bool _handlers;
    private static bool _localSent;
    private static bool _requested;
    private static string _signature;
    private static float _nextTick;
    private static float _nextReapply;
    private static bool _manualChoice;

    internal static void OnLocalSkinChanged()
    {
        _signature = null;
        _localSent = false;
        if (SkinLock.CanChangeOnce)
            _manualChoice = true;
    }

    internal static void OnLockChanged()
    {
        LateJoinAllowance.Clear();
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

        if (!_localSent)
        {
            MpSkinPackage package = MpSkinPackage.FromSlot(SpriteReplacerPlugin.ActiveSlot);
            if (package != null && SkinLock.CanChangeLocal)
            {
                if (MpBody.IsHost)
                {
                    Store(localNetwork, localPlayer, package);
                    Broadcast(package, localNetwork, localPlayer);
                    SkinLock.HasLocalSkin = true;
                }
                else if (SendToServer(package, localNetwork, localPlayer))
                {
                    SkinLock.HasLocalSkin = true;
                    if (_manualChoice)
                    {
                        _manualChoice = false;
                        SkinLock.ConsumeChoice();
                    }
                }
                _localSent = true;
            }
        }

        if (!_requested && MpBody.IsClient && !MpBody.IsHost)
        {
            _requested = true;
            RequestExistingSkins(localNetwork);
            Log("requested existing skins.");
        }

        ApplyPending();

        if (Time.unscaledTime >= _nextReapply)
        {
            _nextReapply = Time.unscaledTime + 3f;
            foreach (KeyValuePair<uint, MpSkinPackage> pair in Skins)
            {
                if (pair.Key == localNetwork) continue;
                uint player = PlayerIds.TryGetValue(pair.Key, out uint id) ? id : 0;
                MpBody.ApplyRemoteSkin(pair.Key, player, pair.Value.Sprites);
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
            MultiplayerApi.RegisterServerHandler(FullChannel, OnServerFull);
            MultiplayerApi.RegisterServerHandler(RequestChannel, OnServerRequest);
            MultiplayerApi.RegisterClientHandler(ApplyChannel, OnClientApply);
            MultiplayerApi.RegisterClientHandler(SkinLock.LockChannel, SkinLock.ApplyRemote);
            Log("handlers registered.");
        }
        catch (Exception ex)
        {
            Log("handler registration failed: " + ex);
        }

        _handlers = true;
    }

    private static JToken OnServerFull(JToken token)
    {
        try
        {
            var payload = token as JObject;
            if (payload == null) return new JValue("bad");

            uint ownerNetwork = ReadUInt(payload, "clientId");
            uint ownerPlayer = ReadUInt(payload, "playerId");
            if (ownerNetwork == 0) return new JValue("bad owner");

            MpSkinPackage package = Parse(payload);
            if (package == null) return new JValue("reject");

            bool allow = false;
            if (SkinLock.IsLocked)
            {
                bool hasSkin = Skins.ContainsKey(ownerNetwork);

                if (hasSkin && !LateJoinAllowance.Contains(ownerNetwork))
                {
                    Log("rejected locked skin from client " + ownerNetwork + ".");
                    return SkinLock.Payload(false);
                }

                if (hasSkin)
                {
                    LateJoinAllowance.Remove(ownerNetwork);
                }
                else
                {
                    LateJoinAllowance.Add(ownerNetwork);
                    allow = true;
                }
            }

            Store(ownerNetwork, ownerPlayer, package);
            MpBody.ApplyRemoteSkin(ownerNetwork, ownerPlayer, package.Sprites);
            MultiplayerApi.Broadcast(ApplyChannel, payload, false, true);

            Log("stored skin from client " + ownerNetwork + " player " + ownerPlayer
                + " (" + package.SetName + ") allow=" + allow + ".");
            return SkinLock.Payload(allow);
        }
        catch (Exception ex)
        {
            Log("server full failed: " + ex);
            return new JValue("error");
        }
    }

    private static JToken OnServerRequest(JToken token)
    {
        try
        {
            var payload = token as JObject;
            uint requester = payload == null ? 0 : ReadUInt(payload, "clientId");
            if (requester == 0) return new JValue("bad");

            EnsureLocalPackage();

            foreach (KeyValuePair<uint, MpSkinPackage> pair in Skins)
            {
                uint player = PlayerIds.TryGetValue(pair.Key, out uint id) ? id : 0;
                MultiplayerApi.Broadcast(ApplyChannel,
                    ToPayload(pair.Value, pair.Key, player), false, true);
            }

            bool allow = SkinLock.IsLocked && LateJoinAllowance.Contains(requester);
            Log("broadcast " + Skins.Count + " stored skins for client " + requester
                + " allow=" + allow + ".");
            return SkinLock.Payload(allow);
        }
        catch (Exception ex)
        {
            Log("server request failed: " + ex);
            return new JValue("error");
        }
    }

    private static void EnsureLocalPackage()
    {
        if (!MultiplayerApi.IsHost) return;
        if (!MpBody.TryGetLocalIds(out uint network, out uint player)) return;
        if (Skins.ContainsKey(network)) return;

        MpSkinPackage package = MpSkinPackage.FromSlot(SpriteReplacerPlugin.ActiveSlot);
        if (package == null) return;

        Skins[network] = package;
        PlayerIds[network] = player;
        Log("stored local skin for late join: network=" + network + " player=" + player + ".");
    }

    private static void OnClientApply(JToken token)
    {
        try
        {
            var payload = token as JObject;
            if (payload == null) return;

            uint ownerNetwork = ReadUInt(payload, "clientId");
            uint ownerPlayer = ReadUInt(payload, "playerId");
            if (ownerNetwork == MpBody.LocalClientId) return;

            MpSkinPackage package = Parse(payload);
            if (package == null) return;

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

    private static void OnSyncResponse(JToken token)
    {
        SkinLock.ApplyRemote(token);
        Log("sync response: " + (token == null ? "null" : token.ToString()));
    }

    private static void OnUploadResponse(JToken token)
    {
        SkinLock.ApplyRemote(token);
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

    private static void Store(uint ownerNetwork, uint ownerPlayer, MpSkinPackage package)
    {
        Skins[ownerNetwork] = package;
        PlayerIds[ownerNetwork] = ownerPlayer;
        Pending[ownerNetwork] = package;
    }

    private static void ApplyPending()
    {
        if (Pending.Count == 0) return;

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
        _signature = null;
        _manualChoice = false;
        Skins.Clear();
        Pending.Clear();
        PlayerIds.Clear();
        Waiting.Clear();
        LateJoinAllowance.Clear();
        SkinLock.Reset();
    }

    private static void Log(string message)
    {
        if (SpriteReplacerPlugin.Log != null)
            SpriteReplacerPlugin.Log.LogInfo("[MP skin] " + message);
    }
}
