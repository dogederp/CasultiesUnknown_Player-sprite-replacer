using System;

// Side suffix rules for Body and Head sprite files. _left and _right name
// the facing direction, not the arm. A file without a suffix stays shared.
internal static class SideSprites
{
    internal const string LeftSuffix = "_left";
    internal const string RightSuffix = "_right";

    internal static bool TryParse(string name, out string baseName, out bool isRight)
    {
        baseName = name;
        isRight = false;
        if (string.IsNullOrEmpty(name)) return false;

        if (name.EndsWith(RightSuffix, StringComparison.OrdinalIgnoreCase))
        {
            string candidate = name.Substring(0, name.Length - RightSuffix.Length);
            if (candidate.Length == 0) return false;
            baseName = candidate;
            isRight = true;
            return true;
        }

        if (name.EndsWith(LeftSuffix, StringComparison.OrdinalIgnoreCase))
        {
            string candidate = name.Substring(0, name.Length - LeftSuffix.Length);
            if (candidate.Length == 0) return false;
            baseName = candidate;
            return true;
        }

        return false;
    }

    internal static string BaseName(string name)
    {
        return TryParse(name, out string baseName, out _) ? baseName : name;
    }

    internal static string Key(string baseName, bool isRight)
    {
        return baseName + (isRight ? RightSuffix : LeftSuffix);
    }
}
