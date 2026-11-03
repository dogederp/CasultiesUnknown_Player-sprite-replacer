using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

// One skin zip. The sender builds it from a slot folder. The receiver parses
// it into sprites and owns the textures until the session ends.
internal sealed class MpSkinPackage
{
    internal string SetName;
    internal string Hash;
    internal byte[] Zip;
    internal Dictionary<string, Sprite> Sprites;
    internal readonly List<Texture2D> Textures = new List<Texture2D>();

    internal static MpSkinPackage FromSlot(string slot)
    {
        string root = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot);
        if (!Directory.Exists(root)) return null;

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            AddFolder(zip, Path.Combine(root, "Body"), "Body", SkinImporter.BodyNames);
            AddFolder(zip, Path.Combine(root, "Head"), "Head", SkinImporter.HeadNames);
        }

        byte[] bytes = memory.ToArray();
        if (!MpSkinRules.ZipSizeOk(bytes.Length)) return null;

        return new MpSkinPackage
        {
            SetName = SlotFolders.DisplayName(slot),
            Hash = HashHex(bytes),
            Zip = bytes
        };
    }

    internal static MpSkinPackage FromZip(byte[] zipBytes, string expectedHash, string setName)
    {
        if (!MpSkinRules.ZipSizeOk(zipBytes == null ? 0 : zipBytes.Length)) return null;
        if (!string.Equals(HashHex(zipBytes), expectedHash, StringComparison.OrdinalIgnoreCase)) return null;

        var package = new MpSkinPackage
        {
            SetName = Sanitize(setName),
            Hash = expectedHash.ToLowerInvariant(),
            Zip = zipBytes,
            Sprites = new Dictionary<string, Sprite>()
        };

        try
        {
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            int entries = 0;
            int total = 0;
            long pixels = 0;

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (!MpSkinRules.EntryCountOk(++entries)) return Reject(package);

                string path = entry.FullName.Replace('\\', '/');
                if (path.Contains("..") || path.StartsWith("/")) return Reject(package);

                string[] parts = path.Split('/');
                if (parts.Length != 2) continue;

                string folder = parts[0];
                if (folder != "Body" && folder != "Head") continue;

                string name = Path.GetFileNameWithoutExtension(parts[1]);
                HashSet<string> known = folder == "Body" ? SkinImporter.BodyNames : SkinImporter.HeadNames;
                if (!known.Contains(name)) continue;
                if (package.Sprites.ContainsKey(name)) continue;
                if (!MpSkinRules.EntrySizeOk(entry.Length)) return Reject(package);

                total += (int)entry.Length;
                if (!MpSkinRules.TotalSizeOk(total)) return Reject(package);
                if (!MpSkinRules.CompressionRatioOk(total, zipBytes.Length)) return Reject(package);

                byte[] data;
                using (Stream stream = entry.Open())
                {
                    if (!TryReadLimited(stream, MpSkinRules.MaxEntryBytes, out data))
                        return Reject(package);
                }

                if (!MpSkinRules.TryReadPngSize(data, out int pngWidth, out int pngHeight) ||
                    !MpSkinRules.TextureSizeOk(pngWidth, pngHeight))
                {
                    continue;
                }

                pixels += (long)pngWidth * pngHeight;
                if (!MpSkinRules.TexturePixelsOk(pixels)) return Reject(package);

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point
                };

                if (!ImageConversion.LoadImage(texture, data) ||
                    !MpSkinRules.TextureSizeOk(texture.width, texture.height))
                {
                    UnityEngine.Object.Destroy(texture);
                    continue;
                }

                Sprite sprite = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 8f);
                sprite.name = name;

                package.Sprites[name] = sprite;
                package.Textures.Add(texture);
            }
        }
        catch (Exception ex)
        {
            SpriteReplacerPlugin.Log.LogWarning("Skin package failed: " + ex.Message);
            return Reject(package);
        }

        return package.Sprites.Count == 0 ? Reject(package) : package;
    }

    internal void Dispose()
    {
        if (Sprites != null)
        {
            foreach (Sprite sprite in Sprites.Values)
            {
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
            }
            Sprites.Clear();
        }

        foreach (Texture2D texture in Textures)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
        Textures.Clear();
    }

    private static MpSkinPackage Reject(MpSkinPackage package)
    {
        package.Dispose();
        return null;
    }

    private static void AddFolder(ZipArchive zip, string folder, string entryFolder,
        HashSet<string> names)
    {
        if (!Directory.Exists(folder)) return;

        foreach (string file in Directory.GetFiles(folder, "*.png"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!names.Contains(name)) continue;

            ZipArchiveEntry entry = zip.CreateEntry(entryFolder + "/" + Path.GetFileName(file));
            using Stream target = entry.Open();
            using FileStream source = File.OpenRead(file);
            source.CopyTo(target);
        }
    }

    private static bool TryReadLimited(Stream stream, int limit, out byte[] data)
    {
        using var memory = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > limit)
            {
                data = null;
                return false;
            }
            memory.Write(buffer, 0, read);
        }

        data = memory.ToArray();
        return true;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "remote";
        value = value.Trim();
        return value.Length > 64 ? value.Substring(0, 64) : value;
    }

    private static string HashHex(byte[] data)
    {
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(data);
        var text = new System.Text.StringBuilder(hash.Length * 2);
        foreach (byte value in hash) text.Append(value.ToString("x2"));
        return text.ToString();
    }
}
