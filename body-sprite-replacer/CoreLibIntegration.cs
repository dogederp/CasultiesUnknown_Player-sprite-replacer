using System;
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
        // The active slot is per-player. Do not register it as a CUCoreLib
        // option, because CUCoreLib syncs every mod option from the host.
        bool backupOk = ModOptionsRegistry.Register(ModOptionDefinition.Int(
            "spritereplacer.backupCount",
            "Backups kept per slot",
            "Number of backup folders to keep for each slot. Older backups are deleted. 0 keeps all backups.",
            "Skin Replacer",
            SpriteReplacerPlugin.BackupCount,
            0,
            SpriteReplacerPlugin.MaxBackupCount,
            OnBackupCountChanged));

        if (!backupOk)
            SpriteReplacerPlugin.Log.LogWarning("CUCoreLib rejected the backup count option.");

        if (backupOk)
        {
            SpriteReplacerPlugin.Log.LogInfo("CUCoreLib settings tab registered.");
            StoreBackupCount(SpriteReplacerPlugin.BackupCount);
        }
    }

    internal static void StoreActiveSlot(string slot)
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
}
