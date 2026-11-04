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

    public static SkinSpriteReplacer Attach(SpriteRenderer renderer,
        IReadOnlyDictionary<string, Sprite> sprites)
    {
        SkinSpriteReplacer replacer = renderer.GetComponent<SkinSpriteReplacer>();
        if (replacer == null)
        {
            replacer = renderer.gameObject.AddComponent<SkinSpriteReplacer>();
            replacer.Original = renderer.sprite;
        }

        replacer.Renderer = renderer;
        replacer.Sprites = sprites;
        return replacer;
    }

    public void SetSprites(IReadOnlyDictionary<string, Sprite> sprites)
    {
        Sprites = sprites;
        Apply();
    }

    public void Apply()
    {
        Active = true;
        if (Renderer == null) return;
        if (Original != null) Renderer.sprite = Original;
        if (Renderer.sprite == null || Sprites == null) return;
        if (Sprites.TryGetValue(Renderer.sprite.name, out Sprite replacement))
            Renderer.sprite = replacement;
    }

    public void Restore()
    {
        Active = false;
        if (Renderer != null) Renderer.sprite = Original;
    }

    private void OnDestroy() => Restore();
}

[HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
internal static class SkinSpritePatch
{
    private static void Prefix(SpriteRenderer __instance, ref Sprite value)
    {
        SkinSpriteReplacer replacer = __instance.GetComponent<SkinSpriteReplacer>();
        if (replacer == null || !replacer.Active || value == null || replacer.Sprites == null) return;
        if (replacer.Sprites.TryGetValue(value.name, out Sprite replacement))
            value = replacement;
    }
}
