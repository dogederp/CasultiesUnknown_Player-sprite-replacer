using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using HarmonyLib;

internal static class CoreLibIntegration
{
    private const string SlotOptionId = "spritereplacer.activeSlot";

    private static ModOptionDefinition _slotOption;
    private static bool _slotOptionHidden;
    private static bool _slotSyncPatchApplied;

    internal static void TryRegister()
    {
        if (!IsAvailable())
        {
            SpriteReplacerPlugin.Log.LogInfo("CUCoreLib not found. The Skin Replacer settings tab is disabled.");
            return;
        }

        try
        {
            RegisterCoreLib();
        }
        catch (Exception ex)
        {
            SpriteReplacerPlugin.Log.LogWarning("Could not register the CUCoreLib settings tab: " + ex);
        }
    }

    private static bool IsAvailable()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, "CUCoreLib", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterCoreLib()
    {
        ModDropdownChoice[] choices = Enumerable.Range(1, SpriteReplacerPlugin.SlotCount)
            .Select(i => new ModDropdownChoice(SlotFolders.SlotId(i), "Slot " + i))
            .ToArray();

        int defaultIndex = Math.Max(0, SlotFolders.Index(SpriteReplacerPlugin.ActiveSlot));
        ModOptionDefinition slotOption = ModOptionDefinition.Dropdown(
            SlotOptionId,
            "Active slot",
            "Select the CustomSprites slot to use. This choice stays local.",
            "Skin Replacer",
            defaultIndex,
            choices,
            OnDropdownChanged);
        bool slotOk = ModOptionsRegistry.Register(slotOption);

        bool backupOk = ModOptionsRegistry.Register(ModOptionDefinition.Int(
            "spritereplacer.backupCount",
            "Backups kept per slot",
            "Number of backup folders to keep for each slot. Older backups are deleted. 0 keeps all backups.",
            "Skin Replacer",
            SpriteReplacerPlugin.BackupCount,
            0,
            SpriteReplacerPlugin.MaxBackupCount,
            OnBackupCountChanged));

        if (!slotOk)
            SpriteReplacerPlugin.Log.LogWarning("CUCoreLib rejected the active slot option.");
        if (!backupOk)
            SpriteReplacerPlugin.Log.LogWarning("CUCoreLib rejected the backup count option.");

        if (slotOk)
        {
            // The option stays registered, so the dropdown stays in the settings
            // tab. A Harmony patch skips it while a host snapshot is applied.
            _slotOption = slotOption;
            BlockSlotHostSync();
            StoreActiveSlot(SpriteReplacerPlugin.ActiveSlot);
        }

        if (slotOk || backupOk)
        {
            SpriteReplacerPlugin.Log.LogInfo("CUCoreLib settings tab registered.");
            if (backupOk) StoreBackupCount(SpriteReplacerPlugin.BackupCount);
        }
    }

    private static void BlockSlotHostSync()
    {
        if (_slotSyncPatchApplied) return;

        try
        {
            MethodInfo method = typeof(ModOptionsRegistry).GetMethod(
                "ApplyNetworkSnapshot",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                SpriteReplacerPlugin.Log.LogWarning(
                    "Could not find ApplyNetworkSnapshot. The active slot may sync from the host.");
                return;
            }

            var harmony = new Harmony("com.yourname.spritereplacer.cucorelib");
            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(CoreLibIntegration).GetMethod(
                    nameof(HideSlotOption), BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(typeof(CoreLibIntegration).GetMethod(
                    nameof(RestoreSlotOption), BindingFlags.Static | BindingFlags.NonPublic)));
            _slotSyncPatchApplied = true;
            SpriteReplacerPlugin.Log.LogInfo("Host sync no longer changes the local active slot.");
        }
        catch (Exception ex)
        {
            SpriteReplacerPlugin.Log.LogWarning(
                "Could not block the active slot from host sync: " + ex.Message);
        }
    }

    private static void HideSlotOption()
    {
        if (_slotOptionHidden) return;

        List<ModOptionDefinition> options = GetRegisteredOptions();
        if (options == null) return;

        _slotOptionHidden = options.RemoveAll(
            option => option != null && option.Id == SlotOptionId) > 0;
    }

    private static Exception RestoreSlotOption(Exception __exception)
    {
        if (!_slotOptionHidden) return __exception;

        _slotOptionHidden = false;
        List<ModOptionDefinition> options = GetRegisteredOptions();
        if (options != null && _slotOption != null &&
            !options.Any(option => option != null && option.Id == SlotOptionId))
        {
            options.Add(_slotOption);
        }

        return __exception;
    }

    private static List<ModOptionDefinition> GetRegisteredOptions()
    {
        FieldInfo field = typeof(ModOptionsRegistry).GetField(
            "RegisteredOptions", BindingFlags.Static | BindingFlags.NonPublic);
        return field == null ? null : field.GetValue(null) as List<ModOptionDefinition>;
    }

    internal static void StoreActiveSlot(string slot)
    {
        if (!IsAvailable()) return;
        StoreActiveSlotCore(slot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreActiveSlotCore(string slot)
    {
        try
        {
            if (Settings.settings == null) return;

            int index = Math.Max(0, SlotFolders.Index(slot));
            foreach (Setting setting in Settings.settings)
            {
                if (!(setting is SettingDropdown dropdown)) continue;
                if (dropdown.name != "spritereplacer.activeSlot") continue;
                dropdown.value = index;
                return;
            }
        }
        catch (Exception ex)
        {
            SpriteReplacerPlugin.Log.LogWarning("Could not update the saved active slot: " + ex.Message);
        }
    }

    internal static void StoreBackupCount(int count)
    {
        if (!IsAvailable()) return;
        StoreBackupCountCore(count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreBackupCountCore(int count)
    {
        try
        {
            if (Settings.settings == null) return;

            foreach (Setting setting in Settings.settings)
            {
                if (!(setting is SettingInt intSetting)) continue;
                if (intSetting.name != "spritereplacer.backupCount") continue;
                intSetting.value = Math.Max(0, Math.Min(SpriteReplacerPlugin.MaxBackupCount, count));
                return;
            }
        }
        catch (Exception ex)
        {
            SpriteReplacerPlugin.Log.LogWarning("Could not update the saved backup count: " + ex.Message);
        }
    }

    private static void OnBackupCountChanged(int value)
    {
        SpriteReplacerPlugin.SetBackupCount(value);
        SkinImporter.PruneAllBackups();
    }

    private static void OnDropdownChanged(int index)
    {
        string slot = SlotFolders.SlotId(index + 1);
        if (SpriteReplacerPlugin.SwitchToSlot(slot)) return;

        // The switch was blocked by a lock or a cooldown. Show the slot in use.
        StoreActiveSlot(SpriteReplacerPlugin.ActiveSlot);
    }
}
