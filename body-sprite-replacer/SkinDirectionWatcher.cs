using UnityEngine;

// Re-applies the side sprite when the body turns. The game toggles isRight
// and flips the body scale in SwitchDir. The watcher runs after that change
// and before the next frame renders.
public sealed class SkinDirectionWatcher : MonoBehaviour
{
    private Body _body;
    private bool _lastRight;
    private bool _lastMirror;
    private bool _ready;

    internal void Bind(Body body)
    {
        if (body == null || body == _body) return;

        _body = body;
        _lastRight = body.isRight;
        _lastMirror = SpriteReplacerPlugin.IsMirrored(body);
        _ready = true;
    }

    private void LateUpdate()
    {
        if (_body == null)
        {
            _body = GetComponent<Body>();
            if (_body == null) return;
            _lastRight = _body.isRight;
            _lastMirror = SpriteReplacerPlugin.IsMirrored(_body);
            _ready = true;
            return;
        }

        if (!_ready)
        {
            _lastRight = _body.isRight;
            _lastMirror = SpriteReplacerPlugin.IsMirrored(_body);
            _ready = true;
            return;
        }

        bool isRight = _body.isRight;
        bool mirror = SpriteReplacerPlugin.IsMirrored(_body);
        if (isRight == _lastRight && mirror == _lastMirror) return;

        _lastRight = isRight;
        _lastMirror = mirror;
        SpriteReplacerPlugin.OnBodyDirectionChanged(_body);
    }
}
