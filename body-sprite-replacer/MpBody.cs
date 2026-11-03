using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CUCoreLib.Networking;
using UnityEngine;

// Selects the one body that belongs to the local player in a multiplayer
// session. Single-player has no filter, so every body still gets the skin.
internal static class MpBody
{
    private static Body _localBody;
    private static float _nextRefresh;
    private static float _nextCoreCheck;
    private static bool _coreAvailable;
    private static bool _needsApply;

    internal static bool IsActive
    {
        get
        {
            if (!CoreAvailable()) return false;
            return ActiveCore();
        }
    }

    internal static bool IsHost
    {
        get
        {
            if (!CoreAvailable()) return false;
            return HostCore();
        }
    }

    internal static bool IsClient
    {
        get
        {
            if (!CoreAvailable()) return false;
            return ClientCore();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HostCore()
    {
        try
        {
            return MultiplayerApi.IsHost;
        }
        catch
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ClientCore()
    {
        try
        {
            return MultiplayerApi.IsClient;
        }
        catch
        {
            return false;
        }
    }

    internal static bool CanApply(Body body)
    {
        if (!IsActive) return true;

        if (_localBody == null) Refresh();
        if (_localBody == null) return false;

        return body == _localBody;
    }

    internal static void Tick()
    {
        if (!IsActive)
        {
            _localBody = null;
            _needsApply = false;
            return;
        }

        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 1f;

        Refresh();
        if (!_needsApply) return;

        _needsApply = false;
        ApplyLocalSkin(SpriteReplacerPlugin.LoadedSprites);
    }

    internal static void ApplyLocalSkin(IReadOnlyDictionary<string, Sprite> sprites)
    {
        if (!IsActive) return;
        if (_localBody == null) Refresh();
        if (_localBody == null) return;

        BodySkinBinding binding = _localBody.GetComponent<BodySkinBinding>();
        if (binding == null)
            binding = _localBody.gameObject.AddComponent<BodySkinBinding>();

        binding.Bind(_localBody, sprites);
    }

    internal static bool HasLocalBody
    {
        get
        {
            if (_localBody == null) Refresh();
            return _localBody != null;
        }
    }

    internal static uint LocalClientId
    {
        get { return TryGetLocalIds(out uint clientId, out _) ? clientId : 0; }
    }

    internal static uint LocalPlayerId
    {
        get { return TryGetLocalIds(out _, out uint playerId) ? playerId : 0; }
    }

    internal static bool TryGetLocalIds(out uint networkClientId, out uint playerId)
    {
        networkClientId = 0;
        playerId = 0;
        if (!IsActive) return false;
        if (_localBody == null) Refresh();
        if (_localBody == null) return false;

        try
        {
            Type scavType = ResolvePlayerType();
            if (scavType == null) return false;

            MethodInfo method = MultiplayerApi.FindKrokMpMethod(scavType, "GetClientIdFromBody",
                new[] { typeof(Body) });
            if (method != null)
            {
                object value = method.Invoke(null, new object[] { _localBody });
                networkClientId = KnetToUInt(value);
            }

            MemberInfo localMember = MultiplayerApi.FindKrokMpMember(scavType, "LocalPlayer")
                ?? MultiplayerApi.FindKrokMpMember(scavType, "LOCAL_PLAYER");
            object localPlayer = ReadMember(localMember, null);
            MemberInfo idMember = MultiplayerApi.FindKrokMpMember(scavType, "playerId")
                ?? MultiplayerApi.FindKrokMpMember(scavType, "PlayerId");
            object idValue = ReadMember(idMember, localPlayer);
            playerId = KnetToUInt(idValue);

            return method != null || localPlayer != null;
        }
        catch (Exception ex)
        {
            LogDebug("Local ids failed: " + ex.Message);
            return false;
        }
    }

    private static object ReadMember(MemberInfo member, object target)
    {
        if (member == null) return null;

        try
        {
            if (member is FieldInfo field) return field.GetValue(target);
            if (member is PropertyInfo property) return property.GetValue(target);
        }
        catch (Exception ex)
        {
            LogDebug("Read " + member.Name + " failed: " + ex.Message);
        }

        return null;
    }

    internal static Body GetBodyByPlayerId(uint playerId)
    {
        if (!IsActive) return null;

        try
        {
            Type scavType = ResolvePlayerType();
            if (scavType == null) return null;

            Type knetType = Resolve("knetid");
            if (knetType == null)
            {
                MemberInfo idMember = MultiplayerApi.FindKrokMpMember(scavType, "playerId");
                if (idMember is FieldInfo field) knetType = field.FieldType;
                else if (idMember is PropertyInfo property) knetType = property.PropertyType;
            }
            if (knetType == null) return null;

            MethodInfo method = MultiplayerApi.FindKrokMpMethod(scavType, "GetBodyFromClientId",
                new[] { knetType });
            if (method == null) return null;

            object id = Activator.CreateInstance(knetType, new object[] { (ushort)playerId });
            return method.Invoke(null, new[] { id }) as Body;
        }
        catch (Exception ex)
        {
            LogDebug("Body lookup failed for player " + playerId + ": " + ex.Message);
            return null;
        }
    }

    internal static bool ApplyRemoteSkin(uint networkClientId, uint playerId,
        IReadOnlyDictionary<string, Sprite> sprites)
    {
        if (!IsActive || sprites == null) return false;
        if (_localBody == null) Refresh();

        Body body = GetBodyByPlayerId(playerId);
        if (body == null)
        {
            LogDebug("Remote body not ready: network=" + networkClientId + " player=" + playerId);
            return false;
        }
        if (body == _localBody) return true;

        BodySkinBinding binding = body.GetComponent<BodySkinBinding>();
        if (binding == null)
            binding = body.gameObject.AddComponent<BodySkinBinding>();

        binding.Bind(body, sprites);
        LogDebug("Applied remote skin: network=" + networkClientId + " player=" + playerId);
        return true;
    }

    internal static bool ClearRemoteSkin(uint networkClientId, uint playerId)
    {
        if (!IsActive) return false;

        Body body = GetBodyByPlayerId(playerId);
        if (body == null) return false;
        if (body == _localBody) return false;

        BodySkinBinding binding = body.GetComponent<BodySkinBinding>();
        if (binding != null) binding.Reset();

        LogDebug("Cleared remote skin: network=" + networkClientId + " player=" + playerId);
        return true;
    }

    private static Type Resolve(string name)
    {
        try
        {
            return MultiplayerApi.ResolveKrokMpType(name);
        }
        catch
        {
            return null;
        }
    }

    private static Type ResolvePlayerType()
    {
        return Resolve("NetPlayer") ?? Resolve("ScavPlayer");
    }

    private static uint KnetToUInt(object value)
    {
        if (value == null) return 0;

        FieldInfo id = value.GetType().GetField("id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return id == null ? 0 : Convert.ToUInt32(id.GetValue(value));
    }

    private static void LogDebug(string message)
    {
        if (SpriteReplacerPlugin.Log != null)
            SpriteReplacerPlugin.Log.LogDebug(message);
    }

    private static bool CoreAvailable()
    {
        if (_coreAvailable) return true;
        if (Time.unscaledTime < _nextCoreCheck) return false;
        _nextCoreCheck = Time.unscaledTime + 2f;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, "CUCoreLib", StringComparison.Ordinal))
                continue;

            _coreAvailable = true;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ActiveCore()
    {
        try
        {
            return MultiplayerApi.IsAvailable && MultiplayerApi.IsRunning;
        }
        catch
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Refresh()
    {
        try
        {
            Body before = _localBody;
            if (MultiplayerApi.TryGetLocalBody(out Body body))
                _localBody = body;

            if (_localBody != before)
                _needsApply = true;
        }
        catch (Exception ex)
        {
            _localBody = null;
            if (SpriteReplacerPlugin.Log != null)
                SpriteReplacerPlugin.Log.LogDebug("Local body lookup failed: " + ex.Message);
        }
    }
}
