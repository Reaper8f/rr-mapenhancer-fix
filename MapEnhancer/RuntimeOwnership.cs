using System;
using System.Collections.Generic;
using Model;
using UI.Map;
using UnityEngine;

namespace MapEnhancer
{
    public partial class MapEnhancer
    {
        private readonly Dictionary<Game.FlarePickable, MapIcon> _ownedFlareIcons = new Dictionary<Game.FlarePickable, MapIcon>();
        private readonly Dictionary<Car, MapIcon> _ownedCarIcons = new Dictionary<Car, MapIcon>();
        private readonly Dictionary<MapIcon, Action> _originalClicks = new Dictionary<MapIcon, Action>();
        private static readonly List<UnityEngine.Object> OwnedAssets = new List<UnityEngine.Object>();
        private MapBuilder? _originalBuilder;
        private float _originalLineMin;
        private float _originalLineMax;

        private static void RememberClickHandler(MapIcon icon)
        {
            if (Instance != null && icon != null && !Instance._originalClicks.ContainsKey(icon))
                Instance._originalClicks.Add(icon, icon.OnClick);
        }

        private void CaptureNativeMapSettings()
        {
            _originalBuilder = MapBuilder.Shared;
            if (_originalBuilder == null) return;
            _originalLineMin = _originalBuilder.segmentLineWidthMin;
            _originalLineMax = _originalBuilder.segmentLineWidthMax;
        }

        private void ReleaseMapOwnership()
        {
            DestroyTraincarMarkers();
            DestroyFlareMarkers();
            foreach (var entry in _originalClicks)
                if (entry.Key != null) entry.Key.OnClick = entry.Value;
            _originalClicks.Clear();
            foreach (var helper in _turntableHelpers)
                if (helper != null) DestroyImmediate(helper);
            _turntableHelpers.Clear();
            foreach (var component in FindObjectsByType<TrainMarkerData>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                DestroyImmediate(component);
            foreach (var component in FindObjectsByType<SignalIconColorizer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                DestroyImmediate(component);
            if (_originalBuilder != null)
            {
                _originalBuilder.segmentLineWidthMin = _originalLineMin;
                _originalBuilder.segmentLineWidthMax = _originalLineMax;
                _originalBuilder.UpdateForZoom();
            }
            _originalBuilder = null;
        }

        private static void ResetStaticState()
        {
            foreach (var asset in OwnedAssets) if (asset != null) Destroy(asset);
            OwnedAssets.Clear();
            _mainlineSegments = null;
            _mainlineSwitches = null;
            _industrialSegments.Clear();
            _industrialSegmentColors.Clear();
            _industrialSegmentNames.Clear();
            _passengerStopSegments.Clear();
            _passengerStopSegmentNames.Clear();
            _segmentGrades.Clear();
            _spawnPointCategories.Clear();
            _prefabHolder = null;
            _traincarPrefab = null;
            _flarePrefab = null;
            _turntablePrefab = null;
            _crossingPrefab = null;
            JunctionMarker.junctionMarkerPrefabL = null;
            JunctionMarker.junctionMarkerPrefabR = null;
            JunctionMarker.matJunctionGreen = null;
            JunctionMarker.matJunctionRed = null;
        }
    }
}
