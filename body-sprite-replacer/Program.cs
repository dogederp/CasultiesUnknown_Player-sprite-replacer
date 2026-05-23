using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;

[BepInPlugin("com.yourname.spritereplacer", "Sprite Replacer", "1.1.0")]
public class SpriteReplacerPlugin : BaseUnityPlugin
{
    public static string SpritesPath => Path.Combine(Paths.PluginPath, "CustomSprites");
    public static Dictionary<string, Sprite> LoadedSprites = new Dictionary<string, Sprite>();
    private static string _currentCharacter = "st1";
    internal static ManualLogSource Log;

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

        if (CharacterFolderExists("st1"))
        {
            LoadCharacterSprites("st1");
        }
        else
        {
            string firstAvailable = FindFirstAvailableCharacter();
            if (firstAvailable != null)
            {
                _currentCharacter = firstAvailable;
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

        var listenerObj = new GameObject("SpriteReplacerInputListener");
        DontDestroyOnLoad(listenerObj);
        listenerObj.hideFlags = HideFlags.HideAndDontSave;
        listenerObj.AddComponent<InputListener>();

        Log.LogInfo("Sprite Replacer loaded! Numpad 1-9 to switch characters.");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToAll();
    }

    private class InputListener : MonoBehaviour
    {
        private void Update()
        {
            foreach (var kvp in KeyMapping)
            {
                if (UnityEngine.Input.GetKeyDown(kvp.Key))
                {
                    string target = kvp.Value;
                    if (CharacterFolderExists(target))
                    {
                        if (_currentCharacter != target)
                        {
                            Log.LogInfo($"Keypad {kvp.Key} -> {target}");
                            _currentCharacter = target;
                            LoadCharacterSprites(target);
                            ApplyToAll();
                            Log.LogInfo("Switched to " + target);
                        }
                    }
                    else
                    {
                        Log.LogWarning($"Character folder for {target} not found");
                    }
                    break;
                }
            }
        }
    }

    private static bool CharacterFolderExists(string character)
    {
        string basePath = Path.Combine(Paths.PluginPath, "CustomSprites", character);
        return Directory.Exists(Path.Combine(basePath, "Body")) ||
               Directory.Exists(Path.Combine(basePath, "Head"));
    }

    private static string FindFirstAvailableCharacter()
    {
        for (int i = 1; i <= 9; i++)
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
        LoadSpritesFromFolder(character, "Body");
        LoadSpritesFromFolder(character, "Head");
        Log.LogInfo($"Loaded {LoadedSprites.Count} sprites for character {character}");
    }

    private static void LoadSpritesFromFolder(string character, string subfolder)
    {
        string path = Path.Combine(Paths.PluginPath, "CustomSprites", character, subfolder);
        if (!Directory.Exists(path)) return;

        foreach (string file in Directory.GetFiles(path, "*.png"))
        {
            string spriteName = Path.GetFileNameWithoutExtension(file);
            byte[] data = File.ReadAllBytes(file);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            ImageConversion.LoadImage(tex, data);

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 8f);
            sprite.name = spriteName;
            LoadedSprites[spriteName] = sprite;

            Log.LogInfo("Loaded sprite: " + spriteName);
        }
    }

    private static void ApplyToAll()
    {
        foreach (var limb in Object.FindObjectsOfType<Limb>())
        {
            ApplyReplacement(limb);
        }
        foreach (var tail in Object.FindObjectsOfType<TailScript>())
        {
            ApplyReplacementToTail(tail);
        }
        foreach (var face in Object.FindObjectsOfType<FacialExpression>())
        {
            ApplyReplacementToFace(face);
        }
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
