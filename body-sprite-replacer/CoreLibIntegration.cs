using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CUCoreLib.Data;
using CUCoreLib.Registries;

internal static class CoreLibIntegration
{
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
            .Select(i => new ModDropdownChoice("st" + i, "st" + i))
            .ToArray();

        int defaultIndex = SlotIndex(SpriteReplacerPlugin.ActiveSlot);
        bool slotOk = ModOptionsRegistry.Register(ModOptionDefinition.Dropdown(
            "spritereplacer.activeSlot",
            "Active slot",
            "Select the CustomSprites slot to use.",
            "Skin Replacer",
            defaultIndex,
            choices,
            OnDropdownChanged));

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

        if (slotOk || backupOk)
        {
            SpriteReplacerPlugin.Log.LogInfo("CUCoreLib settings tab registered.");
            // The game can re-apply saved settings after registration. Keep the
            // stored values on the values that the config restored.
            if (slotOk) StoreActiveSlot(SpriteReplacerPlugin.ActiveSlot);
            if (backupOk) StoreBackupCount(SpriteReplacerPlugin.BackupCount);
        }
    }

    internal static void StoreActiveSlot(string slot)
    {
        try
        {
            if (Settings.settings == null) return;

            int index = SlotIndex(slot);
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
        SpriteReplacerPlugin.SwitchToSlot("st" + (index + 1));
    }

    private static int SlotIndex(string slot)
    {
        if (slot != null && slot.Length == 3 &&
            slot.StartsWith("st", StringComparison.Ordinal) &&
            slot[2] >= '1' && slot[2] <= '9')
        {
            return slot[2] - '1';
        }
        return 0;
    }
}
