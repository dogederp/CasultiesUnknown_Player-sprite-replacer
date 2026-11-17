using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class MenuGate
{
    private static bool _resolved;
    private static FieldInfo _instanceField;
    private static FieldInfo _contentWarningField;
    private static FieldInfo _tutorialWarningField;
    private static FieldInfo _runSettingsScreenField;
    private static FieldInfo _creditsScreenField;
    private static FieldInfo _textObjField;

    internal static bool ShouldShowButton()
    {
        bool mainMenu = SceneManager.GetActiveScene().name == "PreGen" && IsMainMenuVisible();
        bool pauseMenu = PauseMenuProbe.IsOpen()
                      && !GameplayOverlayProbe.IsOpen()
                      && !SettingsMenuProbe.IsOpen();
        return mainMenu || pauseMenu;
    }

    // Main menu: hide the button until the startup screens and the intro
    // story close. This mirrors AdaptiveButton.overlayActive, the game's own
    // gate for its menu buttons. didIntro is static and resets each launch,
    // so it cannot show that the intro has ended. ScrollableText.textObj is
    // null when no intro is alive.
    internal static bool IsMainMenuVisible()
    {
        Resolve();

        object preRun = _instanceField != null ? _instanceField.GetValue(null) : null;
        if (!IsAlive(preRun)) return true;

        if (IsActive(_runSettingsScreenField, preRun)) return false;
        if (IsActive(_contentWarningField, preRun)) return false;
        if (IsActive(_tutorialWarningField, preRun)) return false;
        if (IsActive(_creditsScreenField, preRun)) return false;
        if (SettingsMenuProbe.IsOpen()) return false;

        if (_textObjField != null && IsAlive(_textObjField.GetValue(null)))
            return false;

        return true;
    }

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Assembly asm = typeof(Limb).Assembly;
        Type preRunType = asm.GetType("PreRunScript");
        if (preRunType != null)
        {
            _instanceField = preRunType.GetField("instance",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            _contentWarningField = preRunType.GetField("contentWarning",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _tutorialWarningField = preRunType.GetField("tutorialWarning",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _runSettingsScreenField = preRunType.GetField("runSettingsScreen",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _creditsScreenField = preRunType.GetField("creditsScreen",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }
        if (_instanceField == null)
            SpriteReplacerPlugin.Log.LogWarning("Could not find PreRunScript.instance via reflection");

        Type scrollType = asm.GetType("ScrollableText");
        if (scrollType != null)
            _textObjField = scrollType.GetField("textObj",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (_textObjField == null)
            SpriteReplacerPlugin.Log.LogWarning("Could not find ScrollableText.textObj via reflection");
    }

    private static bool IsActive(FieldInfo field, object instance)
    {
        if (field == null || instance == null) return false;
        GameObject go = field.GetValue(instance) as GameObject;
        return go != null && go.activeSelf;
    }

    private static bool IsAlive(object value)
    {
        if (value is UnityEngine.Object unityObject) return unityObject != null;
        return value != null;
    }
}

internal static class PauseMenuProbe
{
    private static bool _resolved;
    private static FieldInfo _mainField;
    private static FieldInfo _pauseContainerField;
    private static PropertyInfo _pausedProperty;
    private static FieldInfo _pausedField;

    internal static bool IsOpen()
    {
        Resolve();
        if (_mainField == null || _pauseContainerField == null) return false;

        object main = _mainField.GetValue(null);
        if (!IsAlive(main)) return false;

        GameObject container = _pauseContainerField.GetValue(main) as GameObject;
        if (container == null || !container.activeInHierarchy) return false;

        if (_pausedProperty != null)
        {
            MethodInfo getter = _pausedProperty.GetGetMethod(true);
            object value = getter != null && getter.IsStatic
                ? _pausedProperty.GetValue(null, null)
                : _pausedProperty.GetValue(main, null);
            return value is bool paused && paused;
        }

        if (_pausedField != null)
            return _pausedField.GetValue(main) is bool pausedField && pausedField;

        return true;
    }

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Type type = typeof(Limb).Assembly.GetType("PauseHandler");
        if (type == null)
        {
            SpriteReplacerPlugin.Log.LogWarning("Could not find PauseHandler via reflection");
            return;
        }

        _mainField = type.GetField("main", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        _pauseContainerField = type.GetField("pauseContainer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _pausedProperty = type.GetProperty("paused", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        _pausedField = type.GetField("isPaused", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (_mainField == null)
            SpriteReplacerPlugin.Log.LogWarning("Could not find PauseHandler.main via reflection");
        if (_pauseContainerField == null)
            SpriteReplacerPlugin.Log.LogWarning("Could not find PauseHandler.pauseContainer via reflection");
        if (_pausedProperty == null && _pausedField == null)
            SpriteReplacerPlugin.Log.LogWarning("Could not find PauseHandler.paused via reflection");
    }

    private static bool IsAlive(object value)
    {
        if (value is UnityEngine.Object unityObject) return unityObject != null;
        return value != null;
    }
}

internal static class GameplayOverlayProbe
{
    private static bool _resolved;
    private static FieldInfo _mainField;
    private static FieldInfo _woundViewField;
    private static FieldInfo _craftingPanelField;
    private static FieldInfo _tradeMenuField;
    private static FieldInfo _radialOpenField;

    internal static bool IsOpen()
    {
        Resolve();
        if (_mainField == null) return false;

        object main = _mainField.GetValue(null);
        if (!IsAlive(main)) return false;

        if (IsActive(_woundViewField, main)) return true;
        if (IsActive(_craftingPanelField, main)) return true;
        if (IsActive(_tradeMenuField, main)) return true;
        if (_radialOpenField != null && _radialOpenField.GetValue(main) is bool radial && radial) return true;

        return false;
    }

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Type type = typeof(Limb).Assembly.GetType("PlayerCamera");
        if (type == null)
        {
            SpriteReplacerPlugin.Log.LogWarning("Could not find PlayerCamera via reflection");
            return;
        }

        _mainField = type.GetField("main", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        _woundViewField = type.GetField("woundView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _craftingPanelField = type.GetField("craftingPanel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _tradeMenuField = type.GetField("tradeMenu", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _radialOpenField = type.GetField("radialOpen", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (_mainField == null)
            SpriteReplacerPlugin.Log.LogWarning("Could not find PlayerCamera.main via reflection");
    }

    private static bool IsActive(FieldInfo field, object instance)
    {
        if (field == null) return false;
        GameObject go = field.GetValue(instance) as GameObject;
        return go != null && go.activeInHierarchy;
    }

    private static bool IsAlive(object value)
    {
        if (value is UnityEngine.Object unityObject) return unityObject != null;
        return value != null;
    }
}

internal static class SettingsMenuProbe
{
    private static bool _resolved;
    private static FieldInfo _instanceField;

    internal static bool IsOpen()
    {
        Resolve();
        if (_instanceField == null) return false;

        object instance = _instanceField.GetValue(null);
        if (instance is UnityEngine.Object unityObject) return unityObject != null;
        return instance != null;
    }

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Type type = typeof(Limb).Assembly.GetType("SettingsMenu");
        if (type == null)
        {
            SpriteReplacerPlugin.Log.LogWarning("Could not find SettingsMenu via reflection");
            return;
        }

        _instanceField = type.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (_instanceField == null)
            SpriteReplacerPlugin.Log.LogWarning("Could not find SettingsMenu.instance via reflection");
    }
}
