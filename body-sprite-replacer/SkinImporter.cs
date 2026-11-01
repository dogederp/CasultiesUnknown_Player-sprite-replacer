using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal sealed class SkinCheckResult
{
    internal readonly List<string> Errors = new List<string>();
    internal readonly List<string> Warnings = new List<string>();
    internal int BodyCount;
    internal int HeadCount;
    internal int WoundViewCount;

    internal bool Ok => Errors.Count == 0;
    internal int TotalCount => BodyCount + HeadCount + WoundViewCount;
}

internal static class SkinImporter
{
    internal static readonly HashSet<string> BodyNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "experimentCrus",
        "experimentDownArm",
        "experimentDownTorso",
        "experimentFoot",
        "experimentHandB",
        "experimentHandF",
        "experimentTail",
        "experimentThigh",
        "experimentUpArm",
        "experimentUpTorso"
    };

    internal static readonly HashSet<string> HeadNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "experimentEyeClosed",
        "experimentEyeGone",
        "experimentEyeGoneHealed",
        "experimentEyeHalfClosed",
        "experimentEyeHalfClosedBack",
        "experimentEyeHappy",
        "experimentEyeLookBack",
        "experimentEyeOpen",
        "experimentEyePanic",
        "experimentEyeSad",
        "experimentEyeSadBack",
        "experimentEyeScared",
        "experimentEyeScaredBack",
        "experimentHead",
        "experimentHeadBack",
        "experimentHeadBackMouth",
        "experimentHeadBackMouthMini",
        "experimentHeadDisfigured1",
        "experimentHeadDisfigured1Healed",
        "experimentHeadDisfigured2",
        "experimentHeadDisfigured2Healed",
        "experimentHeadDisfigured3",
        "experimentHeadDisfigured3Healed",
        "experimentNosebleed"
    };

    internal static readonly HashSet<string> KnownNames = new HashSet<string>(
        BodyNames.Concat(HeadNames), StringComparer.Ordinal);

    internal static readonly HashSet<string> WoundViewNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "abdomenMiddle",
        "abdomenOutline",
        "armMiddle",
        "armOutline",
        "crusMiddle",
        "crusOutline",
        "disfiguredExpWound",
        "experimentBody",
        "eyeGoneExpWound",
        "footMiddle",
        "footOutline",
        "forearmMiddle",
        "forearmOutline",
        "handMiddle",
        "handOutline",
        "headMiddle",
        "headoutline",
        "thighMiddle",
        "thighOutline",
        "thoraxMiddle",
        "thoraxOutline"
    };

    internal static SkinCheckResult Check(string sourceDir, string slot)
    {
        var result = new SkinCheckResult();

        if (string.IsNullOrWhiteSpace(sourceDir))
        {
            result.Errors.Add("No folder was selected.");
            return result;
        }

        if (!Directory.Exists(sourceDir))
        {
            result.Errors.Add("Folder not found: " + sourceDir);
            return result;
        }

        string bodyDir = Path.Combine(sourceDir, "Body");
        string headDir = Path.Combine(sourceDir, "Head");
        string woundViewDir = Path.Combine(sourceDir, "WoundView");
        bool hasBody = Directory.Exists(bodyDir);
        bool hasHead = Directory.Exists(headDir);
        bool hasWoundView = Directory.Exists(woundViewDir);

        if (!hasBody && !hasHead && !hasWoundView)
        {
            result.Errors.Add("The folder must contain a Body, Head, or WoundView subfolder.");
            return result;
        }

        string targetRoot = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot);
        if (SamePath(sourceDir, targetRoot))
        {
            result.Errors.Add("The source folder is the target slot. Choose a different folder.");
            return result;
        }

        var bodySet = new HashSet<string>(StringComparer.Ordinal);
        var headSet = new HashSet<string>(StringComparer.Ordinal);
        var woundViewSet = new HashSet<string>(StringComparer.Ordinal);

        if (hasBody) CheckFolder(bodyDir, "Body", slot, BodyNames, bodySet, result);
        if (hasHead) CheckFolder(headDir, "Head", slot, HeadNames, headSet, result);
        if (hasWoundView) CheckFolder(woundViewDir, "WoundView", slot, WoundViewNames, woundViewSet, result);

        foreach (string name in bodySet)
        {
            if (headSet.Contains(name))
                result.Errors.Add("Sprite name appears in both Body and Head: " + name);
        }

        result.BodyCount = bodySet.Count;
        result.HeadCount = headSet.Count;
        result.WoundViewCount = woundViewSet.Count;

        if (result.TotalCount == 0)
            result.Errors.Add("No PNG files were found.");

        if (woundViewSet.Count == 0 && HasAnyTargetFolder(slot, "WoundView"))
        {
            result.Warnings.Add("No WoundView PNG files: the import removes the current wound view.");
        }

        int present = 0;
        foreach (string name in KnownNames)
        {
            if (bodySet.Contains(name) || headSet.Contains(name)) present++;
        }

        int missing = KnownNames.Count - present;
        if (missing > 0 && result.TotalCount > 0)
        {
            result.Warnings.Add("Partial skin: " + missing + " of " + KnownNames.Count +
                                " names are missing. Missing parts keep the current sprite.");
        }

        return result;
    }

    internal static bool Import(string sourceDir, string slot, out string backupPath, out string error)
    {
        backupPath = null;
        error = null;

        SkinCheckResult check = Check(sourceDir, slot);
        if (!check.Ok)
        {
            error = string.Join("\n", check.Errors);
            return false;
        }

        string targetRoot = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot);

        try
        {
            if (HasAnyTargetFiles(targetRoot))
                backupPath = CreateBackup(targetRoot, slot);

            foreach (string subfolder in new[] { "Body", "Head", "WoundView" })
            {
                string sourceSub = Path.Combine(sourceDir, subfolder);
                if (!Directory.Exists(sourceSub)) continue;

                string[] pngs = GetPngFiles(sourceSub);
                if (pngs.Length == 0) continue;

                string targetSub = Path.Combine(targetRoot, subfolder);
                Directory.CreateDirectory(targetSub);
                DeletePngAndTxt(targetSub);

                foreach (string png in pngs)
                    File.Copy(png, Path.Combine(targetSub, Path.GetFileName(png)), true);
            }

            string sourceWoundDir = Path.Combine(sourceDir, "WoundView");
            bool sourceHasWoundPngs = Directory.Exists(sourceWoundDir) && GetPngFiles(sourceWoundDir).Length > 0;
            if (!sourceHasWoundPngs)
            {
                string targetWoundDir = Path.Combine(targetRoot, "WoundView");
                if (Directory.Exists(targetWoundDir)) Directory.Delete(targetWoundDir, true);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (!string.IsNullOrEmpty(backupPath))
            {
                try
                {
                    RestoreBackup(backupPath, targetRoot);
                }
                catch (Exception restoreEx)
                {
                    error += "\nBackup restore failed: " + restoreEx.Message;
                }
            }
            return false;
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            string a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CheckFolder(string folder, string subfolder, string slot,
        HashSet<string> allowed, HashSet<string> present, SkinCheckResult result)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(folder);
        }
        catch (Exception ex)
        {
            result.Errors.Add("Cannot read " + subfolder + ": " + ex.Message);
            return;
        }

        foreach (string file in files)
        {
            if (!Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase)) continue;

            string name = Path.GetFileNameWithoutExtension(file);
            if (!present.Add(name)) continue;

            if (!allowed.Contains(name))
            {
                result.Errors.Add("Unknown " + subfolder + " sprite name: " + name);
                continue;
            }

            CheckPng(file, name, subfolder, slot, result);
        }
    }

    private static void CheckPng(string file, string name, string subfolder, string slot, SkinCheckResult result)
    {
        Texture2D tex = null;
        try
        {
            byte[] data = File.ReadAllBytes(file);
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!ImageConversion.LoadImage(tex, data) || tex.width <= 1 || tex.height <= 1)
            {
                result.Errors.Add("Cannot decode PNG: " + name + ".png");
                return;
            }

            int targetWidth;
            int targetHeight;
            if (TryGetTargetSize(slot, subfolder, name, out targetWidth, out targetHeight))
            {
                if (targetWidth != tex.width || targetHeight != tex.height)
                {
                    result.Warnings.Add("Size differs for " + name + ".png: " + tex.width + "x" + tex.height +
                                        ", slot has " + targetWidth + "x" + targetHeight + ".");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add("Cannot read " + name + ".png: " + ex.Message);
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private static bool TryGetTargetSize(string slot, string subfolder, string name, out int width, out int height)
    {
        width = 0;
        height = 0;

        string path = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot, subfolder, name + ".png");
        if (!File.Exists(path)) return false;

        Texture2D tex = null;
        try
        {
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) return false;
            width = tex.width;
            height = tex.height;
            return width > 1 && height > 1;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private static bool HasAnyTargetFiles(string targetRoot)
    {
        foreach (string subfolder in new[] { "Body", "Head", "WoundView" })
        {
            string path = Path.Combine(targetRoot, subfolder);
            if (Directory.Exists(path) && Directory.GetFiles(path).Length > 0) return true;
        }
        return false;
    }

    private static bool HasAnyTargetFolder(string slot, string subfolder)
    {
        string path = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot, subfolder);
        return Directory.Exists(path) && Directory.GetFiles(path).Length > 0;
    }

    private static string CreateBackup(string targetRoot, string slot)
    {
        string root = Path.Combine(SpriteReplacerPlugin.SpritesPath, "backups");
        Directory.CreateDirectory(root);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backup = Path.Combine(root, slot + "_" + stamp);
        int suffix = 1;
        while (Directory.Exists(backup))
        {
            backup = Path.Combine(root, slot + "_" + stamp + "_" + suffix);
            suffix++;
        }

        Directory.CreateDirectory(backup);

        foreach (string subfolder in new[] { "Body", "Head", "WoundView" })
        {
            string source = Path.Combine(targetRoot, subfolder);
            if (!Directory.Exists(source)) continue;

            string destination = Path.Combine(backup, subfolder);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        return backup;
    }

    private static void RestoreBackup(string backup, string targetRoot)
    {
        foreach (string subfolder in new[] { "Body", "Head", "WoundView" })
        {
            string source = Path.Combine(backup, subfolder);
            string destination = Path.Combine(targetRoot, subfolder);

            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            if (!Directory.Exists(source)) continue;

            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
    }

    private static string[] GetPngFiles(string folder)
    {
        return Directory.GetFiles(folder)
            .Where(file => Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static void DeletePngAndTxt(string folder)
    {
        foreach (string file in Directory.GetFiles(folder))
        {
            string extension = Path.GetExtension(file);
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }
}
