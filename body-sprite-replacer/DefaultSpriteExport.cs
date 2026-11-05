using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Writes the game's own body, head, and wound-view sprites to
// CustomSprites/default. The sprites are captured before the mod replaces
// them, so the folder always holds the game art.
internal static class DefaultSpriteExport
{
    private const string FolderName = "default";

    private static readonly Dictionary<string, Sprite> Captured =
        new Dictionary<string, Sprite>(StringComparer.Ordinal);

    internal static void Capture(Sprite sprite)
    {
        if (sprite == null) return;

        string name = sprite.name;
        if (string.IsNullOrEmpty(name) || Captured.ContainsKey(name)) return;
        if (FolderFor(name) == null) return;

        Captured[name] = sprite;
    }

    private static string FolderFor(string name)
    {
        if (SkinImporter.BodyNames.Contains(name)) return "Body";
        if (SkinImporter.HeadNames.Contains(name)) return "Head";
        if (SkinImporter.WoundViewNames.Contains(name)) return "WoundView";
        return null;
    }

    // Writes only the files that are not on disk yet. A user can delete one
    // file to get a fresh copy, and an edited file is never overwritten.
    internal static int ExportNew()
    {
        if (Captured.Count == 0) return 0;

        string root = Path.Combine(SpriteReplacerPlugin.SpritesPath, FolderName);
        int written = 0;

        foreach (KeyValuePair<string, Sprite> pair in Captured)
        {
            string folder = FolderFor(pair.Key);
            if (folder == null) continue;

            string path = Path.Combine(root, folder, pair.Key + ".png");
            if (File.Exists(path)) continue;

            byte[] png = Encode(pair.Value);
            if (png == null) continue;

            try
            {
                Directory.CreateDirectory(Path.Combine(root, folder));
                File.WriteAllBytes(path, png);
                written++;
            }
            catch (Exception ex)
            {
                SpriteReplacerPlugin.Log.LogWarning(
                    "Could not export default sprite " + path + ": " + ex.Message);
            }
        }

        if (written > 0)
            SpriteReplacerPlugin.Log.LogInfo(
                "Exported " + written + " default sprites to " + root);

        return written;
    }

    private static byte[] Encode(Sprite sprite)
    {
        Texture2D source = null;

        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        Texture2D full = null;
        Texture2D cropped = null;

        try
        {
            source = sprite.texture;
            if (source == null) return null;

            // Read through a render texture, so compressed and non-readable
            // game textures export the same way.
            rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            full = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            full.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            full.Apply();
            RenderTexture.active = previous;

            Rect rect = sprite.packed ? sprite.textureRect : sprite.rect;
            int x = Mathf.RoundToInt(rect.x);
            int y = Mathf.RoundToInt(rect.y);
            int w = Mathf.RoundToInt(rect.width);
            int h = Mathf.RoundToInt(rect.height);

            if (w <= 0 || h <= 0 || x < 0 || y < 0 ||
                x + w > source.width || y + h > source.height)
            {
                x = 0;
                y = 0;
                w = source.width;
                h = source.height;
            }

            Texture2D image = full;
            if (w != source.width || h != source.height)
            {
                cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
                cropped.SetPixels(full.GetPixels(x, y, w, h));
                cropped.Apply();
                image = cropped;
            }

            return ImageConversion.EncodeToPNG(image);
        }
        catch (Exception ex)
        {
            SpriteReplacerPlugin.Log.LogWarning(
                "Could not read sprite '" + sprite.name + "': " + ex.Message);
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (cropped != null) UnityEngine.Object.Destroy(cropped);
            if (full != null) UnityEngine.Object.Destroy(full);
        }
    }
}
