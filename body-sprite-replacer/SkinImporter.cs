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
    // One list for each species. The body and head names do not overlap.
    private static readonly string[] ExperimentBodyNames =
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

    private static readonly string[] MilkyBodyNames =
    {
        "mildownarm",
        "milkycrus",
        "milkydowntorso",
        "milkyfoot",
        "milkyhandb",
        "milkyhandf",
        "milkytail",
        "milkythigh",
        "milkyuparm",
        "milkytorso"
    };

    private static readonly string[] DuneBodyNames =
    {
        "duneCrus",
        "duneDownArm",
        "duneDownArmAlt",
        "duneDownTorso",
        "duneFoot",
        "duneHand",
        "duneHandAlt",
        "duneTail",
        "duneThigh",
        "duneThighAlt",
        "duneUpArm",
        "duneUpArmAlt",
        "duneUpTorso"
    };

    private static readonly string[] ExperimentHeadNames =
    {
        "experimentEyeClosed",
        "experimentEyeGone",
        "experimentEyeGoneHealed",
        "experimentEyeHalfClosed",
        "experimentEyeHalfClosedBack",
        "experimentEyeHappy",
        "experimentEyeLockedIn",
        "experimentEyeLockedInBack",
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

    private static readonly string[] MilkyHeadNames =
    {
        "milkydisfigure1",
        "milkydisfigure1healed",
        "milkydisfigure2",
        "milkydisfigure2healed",
        "milkyeyeclosed",
        "milkyeyegone",
        "milkyeyegonehealed",
        "milkyeyehalfclosed",
        "milkyeyehalfclosedback",
        "milkyeyehappy",
        "milkyeyehappyback",
        "milkyeyelockedin",
        "milkyeyelockedinback",
        "milkyeyeopen",
        "milkyeyeopenback",
        "milkyeyepanic",
        "milkyeyesad",
        "milkyeyesadback",
        "milkyeyescared",
        "milkyeyescaredback",
        "milkyheadback",
        "milkyheadbackmouthhalfopen",
        "milkyheadbackmouthopen"
    };

    private static readonly string[] DuneHeadNames =
    {
        "duneDisfigured",
        "duneDisfiguredHealed",
        "duneEyeGone",
        "duneEyeGoneHealed",
        "duneEyeHappy",
        "duneEyePanic",
        "duneEyeSad",
        "duneEyeScared",
        "duneEyes",
        "duneEyesClosed",
        "duneEyesHalfClosed",
        "duneHeadBack",
        "duneHeadMouth",
        "duneHeadMouthHalf",
        "duneeyelockedin"
    };

    // Wound sprites that every species uses.
    private static readonly string[] SharedWoundNames =
    {
        "abdomenMiddle",
        "abdomenOutline",
        "armMiddle",
        "armOutline",
        "crusMiddle",
        "crusOutline",
        "footMiddle",
        "footOutline",
        "forearmMiddle",
        "forearmOutline",
        "handMiddle",
        "handOutline",
        "thighMiddle",
        "thighOutline",
        "thoraxMiddle",
        "thoraxOutline"
    };

    private static readonly string[] ExperimentWoundNames =
    {
        "disfiguredExpWound",
        "experimentBody",
        "eyeGoneExpWound",
        "headMiddle",
        "headoutline"
    };

    private static readonly string[] MilkyWoundNames =
    {
        "milkheadmiddle",
        "milkheadouter"
    };

    private static readonly string[] DuneWoundNames =
    {
        "headMiddleDune",
        "headOuterDune"
    };

    private static readonly string[][] BodyGroups =
    {
        ExperimentBodyNames, MilkyBodyNames, DuneBodyNames
    };

    private static readonly string[][] HeadGroups =
    {
        ExperimentHeadNames, MilkyHeadNames, DuneHeadNames
    };

    private static readonly HashSet<string>[] BodySets = BuildSets(BodyGroups);
    private static readonly HashSet<string>[] HeadSets = BuildSets(HeadGroups);
    private static readonly HashSet<string>[] WoundSets = BuildWoundSets();

    internal static readonly HashSet<string> BodyNames = MakeSet(BodyGroups);
    internal static readonly HashSet<string> HeadNames = MakeSet(HeadGroups);

    internal static readonly HashSet<string> WoundViewNames = MakeSet(new[]
    {
        SharedWoundNames, ExperimentWoundNames, MilkyWoundNames, DuneWoundNames
    });

    internal static readonly HashSet<string> KnownNames = new HashSet<string>(
        BodyNames.Concat(HeadNames), StringComparer.Ordinal);

    private static HashSet<string>[] BuildSets(string[][] groups)
    {
        var sets = new HashSet<string>[groups.Length];
        for (int i = 0; i < groups.Length; i++)
            sets[i] = new HashSet<string>(groups[i], StringComparer.Ordinal);
        return sets;
    }

    private static HashSet<string>[] BuildWoundSets()
    {
        var sets = new HashSet<string>[3];
        for (int i = 0; i < sets.Length; i++)
        {
            sets[i] = new HashSet<string>(SharedWoundNames, StringComparer.Ordinal);
            foreach (string name in i == 0 ? ExperimentWoundNames : i == 1 ? MilkyWoundNames : DuneWoundNames)
                sets[i].Add(name);
        }
        return sets;
    }

    private static HashSet<string> MakeSet(string[][] groups)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] group in groups)
        {
            foreach (string name in group)
                set.Add(name);
        }
        return set;
    }

    internal static HashSet<string> BodyNamesFor(int species)
    {
        return BodySets[ClampSpecies(species)];
    }

    internal static HashSet<string> HeadNamesFor(int species)
    {
        return HeadSets[ClampSpecies(species)];
    }

    internal static HashSet<string> WoundNamesFor(int species)
    {
        return WoundSets[ClampSpecies(species)];
    }

    internal static string SpeciesName(int species)
    {
        return SpriteReplacerPlugin.SpeciesNames[ClampSpecies(species)];
    }

    // A species page accepts its own names plus experiment names. The loader
    // aliases experiment names to the live species names.
    private static HashSet<string> AllowedBodyNames(int species)
    {
        int index = ClampSpecies(species);
        if (index == 0) return BodySets[0];

        var set = new HashSet<string>(BodySets[index], StringComparer.Ordinal);
        set.UnionWith(BodySets[0]);
        return set;
    }

    private static HashSet<string> AllowedHeadNames(int species)
    {
        int index = ClampSpecies(species);
        if (index == 0) return HeadSets[0];

        var set = new HashSet<string>(HeadSets[index], StringComparer.Ordinal);
        set.UnionWith(HeadSets[0]);
        return set;
    }

    private static int ClampSpecies(int species)
    {
        if (species >= 0 && species <= 2) return species;
        return SpriteReplacerPlugin.ActiveSpecies;
    }

    internal static SkinCheckResult Check(string sourceDir, string slot, int species = -1)
    {
        var result = new SkinCheckResult();
        species = ClampSpecies(species);

        string normalized = SlotFolders.Normalize(slot);
        if (normalized == null)
        {
            result.Errors.Add("Invalid slot: " + slot);
            return result;
        }
        slot = normalized;

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

        string targetRoot = SpriteReplacerPlugin.SlotDirectory(slot, species);
        if (SamePath(sourceDir, targetRoot))
        {
            result.Errors.Add("The source folder is the target slot. Choose a different folder.");
            return result;
        }

        var bodySet = new HashSet<string>(StringComparer.Ordinal);
        var headSet = new HashSet<string>(StringComparer.Ordinal);
        var woundViewSet = new HashSet<string>(StringComparer.Ordinal);

        HashSet<string> allowedBody = AllowedBodyNames(species);
        HashSet<string> allowedHead = AllowedHeadNames(species);
        HashSet<string> allowedWound = WoundNamesFor(species);

        if (hasBody) CheckFolder(bodyDir, "Body", slot, species, allowedBody, bodySet, result);
        if (hasHead) CheckFolder(headDir, "Head", slot, species, allowedHead, headSet, result);
        if (hasWoundView) CheckFolder(woundViewDir, "WoundView", slot, species, allowedWound, woundViewSet, result);

        foreach (string name in bodySet)
        {
            if (headSet.Contains(name))
                result.Errors.Add("Sprite name appears in both Body and Head: " + name);
        }

        result.BodyCount = bodySet.Count;
        result.HeadCount = headSet.Count;
        result.WoundViewCount = woundViewSet.Count;

        if (result.TotalCount == 0)
            result.Errors.Add("No PNG files with known sprite names were found.");

        if (woundViewSet.Count == 0 && HasAnyTargetFolder(slot, species, "WoundView"))
        {
            result.Warnings.Add("No WoundView PNG files: the import removes the current wound view.");
        }

        // Count a partial skin against one name set. A species-named skin counts
        // the species names. An experiment-named skin counts the experiment names.
        bool speciesStyle = false;
        foreach (string name in BodySets[species])
        {
            if (bodySet.Contains(name))
            {
                speciesStyle = true;
                break;
            }
        }

        if (!speciesStyle)
        {
            foreach (string name in HeadSets[species])
            {
                if (headSet.Contains(name))
                {
                    speciesStyle = true;
                    break;
                }
            }
        }

        int style = species == 0 ? 0 : (speciesStyle ? species : 0);
        int present = CountPresent(BodyGroups[style], bodySet, headSet);
        present += CountPresent(HeadGroups[style], bodySet, headSet);
        int total = BodyGroups[style].Length + HeadGroups[style].Length;
        if (style == 0) total--; // experimentNosebleed is optional

        int missing = total - present;
        if (missing > 0 && total > 0)
        {
            result.Warnings.Add("Partial skin: " + missing + " of " + total +
                                " names are missing. Missing parts keep the current sprite.");
        }

        return result;
    }

    private static int CountPresent(string[] names, HashSet<string> body, HashSet<string> head)
    {
        int count = 0;
        foreach (string name in names)
        {
            if (name == "experimentNosebleed") continue;
            if (body.Contains(name) || head.Contains(name)) count++;
        }
        return count;
    }

    internal static bool Import(string sourceDir, string slot, out string backupPath, out string error,
        int species = -1)
    {
        backupPath = null;
        error = null;
        species = ClampSpecies(species);

        string normalized = SlotFolders.Normalize(slot);
        if (normalized == null)
        {
            error = "Invalid slot: " + slot;
            return false;
        }
        slot = normalized;

        SkinCheckResult check = Check(sourceDir, slot, species);
        if (!check.Ok)
        {
            error = string.Join("\n", check.Errors);
            return false;
        }

        string targetRoot = SpriteReplacerPlugin.SlotDirectory(slot, species);

        try
        {
            if (HasAnyTargetFiles(targetRoot))
                backupPath = CreateBackup(targetRoot, slot, species);

            foreach (string subfolder in new[] { "Body", "Head", "WoundView" })
            {
                string sourceSub = Path.Combine(sourceDir, subfolder);
                if (!Directory.Exists(sourceSub)) continue;

                string[] pngs = GetPngFiles(sourceSub);
                if (pngs.Length == 0) continue;

                HashSet<string> allowed = AllowedNamesFor(subfolder, species);
                string[] knownPngs = pngs
                    .Where(png => allowed.Contains(Path.GetFileNameWithoutExtension(png)))
                    .ToArray();
                if (knownPngs.Length == 0) continue;

                string targetSub = Path.Combine(targetRoot, subfolder);
                Directory.CreateDirectory(targetSub);
                DeletePngAndTxt(targetSub);

                foreach (string png in knownPngs)
                    File.Copy(png, Path.Combine(targetSub, Path.GetFileName(png)), true);
            }

            string sourceWoundDir = Path.Combine(sourceDir, "WoundView");
            bool sourceHasWoundPngs = Directory.Exists(sourceWoundDir) &&
                GetPngFiles(sourceWoundDir).Any(png => WoundNamesFor(species).Contains(
                    Path.GetFileNameWithoutExtension(png)));
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

    internal static bool PrepareSkinFolder(string sourceDir, string slot, out int movedCount,
        out string error, int species = -1)
    {
        movedCount = 0;
        error = null;
        species = ClampSpecies(species);

        string normalized = SlotFolders.Normalize(slot);
        if (normalized == null)
        {
            error = "Invalid slot: " + slot;
            return false;
        }
        slot = normalized;

        string bodyDir = Path.Combine(sourceDir, "Body");
        string headDir = Path.Combine(sourceDir, "Head");
        if (!Directory.Exists(sourceDir) || !Directory.Exists(bodyDir)) return true;

        bool headHasPngs = Directory.Exists(headDir) && GetPngFiles(headDir).Length > 0;
        if (headHasPngs) return true;

        string targetRoot = SpriteReplacerPlugin.SlotDirectory(slot, species);
        if (SamePath(sourceDir, targetRoot)) return true;

        HashSet<string> headNames = AllowedHeadNames(species);
        var headFiles = new List<string>();
        foreach (string png in GetPngFiles(bodyDir))
        {
            if (headNames.Contains(Path.GetFileNameWithoutExtension(png))) headFiles.Add(png);
        }

        if (headFiles.Count == 0)
        {
            error = "No Head folder and no Head sprites in Body. Make a Head folder for the Head PNG files, " +
                    "or put them in Body with their exact sprite names.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(headDir);
            foreach (string png in headFiles)
            {
                File.Move(png, Path.Combine(headDir, Path.GetFileName(png)));
                string txt = Path.ChangeExtension(png, ".txt");
                if (File.Exists(txt))
                    File.Move(txt, Path.Combine(headDir, Path.GetFileName(txt)));
                movedCount++;
            }
        }
        catch (Exception ex)
        {
            error = "Cannot move the Head sprites into the Head folder: " + ex.Message;
            return false;
        }

        return true;
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

    private static void CheckFolder(string folder, string subfolder, string slot, int species,
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
            if (!allowed.Contains(name))
            {
                result.Warnings.Add("Unknown " + subfolder + " PNG skipped: " + name + ".");
                continue;
            }

            if (!present.Add(name)) continue;

            CheckPng(file, name, subfolder, slot, species, result);
        }
    }

    private static HashSet<string> AllowedNamesFor(string subfolder, int species)
    {
        if (subfolder == "Body") return AllowedBodyNames(species);
        if (subfolder == "Head") return AllowedHeadNames(species);
        return WoundNamesFor(species);
    }

    private static void CheckPng(string file, string name, string subfolder, string slot,
        int species, SkinCheckResult result)
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
            if (TryGetTargetSize(slot, species, subfolder, name, out targetWidth, out targetHeight))
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

    private static bool TryGetTargetSize(string slot, int species, string subfolder, string name,
        out int width, out int height)
    {
        width = 0;
        height = 0;

        string path = Path.Combine(SpriteReplacerPlugin.SlotDirectory(slot, species),
            subfolder, name + ".png");
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

    private static bool HasAnyTargetFolder(string slot, int species, string subfolder)
    {
        string path = Path.Combine(SpriteReplacerPlugin.SlotDirectory(slot, species), subfolder);
        return Directory.Exists(path) && Directory.GetFiles(path).Length > 0;
    }

    private static string CreateBackup(string targetRoot, string slot, int species)
    {
        string root = SpriteReplacerPlugin.BackupRootFor(species);
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

        PruneBackups(slot, species, SpriteReplacerPlugin.BackupCount);
        return backup;
    }

    internal static void PruneAllBackups()
    {
        int keep = SpriteReplacerPlugin.BackupCount;
        if (keep <= 0) return;

        for (int species = 0; species < 3; species++)
        {
            for (int i = 1; i <= SpriteReplacerPlugin.SlotCount; i++)
                PruneBackups(SlotFolders.SlotId(i), species, keep);
        }
    }

    private static void PruneBackups(string slot, int species, int keep)
    {
        if (keep <= 0) return;

        string normalized = SlotFolders.Normalize(slot);
        if (normalized == null) return;
        slot = normalized;

        string root = SpriteReplacerPlugin.BackupRootFor(species);
        if (!Directory.Exists(root)) return;

        var dirs = new List<string>();
        try
        {
            dirs.AddRange(Directory.GetDirectories(root, slot + "_*"));

            string legacy = SlotFolders.LegacySlotId(SlotFolders.Index(slot) + 1);
            if (legacy != null && legacy != slot)
                dirs.AddRange(Directory.GetDirectories(root, legacy + "_*"));
        }
        catch (Exception ex)
        {
            SpriteReplacerPlugin.Log.LogWarning("Could not list old backups for " + slot + ": " + ex.Message);
            return;
        }

        if (dirs.Count <= keep) return;

        var entries = new List<KeyValuePair<string, DateTime>>();
        foreach (string dir in dirs)
        {
            try
            {
                entries.Add(new KeyValuePair<string, DateTime>(dir, Directory.GetCreationTimeUtc(dir)));
            }
            catch
            {
            }
        }

        entries.Sort((left, right) =>
        {
            int result = right.Value.CompareTo(left.Value);
            if (result != 0) return result;
            return string.CompareOrdinal(Path.GetFileName(right.Key), Path.GetFileName(left.Key));
        });

        for (int i = keep; i < entries.Count; i++)
        {
            try
            {
                Directory.Delete(entries[i].Key, true);
            }
            catch (Exception ex)
            {
                SpriteReplacerPlugin.Log.LogWarning("Could not delete old backup " + entries[i].Key + ": " + ex.Message);
            }
        }
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
