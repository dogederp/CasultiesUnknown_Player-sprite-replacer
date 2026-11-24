using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// One renderer that belongs to a skinned body. The setter patch maps any
// sprite the game assigns by name, so expressions and limb changes keep the
// custom look.
public sealed class SkinSpriteReplacer : MonoBehaviour
{
    public SpriteRenderer Renderer { get; private set; }
    public Sprite Original { get; private set; }
    public bool Active { get; private set; }
    public IReadOnlyDictionary<string, Sprite> Sprites { get; private set; }
    public Body Body { get; private set; }

    public static SkinSpriteReplacer Attach(SpriteRenderer renderer,
        IReadOnlyDictionary<string, Sprite> sprites, Body body = null)
    {
        SkinSpriteReplacer replacer = renderer.GetComponent<SkinSpriteReplacer>();
        if (replacer == null)
        {
            replacer = renderer.gameObject.AddComponent<SkinSpriteReplacer>();
            replacer.Original = renderer.sprite;
        }

        DefaultSpriteExport.Capture(replacer.Original);

        replacer.Renderer = renderer;
        replacer.Sprites = sprites;
        replacer.Body = body;
        return replacer;
    }

    public void SetSprites(IReadOnlyDictionary<string, Sprite> sprites, Body body)
    {
        Sprites = sprites;
        Body = body;
        Apply();
    }

    public void Apply()
    {
        Active = true;
        if (Renderer == null) return;
        if (Original != null) Renderer.sprite = Original;
        if (Renderer.sprite == null || Sprites == null)
        {
            SpriteReplacerPlugin.ApplySideFlip(Renderer, false, Body);
            return;
        }

        if (SpriteReplacerPlugin.TryGetSprite(Sprites, Renderer.sprite.name, Body,
            out Sprite replacement, out bool side))
        {
            Renderer.sprite = replacement;
        }

        SpriteReplacerPlugin.ApplySideFlip(Renderer, side, Body);
    }

    internal bool TryMap(Sprite value, out Sprite result)
    {
        result = value;
        if (!Active || value == null || Sprites == null) return false;

        if (!SpriteReplacerPlugin.TryGetSprite(Sprites, value.name, Body,
            out Sprite replacement, out bool side))
        {
            SpriteReplacerPlugin.ApplySideFlip(Renderer, false, Body);
            return false;
        }

        result = replacement;
        SpriteReplacerPlugin.ApplySideFlip(Renderer, side, Body);
        return true;
    }

    public void Restore()
    {
        Active = false;
        if (Renderer != null)
        {
            SpriteReplacerPlugin.ApplySideFlip(Renderer, false, Body);
            Renderer.sprite = Original;
        }
    }

    private void OnDestroy() => Restore();
}

[HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
internal static class SkinSpritePatch
{
    private static void Prefix(SpriteRenderer __instance, ref Sprite value)
    {
        DefaultSpriteExport.Capture(value);

        SkinSpriteReplacer replacer = __instance.GetComponent<SkinSpriteReplacer>();
        if (replacer == null || !replacer.Active || value == null || replacer.Sprites == null) return;
        if (replacer.TryMap(value, out Sprite replacement))
            value = replacement;
    }
}
