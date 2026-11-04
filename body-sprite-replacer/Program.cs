using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System;

[BepInPlugin("com.yourname.spritereplacer", "Sprite Replacer", "1.4.0")]
[BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
public class SpriteReplacerPlugin : BaseUnityPlugin
{
    public const int SlotCount = 9;
    public const int DefaultBackupCount = 5;
    public const int MaxBackupCount = 50;

    public static string SpritesPath => Path.Combine(Paths.PluginPath, "CustomSprites");
    public static Dictionary<string, Sprite> LoadedSprites = new Dictionary<string, Sprite>();
    internal static readonly HashSet<string> ColoredWoundSprites = new HashSet<string>();
    internal static ManualLogSource Log;

    private static string _activeSlot = SlotFolders.SlotId(1);
    public static string ActiveSlot => _activeSlot;

    private static ConfigEntry<string> _activeSlotSetting;
    private static ConfigEntry<int> _backupCountSetting;
    private static ConfigEntry<bool> _shareSetting;
    private static ConfigEntry<bool> _downloadSetting;
    private static ConfigEntry<int> _cacheMbSetting;

    internal static int BackupCount =>
        _backupCountSetting == null ? DefaultBackupCount : Math.Max(0, _backupCountSetting.Value);

    internal static bool ShareSkins => _shareSetting == null || _shareSetting.Value;
    internal static bool DownloadSkins => _downloadSetting == null || _downloadSetting.Value;

    internal static int MaxSkinCacheMB =>
        _cacheMbSetting == null ? 64 : Math.Max(4, _cacheMbSetting.Value);

    private static readonly Dictionary<KeyCode, string> KeyMapping = new Dictionary<KeyCode, string>
    {
        { KeyCode.Keypad1, SlotFolders.SlotId(1) },
        { KeyCode.Keypad2, SlotFolders.SlotId(2) },
        { KeyCode.Keypad3, SlotFolders.SlotId(3) },
        { KeyCode.Keypad4, SlotFolders.SlotId(4) },
        { KeyCode.Keypad5, SlotFolders.SlotId(5) },
        { KeyCode.Keypad6, SlotFolders.SlotId(6) },
        { KeyCode.Keypad7, SlotFolders.SlotId(7) },
        { KeyCode.Keypad8, SlotFolders.SlotId(8) },
        { KeyCode.Keypad9, SlotFolders.SlotId(9) }
    };

    private void Awake()
    {
        Log = Logger;

        _activeSlotSetting = Config.Bind("General", "ActiveSlot", SlotFolders.SlotId(1),
            "The CustomSprites slot that loads at startup. Values: slot1 to slot9. Old st1 to st9 values still work.");

        _backupCountSetting = Config.Bind("General", "BackupCount", DefaultBackupCount,
            new ConfigDescription(
                "Number of backup folders to keep for each slot. Older backups are deleted. 0 keeps all backups.",
                new AcceptableValueRange<int>(0, MaxBackupCount)));

        _shareSetting = Config.Bind("Multiplayer", "ShareSkins", true,
            "Share your selected skin with the other players.");

        _downloadSetting = Config.Bind("Multiplayer", "DownloadSkins", true,
            "Download and apply the skins that other players share.");

        _cacheMbSetting = Config.Bind("Multiplayer", "MaxSkinCacheMB", 64,
            new ConfigDescription(
                "Maximum disk space for the remote skin cache.",
                new AcceptableValueRange<int>(4, 512)));

        bool rootCreated = !Directory.Exists(SpritesPath);
        Directory.CreateDirectory(SpritesPath);
        foreach (string message in SlotFolders.Migrate(SpritesPath, SlotCount))
            Log.LogInfo(message);

        EnsureCustomSpritesStructure();
        if (rootCreated)
            Log.LogInfo("Created CustomSprites folder with slot1..slot9/Body|Head subfolders.");

        string savedSlot = SlotFolders.Normalize(_activeSlotSetting.Value);
        if (savedSlot != null && CharacterFolderExists(savedSlot))
        {
            _activeSlot = savedSlot;
            LoadCharacterSprites(savedSlot);
        }
        else if (CharacterFolderExists(SlotFolders.SlotId(1)))
        {
            _activeSlot = SlotFolders.SlotId(1);
            LoadCharacterSprites(_activeSlot);
        }
        else
        {
            string firstAvailable = FindFirstAvailableCharacter();
            if (firstAvailable != null)
            {
                _activeSlot = firstAvailable;
                LoadCharacterSprites(firstAvailable);
            }
            else
            {
                Log.LogWarning("No CustomSprites/slot* folders found! Mod will be inactive.");
            }
        }

        SaveActiveSlot(_activeSlot);

        var harmony = new Harmony("com.yourname.spritereplacer");
        harmony.PatchAll();

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        var listenerObj = new GameObject("SpriteReplacerInputListener");
        DontDestroyOnLoad(listenerObj);
        listenerObj.hideFlags = HideFlags.HideAndDontSave;
        listenerObj.AddComponent<InputListener>();

        Log.LogInfo("Sprite Replacer loaded! Numpad 1-9 to switch characters.");
    }

    private void Start()
    {
        CoreLibIntegration.TryRegister();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log.LogInfo($"Scene loaded: '{scene.name}' (mode: {mode})");
        SkinMenuUI.EnsureCanvas();
        SkinMenuUI.SetVisible(false);
        ApplyToAll();
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        Log.LogInfo($"Scene unloaded: '{scene.name}'");
    }

    private static void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        Log.LogInfo($"Active scene changed: '{oldScene.name}' -> '{newScene.name}'");
    }

    private class InputListener : MonoBehaviour
    {
        private static bool _mpTickError;

        private void Update()
        {
            SkinMenuUI.SetVisible(MenuGate.ShouldShowButton());
            SkinMenuUI.TickCooldown();

            try
            {
                MpBody.Tick();
                MpSkinSync.Tick();
            }
            catch (Exception ex)
            {
                if (!_mpTickError)
                {
                    _mpTickError = true;
                    SpriteReplacerPlugin.Log.LogWarning("Multiplayer tick failed: " + ex);
                }
            }

            if (SkinMenuUI.IsFolderBrowserOpen) return;
            if (IsInputFieldFocused()) return;

            foreach (var kvp in KeyMapping)
            {
                if (UnityEngine.Input.GetKeyDown(kvp.Key))
                {
                    SwitchToSlot(kvp.Value);
                    break;
                }
            }
        }

        private static bool IsInputFieldFocused()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null) return false;

            TMP_InputField field = selected.GetComponentInParent<TMP_InputField>();
            return field != null && field.isFocused;
        }
    }

    public static bool SwitchToSlot(string slot, bool forceReload = false)
    {
        string normalized = SlotFolders.Normalize(slot);
        if (normalized == null)
        {
            Log.LogWarning("Invalid slot: " + slot);
            return false;
        }
        slot = normalized;

        if (MpSkinSync.IsCoolingDown)
        {
            SkinMenuUI.ShowStatus("Skin changes paused. Try again in "
                + MpSkinSync.CooldownSeconds + "s.", GameTheme.WarningColor);
            CoreLibIntegration.StoreActiveSlot(_activeSlot);
            Log.LogInfo("Skin change blocked: rate limited.");
            return false;
        }

        if (!SkinLock.CanChangeLocal)
        {
            SkinMenuUI.ShowStatus("Skins locked by host.", GameTheme.WarningColor);
            CoreLibIntegration.StoreActiveSlot(_activeSlot);
            Log.LogInfo("Skin change blocked: skins are locked.");
            return false;
        }

        if (!forceReload && slot == _activeSlot && LoadedSprites.Count > 0)
        {
            SkinMenuUI.RefreshSelection();
            return true;
        }

        if (!CharacterFolderExists(slot))
        {
            Log.LogWarning("Character folder for " + slot + " not found");
            return false;
        }

        _activeSlot = slot;
        SaveActiveSlot(slot);
        CoreLibIntegration.StoreActiveSlot(slot);
        LoadCharacterSprites(slot);
        ApplyToAll();
        MpSkinSync.OnLocalSkinChanged();
        SkinMenuUI.RefreshSelection();
        Log.LogInfo("Switched to " + slot);
        return true;
    }

    private static void SaveActiveSlot(string slot)
    {
        if (_activeSlotSetting == null) return;
        if (_activeSlotSetting.Value == slot) return;

        _activeSlotSetting.Value = slot;
        if (_activeSlotSetting.ConfigFile != null)
            _activeSlotSetting.ConfigFile.Save();
    }

    internal static void SetBackupCount(int value)
    {
        if (_backupCountSetting == null) return;
        value = Math.Max(0, Math.Min(MaxBackupCount, value));
        if (_backupCountSetting.Value == value) return;

        _backupCountSetting.Value = value;
        if (_backupCountSetting.ConfigFile != null)
            _backupCountSetting.ConfigFile.Save();
    }

    internal static void SetShareSkins(bool value)
    {
        if (_shareSetting == null || _shareSetting.Value == value) return;
        _shareSetting.Value = value;
        if (_shareSetting.ConfigFile != null)
            _shareSetting.ConfigFile.Save();
    }

    internal static void SetDownloadSkins(bool value)
    {
        if (_downloadSetting == null || _downloadSetting.Value == value) return;
        _downloadSetting.Value = value;
        if (_downloadSetting.ConfigFile != null)
            _downloadSetting.ConfigFile.Save();
    }

    internal static void SetMaxSkinCacheMB(int value)
    {
        if (_cacheMbSetting == null) return;
        value = Math.Max(4, Math.Min(512, value));
        if (_cacheMbSetting.Value == value) return;
        _cacheMbSetting.Value = value;
        if (_cacheMbSetting.ConfigFile != null)
            _cacheMbSetting.ConfigFile.Save();
    }

    internal static bool CharacterFolderExists(string character)
    {
        string normalized = SlotFolders.Normalize(character);
        if (normalized == null) return false;

        string basePath = Path.Combine(SpritesPath, normalized);
        string bodyPath = Path.Combine(basePath, "Body");
        string headPath = Path.Combine(basePath, "Head");
        // Empty prepared dirs don't count as an available character; need actual png sprites.
        return (Directory.Exists(bodyPath) && Directory.GetFiles(bodyPath, "*.png").Length > 0) ||
               (Directory.Exists(headPath) && Directory.GetFiles(headPath, "*.png").Length > 0);
    }

    private static void EnsureCustomSpritesStructure()
    {
        string root = SpritesPath;

        for (int i = 1; i <= SlotCount; i++)
        {
            string slot = Path.Combine(root, SlotFolders.SlotId(i));
            Directory.CreateDirectory(Path.Combine(slot, "Body"));
            Directory.CreateDirectory(Path.Combine(slot, "Head"));
        }
    }

    private static string FindFirstAvailableCharacter()
    {
        for (int i = 1; i <= SlotCount; i++)
        {
            string slot = SlotFolders.SlotId(i);
            if (CharacterFolderExists(slot))
                return slot;
        }
        return null;
    }

    private static void LoadCharacterSprites(string character)
    {
        LoadedSprites.Clear();
        ColoredWoundSprites.Clear();
        LoadSpritesFromFolder(character, "Body");
        LoadSpritesFromFolder(character, "Head");
        LoadSpritesFromFolder(character, "WoundView");
        Log.LogInfo($"Loaded {LoadedSprites.Count} sprites for character {character}");
    }

    private static void LoadSpritesFromFolder(string character, string subfolder)
    {
        string path = Path.Combine(SpritesPath, character, subfolder);
        if (!Directory.Exists(path)) return;

        foreach (string file in Directory.GetFiles(path, "*.png"))
        {
            string spriteName = Path.GetFileNameWithoutExtension(file);
            byte[] data = File.ReadAllBytes(file);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            if (!ImageConversion.LoadImage(tex, data))
            {
                UnityEngine.Object.Destroy(tex);
                Log.LogWarning("Could not decode sprite: " + file);
                continue;
            }

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 8f);
            sprite.name = spriteName;
            LoadedSprites[spriteName] = sprite;

            if (subfolder == "WoundView" && HasColorPixels(tex))
                ColoredWoundSprites.Add(spriteName);

            Log.LogInfo("Loaded sprite: " + spriteName);
        }
    }

    private static bool HasColorPixels(Texture2D tex)
    {
        Color32[] pixels = tex.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            if (p.a < 128) continue;
            if (p.r != p.g || p.g != p.b) return true;
        }
        return false;
    }

    internal static Sprite LoadEmbeddedSprite(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                Log.LogWarning($"Embedded resource '{resourceName}' not found");
                return null;
            }
            byte[] data = new byte[stream.Length];
            stream.Read(data, 0, data.Length);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            if (!ImageConversion.LoadImage(tex, data))
            {
                UnityEngine.Object.Destroy(tex);
                Log.LogWarning("Could not decode embedded sprite: " + resourceName);
                return null;
            }
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 8f);
            sprite.name = "MenuButtonPlaceholder";
            return sprite;
        }
    }

    internal static void ApplyToAll()
    {
        if (MpBody.IsActive)
        {
            MpBody.ApplyLocalSkin(LoadedSprites);
            WoundViewIntegration.ApplyAll();
            return;
        }

        foreach (var limb in UnityEngine.Object.FindObjectsOfType<Limb>())
        {
            ApplyReplacement(limb);
        }
        foreach (var tail in UnityEngine.Object.FindObjectsOfType<TailScript>())
        {
            ApplyReplacementToTail(tail);
        }
        foreach (var face in UnityEngine.Object.FindObjectsOfType<FacialExpression>())
        {
            ApplyReplacementToFace(face);
        }
        WoundViewIntegration.ApplyAll();
    }

    public static void ApplyReplacement(Limb limb)
    {
        if (!MpBody.CanApply(limb.body)) return;

        SpriteRenderer sr = limb.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null && LoadedSprites.TryGetValue(sr.sprite.name, out Sprite replacement))
        {
            sr.sprite = replacement;
        }
    }

    public static void ApplyReplacementToTail(TailScript tail)
    {
        if (!MpBody.CanApply(tail.GetComponentInParent<Body>())) return;

        SpriteRenderer sr = tail.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null && LoadedSprites.TryGetValue(sr.sprite.name, out Sprite replacement))
        {
            sr.sprite = replacement;
        }
    }

    public static void ApplyReplacementToFace(FacialExpression face)
    {
        if (!MpBody.CanApply(face.body)) return;
        face.defaultHead = ReplaceSprite(face.defaultHead);
        face.defaultHeadMouth = ReplaceSprite(face.defaultHeadMouth);
        face.defaultHeadMouthHalf = ReplaceSprite(face.defaultHeadMouthHalf);
        face.eyesGone = ReplaceSprite(face.eyesGone);
        face.eyesGoneHealed = ReplaceSprite(face.eyesGoneHealed);

        if (face.nosebleedSprite != null)
        {
            SkinSpriteReplacer replacer = face.nosebleedSprite.GetComponent<SkinSpriteReplacer>();
            if (replacer == null)
            {
                replacer = SkinSpriteReplacer.Attach(face.nosebleedSprite, LoadedSprites);
                replacer.Apply();
            }
            else
            {
                replacer.SetSprites(LoadedSprites);
            }
        }

        for (int i = 0; i < face.disfiguredHead.Length; i++)
            face.disfiguredHead[i] = ReplaceSprite(face.disfiguredHead[i]);

        for (int i = 0; i < face.disfiguredHeadHeal.Length; i++)
            face.disfiguredHeadHeal[i] = ReplaceSprite(face.disfiguredHeadHeal[i]);

        for (int i = 0; i < face.eyeList.Count; i++)
        {
            Eye eye = face.eyeList[i];
            eye.front = ReplaceSprite(eye.front);
            eye.back = ReplaceSprite(eye.back);
            face.eyeList[i] = eye;
        }
    }

    private static Sprite ReplaceSprite(Sprite s)
    {
        if (s != null && LoadedSprites.TryGetValue(s.name, out Sprite r))
            return r;
        return s;
    }
}

[HarmonyPatch(typeof(Limb), "Awake")]
public class LimbAwakePatch
{
    static void Postfix(Limb __instance)
    {
        if (MpBody.IsActive) return;

        SpriteRenderer sr = __instance.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
            SpriteReplacerPlugin.Log.LogDebug("[SpriteReplacer] Limb sprite name: '" + sr.sprite.name + "'");

        SpriteReplacerPlugin.ApplyReplacement(__instance);
    }
}

[HarmonyPatch(typeof(FacialExpression), "Start")]
public class FacialExpressionStartPatch
{
    static void Postfix(FacialExpression __instance)
    {
        if (MpBody.IsActive) return;
        SpriteReplacerPlugin.ApplyReplacementToFace(__instance);
    }
}

[HarmonyPatch(typeof(TailScript), "Start")]
public class TailScriptStartPatch
{
    static void Postfix(TailScript __instance)
    {
        if (MpBody.IsActive) return;
        SpriteReplacerPlugin.ApplyReplacementToTail(__instance);
    }
}

[HarmonyPatch(typeof(Settings), "ApplyAll")]
public class SettingsApplyAllPatch
{
    // The game re-applies saved settings at startup. Put the saved slot and
    // backup count back first, so a stale value cannot win.
    static void Prefix()
    {
        CoreLibIntegration.StoreActiveSlot(SpriteReplacerPlugin.ActiveSlot);
        CoreLibIntegration.StoreBackupCount(SpriteReplacerPlugin.BackupCount);
    }
}

[HarmonyPatch(typeof(WoundView), "Awake")]
public class WoundViewAwakePatch
{
    static void Postfix(WoundView __instance)
    {
        WoundViewIntegration.Apply(__instance);
    }
}

[HarmonyPatch(typeof(WoundView), "UpdateView")]
public class WoundViewUpdatePatch
{
    static void Postfix(WoundView __instance)
    {
        WoundViewIntegration.ShowCustomColors(__instance);
    }
}

[HarmonyPatch(typeof(PlayerCamera), "Awake")]
public class PlayerCameraAwakePatch
{
    static void Postfix()
    {
        WoundViewIntegration.ApplyAll();
    }
}
