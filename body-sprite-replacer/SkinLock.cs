using System;
using System.Reflection;
using CUCoreLib.Networking;
using Newtonsoft.Json.Linq;

// Host-controlled skin lock. The server rejects a new skin for a client that
// already has one. A client that joins while locked gets one extra change.
internal static class SkinLock
{
    internal const string LockChannel = "spritereplacer.skin.lock";

    internal static bool IsLocked { get; private set; }
    internal static bool HasLocalSkin { get; set; }
    internal static bool CanChangeOnce { get; private set; }

    internal static bool CanChangeLocal
    {
        get
        {
            if (!MpBody.IsActive) return true;
            if (!IsLocked) return true;
            if (CanChangeOnce) return true;
            return !HasLocalSkin;
        }
    }

    internal static void Toggle()
    {
        if (!MpBody.IsHost) return;
        SetLocked(!IsLocked);
    }

    internal static void SetLocked(bool locked)
    {
        if (!MpBody.IsHost) return;

        bool changed = IsLocked != locked;
        IsLocked = locked;
        CanChangeOnce = false;
        Log("host set locked=" + locked + ".");

        if (changed)
        {
            Announce(locked);
            MpSkinSync.OnLockChanged();
        }

        try
        {
            MultiplayerApi.Broadcast(LockChannel, Payload(false), false, true);
        }
        catch (Exception ex)
        {
            Log("broadcast failed: " + ex.Message);
        }

        SkinMenuUI.RefreshLockUi();
    }

    internal static void ApplyRemote(JToken token)
    {
        var payload = token as JObject;
        if (payload == null) return;

        JToken lockedToken = payload["locked"];
        if (lockedToken == null) return;

        bool wasLocked = IsLocked;
        IsLocked = lockedToken.Value<bool>();

        bool allow = payload["allowOneChange"] != null &&
                     payload["allowOneChange"].Value<bool>();

        if (!IsLocked)
            CanChangeOnce = false;
        else if (allow)
            CanChangeOnce = true;

        if (wasLocked != IsLocked)
            Announce(IsLocked);

        if (CanChangeOnce)
            SkinMenuUI.OpenPopupForChoice();

        SkinMenuUI.RefreshLockUi();
    }

    internal static void ConsumeChoice()
    {
        if (!CanChangeOnce) return;
        CanChangeOnce = false;
        SkinMenuUI.RefreshLockUi();
    }

    internal static JObject Payload(bool allowOneChange)
    {
        return new JObject
        {
            ["locked"] = IsLocked,
            ["allowOneChange"] = allowOneChange
        };
    }

    internal static void Reset()
    {
        if (!IsLocked && !HasLocalSkin && !CanChangeOnce) return;

        IsLocked = false;
        HasLocalSkin = false;
        CanChangeOnce = false;
        SkinMenuUI.RefreshLockUi();
    }

    private static void Announce(bool locked)
    {
        ChatMessage(locked
            ? "[Skin Replacer] The host locked all skins."
            : "[Skin Replacer] The host released the skin lock.");
    }

    private static void ChatMessage(string message)
    {
        try
        {
            Assembly assembly = MultiplayerApi.KrokMpAssembly;
            Type chat = assembly == null ? null : assembly.GetType("Together.Chat");
            if (chat == null) return;

            MethodInfo method = chat.GetMethod("LogMessage",
                new[] { typeof(string), typeof(string), typeof(bool).MakeByRefType() });
            if (method == null) return;

            object[] args = { message, string.Empty, false };
            method.Invoke(null, args);
        }
        catch (Exception ex)
        {
            Log("chat failed: " + ex.Message);
        }
    }

    private static void Log(string message)
    {
        if (SpriteReplacerPlugin.Log != null)
            SpriteReplacerPlugin.Log.LogInfo("[MP lock] " + message);
    }
}
