using HarmonyLib;
using UI.Map;
using UnityEngine;

namespace MapEnhancer;

internal static class MapToolbarIntegration
{
    private static bool _enabled;

    internal static void Acquire()
    {
        _enabled = true;
        foreach (var window in UnityEngine.Object.FindObjectsByType<MapWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (window == null) continue;
            window._displayMenu?.Dismiss();
            window._toolbarPanel?.SetNeedsRebuild();
        }
    }

    internal static void Release()
    {
        _enabled = false;
        foreach (var window in UnityEngine.Object.FindObjectsByType<MapWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (window == null) continue;
            window._toolbarPanel?.SetNeedsRebuild();
        }
    }

    [HarmonyPatch(typeof(MapWindow), nameof(MapWindow.BuildDisplayOptionsButton))]
    private static class DisplayOptionsButtonPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !_enabled;
    }
}
