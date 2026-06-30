using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System;

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

        EnsureCustomSpritesStructure();

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
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        var listenerObj = new GameObject("SpriteReplacerInputListener");
        DontDestroyOnLoad(listenerObj);
        listenerObj.hideFlags = HideFlags.HideAndDontSave;
        listenerObj.AddComponent<InputListener>();

        Log.LogInfo("Sprite Replacer loaded! Numpad 1-9 to switch characters.");
    }
    
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log.LogInfo($"Scene loaded: '{scene.name}' (mode: {mode})");
        if (_menuButtonCanvas == null)
            CreateMenuButton();
        _menuButtonCanvas.SetActive(false);
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
            if (_menuButtonCanvas != null)
            {
                bool onMenu = SceneManager.GetActiveScene().name == "PreGen";
                _menuButtonCanvas.SetActive(onMenu && IsMainMenuVisible());
            }

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
        string bodyPath = Path.Combine(basePath, "Body");
        string headPath = Path.Combine(basePath, "Head");
        // Empty prepared dirs don't count as an available character; need actual png sprites.
        return (Directory.Exists(bodyPath) && Directory.GetFiles(bodyPath, "*.png").Length > 0) ||
               (Directory.Exists(headPath) && Directory.GetFiles(headPath, "*.png").Length > 0);
    }

    private static void EnsureCustomSpritesStructure()
    {
        string root = Path.Combine(Paths.PluginPath, "CustomSprites");
        bool created = !Directory.Exists(root);
        Directory.CreateDirectory(root);

        for (int i = 1; i <= 9; i++)
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

    private static Sprite LoadEmbeddedSprite(string resourceName)
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
            ImageConversion.LoadImage(tex, data);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 8f);
            sprite.name = "MenuButtonPlaceholder";
            return sprite;
        }
    }
    
    // --- menu button stuff ---

    private static GameObject _menuButtonCanvas;
    private static GameObject _popupWindow;
    private static FieldInfo _didIntroField;

    private static bool IsMainMenuVisible()
    {
        if (_didIntroField == null)
        {
            Type preRunType = typeof(Limb).Assembly.GetType("PreRunScript");
            if (preRunType != null)
                _didIntroField = preRunType.GetField("didIntro", BindingFlags.Static | BindingFlags.NonPublic);
            if (_didIntroField == null)
                Log.LogWarning("Could not find PreRunScript.didIntro via reflection");
        }
        if (_didIntroField == null) return false;
        return (bool)_didIntroField.GetValue(null);
    }

    private static void CreateMenuButton()
    {
        Sprite buttonSprite = LoadEmbeddedSprite("body_sprite_replacer.resources.placeholder.png");
        if (buttonSprite == null)
        {
            Log.LogWarning("Failed to load menu button sprite");
            return;
        }

        GameObject canvasObj = new GameObject("SpriteReplacerMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(canvasObj);
        _menuButtonCanvas = canvasObj;

        GameObject buttonObj = new GameObject("MenuButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);

        RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 1);
        rectTransform.anchorMax = new Vector2(0, 1);
        rectTransform.pivot = new Vector2(0, 1);
        rectTransform.anchoredPosition = new Vector2(20, -20);
        rectTransform.sizeDelta = new Vector2(64, 64);

        Image image = buttonObj.AddComponent<Image>();
        image.sprite = buttonSprite;
        image.preserveAspect = true;

        Button button = buttonObj.AddComponent<Button>();
        button.onClick.AddListener(() => TogglePopupWindow());

        Log.LogInfo("Menu button created");
    }

    private static void TogglePopupWindow()
    {
        if (_popupWindow == null)
            CreatePopupWindow();
        else
            _popupWindow.SetActive(!_popupWindow.activeSelf);
    }

    private static void CreatePopupWindow()
    {
        if (_menuButtonCanvas == null) return;

        GameObject windowObj = new GameObject("PopupWindow");
        windowObj.transform.SetParent(_menuButtonCanvas.transform, false);

        RectTransform windowRect = windowObj.AddComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(500, 500);

        Image windowBg = windowObj.AddComponent<Image>();
        windowBg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        // Close button — top-right corner
        GameObject closeBtnObj = new GameObject("CloseButton");
        closeBtnObj.transform.SetParent(windowObj.transform, false);

        RectTransform closeRect = closeBtnObj.AddComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1, 1);
        closeRect.anchorMax = new Vector2(1, 1);
        closeRect.pivot = new Vector2(1, 1);
        closeRect.anchoredPosition = new Vector2(-10, -10);
        closeRect.sizeDelta = new Vector2(40, 40);

        Image closeImg = closeBtnObj.AddComponent<Image>();
        closeImg.color = Color.red;

        Button closeBtn = closeBtnObj.AddComponent<Button>();
        closeBtn.onClick.AddListener(() => _popupWindow.SetActive(false));

        // "X" label on close button
        GameObject closeLabelObj = new GameObject("CloseLabel");
        closeLabelObj.transform.SetParent(closeBtnObj.transform, false);

        RectTransform labelRect = closeLabelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;

        Text closeText = closeLabelObj.AddComponent<Text>();
        closeText.text = "X";
        closeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        closeText.fontSize = 24;
        closeText.color = Color.white;
        closeText.alignment = TextAnchor.MiddleCenter;

        string[] characters = GetAvailableCharacters();
        if (characters.Length > 0)
            BuildCharacterGrid(windowObj);
        else
            BuildEmptyStateMessage(windowObj);

        _popupWindow = windowObj;
        Log.LogInfo("Popup window created");
    }

    private static void BuildEmptyStateMessage(GameObject window)
    {
        GameObject msgObj = new GameObject("EmptyStateMessage");
        msgObj.transform.SetParent(window.transform, false);

        RectTransform msgRect = msgObj.AddComponent<RectTransform>();
        msgRect.anchorMin = new Vector2(0, 0);
        msgRect.anchorMax = new Vector2(1, 1);
        msgRect.pivot = new Vector2(0.5f, 0.5f);
        msgRect.anchoredPosition = Vector2.zero;
        msgRect.sizeDelta = new Vector2(-40f, -80f);

        Text msg = msgObj.AddComponent<Text>();
        msg.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        msg.fontSize = 18;
        msg.color = Color.white;
        msg.alignment = TextAnchor.UpperCenter;
        msg.horizontalOverflow = HorizontalWrapMode.Wrap;
        msg.verticalOverflow = VerticalWrapMode.Overflow;
        msg.text =
            "No custom sprites found.\n\n" +
            "To add your own skins:\n\n" +
            "1. Open the CustomSprites folder inside this mod's plugin directory.\n" +
            "2. Choose a slot: st1 through st9.\n" +
            "3. Place your .png files into that slot's Body or Head subfolder.\n" +
            "4. Each PNG filename must match the original in-game sprite name.\n" +
            "5. Press Numpad 1-9 to load and switch to that slot in-game.\n\n" +
            "Example: CustomSprites/st1/Body/experimentCrus.png";

        Log.LogWarning("No characters with sprites available; showing empty-state instructions.");
    }

    // --- Character grid ---

    private static void BuildCharacterGrid(GameObject window)
    {
        string[] characters = GetAvailableCharacters();
        if (characters.Length == 0) return;

        GameObject gridObj = new GameObject("CharacterGrid");
        gridObj.transform.SetParent(window.transform, false);

        RectTransform gridRect = gridObj.AddComponent<RectTransform>();
        gridRect.anchorMin = Vector2.zero;
        gridRect.anchorMax = Vector2.one;
        gridRect.pivot = new Vector2(0.5f, 1f);
        gridRect.anchoredPosition = new Vector2(0, -55f);
        gridRect.sizeDelta = new Vector2(-20f, -75f);

        int cols = 3;
        float cellSize = 110f;
        float spacing = 10f;
        float gridWidth = cols * cellSize + (cols - 1) * spacing;
        float startX = -(gridWidth / 2f) + cellSize / 2f;

        for (int i = 0; i < characters.Length; i++)
        {
            int row = i / cols;
            int col = i % cols;

            float x = startX + col * (cellSize + spacing);
            float y = -(row * (cellSize + spacing) + cellSize / 2f);

            CreateCharacterCell(gridObj, characters[i], x, y, cellSize);
        }
    }

    private static void CreateCharacterCell(GameObject parent, string character, float x, float y, float size)
    {
        GameObject cellObj = new GameObject("Cell_" + character);
        cellObj.transform.SetParent(parent.transform, false);

        RectTransform cellRect = cellObj.AddComponent<RectTransform>();
        cellRect.anchorMin = new Vector2(0.5f, 1f);
        cellRect.anchorMax = new Vector2(0.5f, 1f);
        cellRect.pivot = new Vector2(0.5f, 0.5f);
        cellRect.anchoredPosition = new Vector2(x, y);
        cellRect.sizeDelta = new Vector2(size, size);

        Image bgImg = cellObj.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        Button btn = cellObj.AddComponent<Button>();
        btn.onClick.AddListener(() => { Log.LogInfo($"Clicked character: {character}"); });

        // Highlight on hover
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        btn.colors = colors;

        Sprite preview = ComposeCharacterPreview(character);
        if (preview != null)
        {
            GameObject imgObj = new GameObject("PreviewImage");
            imgObj.transform.SetParent(cellObj.transform, false);

            RectTransform imgRect = imgObj.AddComponent<RectTransform>();
            imgRect.anchorMin = new Vector2(0.5f, 0.5f);
            imgRect.anchorMax = new Vector2(0.5f, 0.5f);
            imgRect.pivot = new Vector2(0.5f, 0.5f);
            imgRect.anchoredPosition = new Vector2(0, 8f);
            imgRect.sizeDelta = new Vector2(size - 16f, size - 30f);

            Image img = imgObj.AddComponent<Image>();
            img.sprite = preview;
            img.preserveAspect = true;
        }

        // Character name label
        GameObject labelObj = new GameObject("CharacterLabel");
        labelObj.transform.SetParent(cellObj.transform, false);

        RectTransform lblRect = labelObj.AddComponent<RectTransform>();
        lblRect.anchorMin = new Vector2(0f, 0f);
        lblRect.anchorMax = new Vector2(1f, 0f);
        lblRect.pivot = new Vector2(0.5f, 0f);
        lblRect.anchoredPosition = new Vector2(0f, 5f);
        lblRect.sizeDelta = new Vector2(0f, 20f);

        Text lbl = labelObj.AddComponent<Text>();
        lbl.text = character;
        lbl.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        lbl.fontSize = 14;
        lbl.color = Color.white;
        lbl.alignment = TextAnchor.MiddleCenter;
    }

    // --- Character scanning & compositing ---

    private static string[] GetAvailableCharacters()
    {
        var chars = new List<string>();
        string basePath = Path.Combine(Paths.PluginPath, "CustomSprites");
        if (!Directory.Exists(basePath)) return chars.ToArray();

        foreach (string dir in Directory.GetDirectories(basePath))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("st") && CharacterFolderExists(name))
                chars.Add(name);
        }
        chars.Sort();
        return chars.ToArray();
    }

    private static Dictionary<string, Sprite> LoadCharacterPreviewSprites(string character)
    {
        var dict = new Dictionary<string, Sprite>();
        string bodyPath = Path.Combine(Paths.PluginPath, "CustomSprites", character, "Body");
        string headPath = Path.Combine(Paths.PluginPath, "CustomSprites", character, "Head");
        if (Directory.Exists(bodyPath)) LoadSpritesFromFolderToDict(bodyPath, dict);
        if (Directory.Exists(headPath)) LoadSpritesFromFolderToDict(headPath, dict);
        return dict;
    }

    private static void LoadSpritesFromFolderToDict(string folderPath, Dictionary<string, Sprite> dict)
    {
        foreach (string file in Directory.GetFiles(folderPath, "*.png"))
        {
            string spriteName = Path.GetFileNameWithoutExtension(file);
            if (dict.ContainsKey(spriteName)) continue;

            byte[] data = File.ReadAllBytes(file);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            ImageConversion.LoadImage(tex, data);

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 8f);
            sprite.name = spriteName;
            dict[spriteName] = sprite;
        }
    }

    private static Sprite ComposeCharacterPreview(string character)
    {
        var sprites = LoadCharacterPreviewSprites(character);

        const int canvasW = 48, canvasH = 80;
        Texture2D composite = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
        composite.filterMode = FilterMode.Point;

        Color[] clear = new Color[canvasW * canvasH];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        composite.SetPixels(clear);

        // Standing-pose layout: (spriteName, x, y) in Texture2D bottom-left coords
        // Drawn back-to-front so later parts overlay earlier ones
        var layout = new (string name, int x, int y)[]
        {
            ("experimentThigh",      16, 25),   // left thigh
            ("experimentThigh",      26, 25),   // right thigh
            ("experimentDownTorso",  19, 38),
            ("experimentUpTorso",    19, 50),
            ("experimentCrus",       17, 15),   // left crus
            ("experimentCrus",       27, 15),   // right crus
            ("experimentFoot",       16,  0),   // left foot
            ("experimentFoot",       26,  0),   // right foot
            ("experimentUpArm",      11, 50),   // left upper arm
            ("experimentUpArm",      31, 50),   // right upper arm
            ("experimentDownArm",    11, 34),   // left lower arm
            ("experimentDownArm",    31, 34),   // right lower arm
            ("experimentHandF",      10, 28),   // left hand
            ("experimentHandF",      31, 28),   // right hand
            ("experimentHead",       10, 64),   // head (top)
        };

        foreach (var (name, x, y) in layout)
        {
            if (sprites.TryGetValue(name, out Sprite part))
                BlitSprite(composite, part, x, y);
        }

        composite.Apply();
        Sprite preview = Sprite.Create(composite, new Rect(0, 0, canvasW, canvasH), new Vector2(0.5f, 0.5f), 8f);
        preview.name = character + "_preview";
        return preview;
    }

    private static void BlitSprite(Texture2D dst, Sprite src, int dstX, int dstY)
    {
        Texture2D srcTex = src.texture;
        int srcW = (int)src.rect.width;
        int srcH = (int)src.rect.height;
        int srcX = (int)src.rect.x;
        int srcY = (int)src.rect.y;

        if (srcW <= 0 || srcH <= 0) return;

        Color[] pixels;
        try { pixels = srcTex.GetPixels(srcX, srcY, srcW, srcH); }
        catch { return; }

        int dstW = dst.width;
        int dstH = dst.height;

        for (int py = 0; py < srcH; py++)
        {
            for (int px = 0; px < srcW; px++)
            {
                int dx = dstX + px;
                int dy = dstY + py;
                if (dx >= 0 && dx < dstW && dy >= 0 && dy < dstH)
                {
                    Color c = pixels[py * srcW + px];
                    if (c.a > 0.01f)
                        dst.SetPixel(dx, dy, c);
                }
            }
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
