using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Patches each target one at a time. A missing or changed target logs a
// warning and skips that feature, so one full-game change cannot abort the
// whole mod load.
internal static class PatchLoader
{
    internal static void Apply(Harmony harmony)
    {
        Patch(harmony, typeof(Limb), "Awake", Type.EmptyTypes, typeof(LimbAwakePatch), null, "Postfix");
        Patch(harmony, typeof(FacialExpression), "Start", Type.EmptyTypes, typeof(FacialExpressionStartPatch), null, "Postfix");
        Patch(harmony, typeof(TailScript), "Start", Type.EmptyTypes, typeof(TailScriptStartPatch), null, "Postfix");
        Patch(harmony, typeof(Settings), "ApplyAll", Type.EmptyTypes, typeof(SettingsApplyAllPatch), "Prefix", null);
        Patch(harmony, typeof(WoundView), "Awake", Type.EmptyTypes, typeof(WoundViewAwakePatch), null, "Postfix");
        Patch(harmony, typeof(WoundView), "UpdateView", Type.EmptyTypes, typeof(WoundViewUpdatePatch), null, "Postfix");
        Patch(harmony, typeof(PlayerCamera), "Awake", Type.EmptyTypes, typeof(PlayerCameraAwakePatch), null, "Postfix");
        PatchSetter(harmony, typeof(SpriteRenderer), "sprite", typeof(SkinSpritePatch), "Prefix");

        Type adaptive = typeof(Limb).Assembly.GetType("AdaptiveButton", false);
        if (adaptive != null)
        {
            Patch(harmony, adaptive, "Clicked", Type.EmptyTypes, typeof(MainMenuGuardPatch), "Prefix", null);
        }
        else
        {
            Log("Patch target not found: AdaptiveButton. Main menu guard skipped.");
        }
    }

    private static void Patch(Harmony harmony, Type type, string name, Type[] parameters,
        Type patchType, string prefixName, string postfixName)
    {
        MethodInfo target = FindTarget(type, name, parameters);
        if (target == null) return;

        MethodInfo prefix = prefixName == null ? null : AccessTools.Method(patchType, prefixName);
        MethodInfo postfix = postfixName == null ? null : AccessTools.Method(patchType, postfixName);
        if (prefix == null && postfix == null)
        {
            Log("No patch method on " + patchType.Name + ". Skipped " + type.Name + "." + name + ".");
            return;
        }

        try
        {
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix));
            Log("Patched " + type.Name + "." + name + ".");
        }
        catch (Exception ex)
        {
            Log("Patch failed for " + type.Name + "." + name + ": " + ex.Message);
        }
    }

    private static void PatchSetter(Harmony harmony, Type type, string property,
        Type patchType, string prefixName)
    {
        MethodInfo target = null;
        try
        {
            target = AccessTools.PropertySetter(type, property);
        }
        catch (Exception ex)
        {
            Log("Patch lookup failed for " + type.Name + "." + property + ": " + ex.Message);
        }

        if (target == null)
        {
            Log("Patch target not found: " + type.Name + "." + property + " setter. Skipped.");
            return;
        }

        MethodInfo prefix = AccessTools.Method(patchType, prefixName);
        if (prefix == null)
        {
            Log("No patch method on " + patchType.Name + ". Skipped " + type.Name + "." + property + ".");
            return;
        }

        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            Log("Patched " + type.Name + "." + property + " setter.");
        }
        catch (Exception ex)
        {
            Log("Patch failed for " + type.Name + "." + property + " setter: " + ex.Message);
        }
    }

    private static MethodInfo FindTarget(Type type, string name, Type[] parameters)
    {
        try
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            if (method != null) return method;
        }
        catch (Exception ex)
        {
            Log("Patch lookup failed for " + type.Name + "." + name + ": " + ex.Message);
            return null;
        }

        Log("Patch target not found: " + type.Name + "." + name + ". Skipped.");
        return null;
    }

    private static void Log(string message)
    {
        if (SpriteReplacerPlugin.Log != null)
            SpriteReplacerPlugin.Log.LogInfo("[Patch] " + message);
    }
}

// The main menu uses AdaptiveButton, which reads the mouse directly and
// bypasses the UI event system. Block its action while the custom menu is
// open.
public static class MainMenuGuardPatch
{
    static bool Prefix()
    {
        return !SkinMenuUI.IsPopupOpen;
    }
}
