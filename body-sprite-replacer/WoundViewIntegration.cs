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

    // The game sets the Milky and Dune wound head sprites in WoundView.Start,
    // after WoundView.Awake. Re-capture those two, so the species sprite is
    // the base for the custom one.
    internal static void ForgetHeadOriginals(WoundView view)
    {
        if (view == null || view.limbImages == null || view.limbImages.Length == 0) return;

        Image head = view.limbImages[0];
        if (head == null) return;

        Originals.Remove(head.GetInstanceID());
        if (head.transform.childCount == 0) return;

        Image middle = head.transform.GetChild(0).GetComponent<Image>();
        if (middle != null) Originals.Remove(middle.GetInstanceID());
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

        ApplySpeciesHead(view, replaced);

        if (replaced.Count > 0)
        {
            SpriteReplacerPlugin.Log.LogInfo("Applied " + replaced.Count
                + " wound view sprites: " + string.Join(", ", replaced) + ".");
        }

        DefaultSpriteExport.ExportNew();
    }

    // The game sets the species head sprites in WoundView.Start, but a slot
    // switch can run before that call. Set the species head directly from the
    // active slot, so the order of the slot switch and the view start does not
    // matter.
    private static void ApplySpeciesHead(WoundView view, HashSet<string> replaced)
    {
        if (view == null || view.body == null) return;
        if (view.limbImages == null || view.limbImages.Length == 0) return;

        string outer;
        string middle;
        switch (view.body.charType)
        {
            case 1:
                outer = "milkheadouter";
                middle = "milkheadmiddle";
                break;
            case 2:
                outer = "headOuterDune";
                middle = "headMiddleDune";
                break;
            default:
                return;
        }

        Image head = view.limbImages[0];
        if (head == null) return;

        Sprite sprite;
        if (SpriteReplacerPlugin.LoadedSprites.TryGetValue(outer, out sprite))
        {
            head.sprite = sprite;
            replaced.Add(outer);
        }

        if (head.transform.childCount == 0) return;

        Image inner = head.transform.GetChild(0).GetComponent<Image>();
        if (inner == null) return;

        if (SpriteReplacerPlugin.LoadedSprites.TryGetValue(middle, out sprite))
        {
            inner.sprite = sprite;
            replaced.Add(middle);
        }
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
