using System.Collections.Generic;
using UnityEngine;

// Holds one skin map for one body. The local body gets the active slot.
// Remote bodies stay on the game sprites until a synced skin arrives.
public sealed class BodySkinBinding : MonoBehaviour
{
    private readonly List<SkinSpriteReplacer> _replacers = new List<SkinSpriteReplacer>();
    private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
    private IReadOnlyDictionary<string, Sprite> _sprites;

    internal void Bind(Body body, IReadOnlyDictionary<string, Sprite> sprites)
    {
        if (body == null) return;

        _renderers.Clear();
        foreach (SpriteRenderer renderer in body.GetComponentsInChildren<SpriteRenderer>(true))
            _renderers.Add(renderer);

        _sprites = sprites;
        Apply();
    }

    internal void Apply()
    {
        foreach (SkinSpriteReplacer replacer in _replacers)
        {
            if (replacer != null) replacer.Restore();
        }
        _replacers.Clear();

        if (_sprites == null) return;

        foreach (SpriteRenderer renderer in _renderers)
        {
            if (renderer == null) continue;

            SkinSpriteReplacer replacer = SkinSpriteReplacer.Attach(renderer, _sprites);
            replacer.Apply();
            _replacers.Add(replacer);
        }
    }

    internal void Reset()
    {
        foreach (SkinSpriteReplacer replacer in _replacers)
        {
            if (replacer == null) continue;
            replacer.Restore();
            Destroy(replacer);
        }
        _replacers.Clear();
        _sprites = null;
    }

    private void OnDestroy() => Reset();
}
