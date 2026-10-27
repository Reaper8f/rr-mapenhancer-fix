using System;
using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using Model.Ops;
using Track;
using UI;
using UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using Loader = MapEnhancer.Native.Loader;

namespace MapEnhancer;

internal static class IndustryMapClick
{
    private static readonly OptionalIndustryMenu Menu = new OptionalIndustryMenu(
        () => Loader.Context != null && Loader.Context.CheckDependency(OptionalIndustryMenu.DependencyExpression),
        () => ResolveLoadedType(OptionalIndustryMenu.WindowTypeName),
        message => Loader.Context?.Log.Info(message), IsProviderReady);

    private static Type? ResolveLoadedType(string typeName)
    {
        // Never load a DLL from disk: installed-but-disabled mods must remain inactive.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "PrettyConvenientMenu")
                return assembly.GetType(typeName, false);
        return null;
    }

    private static bool IsProviderReady()
    {
        var type = ResolveLoadedType("PrettyConvenientMenu.Interaction.IndustryWorldInteraction");
        var instance = type?.GetProperty("Instance", System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static)?.GetValue(null, null) as UnityEngine.Object;
        // A selected pack may have failed initialization, or its world host may have been destroyed.
        return instance != null;
    }

    internal static void Reset() => Menu.Reset();

    [HarmonyPatch(typeof(MapDrag), nameof(MapDrag.OnPointerClick))]
    private static class MapTrackClickPatch
    {
        private static bool Prefix(MapDrag __instance, PointerEventData data) => !TryOpen(__instance, data, Menu);
    }

    internal static bool TryOpen(MapDrag drag, PointerEventData data, OptionalIndustryMenu menu)
    {
        try
        {
            if (data == null || data.button != PointerEventData.InputButton.Left || drag == null || drag._isDragging)
                return false;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return false;
            var window = MapWindow.instance;
            if (MapEnhancer.Instance == null || MapEnhancer.MapState != MapEnhancer.MapStates.MAPLOADED ||
                window == null || window.mapDrag != drag || window._window == null || !window._window.IsShown ||
                Native.SettingsWindow.IsOpen || !GameInput.IsMouseOverGameWindow(window._window)) return false;
            if (TrackLocationPicker.Shared != null && TrackLocationPicker.Shared.IsPicking) return false;
            if (!menu.IsAvailable) return false;
            var camera = window.mapBuilder?.mapCamera;
            if (camera == null || Graph.Shared == null || OpsController.Shared == null || drag.RectHeight <= 0f)
                return false;
            var viewportPoint = drag.NormalizedMousePosition();
            if (viewportPoint.x < 0f || viewportPoint.x > 1f || viewportPoint.y < 0f || viewportPoint.y > 1f)
                return false;
            var ray = camera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            // Existing map markers retain priority over the track beneath them.
            if (Physics.Raycast(ray, out _, camera.transform.position.y, 1 << Layers.Map)) return false;
            if (!window.TryGetGamePointForViewportNormalizedPoint(viewportPoint, out var gamePoint)) return false;
            float radius = camera.orthographicSize * 2f * 6f / drag.RectHeight;
            if (!Graph.Shared.TryGetLocationFromGamePoint(gamePoint, radius, out var location)) return false;
            return menu.TryOpen(FindIndustry(OpsController.Shared.AllIndustries, location));
        }
        catch (Exception error)
        {
            menu.ReportFailure(error.GetBaseException().Message);
            return false;
        }
    }

    internal static Industry? FindIndustry(IEnumerable<Industry> industries, Location location)
    {
        Industry? found = null;
        foreach (var industry in industries)
        {
            if (industry == null) continue;
            foreach (var component in industry.Components)
            {
                if (component == null || !component.IsProgressionEnabled) continue;
                foreach (var span in component.TrackSpans)
                {
                    // Test the actual location, not just the segment: spans can share a segment.
                    if (span == null || !span.Contains(location)) continue;
                    if (found != null && found != industry) return null;
                    found = industry;
                }
            }
        }
        return found;
    }
}
