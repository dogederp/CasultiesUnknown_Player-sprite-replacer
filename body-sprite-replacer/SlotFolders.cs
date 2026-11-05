using System;
using System.Collections.Generic;
using System.IO;

// Slot folder names use the form "slot1" to "slot9". Old "st1" to "st9"
// names still work and are renamed on startup.
internal static class SlotFolders
{
    internal static string SlotId(int index)
    {
        return "slot" + index;
    }

    internal static string LegacySlotId(int index)
    {
        return "st" + index;
    }

    // Returns the 0-based slot index, or -1 when the name is not a slot.
    internal static int Index(string slot)
    {
        if (string.IsNullOrEmpty(slot)) return -1;

        int number = ParseNumber(slot, "slot");
        if (number < 0) number = ParseNumber(slot, "st");
        return number < 0 ? -1 : number - 1;
    }

    // Maps "st3" and "slot3" to "slot3". Returns null for any other value.
    internal static string Normalize(string slot)
    {
        int index = Index(slot);
        return index < 0 ? null : SlotId(index + 1);
    }

    internal static string DisplayName(string slot)
    {
        int index = Index(slot);
        return index < 0 ? slot : "Slot " + (index + 1);
    }

    // Renames old "stN" folders to "slotN". Returns one message for each change.
    internal static List<string> Migrate(string root, int count)
    {
        var messages = new List<string>();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return messages;

        for (int i = 1; i <= count; i++)
        {
            string legacy = Path.Combine(root, LegacySlotId(i));
            string current = Path.Combine(root, SlotId(i));
            if (!Directory.Exists(legacy)) continue;

            if (Directory.Exists(current))
            {
                messages.Add("Both " + legacy + " and " + current + " exist. The mod uses " + current + ".");
                continue;
            }

            try
            {
                Directory.Move(legacy, current);
                messages.Add("Renamed " + legacy + " to " + current + ".");
            }
            catch (Exception ex)
            {
                messages.Add("Could not rename " + legacy + " to " + current + ": " + ex.Message);
            }
        }

        return messages;
    }

    private static int ParseNumber(string slot, string prefix)
    {
        if (slot.Length != prefix.Length + 1) return -1;
        if (!slot.StartsWith(prefix, StringComparison.Ordinal)) return -1;

        char digit = slot[prefix.Length];
        if (digit < '1' || digit > '9') return -1;
        return digit - '0';
    }
}
