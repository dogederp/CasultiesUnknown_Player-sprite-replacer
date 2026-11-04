using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// One skin zip. The sender builds it from a slot folder. The receiver parses
// it into sprites and owns the textures until the session ends.
internal sealed class MpSkinPackage
{
    internal string SetName;
    internal string Hash;
    internal string ContentHash;
    internal byte[] Zip;
    internal Dictionary<string, Sprite> Sprites;
    internal readonly List<Texture2D> Textures = new List<Texture2D>();

    internal static MpSkinPackage FromSlot(string slot)
    {
        string root = Path.Combine(SpriteReplacerPlugin.SpritesPath, slot);
        if (!Directory.Exists(root)) return null;

        using var memory = new MemoryStream();
        using var content = SHA256.Create();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            AddFolder(zip, Path.Combine(root, "Body"), "Body", SkinImporter.BodyNames, content);
            AddFolder(zip, Path.Combine(root, "Head"), "Head", SkinImporter.HeadNames, content);
        }

        byte[] bytes = memory.ToArray();
        if (!MpSkinRules.ZipSizeOk(bytes.Length)) return null;

        content.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return new MpSkinPackage
        {
            SetName = SlotFolders.DisplayName(slot),
            Hash = HashHex(bytes),
            ContentHash = Hex(content.Hash),
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

        using var content = SHA256.Create();
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

                HashPart(content, path, data);

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

        content.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        package.ContentHash = Hex(content.Hash);
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
        HashSet<string> names, SHA256 content)
    {
        if (!Directory.Exists(folder)) return;

        string[] files = Directory.GetFiles(folder, "*.png");
        Array.Sort(files, StringComparer.Ordinal);

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!names.Contains(name)) continue;

            byte[] data = File.ReadAllBytes(file);
            string entryName = entryFolder + "/" + Path.GetFileName(file);
            HashPart(content, entryName, data);

            ZipArchiveEntry entry = zip.CreateEntry(entryName);
            using Stream target = entry.Open();
            target.Write(data, 0, data.Length);
        }
    }

    private static void HashPart(SHA256 sha, string name, byte[] data)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] lengthBytes = BitConverter.GetBytes(data.Length);
        sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
        sha.TransformBlock(lengthBytes, 0, lengthBytes.Length, null, 0);
        sha.TransformBlock(data, 0, data.Length, null, 0);
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
        return Hex(sha.ComputeHash(data));
    }

    private static string Hex(byte[] hash)
    {
        var text = new StringBuilder(hash.Length * 2);
        foreach (byte value in hash) text.Append(value.ToString("x2"));
        return text.ToString();
    }
}
