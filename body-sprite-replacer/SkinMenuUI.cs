using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

internal static class SkinMenuUI
{
    private const float WindowWidth = 780f;
    private const float WindowHeight = 600f;

    private static GameObject _canvas;
    private static GameObject _popupWindow;
    private static GameObject _grid;
    private static GameObject _browserWindow;
    private static GameObject _browserBackdrop;
    private static InputField _pathInput;
    private static RectTransform _listContent;
    private static ScrollRect _listScroll;
    private static Text _statusText;
    private static ScrollRect _statusScroll;
    private static string _browserPath;
    private static string _pendingSlot;
    private static bool _visible;
    private static Font _font;
    private static Sprite _placeholder;

    private static readonly Dictionary<string, Image> _cardBackgrounds = new Dictionary<string, Image>();
    private static readonly List<Texture2D> _previewTextures = new List<Texture2D>();

    internal static bool IsFolderBrowserOpen => _browserWindow != null;

    internal static void EnsureCanvas()
    {
        if (_canvas != null) return;

        GetFont();
        _placeholder = SpriteReplacerPlugin.LoadEmbeddedSprite("body_sprite_replacer.resources.placeholder.png");

        GameObject canvasObj = new GameObject("SpriteReplacerMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();
        UnityEngine.Object.DontDestroyOnLoad(canvasObj);
        _canvas = canvasObj;

        CreateTopButton(canvasObj.transform);
    }

    internal static void SetVisible(bool visible)
    {
        if (_canvas == null) return;
        if (_visible == visible && _canvas.activeSelf == visible) return;

        _visible = visible;

        if (!visible)
        {
            CloseFolderBrowser();
            ClosePopup();
        }

        _canvas.SetActive(visible);
    }

    internal static void RefreshSelection()
    {
        foreach (var pair in _cardBackgrounds)
        {
            if (pair.Value != null)
                pair.Value.color = CardColor(pair.Key == SpriteReplacerPlugin.ActiveSlot);
        }
    }

    private static void CreateTopButton(Transform parent)
    {
        GameObject buttonObj = new GameObject("MenuButton");
        buttonObj.transform.SetParent(parent, false);

        RectTransform rect = buttonObj.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -20f), new Vector2(64f, 64f));

        Image image = buttonObj.AddComponent<Image>();
        image.sprite = _placeholder;
        image.preserveAspect = true;

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(TogglePopup);
    }

    private static void TogglePopup()
    {
        if (_popupWindow == null)
        {
            CreatePopup();
        }
        else
        {
            _popupWindow.SetActive(!_popupWindow.activeSelf);
        }
    }

    private static void CreatePopup()
    {
        if (_canvas == null) return;

        GameObject window = new GameObject("SkinReplacerPopup");
        window.transform.SetParent(_canvas.transform, false);

        RectTransform rect = window.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(WindowWidth, WindowHeight));

        Image background = window.AddComponent<Image>();
        background.color = new Color(0.08f, 0.08f, 0.08f, 0.96f);

        Text title = CreateText(window.transform, "Title", "Skin Replacer", 26, Color.white, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -8f), new Vector2(400f, 36f));

        Text hint = CreateText(window.transform, "Hint",
            "Numpad 1-9 also changes the active slot. Switch imports a skin folder into a slot.",
            13, new Color(0.7f, 0.7f, 0.7f), TextAnchor.MiddleLeft);
        SetRect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -44f), new Vector2(700f, 22f));

        Button close = CreateButton(window.transform, "Close", "X", new Color(0.45f, 0.18f, 0.18f, 1f), 18);
        SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-12f, -10f), new Vector2(36f, 36f));
        close.onClick.AddListener(ClosePopup);

        _popupWindow = window;
        BuildGrid();
    }

    private static void BuildGrid()
    {
        if (_popupWindow == null) return;

        if (_grid != null)
        {
            _grid.SetActive(false);
            UnityEngine.Object.Destroy(_grid);
        }

        ClearPreviewTextures();
        _cardBackgrounds.Clear();

        GameObject gridObj = new GameObject("CharacterGrid");
        gridObj.transform.SetParent(_popupWindow.transform, false);

        RectTransform gridRect = gridObj.AddComponent<RectTransform>();
        SetRect(gridRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -80f), new Vector2(740f, 496f));
        _grid = gridObj;

        const int columns = 3;
        const float cellWidth = 236f;
        const float cellHeight = 152f;
        const float gap = 10f;
        float startX = -(columns * cellWidth + (columns - 1) * gap) / 2f + cellWidth / 2f;

        for (int i = 1; i <= SpriteReplacerPlugin.SlotCount; i++)
        {
            int index = i - 1;
            int row = index / columns;
            int column = index % columns;
            float x = startX + column * (cellWidth + gap);
            float y = -(row * (cellHeight + gap) + cellHeight / 2f);
            CreateCard(gridObj.transform, "st" + i, x, y, cellWidth, cellHeight);
        }

        RefreshSelection();
    }

    private static void CreateCard(Transform parent, string slot, float x, float y, float width, float height)
    {
        bool hasSkin = SpriteReplacerPlugin.CharacterFolderExists(slot);

        GameObject card = new GameObject("Card_" + slot);
        card.transform.SetParent(parent, false);

        RectTransform rect = card.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(x, y), new Vector2(width, height));

        Image background = card.AddComponent<Image>();
        background.color = CardColor(slot == SpriteReplacerPlugin.ActiveSlot);
        _cardBackgrounds[slot] = background;

        GameObject previewObj = new GameObject("Preview");
        previewObj.transform.SetParent(card.transform, false);
        RectTransform previewRect = previewObj.AddComponent<RectTransform>();
        SetRect(previewRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -4f), new Vector2(216f, 74f));

        Image preview = previewObj.AddComponent<Image>();
        preview.sprite = LoadHeadPreview(slot);
        preview.preserveAspect = true;
        preview.color = hasSkin ? Color.white : new Color(1f, 1f, 1f, 0.35f);

        Text label = CreateText(card.transform, "SlotLabel",
            hasSkin ? slot : slot + " (empty)",
            15, hasSkin ? Color.white : new Color(0.7f, 0.7f, 0.7f), TextAnchor.MiddleCenter);
        SetRect(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -84f), new Vector2(216f, 20f));

        Button select = CreateButton(card.transform, "Select", "Select", new Color(0.16f, 0.36f, 0.58f, 1f), 14);
        SetRect(select.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-56f, 8f), new Vector2(104f, 26f));
        select.interactable = hasSkin;
        select.onClick.AddListener(() => SpriteReplacerPlugin.SwitchToSlot(slot));

        Button switchButton = CreateButton(card.transform, "Switch", "Switch", new Color(0.18f, 0.45f, 0.25f, 1f), 14);
        SetRect(switchButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(56f, 8f), new Vector2(104f, 26f));
        switchButton.onClick.AddListener(() => OpenFolderBrowser(slot));
    }

    private static Color CardColor(bool active)
    {
        return active
            ? new Color(0.18f, 0.30f, 0.42f, 1f)
            : new Color(0.14f, 0.14f, 0.14f, 1f);
    }

    private static Sprite LoadHeadPreview(string slot)
    {
        string path = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot, "Head", "experimentHead.png");
        if (!File.Exists(path))
            path = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot, "Head", "experimentHeadBack.png");

        if (!File.Exists(path)) return _placeholder;

        Texture2D texture = null;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;

            if (ImageConversion.LoadImage(texture, data))
            {
                _previewTextures.Add(texture);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 8f);
                sprite.name = slot + "_preview";
                return sprite;
            }

            UnityEngine.Object.Destroy(texture);
        }
        catch (Exception ex)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            SpriteReplacerPlugin.Log.LogWarning("Could not load preview for " + slot + ": " + ex.Message);
        }

        return _placeholder;
    }

    private static void ClearPreviewTextures()
    {
        foreach (Texture2D texture in _previewTextures)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
        _previewTextures.Clear();
    }

    private static void ClosePopup()
    {
        if (_popupWindow != null)
            _popupWindow.SetActive(false);
    }

    private static void OpenFolderBrowser(string slot)
    {
        CloseFolderBrowser();
        _pendingSlot = slot;
        _browserPath = GetStartDirectory();
        CreateBrowserWindow();
        Navigate(_browserPath);
    }

    private static string GetStartDirectory()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop)) return desktop;
        return SpriteReplacerPlugin.SpritesPath;
    }

    private static void CreateBrowserWindow()
    {
        if (_canvas == null) return;

        GameObject backdrop = new GameObject("SkinBrowserBackdrop");
        backdrop.transform.SetParent(_canvas.transform, false);
        RectTransform backdropRect = backdrop.AddComponent<RectTransform>();
        SetRect(backdropRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image backdropImage = backdrop.AddComponent<Image>();
        backdropImage.color = new Color(0f, 0f, 0f, 0.65f);
        _browserBackdrop = backdrop;

        GameObject window = new GameObject("SkinBrowserWindow");
        window.transform.SetParent(_canvas.transform, false);
        RectTransform rect = window.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(760f, 560f));

        Image background = window.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.1f, 0.98f);
        _browserWindow = window;

        Text header = CreateText(window.transform, "Header",
            "Switch " + _pendingSlot + ": choose a skin folder",
            20, Color.white, TextAnchor.MiddleLeft);
        SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -12f), new Vector2(700f, 30f));

        Button up = CreateButton(window.transform, "Up", "Up", new Color(0.22f, 0.22f, 0.22f, 1f), 14);
        SetRect(up.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -50f), new Vector2(56f, 32f));
        up.onClick.AddListener(NavigateUp);

        _pathInput = CreateInputField(window.transform, "PathInput");
        SetRect(_pathInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(88f, -50f), new Vector2(500f, 32f));
        _pathInput.onEndEdit.AddListener(Navigate);

        Button go = CreateButton(window.transform, "Go", "Go", new Color(0.2f, 0.35f, 0.55f, 1f), 14);
        SetRect(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(596f, -50f), new Vector2(60f, 32f));
        go.onClick.AddListener(() => Navigate(_pathInput.text));

        Button drives = CreateButton(window.transform, "Drives", "Drives", new Color(0.22f, 0.22f, 0.22f, 1f), 14);
        SetRect(drives.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(664f, -50f), new Vector2(72f, 32f));
        drives.onClick.AddListener(ShowDrives);

        GameObject statusBox = new GameObject("StatusBox");
        statusBox.transform.SetParent(window.transform, false);
        RectTransform statusBoxRect = statusBox.AddComponent<RectTransform>();
        SetRect(statusBoxRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -88f), new Vector2(712f, 104f));
        _statusScroll = statusBox.AddComponent<ScrollRect>();

        GameObject statusViewport = new GameObject("Viewport");
        statusViewport.transform.SetParent(statusBox.transform, false);
        RectTransform statusViewportRect = statusViewport.AddComponent<RectTransform>();
        SetRect(statusViewportRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        statusViewport.AddComponent<RectMask2D>();
        Image statusViewportImage = statusViewport.AddComponent<Image>();
        statusViewportImage.color = new Color(0f, 0f, 0f, 0.01f);

        _statusText = CreateText(statusViewport.transform, "Status",
            "Choose the folder that contains Body and Head.",
            12, new Color(0.8f, 0.8f, 0.8f), TextAnchor.UpperLeft);
        _statusText.supportRichText = false;
        _statusText.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform statusTextRect = _statusText.rectTransform;
        statusTextRect.anchorMin = new Vector2(0f, 1f);
        statusTextRect.anchorMax = new Vector2(1f, 1f);
        statusTextRect.pivot = new Vector2(0.5f, 1f);
        statusTextRect.anchoredPosition = Vector2.zero;
        statusTextRect.sizeDelta = Vector2.zero;
        ContentSizeFitter statusFitter = _statusText.gameObject.AddComponent<ContentSizeFitter>();
        statusFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _statusScroll.viewport = statusViewportRect;
        _statusScroll.content = statusTextRect;
        _statusScroll.horizontal = false;
        _statusScroll.vertical = true;
        _statusScroll.movementType = ScrollRect.MovementType.Clamped;
        _statusScroll.scrollSensitivity = 20f;

        GameObject list = new GameObject("FolderList");
        list.transform.SetParent(window.transform, false);
        RectTransform listRect = list.AddComponent<RectTransform>();
        SetRect(listRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -196f), new Vector2(712f, 300f));
        _listScroll = list.AddComponent<ScrollRect>();

        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(list.transform, false);
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        SetRect(viewportRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.AddComponent<RectMask2D>();
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        _listScroll.viewport = viewportRect;

        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _listContent = content.AddComponent<RectTransform>();
        _listContent.anchorMin = new Vector2(0f, 1f);
        _listContent.anchorMax = new Vector2(1f, 1f);
        _listContent.pivot = new Vector2(0.5f, 1f);
        _listContent.anchoredPosition = Vector2.zero;
        _listContent.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _listScroll.content = _listContent;
        _listScroll.horizontal = false;
        _listScroll.vertical = true;
        _listScroll.movementType = ScrollRect.MovementType.Clamped;
        _listScroll.scrollSensitivity = 30f;

        Button use = CreateButton(window.transform, "Use", "Use this folder", new Color(0.18f, 0.45f, 0.25f, 1f), 15);
        SetRect(use.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -516f), new Vector2(220f, 32f));
        use.onClick.AddListener(UseSelectedFolder);

        Button close = CreateButton(window.transform, "Close", "Close", new Color(0.4f, 0.18f, 0.18f, 1f), 15);
        SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -516f), new Vector2(96f, 32f));
        close.onClick.AddListener(CloseFolderBrowser);
    }

    private static InputField CreateInputField(Transform parent, string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        Image background = obj.AddComponent<Image>();
        background.color = new Color(0.16f, 0.16f, 0.16f, 1f);

        InputField field = obj.AddComponent<InputField>();
        field.targetGraphic = background;
        field.lineType = InputField.LineType.SingleLine;

        Text text = CreateText(obj.transform, "Text", "", 15, Color.white, TextAnchor.MiddleLeft);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            new Vector2(8f, 0f), new Vector2(-16f, 0f));
        text.supportRichText = false;
        field.textComponent = text;

        Text placeholder = CreateText(obj.transform, "Placeholder", "Path", 15,
            new Color(0.6f, 0.6f, 0.6f), TextAnchor.MiddleLeft);
        SetRect(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            new Vector2(8f, 0f), new Vector2(-16f, 0f));
        placeholder.supportRichText = false;
        field.placeholder = placeholder;

        return field;
    }

    private static void NavigateUp()
    {
        if (string.IsNullOrEmpty(_browserPath))
        {
            ShowDrives();
            return;
        }

        DirectoryInfo parent = Directory.GetParent(_browserPath);
        if (parent == null)
        {
            ShowDrives();
            return;
        }

        Navigate(parent.FullName);
    }

    private static void ShowDrives()
    {
        ClearList();
        _browserPath = string.Empty;
        if (_pathInput != null) _pathInput.text = string.Empty;
        SetStatus("Select a drive.", new Color(0.8f, 0.8f, 0.8f));

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            SetStatus("Cannot list drives: " + ex.Message, new Color(1f, 0.45f, 0.45f));
            return;
        }

        foreach (DriveInfo drive in drives)
        {
            if (!drive.IsReady) continue;
            AddFolderButton(drive.Name, drive.RootDirectory.FullName);
        }

        if (_listScroll != null) _listScroll.verticalNormalizedPosition = 1f;
    }

    private static void Navigate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        path = path.Trim().Trim('"');

        try
        {
            if (File.Exists(path)) path = Path.GetDirectoryName(path);
            path = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            SetStatus("Invalid path: " + ex.Message, new Color(1f, 0.45f, 0.45f));
            return;
        }

        if (!Directory.Exists(path))
        {
            SetStatus("Folder not found: " + path, new Color(1f, 0.45f, 0.45f));
            return;
        }

        _browserPath = path;
        if (_pathInput != null) _pathInput.text = path;
        SetStatus("Open a subfolder, or use this folder.", new Color(0.8f, 0.8f, 0.8f));
        PopulateList();
    }

    private static void PopulateList()
    {
        ClearList();

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(_browserPath);
        }
        catch (Exception ex)
        {
            SetStatus("Cannot read this folder: " + ex.Message, new Color(1f, 0.45f, 0.45f));
            return;
        }

        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);

        int count = 0;
        foreach (string directory in directories)
        {
            if (count >= 300) break;

            string name = Path.GetFileName(directory);
            bool isSkin = Directory.Exists(Path.Combine(directory, "Body")) ||
                          Directory.Exists(Path.Combine(directory, "Head")) ||
                          Directory.Exists(Path.Combine(directory, "WoundView"));

            AddFolderButton(name + (isSkin ? "  [skin]" : string.Empty), directory);
            count++;
        }

        if (count == 0)
        {
            Text empty = CreateText(_listContent, "Empty", "No subfolders.", 14,
                new Color(0.6f, 0.6f, 0.6f), TextAnchor.MiddleLeft);
            LayoutElement element = empty.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 28f;
        }

        if (_listScroll != null) _listScroll.verticalNormalizedPosition = 1f;
    }

    private static void AddFolderButton(string label, string path)
    {
        Button button = CreateButton(_listContent, "Folder", label,
            new Color(0.16f, 0.16f, 0.16f, 1f), 14);

        LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 28f;
        element.preferredHeight = 28f;

        Text text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(8f, 0f);
            text.rectTransform.offsetMax = new Vector2(-8f, 0f);
        }

        button.onClick.AddListener(() => Navigate(path));
    }

    private static void ClearList()
    {
        if (_listContent == null) return;

        for (int i = _listContent.childCount - 1; i >= 0; i--)
        {
            GameObject child = _listContent.GetChild(i).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }

    private static void UseSelectedFolder()
    {
        if (string.IsNullOrEmpty(_pendingSlot)) return;

        if (string.IsNullOrEmpty(_browserPath))
        {
            SetStatus("Select a folder first.", new Color(1f, 0.45f, 0.45f));
            return;
        }

        int movedHead;
        string prepareError;
        if (!SkinImporter.PrepareSkinFolder(_browserPath, _pendingSlot, out movedHead, out prepareError))
        {
            SetStatus(prepareError, new Color(1f, 0.45f, 0.45f));
            return;
        }

        SkinCheckResult check = SkinImporter.Check(_browserPath, _pendingSlot);
        if (!check.Ok)
        {
            SetStatus(string.Join("\n", check.Errors.Take(3)), new Color(1f, 0.45f, 0.45f));
            return;
        }

        string backup;
        string error;
        if (!SkinImporter.Import(_browserPath, _pendingSlot, out backup, out error))
        {
            SetStatus("Import failed: " + error, new Color(1f, 0.45f, 0.45f));
            return;
        }

        string message = "";
        if (movedHead > 0) message += "Moved " + movedHead + " Head sprites into Head.\n";
        message += "Imported " + check.TotalCount + " sprites into " + _pendingSlot + ".";
        Color messageColor = new Color(0.55f, 1f, 0.55f);

        if (check.Warnings.Count > 0)
        {
            var lines = new List<string>();
            lines.Add("Warnings (" + check.Warnings.Count + "):");
            foreach (string warning in check.Warnings) lines.Add("Warning: " + warning);
            message += "\n" + string.Join("\n", lines);
            messageColor = new Color(1f, 0.85f, 0.4f);
        }

        if (!string.IsNullOrEmpty(backup)) SpriteReplacerPlugin.Log.LogInfo("Backup: " + backup);

        SetStatus(message, messageColor);
        SpriteReplacerPlugin.SwitchToSlot(_pendingSlot, true);
        BuildGrid();
    }

    private static void SetStatus(string text, Color color)
    {
        if (_statusText == null) return;
        _statusText.text = text;
        _statusText.color = color;

        if (_statusScroll != null)
        {
            _statusText.rectTransform.anchoredPosition = Vector2.zero;
            _statusScroll.verticalNormalizedPosition = 1f;
        }
    }

    internal static void CloseFolderBrowser()
    {
        if (_browserWindow != null) UnityEngine.Object.Destroy(_browserWindow);
        if (_browserBackdrop != null) UnityEngine.Object.Destroy(_browserBackdrop);

        _browserWindow = null;
        _browserBackdrop = null;
        _browserPath = null;
        _pendingSlot = null;
        _pathInput = null;
        _listContent = null;
        _listScroll = null;
        _statusText = null;
        _statusScroll = null;
    }

    private static Text CreateText(Transform parent, string name, string text, int size, Color color, TextAnchor anchor)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        Text label = obj.AddComponent<Text>();
        label.font = GetFont();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = anchor;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color color, int fontSize)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        Image image = obj.AddComponent<Image>();
        image.color = color;

        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        button.colors = colors;

        Text text = CreateText(obj.transform, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        text.rectTransform.offsetMin = new Vector2(4f, 0f);
        text.rectTransform.offsetMax = new Vector2(-4f, 0f);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        return button;
    }

    private static Font GetFont()
    {
        if (_font == null)
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return _font;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }
}
