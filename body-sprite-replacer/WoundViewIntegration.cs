using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal static class WoundViewIntegration
{
    private static readonly Dictionary<int, Sprite> Originals = new Dictionary<int, Sprite>();

    internal static void ApplyAll()
    {
        foreach (WoundView view in Resources.FindObjectsOfTypeAll<WoundView>())
        {
            if (view == null) continue;
            if (!view.gameObject.scene.IsValid()) continue;
            Apply(view);
        }
    }

    internal static void Apply(WoundView view)
    {
        if (view == null) return;

        var replaced = new HashSet<string>();
        foreach (Image image in view.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null) continue;

            Sprite original;
            if (!Originals.TryGetValue(image.GetInstanceID(), out original) || original == null)
            {
                original = image.sprite;
                Originals[image.GetInstanceID()] = original;
            }
            DefaultSpriteExport.Capture(original);

            Sprite replacement;
            if (SpriteReplacerPlugin.LoadedSprites.TryGetValue(original.name, out replacement))
            {
                image.sprite = replacement;
                replaced.Add(replacement.name);
            }
            else
            {
                image.sprite = original;
            }
        }

        foreach (SpriteRenderer renderer in view.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null || renderer.sprite == null) continue;

            Sprite original;
            if (!Originals.TryGetValue(renderer.GetInstanceID(), out original) || original == null)
            {
                original = renderer.sprite;
                Originals[renderer.GetInstanceID()] = original;
            }
            DefaultSpriteExport.Capture(original);

            Sprite replacement;
            if (SpriteReplacerPlugin.LoadedSprites.TryGetValue(original.name, out replacement))
            {
                renderer.sprite = replacement;
                replaced.Add(replacement.name);
            }
            else
            {
                renderer.sprite = original;
            }
        }

        if (replaced.Count > 0)
        {
            SpriteReplacerPlugin.Log.LogInfo("Applied " + replaced.Count + " wound view sprites.");
        }

        DefaultSpriteExport.ExportNew();
    }

    // The game tints the inner wound layer near black. Keep custom colors visible.
    internal static void ShowCustomColors(WoundView view)
    {
        if (view == null || view.limbImages == null) return;

        foreach (Image image in view.limbImages)
        {
            if (image == null || image.transform.childCount == 0) continue;

            Image middle = image.transform.GetChild(0).GetComponent<Image>();
            if (middle == null || middle.sprite == null) continue;
            if (!SpriteReplacerPlugin.ColoredWoundSprites.Contains(middle.sprite.name)) continue;

            Color color = middle.color;
            middle.color = new Color(1f, 1f, 1f, color.a);
        }
    }
}
