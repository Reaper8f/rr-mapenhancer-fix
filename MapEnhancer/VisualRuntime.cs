using System;
using System.Collections.Generic;
using System.Linq;
using Game.Events;
using Game.Mow;
using Track;
using UI.Map;
using UnityEngine;

namespace MapEnhancer
{
    public partial class MapEnhancer
    {
        private bool _visualRefreshPending;
        private float _nextOverlayRefresh;
        private static readonly Color[] MaintenanceColors =
        {
            new Color(0.75f, 0.15f, 0.18f), new Color(0.9f, 0.4f, 0.1f),
            new Color(0.9f, 0.75f, 0.15f), new Color(0.35f, 0.7f, 0.3f),
            new Color(0.1f, 0.65f, 0.85f)
        };
        private static readonly Color[] ClassColors =
        {
            new Color(0.7f, 0.15f, 0.15f), new Color(0.9f, 0.4f, 0.1f),
            new Color(0.9f, 0.75f, 0.15f), new Color(0.35f, 0.7f, 0.3f),
            new Color(0.15f, 0.7f, 0.8f), new Color(0.4f, 0.4f, 0.9f)
        };

        private void RefreshTrackCategories()
        {
            _industrialSegments.Clear();
            _industrialSegmentColors.Clear();
            _industrialSegmentNames.Clear();
            _passengerStopSegments.Clear();
            _passengerStopSegmentNames.Clear();
            _mainlineSegments = null!;
            _mainlineSwitches = null!;
            populateSegmentsAndSwitches();
            var ops = Model.Ops.OpsController.Shared;
            if (ops == null) return;
            var activeIds = new HashSet<string>(Graph.Shared.Segments.Select(s => s.id));
            foreach (var area in ops.Areas)
            foreach (var industry in area.Industries)
            foreach (var component in industry.Components)
            {
                if (!component.IsProgressionEnabled || component is Model.Ops.ProgressionIndustryComponent) continue;
                var color = area.tagColor == default(Color) ? Settings.TrackColorIndustrial : area.tagColor;
                foreach (var span in component.TrackSpans)
                {
                    if (span == null) continue;
                    foreach (var segment in span.GetSegments())
                    {
                        if (!activeIds.Contains(segment.id)) continue;
                        _industrialSegments.Add(segment.id);
                        _industrialSegmentColors[segment.id] = color;
                        _industrialSegmentNames[segment.id] = component.DisplayName;
                    }
                }
            }
            foreach (var stop in ops.AllPassengerStops)
            {
                if (!stop.IsProgressionEnabled) continue;
                foreach (var span in stop.TrackSpans)
                {
                    if (span == null) continue;
                    foreach (var segment in span.GetSegments())
                    {
                        if (!activeIds.Contains(segment.id)) continue;
                        _passengerStopSegments.Add(segment.id);
                        _passengerStopSegmentNames[segment.id] = stop.DisplayName;
                    }
                }
            }
        }

        private void UpdateVisualData()
        {
            if (MapState != MapStates.MAPLOADED) return;
            if (_visualRefreshPending)
            {
                _visualRefreshPending = false;
                RefreshTrackCategories();
                ComputeSegmentGrades();
                Rebuild();
                GatherGradeMarkers();
                MapBuilder.Shared?.Rebuild();
            }
            if (Settings.Overlay >= TrackOverlayMode.TrackClass &&
                MapWindow.instance != null && MapWindow.instance._window.IsShown &&
                Time.unscaledTime >= _nextOverlayRefresh)
            {
                _nextOverlayRefresh = Time.unscaledTime + 1f;
                MapBuilder.Shared?.RefreshSegmentColors();
            }
        }

        private Color OverlayColor(TrackSegment segment, Color nativeColor)
        {
            // Preserve the game's unavailable and construction indicators.
            if (!segment.Available) return Settings.Overlay == TrackOverlayMode.Routes
                ? (Settings.EnableIndustryAreaColors ? Settings.TrackColorUnreachable : Settings.TrackColorUnavailable) : nativeColor;
            if (segment.ConstructionState != default) return nativeColor;
            switch (Settings.Overlay)
            {
                case TrackOverlayMode.Grade:
                    return GetGradeMarkerColor(ComputeSegmentGrade(segment));
                case TrackOverlayMode.TrackClass:
                    var band = OverlayMath.ClassBand(segment.PostedTrackClass);
                    return band < 0 ? Color.gray : ClassColors[band];
                case TrackOverlayMode.Condition:
                case TrackOverlayMode.Cleanliness:
                    var condition = TrackConditionController.Shared;
                    if (condition == null) return Color.gray;
                    float value = Settings.Overlay == TrackOverlayMode.Condition
                        ? condition.GetCondition(segment) : condition.GetRightOfWayClearing(segment);
                    int level = OverlayMath.UnitBand(value);
                    return level < 0 ? Color.gray : MaintenanceColors[level];
                default:
                    if (Settings.EnablePassengerStopTracking && _passengerStopSegments.Contains(segment.id))
                        return Settings.TrackColorPax;
                    if (mainlineSegments.Contains(segment.id)) return Settings.TrackColorMainline;
                    if (_industrialSegments.Contains(segment.id) && !_passengerStopSegments.Contains(segment.id))
                        return Settings.EnableIndustryAreaColors &&
                            _industrialSegmentColors.TryGetValue(segment.id, out var color)
                            ? color : Settings.TrackColorIndustrial;
                    return Settings.TrackColorBranch;
            }
        }

        private void AddOverlayLegendItems()
        {
            if (Settings.Overlay == TrackOverlayMode.Grade)
            {
                AddLegendItem("Flat (< 0.1%)", Settings.GradeColorFlat);
                AddLegendItem("0.1 - 1%", Settings.GradeColor0to1);
                AddLegendItem("1 - 2%", Settings.GradeColor1to2);
                AddLegendItem("2 - 3%", Settings.GradeColor2to3);
                AddLegendItem("3% +", Settings.GradeColorAbove3);
            }
            else if (Settings.Overlay == TrackOverlayMode.TrackClass)
            {
                for (int i = 0; i < ClassColors.Length; i++) AddLegendItem("Class " + i, ClassColors[i]);
                AddLegendItem("Unknown", Color.gray);
            }
            else
            {
                string noun = Settings.Overlay == TrackOverlayMode.Condition ? "condition" : "cleared";
                AddLegendItem("0% " + noun, MaintenanceColors[0]);
                AddLegendItem("1 - 24%", MaintenanceColors[1]);
                AddLegendItem("25 - 49%", MaintenanceColors[2]);
                AddLegendItem("50 - 74%", MaintenanceColors[3]);
                AddLegendItem("75 - 100%", MaintenanceColors[4]);
                AddLegendItem("Unavailable data", Color.gray);
            }
        }
    }
}
