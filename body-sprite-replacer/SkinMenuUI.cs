using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
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
    private static TMP_InputField _pathInput;
    private static RectTransform _listContent;
    private static ScrollRect _listScroll;
    private static TextMeshProUGUI _statusText;
    private static ScrollRect _statusScroll;
    private static string _browserPath;
    private static string _pendingSlot;
    private static bool _visible;
    private static Sprite _placeholder;

    private static readonly Dictionary<string, Image> _cardBackgrounds = new Dictionary<string, Image>();
    private static readonly Dictionary<string, Outline> _cardOutlines = new Dictionary<string, Outline>();
    private static readonly List<Texture2D> _previewTextures = new List<Texture2D>();

    internal static bool IsFolderBrowserOpen => _browserWindow != null;

    internal static void EnsureCanvas()
    {
        if (_canvas != null) return;

        GameTheme.Resolve();
        _placeholder = SpriteReplacerPlugin.LoadEmbeddedSprite("body_sprite_replacer.resources.placeholder.png");

        GameObject canvasObj = new GameObject("SpriteReplacerMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = GameTheme.ReferenceResolution;
        scaler.matchWidthOrHeight = GameTheme.MatchWidthOrHeight;

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
            bool active = pair.Key == SpriteReplacerPlugin.ActiveSlot;

            if (pair.Value != null)
                pair.Value.color = active ? GameTheme.ActiveCardColor : GameTheme.CardColor;

            if (_cardOutlines.TryGetValue(pair.Key, out Outline outline) && outline != null)
                outline.effectColor = active ? GameTheme.AccentColor : GameTheme.BorderColor;
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
        GameTheme.StylePanel(background);

        GameObject bar = new GameObject("HeaderBar");
        bar.transform.SetParent(window.transform, false);
        RectTransform barRect = bar.AddComponent<RectTransform>();
        SetRect(barRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 60f));
        bar.AddComponent<Image>().color = GameTheme.HeaderColor;

        TextMeshProUGUI title = CreateText(bar.transform, "Title", "Skin Replacer", 30,
            GameTheme.TextColor, TextAlignmentOptions.MidlineLeft);
        SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(24f, 0f), new Vector2(500f, 0f));

        Button close = CreateButton(bar.transform, "Close", "X", GameTheme.DangerColor, 20);
        SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-14f, 0f), new Vector2(40f, 40f));
        close.onClick.AddListener(ClosePopup);

        TextMeshProUGUI hint = CreateText(window.transform, "Hint",
            "Numpad 1-9 also changes the active slot. Switch imports a skin folder into a slot.",
            16, GameTheme.MutedColor, TextAlignmentOptions.MidlineLeft);
        SetRect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -66f), new Vector2(700f, 24f));

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
        _cardOutlines.Clear();

        GameObject gridObj = new GameObject("CharacterGrid");
        gridObj.transform.SetParent(_popupWindow.transform, false);

        RectTransform gridRect = gridObj.AddComponent<RectTransform>();
        SetRect(gridRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -96f), new Vector2(740f, 476f));
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
            CreateCard(gridObj.transform, SlotFolders.SlotId(i), x, y, cellWidth, cellHeight);
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

        bool active = slot == SpriteReplacerPlugin.ActiveSlot;

        Image background = card.AddComponent<Image>();
        background.color = active ? GameTheme.ActiveCardColor : GameTheme.CardColor;
        _cardBackgrounds[slot] = background;

        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = active ? GameTheme.AccentColor : GameTheme.BorderColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = false;
        _cardOutlines[slot] = outline;

        GameObject previewObj = new GameObject("Preview");
        previewObj.transform.SetParent(card.transform, false);
        RectTransform previewRect = previewObj.AddComponent<RectTransform>();
        SetRect(previewRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -4f), new Vector2(216f, 74f));

        Image preview = previewObj.AddComponent<Image>();
        preview.sprite = LoadHeadPreview(slot);
        preview.preserveAspect = true;
        preview.color = hasSkin ? Color.white : new Color(1f, 1f, 1f, 0.35f);

        TextMeshProUGUI label = CreateText(card.transform, "SlotLabel",
            hasSkin ? SlotFolders.DisplayName(slot) : SlotFolders.DisplayName(slot) + " (empty)",
            18, hasSkin ? GameTheme.TextColor : GameTheme.MutedColor, TextAlignmentOptions.Center);
        SetRect(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -82f), new Vector2(216f, 24f));

        Button select = CreateButton(card.transform, "Select", "Select", GameTheme.TextColor, 16);
        SetRect(select.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-56f, 8f), new Vector2(104f, 28f));
        select.interactable = hasSkin;
        select.onClick.AddListener(() => SpriteReplacerPlugin.SwitchToSlot(slot));

        Button switchButton = CreateButton(card.transform, "Switch", "Switch", GameTheme.SuccessColor, 16);
        SetRect(switchButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(56f, 8f), new Vector2(104f, 28f));
        switchButton.onClick.AddListener(() => OpenFolderBrowser(slot));
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
        backdropImage.color = new Color(0f, 0f, 0f, 0.7f);
        _browserBackdrop = backdrop;

        GameObject window = new GameObject("SkinBrowserWindow");
        window.transform.SetParent(_canvas.transform, false);
        RectTransform rect = window.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(760f, 560f));

        Image background = window.AddComponent<Image>();
        GameTheme.StylePanel(background);
        _browserWindow = window;

        GameObject bar = new GameObject("HeaderBar");
        bar.transform.SetParent(window.transform, false);
        RectTransform barRect = bar.AddComponent<RectTransform>();
        SetRect(barRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 48f));
        bar.AddComponent<Image>().color = GameTheme.HeaderColor;

        TextMeshProUGUI header = CreateText(bar.transform, "Header",
            "Switch " + SlotFolders.DisplayName(_pendingSlot) + ": choose a skin folder",
            24, GameTheme.TextColor, TextAlignmentOptions.MidlineLeft);
        SetRect(header.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(-48f, 0f));

        Button up = CreateButton(window.transform, "Up", "Up", GameTheme.TextColor, 16);
        SetRect(up.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -56f), new Vector2(56f, 32f));
        up.onClick.AddListener(NavigateUp);

        _pathInput = CreateInputField(window.transform, "PathInput");
        SetRect(_pathInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(88f, -56f), new Vector2(500f, 32f));
        _pathInput.onEndEdit.AddListener(Navigate);

        Button go = CreateButton(window.transform, "Go", "Go", GameTheme.TextColor, 16);
        SetRect(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(596f, -56f), new Vector2(60f, 32f));
        go.onClick.AddListener(() => Navigate(_pathInput.text));

        Button drives = CreateButton(window.transform, "Drives", "Drives", GameTheme.TextColor, 16);
        SetRect(drives.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(664f, -56f), new Vector2(72f, 32f));
        drives.onClick.AddListener(ShowDrives);

        GameObject statusBox = new GameObject("StatusBox");
        statusBox.transform.SetParent(window.transform, false);
        RectTransform statusBoxRect = statusBox.AddComponent<RectTransform>();
        SetRect(statusBoxRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -96f), new Vector2(712f, 100f));
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
            14, GameTheme.MutedColor, TextAlignmentOptions.TopLeft);
        _statusText.richText = false;
        _statusText.overflowMode = TextOverflowModes.Overflow;
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
            new Vector2(24f, -200f), new Vector2(712f, 296f));
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

        Button use = CreateButton(window.transform, "Use", "Use this folder", GameTheme.SuccessColor, 16);
        SetRect(use.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -516f), new Vector2(220f, 32f));
        use.onClick.AddListener(UseSelectedFolder);

        Button close = CreateButton(window.transform, "Close", "Close", GameTheme.DangerColor, 16);
        SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -516f), new Vector2(96f, 32f));
        close.onClick.AddListener(CloseFolderBrowser);
    }

    private static TMP_InputField CreateInputField(Transform parent, string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        obj.SetActive(false);

        Image background = obj.AddComponent<Image>();
        background.color = GameTheme.InputColor;

        TMP_InputField field = obj.AddComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.lineType = TMP_InputField.LineType.SingleLine;

        GameObject viewport = new GameObject("TextArea");
        viewport.transform.SetParent(obj.transform, false);
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        SetRect(viewportRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            new Vector2(8f, 0f), new Vector2(-16f, 0f));
        viewport.AddComponent<RectMask2D>();

        TextMeshProUGUI text = CreateText(viewport.transform, "Text", "", 16,
            GameTheme.TextColor, TextAlignmentOptions.MidlineLeft);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI placeholder = CreateText(viewport.transform, "Placeholder", "Path", 16,
            GameTheme.MutedColor, TextAlignmentOptions.MidlineLeft);
        SetRect(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        placeholder.enableWordWrapping = false;

        field.textViewport = viewportRect;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.caretWidth = 2;

        obj.SetActive(true);
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
        SetStatus("Select a drive.", GameTheme.MutedColor);

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            SetStatus("Cannot list drives: " + ex.Message, GameTheme.DangerColor);
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
            SetStatus("Invalid path: " + ex.Message, GameTheme.DangerColor);
            return;
        }

        if (!Directory.Exists(path))
        {
            SetStatus("Folder not found: " + path, GameTheme.DangerColor);
            return;
        }

        _browserPath = path;
        if (_pathInput != null) _pathInput.text = path;
        SetStatus("Open a subfolder, or use this folder.", GameTheme.MutedColor);
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
            SetStatus("Cannot read this folder: " + ex.Message, GameTheme.DangerColor);
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
            TextMeshProUGUI empty = CreateText(_listContent, "Empty", "No subfolders.", 16,
                GameTheme.MutedColor, TextAlignmentOptions.MidlineLeft);
            LayoutElement element = empty.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 28f;
        }

        if (_listScroll != null) _listScroll.verticalNormalizedPosition = 1f;
    }

    private static void AddFolderButton(string label, string path)
    {
        Button button = CreateButton(_listContent, "Folder", label, GameTheme.TextColor, 16);

        LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 28f;
        element.preferredHeight = 28f;

        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.alignment = TextAlignmentOptions.MidlineLeft;
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
            SetStatus("Select a folder first.", GameTheme.DangerColor);
            return;
        }

        int movedHead;
        string prepareError;
        if (!SkinImporter.PrepareSkinFolder(_browserPath, _pendingSlot, out movedHead, out prepareError))
        {
            SetStatus(prepareError, GameTheme.DangerColor);
            return;
        }

        SkinCheckResult check = SkinImporter.Check(_browserPath, _pendingSlot);
        if (!check.Ok)
        {
            SetStatus(string.Join("\n", check.Errors.Take(3)), GameTheme.DangerColor);
            return;
        }

        string backup;
        string error;
        if (!SkinImporter.Import(_browserPath, _pendingSlot, out backup, out error))
        {
            SetStatus("Import failed: " + error, GameTheme.DangerColor);
            return;
        }

        string message = "";
        if (movedHead > 0) message += "Moved " + movedHead + " Head sprites into Head.\n";
        message += "Imported " + check.TotalCount + " sprites into " + SlotFolders.DisplayName(_pendingSlot) + ".";
        Color messageColor = GameTheme.SuccessColor;

        if (check.Warnings.Count > 0)
        {
            var lines = new List<string>();
            lines.Add("Warnings (" + check.Warnings.Count + "):");
            foreach (string warning in check.Warnings) lines.Add("Warning: " + warning);
            message += "\n" + string.Join("\n", lines);
            messageColor = GameTheme.WarningColor;
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

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, int size, Color color,
        TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
        label.font = GameTheme.Font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color labelColor, int fontSize)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        Image image = obj.AddComponent<Image>();
        GameTheme.StyleButton(image);

        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = GameTheme.ButtonColors;

        TextMeshProUGUI text = CreateText(obj.transform, "Label", label, fontSize, labelColor,
            TextAlignmentOptions.Center);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        text.rectTransform.offsetMin = new Vector2(4f, 0f);
        text.rectTransform.offsetMax = new Vector2(-4f, 0f);
        text.enableWordWrapping = false;

        return button;
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
