using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;

[BepInPlugin("com.yourname.spritereplacer", "Sprite Replacer", "1.0.0")]
public class SpriteReplacerPlugin : BaseUnityPlugin
{
    public static string SpritesPath => Path.Combine(Paths.PluginPath, "CustomSprites");
    public static Dictionary<string, Sprite> LoadedSprites = new Dictionary<string, Sprite>();
    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;

        LoadSprites("Body");
        LoadSprites("Head");

        var harmony = new Harmony("com.yourname.spritereplacer");
        harmony.PatchAll();

        SceneManager.sceneLoaded += OnSceneLoaded;

        Log.LogInfo("Sprite Replacer loaded!");
    }

    private void OnGUI()
    {
        UnityEngine.GUI.Label(new Rect(10, 10, 400, 20), "[SpriteReplacer] Loaded: " + LoadedSprites.Count + " sprites");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var limb in Object.FindObjectsOfType<Limb>())
        {
            ApplyReplacement(limb);
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

    private void LoadSprites(string subfolder)
    {
        string path = Path.Combine(Paths.PluginPath, "CustomSprites", subfolder);
        if (!Directory.Exists(path))
        {
            Log.LogWarning("Folder not found: " + path);
            return;
        }

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
        __instance.defaultHead = Replace(__instance.defaultHead);
        __instance.defaultHeadMouth = Replace(__instance.defaultHeadMouth);
        __instance.defaultHeadMouthHalf = Replace(__instance.defaultHeadMouthHalf);
        __instance.eyesGone = Replace(__instance.eyesGone);
        __instance.eyesGoneHealed = Replace(__instance.eyesGoneHealed);

        for (int i = 0; i < __instance.disfiguredHead.Length; i++)
            __instance.disfiguredHead[i] = Replace(__instance.disfiguredHead[i]);

        for (int i = 0; i < __instance.disfiguredHeadHeal.Length; i++)
            __instance.disfiguredHeadHeal[i] = Replace(__instance.disfiguredHeadHeal[i]);

        for (int i = 0; i < __instance.eyeList.Count; i++)
        {
            Eye eye = __instance.eyeList[i];
            eye.front = Replace(eye.front);
            eye.back = Replace(eye.back);
            __instance.eyeList[i] = eye;
        }
    }

    static Sprite Replace(Sprite s)
    {
        if (s != null && SpriteReplacerPlugin.LoadedSprites.TryGetValue(s.name, out Sprite r))
            return r;
        return s;
    }
}

[HarmonyPatch(typeof(TailScript), "Start")]
public class TailScriptStartPatch
{
    static void Postfix(TailScript __instance)
    {
        SpriteRenderer sr = __instance.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null && SpriteReplacerPlugin.LoadedSprites.TryGetValue(sr.sprite.name, out Sprite replacement))
            sr.sprite = replacement;
    }
}
