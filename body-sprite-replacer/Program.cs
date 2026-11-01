using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System;

[BepInPlugin("com.yourname.spritereplacer", "Sprite Replacer", "1.3.0")]
[BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
public class SpriteReplacerPlugin : BaseUnityPlugin
{
    public const int SlotCount = 9;

    public static string SpritesPath => Path.Combine(Paths.PluginPath, "CustomSprites");
    public static Dictionary<string, Sprite> LoadedSprites = new Dictionary<string, Sprite>();
    internal static readonly HashSet<string> ColoredWoundSprites = new HashSet<string>();
    internal static ManualLogSource Log;

    private static string _activeSlot = "st1";
    public static string ActiveSlot => _activeSlot;

    private static ConfigEntry<string> _activeSlotSetting;

    private static readonly Dictionary<KeyCode, string> KeyMapping = new Dictionary<KeyCode, string>
    {
        { KeyCode.Keypad1, "st1" },
        { KeyCode.Keypad2, "st2" },
        { KeyCode.Keypad3, "st3" },
        { KeyCode.Keypad4, "st4" },
        { KeyCode.Keypad5, "st5" },
        { KeyCode.Keypad6, "st6" },
        { KeyCode.Keypad7, "st7" },
        { KeyCode.Keypad8, "st8" },
        { KeyCode.Keypad9, "st9" }
    };

    private void Awake()
    {
        Log = Logger;

        _activeSlotSetting = Config.Bind("General", "ActiveSlot", "st1",
            "The CustomSprites slot that loads at startup. Values: st1 to st9.");

        EnsureCustomSpritesStructure();

        string savedSlot = _activeSlotSetting.Value;
        if (IsValidSlot(savedSlot) && CharacterFolderExists(savedSlot))
        {
            _activeSlot = savedSlot;
            LoadCharacterSprites(savedSlot);
        }
        else if (CharacterFolderExists("st1"))
        {
            _activeSlot = "st1";
            LoadCharacterSprites("st1");
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
                Log.LogWarning("No CustomSprites/st* folders found! Mod will be inactive.");
            }
        }

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
        private void Update()
        {
            SkinMenuUI.SetVisible(MenuGate.ShouldShowButton());

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

            InputField field = selected.GetComponentInParent<InputField>();
            return field != null && field.isFocused;
        }
    }

    public static bool SwitchToSlot(string slot, bool forceReload = false)
    {
        if (!IsValidSlot(slot))
        {
            Log.LogWarning("Invalid slot: " + slot);
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
        SkinMenuUI.RefreshSelection();
        Log.LogInfo("Switched to " + slot);
        return true;
    }

    internal static bool IsValidSlot(string slot)
    {
        return !string.IsNullOrEmpty(slot) && slot.Length == 3 &&
               slot.StartsWith("st", StringComparison.Ordinal) &&
               slot[2] >= '1' && slot[2] <= '9';
    }

    private static void SaveActiveSlot(string slot)
    {
        if (_activeSlotSetting == null) return;
        if (_activeSlotSetting.Value == slot) return;

        _activeSlotSetting.Value = slot;
        if (_activeSlotSetting.ConfigFile != null)
            _activeSlotSetting.ConfigFile.Save();
    }

    internal static bool CharacterFolderExists(string character)
    {
        string basePath = Path.Combine(SpritesPath, character);
        string bodyPath = Path.Combine(basePath, "Body");
        string headPath = Path.Combine(basePath, "Head");
        // Empty prepared dirs don't count as an available character; need actual png sprites.
        return (Directory.Exists(bodyPath) && Directory.GetFiles(bodyPath, "*.png").Length > 0) ||
               (Directory.Exists(headPath) && Directory.GetFiles(headPath, "*.png").Length > 0);
    }

    private static void EnsureCustomSpritesStructure()
    {
        string root = SpritesPath;
        bool created = !Directory.Exists(root);
        Directory.CreateDirectory(root);

        for (int i = 1; i <= SlotCount; i++)
        {
            string st = Path.Combine(root, "st" + i);
            Directory.CreateDirectory(Path.Combine(st, "Body"));
            Directory.CreateDirectory(Path.Combine(st, "Head"));
        }

        if (created)
            Log.LogInfo("Created CustomSprites folder with st1..st9/Body|Head subfolders.");
    }

    private static string FindFirstAvailableCharacter()
    {
        for (int i = 1; i <= SlotCount; i++)
        {
            string c = "st" + i;
            if (CharacterFolderExists(c))
                return c;
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

    private static void ApplyToAll()
    {
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
        SpriteRenderer sr = limb.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null && LoadedSprites.TryGetValue(sr.sprite.name, out Sprite replacement))
        {
            sr.sprite = replacement;
        }
    }

    public static void ApplyReplacementToTail(TailScript tail)
    {
        SpriteRenderer sr = tail.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null && LoadedSprites.TryGetValue(sr.sprite.name, out Sprite replacement))
        {
            sr.sprite = replacement;
        }
    }

    public static void ApplyReplacementToFace(FacialExpression face)
    {
        face.defaultHead = ReplaceSprite(face.defaultHead);
        face.defaultHeadMouth = ReplaceSprite(face.defaultHeadMouth);
        face.defaultHeadMouthHalf = ReplaceSprite(face.defaultHeadMouthHalf);
        face.eyesGone = ReplaceSprite(face.eyesGone);
        face.eyesGoneHealed = ReplaceSprite(face.eyesGoneHealed);

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
        SpriteRenderer sr = __instance.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
            SpriteReplacerPlugin.Log.LogInfo("[SpriteReplacer] Limb sprite name: '" + sr.sprite.name + "'");

        SpriteReplacerPlugin.ApplyReplacement(__instance);
    }
}

[HarmonyPatch(typeof(FacialExpression), "Start")]
public class FacialExpressionStartPatch
{
    static void Postfix(FacialExpression __instance)
    {
        SpriteReplacerPlugin.ApplyReplacementToFace(__instance);
    }
}

[HarmonyPatch(typeof(TailScript), "Start")]
public class TailScriptStartPatch
{
    static void Postfix(TailScript __instance)
    {
        SpriteReplacerPlugin.ApplyReplacementToTail(__instance);
    }
}

[HarmonyPatch(typeof(Settings), "ApplyAll")]
public class SettingsApplyAllPatch
{
    // The game re-applies saved settings at startup. Put the saved slot back
    // into the dropdown first, so a stale dropdown value cannot win.
    static void Prefix()
    {
        CoreLibIntegration.StoreActiveSlot(SpriteReplacerPlugin.ActiveSlot);
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
