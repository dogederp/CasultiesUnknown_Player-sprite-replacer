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

[BepInPlugin("com.yourname.spritereplacer", "Sprite Replacer", "2.2.0")]
[BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
public class SpriteReplacerPlugin : BaseUnityPlugin
{
    public const int SlotCount = 9;
    public const int DefaultBackupCount = 5;
    public const int MaxBackupCount = 50;

    // The folder names are exact. The page names are for the menu.
    internal static readonly string[] SpeciesNames = { "Exp", "Milky", "Dune" };
    internal static readonly string[] SpeciesPageNames = { "Expie", "Milky", "Dune" };

    public static string SpritesPath => Path.Combine(Paths.PluginPath, "CustomSprites");
    public static Dictionary<string, Sprite> LoadedSprites = new Dictionary<string, Sprite>();
    internal static readonly HashSet<string> ColoredWoundSprites = new HashSet<string>();
    internal static ManualLogSource Log;

    private static string _activeSlot = SlotFolders.DefaultFolder;
    // Species the loaded folder came from. Slot names repeat on each page.
    private static int _appliedSpecies = -1;
    // Last folder that failed to load, so a missing slot is not retried every frame.
    private static string _syncFailed;
    private static int _syncFailedSpecies = -1;
    public static string ActiveSlot => _activeSlot;

    // The local body decides the page. No body counts as Experiment.
    internal static int ActiveSpecies
    {
        get
        {
            PlayerCamera camera = PlayerCamera.main;
            if (camera == null || camera.body == null) return 0;

            int charType = camera.body.charType;
            return charType >= 0 && charType <= 2 ? charType : 0;
        }
    }

    internal static string SpeciesRoot(int species)
    {
        if (species < 0 || species >= SpeciesNames.Length) species = 0;
        return Path.Combine(SpritesPath, SpeciesNames[species]);
    }

    internal static string SlotDirectory(string slot)
    {
        return SlotDirectory(slot, ActiveSpecies);
    }

    internal static string SlotDirectory(string slot, int species)
    {
        return Path.Combine(SpeciesRoot(species), slot);
    }

    internal static string BackupRoot => BackupRootFor(ActiveSpecies);

    internal static string BackupRootFor(int species)
    {
        return Path.Combine(SpeciesRoot(species), "backups");
    }

    internal static string FolderForSpecies(int species)
    {
        ConfigEntry<string> setting = SpeciesSlotSetting(species);
        string slot = setting == null ? null : SlotFolders.Normalize(setting.Value);
        return slot ?? SlotFolders.DefaultFolder;
    }

    internal static bool IsDefaultSlot(string slot)
    {
        return SlotFolders.IsDefault(slot);
    }

    private static ConfigEntry<string> SpeciesSlotSetting(int species)
    {
        if (species == 1) return _milkySlotSetting;
        if (species == 2) return _duneSlotSetting;
        return _expSlotSetting;
    }

    private static ConfigEntry<string> _activeSlotSetting;
    private static ConfigEntry<string> _expSlotSetting;
    private static ConfigEntry<string> _milkySlotSetting;
    private static ConfigEntry<string> _duneSlotSetting;
    private static ConfigEntry<int> _layoutVersionSetting;
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
        { KeyCode.Keypad0, SlotFolders.DefaultFolder },
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

        _activeSlotSetting = Config.Bind("General", "ActiveSlot", SlotFolders.DefaultFolder,
            "Last folder applied. The per-species settings win over this value.");

        _expSlotSetting = Config.Bind("General", "ExpSlot", SlotFolders.DefaultFolder,
            "The Exp folder to use. Values: default, slot1 to slot9.");

        _milkySlotSetting = Config.Bind("General", "MilkySlot", SlotFolders.DefaultFolder,
            "The Milky folder to use. Values: default, slot1 to slot9.");

        _duneSlotSetting = Config.Bind("General", "DuneSlot", SlotFolders.DefaultFolder,
            "The Dune folder to use. Values: default, slot1 to slot9.");

        _layoutVersionSetting = Config.Bind("General", "LayoutVersion", 0,
            "Internal layout version. Do not change this value.");

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

        if (_layoutVersionSetting.Value < 1)
        {
            MigrateLegacyIntoExp();
            string oldSlot = SlotFolders.Normalize(_activeSlotSetting.Value)
                             ?? SlotFolders.DefaultFolder;
            _expSlotSetting.Value = oldSlot;
            SaveSetting(_expSlotSetting);
            _layoutVersionSetting.Value = 1;
            SaveSetting(_layoutVersionSetting);
        }

        EnsureCustomSpritesStructure();
        if (rootCreated)
            Log.LogInfo("Created CustomSprites with Exp, Milky, and Dune slot folders.");

        _activeSlot = FolderForSpecies(0);
        if (!SlotFolders.IsDefault(_activeSlot) && !CharacterFolderExists(_activeSlot, 0))
        {
            // The remembered Exp slot is gone. Start on the stock folder.
            _activeSlot = SlotFolders.DefaultFolder;
            _expSlotSetting.Value = _activeSlot;
            SaveSetting(_expSlotSetting);
        }

        _appliedSpecies = 0;
        LoadCharacterSprites(_activeSlot, 0);
        SaveActiveSlot(_activeSlot);

        var harmony = new Harmony("com.yourname.spritereplacer");
        PatchLoader.Apply(harmony);

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        var listenerObj = new GameObject("SpriteReplacerInputListener");
        DontDestroyOnLoad(listenerObj);
        listenerObj.hideFlags = HideFlags.HideAndDontSave;
        listenerObj.AddComponent<InputListener>();

        Log.LogInfo("Sprite Replacer loaded! Numpad 0 = default, 1-9 = slots.");
    }

    private static void SaveSetting<T>(ConfigEntry<T> setting)
    {
        if (setting != null && setting.ConfigFile != null)
            setting.ConfigFile.Save();
    }

    private void Start()
    {
        CoreLibIntegration.TryRegister();
        BuildInfo.Log(Log);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log.LogInfo($"Scene loaded: '{scene.name}' (mode: {mode})");
        SkinMenuUI.EnsureCanvas();
        SkinMenuUI.SetVisible(false);
        KeybindRecovery.Apply();
        SyncSpeciesCache();
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
                SyncSpeciesCache();
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

    public static bool SwitchToSlot(string slot, bool forceReload = false, bool remember = true)
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

        int species = ActiveSpecies;
        if (!forceReload && slot == _activeSlot && _appliedSpecies == species)
        {
            if (remember) RememberSpeciesSlot(species, slot);
            SkinMenuUI.RefreshSelection();
            return true;
        }

        if (!CharacterFolderExists(slot, species))
        {
            Log.LogWarning("Character folder for " + SpeciesNames[species] + "/" + slot + " not found");
            return false;
        }

        _activeSlot = slot;
        _appliedSpecies = species;
        _syncFailed = null;
        _syncFailedSpecies = -1;
        SaveActiveSlot(slot);
        CoreLibIntegration.StoreActiveSlot(slot);
        LoadCharacterSprites(slot, species);
        ApplyToAll();
        MpSkinSync.OnLocalSkinChanged();
        SkinMenuUI.RefreshSelection();
        if (remember) RememberSpeciesSlot(species, slot);
        Log.LogInfo("Switched to " + SpeciesNames[species] + "/" + slot);
        return true;
    }

    // Loads the saved folder for the local body's species. No body uses Exp.
    internal static void SyncSpeciesCache()
    {
        if (_expSlotSetting == null) return;

        int species = ActiveSpecies;
        string want = FolderForSpecies(species);
        if (string.Equals(want, _activeSlot, StringComparison.Ordinal) && _appliedSpecies == species)
            return;

        // Do not retry a folder that failed for this species. A new import or a
        // config change clears the guard.
        if (string.Equals(want, _syncFailed, StringComparison.Ordinal) && _syncFailedSpecies == species)
            return;

        if (SwitchToSlot(want, true, false))
        {
            _syncFailed = null;
            _syncFailedSpecies = -1;
            return;
        }

        _syncFailed = want;
        _syncFailedSpecies = species;

        if (!SlotFolders.IsDefault(want) && SwitchToSlot(SlotFolders.DefaultFolder, true, false))
        {
            _syncFailed = null;
            _syncFailedSpecies = -1;
        }
    }

    internal static void RememberSpeciesSlot(int species, string slot)
    {
        ConfigEntry<string> setting = SpeciesSlotSetting(species);
        if (setting == null) return;

        string normalized = SlotFolders.Normalize(slot);
        if (normalized == null) return;

        // An import can fill the same remembered slot. Allow a new load attempt.
        _syncFailed = null;
        _syncFailedSpecies = -1;

        if (setting.Value == normalized) return;

        setting.Value = normalized;
        SaveSetting(setting);
    }

    internal static void ClearSpeciesSyncFailure()
    {
        _syncFailed = null;
        _syncFailedSpecies = -1;
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
        return CharacterFolderExists(character, ActiveSpecies);
    }

    internal static bool CharacterFolderExists(string character, int species)
    {
        string normalized = SlotFolders.Normalize(character);
        if (normalized == null) return false;

        // The default folder is the stock art. An empty folder still counts.
        if (SlotFolders.IsDefault(normalized)) return true;

        string basePath = SlotDirectory(normalized, species);
        string bodyPath = Path.Combine(basePath, "Body");
        string headPath = Path.Combine(basePath, "Head");
        // Empty prepared dirs don't count as an available character; need actual png sprites.
        return (Directory.Exists(bodyPath) && Directory.GetFiles(bodyPath, "*.png").Length > 0) ||
               (Directory.Exists(headPath) && Directory.GetFiles(headPath, "*.png").Length > 0);
    }

    // Older builds kept slot1..slot9 and default next to CustomSprites.
    // Move them into Exp one time. The root backups folder stays as legacy.
    private static void MigrateLegacyIntoExp()
    {
        string exp = Path.Combine(SpritesPath, SpeciesNames[0]);
        Directory.CreateDirectory(exp);

        var names = new List<string> { SlotFolders.DefaultFolder, "slot0", "st0" };
        for (int i = 1; i <= SlotCount; i++)
        {
            names.Add(SlotFolders.SlotId(i));
            names.Add(SlotFolders.LegacySlotId(i));
        }

        foreach (string name in names)
        {
            string source = Path.Combine(SpritesPath, name);
            string target = Path.Combine(exp, name);
            if (!Directory.Exists(source) || Directory.Exists(target)) continue;

            try
            {
                Directory.Move(source, target);
                Log.LogInfo("Moved CustomSprites/" + name + " to Exp.");
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not move " + source + " to Exp: " + ex.Message);
            }
        }
    }

    private static void EnsureCustomSpritesStructure()
    {
        for (int species = 0; species < SpeciesNames.Length; species++)
        {
            string root = SpeciesRoot(species);
            Directory.CreateDirectory(root);

            foreach (string message in SlotFolders.Migrate(root, SlotCount))
                Log.LogInfo(message);

            string fallback = Path.Combine(root, SlotFolders.DefaultFolder);
            Directory.CreateDirectory(Path.Combine(fallback, "Body"));
            Directory.CreateDirectory(Path.Combine(fallback, "Head"));
            Directory.CreateDirectory(Path.Combine(fallback, "WoundView"));
            MergeLegacySlotZeroIntoDefault(root, fallback);

            for (int i = 1; i <= SlotCount; i++)
            {
                string slot = Path.Combine(root, SlotFolders.SlotId(i));
                Directory.CreateDirectory(Path.Combine(slot, "Body"));
                Directory.CreateDirectory(Path.Combine(slot, "Head"));
            }
        }
    }

    // slot0 / st0 are not custom slots. Fold leftover art into default.
    private static void MergeLegacySlotZeroIntoDefault(string speciesRoot, string defaultRoot)
    {
        foreach (string legacyName in new[] { "slot0", "st0" })
        {
            string legacy = Path.Combine(speciesRoot, legacyName);
            if (!Directory.Exists(legacy)) continue;

            try
            {
                foreach (string subfolder in new[] { "Body", "Head", "WoundView" })
                {
                    string source = Path.Combine(legacy, subfolder);
                    if (!Directory.Exists(source)) continue;

                    string target = Path.Combine(defaultRoot, subfolder);
                    Directory.CreateDirectory(target);
                    foreach (string file in Directory.GetFiles(source))
                    {
                        string destination = Path.Combine(target, Path.GetFileName(file));
                        if (!File.Exists(destination))
                            File.Copy(file, destination, false);
                    }
                }

                Directory.Delete(legacy, true);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not merge " + legacy + " into default: " + ex.Message);
            }
        }
    }

    private static void LoadCharacterSprites(string character, int species)
    {
        LoadedSprites.Clear();
        ColoredWoundSprites.Clear();

        string root = SlotDirectory(character, species);
        LoadSpritesFromFolder(root, "Body");
        LoadSpritesFromFolder(root, "Head");
        LoadSpritesFromFolder(root, "WoundView");

        // Species slots often keep experiment* file names. Map them onto the
        // live names, so one skin set works on a species body.
        AddSpeciesAliases(LoadedSprites, species);

        Log.LogInfo($"Loaded {LoadedSprites.Count} sprites for {SpeciesNames[species]}/{character}");
    }

    internal static IReadOnlyDictionary<string, Sprite> WithSpeciesAliases(
        IReadOnlyDictionary<string, Sprite> sprites, int species)
    {
        if (sprites == null || species < 1 || species > 2) return sprites;

        var copy = new Dictionary<string, Sprite>(sprites.Count + 32);
        foreach (KeyValuePair<string, Sprite> pair in sprites)
            copy[pair.Key] = pair.Value;

        AddSpeciesAliases(copy, species);
        return copy;
    }

    internal static void AddSpeciesAliases(IDictionary<string, Sprite> sprites, int species)
    {
        if (sprites == null) return;

        if (species == 1)
        {
            Alias(sprites, "milkycrus", "experimentCrus");
            Alias(sprites, "mildownarm", "experimentDownArm");
            Alias(sprites, "milkydowntorso", "experimentDownTorso");
            Alias(sprites, "milkyfoot", "experimentFoot");
            Alias(sprites, "milkyhandb", "experimentHandB");
            Alias(sprites, "milkyhandf", "experimentHandF");
            Alias(sprites, "milkytail", "experimentTail");
            Alias(sprites, "milkythigh", "experimentThigh");
            Alias(sprites, "milkyuparm", "experimentUpArm");
            Alias(sprites, "milkytorso", "experimentUpTorso");
            Alias(sprites, "milkyeyeclosed", "experimentEyeClosed");
            Alias(sprites, "milkyeyegone", "experimentEyeGone");
            Alias(sprites, "milkyeyegonehealed", "experimentEyeGoneHealed");
            Alias(sprites, "milkyeyehalfclosed", "experimentEyeHalfClosed");
            Alias(sprites, "milkyeyehalfclosedback", "experimentEyeHalfClosedBack");
            Alias(sprites, "milkyeyehappy", "experimentEyeHappy");
            Alias(sprites, "milkyeyehappyback", "experimentEyeLookBack");
            Alias(sprites, "milkyeyelockedin", "experimentEyeLockedIn");
            Alias(sprites, "milkyeyelockedinback", "experimentEyeLockedInBack");
            Alias(sprites, "milkyeyeopen", "experimentEyeOpen");
            Alias(sprites, "milkyeyeopenback", "experimentEyeLookBack");
            Alias(sprites, "milkyeyepanic", "experimentEyePanic");
            Alias(sprites, "milkyeyesad", "experimentEyeSad");
            Alias(sprites, "milkyeyesadback", "experimentEyeSadBack");
            Alias(sprites, "milkyeyescared", "experimentEyeScared");
            Alias(sprites, "milkyeyescaredback", "experimentEyeScaredBack");
            Alias(sprites, "milkyheadback", "experimentHeadBack");
            Alias(sprites, "milkyheadbackmouthopen", "experimentHeadBackMouth");
            Alias(sprites, "milkyheadbackmouthhalfopen", "experimentHeadBackMouthMini");
            Alias(sprites, "milkydisfigure1", "experimentHeadDisfigured1");
            Alias(sprites, "milkydisfigure1healed", "experimentHeadDisfigured1Healed");
            Alias(sprites, "milkydisfigure2", "experimentHeadDisfigured2");
            Alias(sprites, "milkydisfigure2healed", "experimentHeadDisfigured2Healed");
            Alias(sprites, "milkheadmiddle", "headMiddle");
            Alias(sprites, "milkheadouter", "headoutline");
            return;
        }

        if (species != 2) return;

        Alias(sprites, "duneCrus", "experimentCrus");
        Alias(sprites, "duneDownArm", "experimentDownArm");
        Alias(sprites, "duneDownArmAlt", "experimentDownArm");
        Alias(sprites, "duneDownTorso", "experimentDownTorso");
        Alias(sprites, "duneFoot", "experimentFoot");
        Alias(sprites, "duneHand", "experimentHandF");
        Alias(sprites, "duneHandAlt", "experimentHandB");
        Alias(sprites, "duneTail", "experimentTail");
        Alias(sprites, "duneThigh", "experimentThigh");
        Alias(sprites, "duneThighAlt", "experimentThigh");
        Alias(sprites, "duneUpArm", "experimentUpArm");
        Alias(sprites, "duneUpArmAlt", "experimentUpArm");
        Alias(sprites, "duneUpTorso", "experimentUpTorso");
        Alias(sprites, "duneEyesClosed", "experimentEyeClosed");
        Alias(sprites, "duneEyeGone", "experimentEyeGone");
        Alias(sprites, "duneEyeGoneHealed", "experimentEyeGoneHealed");
        Alias(sprites, "duneEyesHalfClosed", "experimentEyeHalfClosed");
        Alias(sprites, "duneEyeHappy", "experimentEyeHappy");
        Alias(sprites, "duneeyelockedin", "experimentEyeLockedIn");
        Alias(sprites, "duneEyes", "experimentEyeOpen");
        Alias(sprites, "duneEyePanic", "experimentEyePanic");
        Alias(sprites, "duneEyeSad", "experimentEyeSad");
        Alias(sprites, "duneEyeScared", "experimentEyeScared");
        Alias(sprites, "duneHeadBack", "experimentHeadBack");
        Alias(sprites, "duneHeadMouth", "experimentHeadBackMouth");
        Alias(sprites, "duneHeadMouthHalf", "experimentHeadBackMouthMini");
        Alias(sprites, "duneDisfigured", "experimentHeadDisfigured1");
        Alias(sprites, "duneDisfiguredHealed", "experimentHeadDisfigured1Healed");
        Alias(sprites, "headMiddleDune", "headMiddle");
        Alias(sprites, "headOuterDune", "headoutline");
    }

    private static void Alias(IDictionary<string, Sprite> sprites, string liveName, string sourceName)
    {
        AliasOne(sprites, liveName, sourceName);
        AliasOne(sprites, liveName + SideSprites.LeftSuffix, sourceName + SideSprites.LeftSuffix);
        AliasOne(sprites, liveName + SideSprites.RightSuffix, sourceName + SideSprites.RightSuffix);
    }

    private static void AliasOne(IDictionary<string, Sprite> sprites, string liveName, string sourceName)
    {
        if (sprites == null || string.IsNullOrEmpty(liveName) || string.IsNullOrEmpty(sourceName))
            return;
        if (sprites.ContainsKey(liveName)) return;
        if (!sprites.TryGetValue(sourceName, out Sprite sprite) || sprite == null) return;

        sprites[liveName] = sprite;
    }

    private static void LoadSpritesFromFolder(string root, string subfolder)
    {
        string path = Path.Combine(root, subfolder);
        if (!Directory.Exists(path)) return;

        foreach (string file in Directory.GetFiles(path, "*.png"))
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            string spriteName = fileName;
            string key = fileName;

            // Only the body and the head use the side suffix. The wound view
            // keeps its plain names.
            bool sideFolder = subfolder == "Body" || subfolder == "Head";
            if (sideFolder && SideSprites.TryParse(fileName, out string baseName, out bool isRight))
            {
                spriteName = baseName;
                key = SideSprites.Key(baseName, isRight);
            }

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
            LoadedSprites[key] = sprite;

            if (subfolder == "WoundView" && HasColorPixels(tex))
                ColoredWoundSprites.Add(spriteName);
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

    // Reads the side sprite for the current facing. A missing side file falls
    // back to the shared file. The returned sprite keeps the base name, so the
    // next direction change can find the other side.
    internal static bool TryGetSprite(IReadOnlyDictionary<string, Sprite> sprites, string name,
        Body body, out Sprite sprite, out bool side)
    {
        sprite = null;
        side = false;
        if (sprites == null || string.IsNullOrEmpty(name)) return false;

        if (body != null && sprites.TryGetValue(SideSprites.Key(name, body.isRight), out sprite))
        {
            side = true;
            return true;
        }

        return sprites.TryGetValue(name, out sprite);
    }

    // The game mirrors the body with a negative X scale. A side sprite is
    // drawn as it appears on screen, so flip it in local space to cancel the
    // body mirror.
    internal static bool IsMirrored(Body body)
    {
        return body != null && body.transform.lossyScale.x < 0f;
    }

    internal static void ApplySideFlip(SpriteRenderer renderer, bool side, Body body)
    {
        if (renderer == null) return;
        bool flip = side && IsMirrored(body);
        if (renderer.flipX != flip) renderer.flipX = flip;
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
        }
        else
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

        WoundViewIntegration.ApplyAll();
        DefaultSpriteExport.ExportNew();
    }

    public static void ApplyReplacement(Limb limb)
    {
        if (limb == null) return;
        Body body = limb.body;
        EnsureDirectionWatcher(body);
        if (!MpBody.CanApply(body)) return;

        SpriteRenderer sr = limb.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            DefaultSpriteExport.Capture(sr.sprite);

            if (TryGetSprite(LoadedSprites, sr.sprite.name, body, out Sprite replacement, out bool side))
                sr.sprite = replacement;

            ApplySideFlip(sr, side, body);
        }
    }

    public static void ApplyReplacementToTail(TailScript tail)
    {
        if (tail == null) return;
        Body body = tail.GetComponentInParent<Body>();
        EnsureDirectionWatcher(body);
        if (!MpBody.CanApply(body)) return;

        SpriteRenderer sr = tail.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            DefaultSpriteExport.Capture(sr.sprite);

            if (TryGetSprite(LoadedSprites, sr.sprite.name, body, out Sprite replacement, out bool side))
                sr.sprite = replacement;

            ApplySideFlip(sr, side, body);
        }
    }

    public static void ApplyReplacementToFace(FacialExpression face)
    {
        if (face == null) return;
        Body body = face.body;
        EnsureDirectionWatcher(body);
        if (!MpBody.CanApply(body)) return;
        face.defaultHead = ReplaceSprite(face.defaultHead, body);
        face.defaultHeadMouth = ReplaceSprite(face.defaultHeadMouth, body);
        face.defaultHeadMouthHalf = ReplaceSprite(face.defaultHeadMouthHalf, body);
        face.eyesGone = ReplaceSprite(face.eyesGone, body);
        face.eyesGoneHealed = ReplaceSprite(face.eyesGoneHealed, body);

        if (face.nosebleedSprite != null)
        {
            DefaultSpriteExport.Capture(face.nosebleedSprite.sprite);
            EnsureReplacer(face.nosebleedSprite, body);
        }

        // The head and the eyes are assigned every frame. A replacer on each
        // renderer maps that frame's sprite and sets the side flip.
        EnsureReplacer(face.head, body);
        EnsureReplacer(face.eyes, body);

        for (int i = 0; i < face.disfiguredHead.Length; i++)
            face.disfiguredHead[i] = ReplaceSprite(face.disfiguredHead[i], body);

        for (int i = 0; i < face.disfiguredHeadHeal.Length; i++)
            face.disfiguredHeadHeal[i] = ReplaceSprite(face.disfiguredHeadHeal[i], body);

        for (int i = 0; i < face.eyeList.Count; i++)
        {
            Eye eye = face.eyeList[i];
            eye.front = ReplaceSprite(eye.front, body);
            eye.back = ReplaceSprite(eye.back, body);
            face.eyeList[i] = eye;
        }
    }

    private static void EnsureReplacer(SpriteRenderer renderer, Body body)
    {
        if (renderer == null) return;

        DefaultSpriteExport.Capture(renderer.sprite);

        SkinSpriteReplacer replacer = renderer.GetComponent<SkinSpriteReplacer>();
        if (replacer == null)
        {
            replacer = SkinSpriteReplacer.Attach(renderer, LoadedSprites, body);
            replacer.Apply();
        }
        else
        {
            replacer.SetSprites(LoadedSprites, body);
        }
    }

    private static Sprite ReplaceSprite(Sprite s, Body body)
    {
        DefaultSpriteExport.Capture(s);

        if (s != null && TryGetSprite(LoadedSprites, s.name, body, out Sprite r, out _))
            return r;
        return s;
    }

    // The game flips the body scale in SwitchDir. A direction change must
    // re-read the side for every part of that body.
    internal static void OnBodyDirectionChanged(Body body)
    {
        if (body == null) return;

        BodySkinBinding binding = body.GetComponent<BodySkinBinding>();
        if (binding != null)
        {
            binding.Apply();
            return;
        }

        if (!MpBody.CanApply(body)) return;

        foreach (Limb limb in body.GetComponentsInChildren<Limb>(true))
            ApplyReplacement(limb);
        foreach (TailScript tail in body.GetComponentsInChildren<TailScript>(true))
            ApplyReplacementToTail(tail);
        foreach (FacialExpression face in body.GetComponentsInChildren<FacialExpression>(true))
            ApplyReplacementToFace(face);
    }

    internal static void EnsureDirectionWatcher(Body body)
    {
        if (body == null) return;

        SkinDirectionWatcher watcher = body.GetComponent<SkinDirectionWatcher>();
        if (watcher == null)
            watcher = body.gameObject.AddComponent<SkinDirectionWatcher>();

        watcher.Bind(body);
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

[HarmonyPatch(typeof(WoundView), "Start")]
public class WoundViewStartPatch
{
    static void Postfix(WoundView __instance)
    {
        // The game changes the head in Start only for Milky and Dune. For the
        // Experiment, the current head can be the custom sprite. Keep the
        // original, so a later slot switch reverts to the game sprite.
        if (__instance != null && __instance.body != null && __instance.body.charType != 0)
            WoundViewIntegration.ForgetHeadOriginals(__instance);

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
        SpriteReplacerPlugin.SyncSpeciesCache();
        WoundViewIntegration.ApplyAll();
    }
}
