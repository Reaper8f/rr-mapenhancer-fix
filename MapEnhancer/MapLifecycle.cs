using System;
using Core;
using Game.Events;
using Game.State;
using MapEnhancer.Native;
using Track;
using UI.Map;
using UI.Windows;
using UnityEngine;

namespace MapEnhancer;

public partial class MapEnhancer
{
    private bool _mapLoadPending;
    private bool _mapResourcesAllocated;
    private bool _lifecycleStopped;
    private MapWindow? _mapWindow;
    private Window? _subscribedMapWindow;
    private Graph? _mapGraph;
    private float _nextMapLoadAttempt;
    private string? _mapLoadFailure;

    private void Start()
    {
        EventBus.Subscribe<MapDidLoadEvent>(this, OnMapDidLoad);
        EventBus.Subscribe<MapWillUnloadEvent>(this, OnMapWillUnload);
        EventBus.Subscribe<GraphDidRebuildCollections>(this, _ => _visualRefreshPending = true);
        EventBus.Subscribe<IndustriesDidChange>(this, _ => _visualRefreshPending = true);
        // Also covers enabling the mod after MapDidLoad, or during world loading.
        _mapLoadPending = true;
    }

    private static MapWindow? FindMapWindow()
    {
        var manager = WindowManager.Shared;
        // Both MapWindow.instance and GetWindow<T> can throw before registration.
        return manager != null ? manager.GetComponentInChildren<MapWindow>(true) : null;
    }

    private void OnMapDidLoad(MapDidLoadEvent evt)
    {
        if (_lifecycleStopped) return;
        _mapLoadPending = true;
    }

    private void UpdateMapLifecycle()
    {
        if (_lifecycleStopped) return;
        var state = StateManager.Shared;
        if (StateManager.IsUnloading)
        {
            OnMapWillUnload(default);
            return;
        }
        if (!_mapLoadPending && !_mapResourcesAllocated) return;

        var window = FindMapWindow();
        if (_mapResourcesAllocated && (_mapWindow == null || window != _mapWindow ||
            Graph.Shared != _mapGraph))
        {
            ReleaseMapLifecycle();
            _mapLoadPending = true;
        }
        if (!_mapLoadPending) return;
        if (_mapResourcesAllocated)
        {
            // Duplicate load notifications must not duplicate handlers or markers.
            _mapLoadPending = false;
            return;
        }
        if (Time.unscaledTime < _nextMapLoadAttempt || state == null || !state.IsSessionReady ||
            Graph.Shared == null || !Graph.Shared.HasPopulatedCollections || window == null ||
            window._window == null || window._renderTexture == null || window._toolbarPanel == null ||
            window.mapDrag == null || window.mapBuilder == null || window.mapBuilder != MapBuilder.Shared ||
            window.mapBuilder.mapCamera == null) return;

        _mapWindow = window;
        _mapGraph = Graph.Shared;
        _mapResourcesAllocated = true;
        try
        {
            _subscribedMapWindow = window._window;
            _subscribedMapWindow.OnShownDidChange += OnMapWindowShown;
            InitializeMap(window);
            _mapLoadPending = false;
            _mapLoadFailure = null;
        }
        catch (Exception error)
        {
            // Release partial initialization before retrying; never leave MAPLOADED set.
            ReleaseMapLifecycle();
            _mapLoadPending = true;
            _nextMapLoadAttempt = Time.unscaledTime + 1f;
            string failure = error.ToString();
            if (_mapLoadFailure != failure)
            {
                _mapLoadFailure = failure;
                Loader.Context?.Log.Info("Map Enhancer initialization will retry: " + failure);
            }
        }
    }

    private void OnMapWillUnload(MapWillUnloadEvent evt)
    {
        _mapLoadPending = false;
        _nextMapLoadAttempt = 0f;
        _mapLoadFailure = null;
        ReleaseMapLifecycle();
    }

    private void ReleaseMapLifecycle()
    {
        MapState = MapStates.MAPUNLOADING;
        _visualRefreshPending = false;
        // Detach from the exact window we subscribed to, even if Shared was replaced.
        if (_subscribedMapWindow != null)
            _subscribedMapWindow.OnShownDidChange -= OnMapWindowShown;
        _subscribedMapWindow = null;
        _mapWindow = null;
        _mapGraph = null;
        if (!_mapResourcesAllocated) return;
        _mapResourcesAllocated = false;
        CleanupMap();
    }

    private void StopMapLifecycle()
    {
        _lifecycleStopped = true;
        EventBus.Unsubscribe<MapDidLoadEvent>(this);
        EventBus.Unsubscribe<MapWillUnloadEvent>(this);
        EventBus.Unsubscribe<GraphDidRebuildCollections>(this);
        EventBus.Unsubscribe<IndustriesDidChange>(this);
        OnMapWillUnload(default);
    }
}
