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
        bool ok = ModOptionsRegistry.Register(ModOptionDefinition.Dropdown(
            "spritereplacer.activeSlot",
            "Active slot",
            "Select the CustomSprites slot to use.",
            "Skin Replacer",
            defaultIndex,
            choices,
            OnDropdownChanged));

        if (ok)
        {
            SpriteReplacerPlugin.Log.LogInfo("CUCoreLib settings tab registered.");
            // The game can re-apply saved settings after registration. Keep the
            // stored dropdown on the slot the config restored.
            StoreActiveSlot(SpriteReplacerPlugin.ActiveSlot);
        }
        else
        {
            SpriteReplacerPlugin.Log.LogWarning("CUCoreLib rejected the settings option.");
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
