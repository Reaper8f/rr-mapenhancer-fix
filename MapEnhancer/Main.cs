using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;


namespace MapEnhancer.Native;

public static partial class Loader
{
    public class MapEnhancerSettings
	{
		public KeyBinding mapToggle = new KeyBinding() { keyCode = KeyCode.Z};
		public KeyBinding mapFollow = new KeyBinding() { modifiers = 1, keyCode = KeyCode.Z };
		public KeyBinding mapRecenter = new KeyBinding() { modifiers = 2, keyCode = KeyCode.Z };

		public bool DoubleClick = false;

		public float FlareScale = 0.6f;
		public float JunctionMarkerScale = 0.6f;
		public float MarkerCutoff = 0.12f;

		public float WindowSizeMin = 800f;
		public float MapZoomMin = 50f;
		public float MapZoomMax = 10000f;

		public float TrackLineThickness = 1.25f;

	public MsaaQuality MSAA = MsaaQuality._4x;

	public static readonly Color TrackColorMainlineOrig = new Color(0f, 155f / 255f, 0f, 1f); // Green (RGB 0, 155, 0)
	public static readonly Color TrackColorBranchOrig = new Color(0f, 0.572f, 0.792f, 1f);
	public static readonly Color TrackColorIndustrialOrig = new Color(0.749f, 0.749f, 0f, 1f);
	public static readonly Color TrackColorUnavailableOrig = new Color(1f, 0f, 0f, 1f);
	public static readonly Color[] TrackClassColorMap = {
		new Color(0,0,0,0),
		new Color(0,0,1,0),
		new Color(0,1,0,0),
		new Color(1,0,0,0)};

	public Color TrackColorMainline = TrackColorMainlineOrig;
	public Color TrackColorBranch = TrackColorBranchOrig;
	public Color TrackColorIndustrial = TrackColorIndustrialOrig;
	public Color TrackColorUnavailable = TrackColorUnavailableOrig;
	public static readonly Color TrackColorPaxOrig = new Color(0.5f, 0f, 0.5f, 1f); // Purple
	public Color TrackColorPax = TrackColorPaxOrig;
	public static readonly Color TrackColorUnreachableOrig = new Color(0.7f, 0.7f, 0.7f, 1f); // Light grey
	public Color TrackColorUnreachable = TrackColorUnreachableOrig;

	// Feature toggles
	public bool UseVisualOnlyTrackColors = true; // Visual-only track coloring (doesn't change track classes)
	public bool EnablePassengerStopTracking = false; // Track passenger stop segments
	public bool EnableIndustryAreaColors = true; // Color industrial tracks by their area colors
	public bool EnableModdedSpawnPoints = false; // Show additional locations from mods in location dropdown (auto-discovers all mods with spawn-points.json)
	public bool ShowAdvancedMapWindowSettings = true; // Show optional settings (switch reset + grade toggles) in in-game map window

	// Turntable marker settings
	public bool ShowTurntableMarkers = true; // Show clickable markers on turntables
	public bool EnableTurntableControl = true; // Enable turntable rotation from map (network-synced for multiplayer)

	public Color TurntableMarkerColor = new Color(0.8f, 0.5f, 0.2f, 0.4f); // Orange with transparency
	public bool ShowRoadCrossingMarkers = true; // Show road crossing markers on the map
	public float CrossingMarkerScale = 0.3f; // Scale for road crossing markers on map

	// Grade Indicators
	public bool ShowGradeOnHover = true;
	public bool ShowGradeMarkers = false;
	public float GradeMarkerMinIntensity = 1.0f;
	public TrackOverlayMode Overlay = TrackOverlayMode.Routes;
    [Newtonsoft.Json.JsonIgnore]
    public bool EnableGradeColorOverlay { get => Overlay == TrackOverlayMode.Grade; set { if (value) Overlay = TrackOverlayMode.Grade; else if (Overlay == TrackOverlayMode.Grade) Overlay = TrackOverlayMode.Routes; } }

	public static readonly Color GradeColorFlatOrig = new Color(0.267f, 0.467f, 0.800f, 1f); // Blue
	public static readonly Color GradeColor0to1Orig = new Color(0.133f, 0.733f, 0.267f, 1f); // Green
	public static readonly Color GradeColor1to2Orig = new Color(0.867f, 0.800f, 0.133f, 1f); // Yellow
	public static readonly Color GradeColor2to3Orig = new Color(0.933f, 0.533f, 0.067f, 1f); // Orange
	public static readonly Color GradeColorAbove3Orig = new Color(0.800f, 0.133f, 0.133f, 1f); // Red

	public Color GradeColorFlat = GradeColorFlatOrig;
	public Color GradeColor0to1 = GradeColor0to1Orig;
	public Color GradeColor1to2 = GradeColor1to2Orig;
	public Color GradeColor2to3 = GradeColor2to3Orig;
	public Color GradeColorAbove3 = GradeColorAbove3Orig;

	// Train Intelligence Layer
	public bool ShowTrainHoverDetails = true;
	public bool ShowTrainDriver       = true;
	public bool ShowTrainStats        = true;

	// Cargo Intelligence Layer
	public bool ShowFuel                 = true;
	public bool ShowTrainCargoSummary    = true;
	public bool ShowDestinations         = true;
	public bool ShowIndividualCarTooltip = true;
	public bool EnableDebugLogging       = false;

	// Tooltip customization removed: tooltips use plain formatting

	public void Save()
		{
			Loader.SaveSettings();
		}

		public void OnChange()
		{
			Instance?.OnSettingsChanged();
            Save();
		}
	}

	internal static void DrawSettings()
	{
		bool changed = false;

		using (new GUILayout.VerticalScope())
		{
			using (new GUILayout.HorizontalScope())
			{
				var dc = GUILayout.Toggle(Settings.DoubleClick, "Require Double Click");
				if (Settings.DoubleClick != dc)
				{
					Settings.DoubleClick = dc;
					changed = true;
				}
			}

		using (new GUILayout.HorizontalScope())
		{
			var enableControl = GUILayout.Toggle(Settings.EnableTurntableControl, "Enable Turntable Control from Map (network-synced)");
			if (Settings.EnableTurntableControl != enableControl)
			{
				Settings.EnableTurntableControl = enableControl;
				// When enabling turntable control, also enable markers by default
				if (enableControl && !Settings.ShowTurntableMarkers)
				{
					Settings.ShowTurntableMarkers = true;
				}
				changed = true;
			}
		}

		if (Settings.EnableTurntableControl)
		{
			GUILayout.Label("  ℹ️ Turntable rotations are synchronized to all players in multiplayer.", GUILayout.ExpandWidth(true));

			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var showMarkers = GUILayout.Toggle(Settings.ShowTurntableMarkers, "Show Turntable Markers (Ctrl+Click = Clockwise, Alt+Click = Counterclockwise, Shift+Click = 180°)");
				if (Settings.ShowTurntableMarkers != showMarkers)
				{
					Settings.ShowTurntableMarkers = showMarkers;
					changed = true;
				}
			}

				using (new GUILayout.HorizontalScope())
				{
					GUILayout.Space(20);
					var showCrossings = GUILayout.Toggle(Settings.ShowRoadCrossingMarkers, "Show Road Crossing Markers");
					if (Settings.ShowRoadCrossingMarkers != showCrossings)
					{
						Settings.ShowRoadCrossingMarkers = showCrossings;
						changed = true;
					}
				}

				if (Settings.ShowRoadCrossingMarkers)
				{
					using (new GUILayout.HorizontalScope())
					{
						GUILayout.Space(20);
						var crossingScale = (float)Math.Round(
							GUILayout.HorizontalSlider(Settings.CrossingMarkerScale, 0.1f, 1.0f, GUILayout.Width(SettingsUi.Scale(160))),
							2,
							MidpointRounding.AwayFromZero);
						GUILayout.Label($"Crossing Marker Scale: {crossingScale}", GUILayout.ExpandWidth(true));
						if (Math.Abs(Settings.CrossingMarkerScale - crossingScale) > 0.0001f)
						{
							Settings.CrossingMarkerScale = crossingScale;
							changed = true;
						}
					}
				}
		}
		else
		{
			GUILayout.Label("  ℹ️ Turntable control from map is disabled. Use in-game turntable UI instead.", GUILayout.ExpandWidth(true));
		}			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("Toggle Map Size Keybind");
			SettingsUi.DrawKeybindingSmart(Settings.mapToggle, "Toggle Map Size", null, GUILayout.Width(SettingsUi.Scale(200)));
			GUILayout.Label("Re-center Map on Player Keybind");
			SettingsUi.DrawKeybindingSmart(Settings.mapRecenter, "Re-center map to currently selected camera", null, GUILayout.Width(SettingsUi.Scale(200)));
			GUILayout.Label("Follow Mode Keybind");
			SettingsUi.DrawKeybindingSmart(Settings.mapFollow, "Follow Mode will keep map focuesed on last selected loco", null, GUILayout.Width(SettingsUi.Scale(200)));

			GUILayout.Label("Docked Map Window Size");
			using (new GUILayout.HorizontalScope())
			{
				var zoomMin = (float)Math.Round(GUILayout.HorizontalSlider(Settings.WindowSizeMin, 200f, 800f, GUILayout.Width(SettingsUi.Scale(200))) / 200, 0, MidpointRounding.AwayFromZero) * 200f;
				GUILayout.Label(zoomMin.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.WindowSizeMin != zoomMin)
				{
					Settings.WindowSizeMin = zoomMin;
					changed = true;
				}
			}

			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("Map Zoom Min (lower = more zoom)");
			using (new GUILayout.HorizontalScope())
			{
				var zoomMin = (float)Math.Round(GUILayout.HorizontalSlider(Settings.MapZoomMin, 50f, 100f, GUILayout.Width(SettingsUi.Scale(200))), 0, MidpointRounding.AwayFromZero);
				GUILayout.Label(zoomMin.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.MapZoomMin != zoomMin)
				{
					Settings.MapZoomMin = zoomMin;
					changed = true;
				}
			}

			GUILayout.Label("Map Zoom Max (higher = more zoom)");
			using (new GUILayout.HorizontalScope())
			{
				var zoomMax = (float)Math.Round(GUILayout.HorizontalSlider(Settings.MapZoomMax, 5000f, 15000f, GUILayout.Width(SettingsUi.Scale(200))), 0, MidpointRounding.AwayFromZero);
				GUILayout.Label(zoomMax.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.MapZoomMax != zoomMax)
				{
					Settings.MapZoomMax = zoomMax;
					changed = true;
				}
			}

			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("Fusee Marker Scale");
			using (new GUILayout.HorizontalScope())
			{
				var fs = (float)Math.Round(GUILayout.HorizontalSlider(Settings.FlareScale, 0.1f, 1f, GUILayout.Width(SettingsUi.Scale(200))), 1, MidpointRounding.AwayFromZero);
				GUILayout.Label(fs.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.FlareScale != fs)
				{
					Settings.FlareScale = fs;
					changed = true;
				}
			}

			GUILayout.Label("Junction Marker Scale");
			using (new GUILayout.HorizontalScope())
			{
				var ms = (float)Math.Round(GUILayout.HorizontalSlider(Settings.JunctionMarkerScale, 0.50f, 1f, GUILayout.Width(SettingsUi.Scale(200))), 2, MidpointRounding.AwayFromZero);
				GUILayout.Label(ms.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.JunctionMarkerScale != ms)
				{
					Settings.JunctionMarkerScale = ms;
					changed = true;
				}
			}

			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("Junction Non-Mainline Marker Cutoff");
			using (new GUILayout.HorizontalScope())
			{
				var co = (float)Math.Round(GUILayout.HorizontalSlider(Settings.MarkerCutoff, 0.01f, 1f, GUILayout.Width(SettingsUi.Scale(200))), 2, MidpointRounding.AwayFromZero);
				GUILayout.Label(co.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.MarkerCutoff != co)
				{
					Settings.MarkerCutoff = co;
					changed = true;
				}
			}

			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("Track Line Thickness");
			using (new GUILayout.HorizontalScope())
			{
				var thickness = (float)Math.Round(GUILayout.HorizontalSlider(Settings.TrackLineThickness, 0.5f, 2f, GUILayout.Width(SettingsUi.Scale(200))) * 4, 0, MidpointRounding.AwayFromZero) / 4;
				GUILayout.Label(thickness.ToString(), GUILayout.ExpandWidth(true));
				if (Settings.TrackLineThickness != thickness)
				{
					Settings.TrackLineThickness = thickness;
					changed = true;
				}
			}

			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("Map Window Antialiasing");
			using (new GUILayout.HorizontalScope())
			{
				var values = (int[])Enum.GetValues(Settings.MSAA.GetType());
				int msaaIndex = Array.IndexOf(values, Settings.MSAA);
				if (SettingsUi.PopupToggleGroup(ref msaaIndex, Enum.GetNames(Settings.MSAA.GetType()), null, GUILayout.Width(SettingsUi.Scale(100))))
				{
					if (values[msaaIndex] != (int)Settings.MSAA)
					{
						Settings.MSAA = (MsaaQuality)values[msaaIndex];
						changed = true;
					}
				}
			}

			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("Mainline Track Color (⚠️ Alpha not supported for tracks)");
			if (DrawColor(ref Settings.TrackColorMainline)) changed = true;
			GUILayout.Label("Branch/Yard Track Color (⚠️ Alpha not supported for tracks)");
			if (DrawColor(ref Settings.TrackColorBranch)) changed = true;
			GUILayout.Label("Industry Track Color (⚠️ Alpha not supported for tracks)");
			if (DrawColor(ref Settings.TrackColorIndustrial)) changed = true;
			GUILayout.Label("Unavailable Track Color (⚠️ Alpha not supported for tracks)");
			if (DrawColor(ref Settings.TrackColorUnavailable)) changed = true;

			GUILayout.Space(SettingsUi.Scale(10));
			GUILayout.Label("--- Optional Features ---", GUILayout.ExpandWidth(true));

			// Visual-only mode is now permanently enabled (UI toggle disabled)
			// TODO: Remove all non-visual-only mode code when ready to clean up
			GUILayout.Space(SettingsUi.Scale(5));
			GUILayout.Label("ℹ️ Visual-Only Track Coloring: ENABLED (track classes remain unchanged)", GUILayout.ExpandWidth(true));

			/* DISABLED - Visual-only mode is now permanent
			using (new GUILayout.HorizontalScope())
			{
				var visualOnly = GUILayout.Toggle(Settings.UseVisualOnlyTrackColors, "Use Visual-Only Track Coloring (doesn't modify track classes)");
				if (Settings.UseVisualOnlyTrackColors != visualOnly)
				{
					Settings.UseVisualOnlyTrackColors = visualOnly;
					changed = true;
				}
			}
			if (Settings.UseVisualOnlyTrackColors)
			{
				GUILayout.Label("  ℹ️ Track colors are visual only. Track classes remain unchanged.", GUILayout.ExpandWidth(true));
			}
			*/

			GUILayout.Space(SettingsUi.Scale(5));
			using (new GUILayout.HorizontalScope())
			{
				var paxTracking = GUILayout.Toggle(Settings.EnablePassengerStopTracking, "Enable Passenger Stop Tracking");
				if (Settings.EnablePassengerStopTracking != paxTracking)
				{
					Settings.EnablePassengerStopTracking = paxTracking;
					changed = true;
				}
			}
			if (Settings.EnablePassengerStopTracking)
			{
				GUILayout.Label("  ℹ️ Passenger stops will be highlighted in custom color.", GUILayout.ExpandWidth(true));
				GUILayout.Label("Passenger Stop Track Color");
				if (DrawColor(ref Settings.TrackColorPax)) changed = true;
			}

			GUILayout.Space(SettingsUi.Scale(5));
			using (new GUILayout.HorizontalScope())
			{
				var industryColors = GUILayout.Toggle(Settings.EnableIndustryAreaColors, "Enable Industry Area Colors for Industrial Tracks");
				if (Settings.EnableIndustryAreaColors != industryColors)
				{
					Settings.EnableIndustryAreaColors = industryColors;
					changed = true;
				}
			}
			if (Settings.EnableIndustryAreaColors)
			{
				GUILayout.Label("  ℹ️ Industrial tracks will be colored by their area's color.", GUILayout.ExpandWidth(true));
				GUILayout.Label("Unreachable Track Color (⚠️ Alpha not supported for tracks)");
				if (DrawColor(ref Settings.TrackColorUnreachable)) changed = true;
			}
			else
			{
				GUILayout.Label("  ℹ️ Industrial tracks will use the default industrial color.", GUILayout.ExpandWidth(true));
			}

		GUILayout.Space(SettingsUi.Scale(5));
		using (new GUILayout.HorizontalScope())
		{
			var moddedSpawnPoints = GUILayout.Toggle(Settings.EnableModdedSpawnPoints, "Enable Additional Locations from Mods");
			if (Settings.EnableModdedSpawnPoints != moddedSpawnPoints)
			{
				Settings.EnableModdedSpawnPoints = moddedSpawnPoints;
				changed = true;
			}
		}
		if (Settings.EnableModdedSpawnPoints)
		{
			GUILayout.Label("  ℹ️ Additional locations will appear under '---- Other ----' separator.", GUILayout.ExpandWidth(true));
		}
		else
		{
			GUILayout.Label("  ℹ️ Only vanilla locations will be shown in the teleport dropdown.", GUILayout.ExpandWidth(true));
		}

		GUILayout.Space(SettingsUi.Scale(5));
		using (new GUILayout.HorizontalScope())
		{
			var showMapWindowAdvanced = GUILayout.Toggle(Settings.ShowAdvancedMapWindowSettings, "Show Advanced Controls in In-Game Map Window");
			if (Settings.ShowAdvancedMapWindowSettings != showMapWindowAdvanced)
			{
				Settings.ShowAdvancedMapWindowSettings = showMapWindowAdvanced;
				changed = true;
			}
		}
		if (Settings.ShowAdvancedMapWindowSettings)
		{
			GUILayout.Label("  ℹ️ Shows Switch Reset, Hover Grade, and Grade Markers controls on the in-game map.", GUILayout.ExpandWidth(true));
		}
		else
		{
			GUILayout.Label("  ℹ️ Hides optional map controls to keep the in-game map panel minimal.", GUILayout.ExpandWidth(true));
		}

			GUILayout.Space(SettingsUi.Scale(10));
			GUILayout.Label("--- Debug Logging ---", GUILayout.ExpandWidth(true));
			using (new GUILayout.HorizontalScope())
			{
				var debugLogging = GUILayout.Toggle(Settings.EnableDebugLogging, "Enable Debug Logging");
				if (Settings.EnableDebugLogging != debugLogging)
				{
					Settings.EnableDebugLogging = debugLogging;
					changed = true;
				}
			}
			GUILayout.Label("  ℹ️ Keeps verbose diagnostics out of Player.log unless you are actively debugging.", GUILayout.ExpandWidth(true));

		GUILayout.Space(SettingsUi.Scale(10));
		GUILayout.Label("--- Grade Indicators ---", GUILayout.ExpandWidth(true));

		GUILayout.Space(SettingsUi.Scale(5));
		using (new GUILayout.HorizontalScope())
		{
			var showHover = GUILayout.Toggle(Settings.ShowGradeOnHover, "Show Grade on Hover");
			if (Settings.ShowGradeOnHover != showHover)
			{
				Settings.ShowGradeOnHover = showHover;
				changed = true;
			}
		}

		GUILayout.Space(SettingsUi.Scale(5));
		using (new GUILayout.HorizontalScope())
		{
			var showMarkers = GUILayout.Toggle(Settings.ShowGradeMarkers, "Show Grade Markers");
			if (Settings.ShowGradeMarkers != showMarkers)
			{
				Settings.ShowGradeMarkers = showMarkers;
				changed = true;
			}
		}

		if (Settings.ShowGradeMarkers)
		{
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var minIntensity = (float)Math.Round(
					GUILayout.HorizontalSlider(Settings.GradeMarkerMinIntensity, 0.1f, 3.0f, GUILayout.Width(SettingsUi.Scale(160))),
					2,
					MidpointRounding.AwayFromZero);
				GUILayout.Label($"Min Grade Marker Intensity: {minIntensity}%", GUILayout.ExpandWidth(true));
				if (Math.Abs(Settings.GradeMarkerMinIntensity - minIntensity) > 0.0001f)
				{
					Settings.GradeMarkerMinIntensity = minIntensity;
					changed = true;
				}
			}
		}

		GUILayout.Space(SettingsUi.Scale(5));
		using (new GUILayout.HorizontalScope())
		{
			var nextOverlay = (TrackOverlayMode)GUILayout.SelectionGrid((int)Settings.Overlay,
                new[] { "Off", "Grade", "Track Class", "Track Condition", "Track Cleanliness" }, 2);
            if (nextOverlay != Settings.Overlay) { Settings.Overlay = nextOverlay; changed = true; }
        }

        if (Settings.EnableGradeColorOverlay)
		{
			GUILayout.Label("Flat (< 0.1%) Grade Color");
			if (DrawColor(ref Settings.GradeColorFlat)) changed = true;

			GUILayout.Label("0–1% Grade Color");
			if (DrawColor(ref Settings.GradeColor0to1)) changed = true;

			GUILayout.Label("1–2% Grade Color");
			if (DrawColor(ref Settings.GradeColor1to2)) changed = true;

			GUILayout.Label("2–3% Grade Color");
			if (DrawColor(ref Settings.GradeColor2to3)) changed = true;

			GUILayout.Label("> 3% Grade Color");
			if (DrawColor(ref Settings.GradeColorAbove3)) changed = true;
		}

		GUILayout.Space(SettingsUi.Scale(10));
		GUILayout.Label("--- Train Hover Info ---", GUILayout.ExpandWidth(true));
		GUILayout.Space(SettingsUi.Scale(5));
		using (new GUILayout.HorizontalScope())
		{
			var showDetails = GUILayout.Toggle(Settings.ShowTrainHoverDetails, "Show Train Hover Details");
			if (Settings.ShowTrainHoverDetails != showDetails)
			{
				Settings.ShowTrainHoverDetails = showDetails;
				changed = true;
			}
		}

		if (Settings.ShowTrainHoverDetails)
		{
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var showDriver = GUILayout.Toggle(Settings.ShowTrainDriver, "Show Driver Name");
				if (Settings.ShowTrainDriver != showDriver)
				{
					Settings.ShowTrainDriver = showDriver;
					changed = true;
				}
			}
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var showStats = GUILayout.Toggle(Settings.ShowTrainStats, "Show Train Stats (speed, weight, cars, length)");
				if (Settings.ShowTrainStats != showStats)
				{
					Settings.ShowTrainStats = showStats;
					changed = true;
				}
			}
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var showFuel = GUILayout.Toggle(Settings.ShowFuel, "Show Locomotive Fuel Levels");
				if (Settings.ShowFuel != showFuel)
				{
					Settings.ShowFuel = showFuel;
					changed = true;
				}
			}
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var showCargo = GUILayout.Toggle(Settings.ShowTrainCargoSummary, "Show Cargo Summary (freight trains)");
				if (Settings.ShowTrainCargoSummary != showCargo)
				{
					Settings.ShowTrainCargoSummary = showCargo;
					changed = true;
				}
			}
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var showDest = GUILayout.Toggle(Settings.ShowDestinations, "Show Destination Summary (freight trains)");
				if (Settings.ShowDestinations != showDest)
				{
					Settings.ShowDestinations = showDest;
					changed = true;
				}
			}
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(20);
				var showCarTooltip = GUILayout.Toggle(Settings.ShowIndividualCarTooltip, "Show Individual Freight Car Tooltip on Hover");
				if (Settings.ShowIndividualCarTooltip != showCarTooltip)
				{
					Settings.ShowIndividualCarTooltip = showCarTooltip;
					changed = true;
				}
			}
		}

		// Reset to Defaults button
		GUILayout.Space(SettingsUi.Scale(15));
		GUILayout.Label("--- Reset Settings ---", GUILayout.ExpandWidth(true));
		GUILayout.Space(SettingsUi.Scale(5));

		if (GUILayout.Button("Reset All Settings to Default", GUILayout.Width(SettingsUi.Scale(300))))
		{
			ResetSettingsToDefault();
			changed = true;
		}
		GUILayout.Label("  ⚠️ This will reset ALL settings including colors, scales, keybinds, and feature toggles.", GUILayout.ExpandWidth(true));
	}		if (changed) Settings.OnChange();

		static void ResetSettingsToDefault()
		{
			// Reset keybinds
			Settings.mapToggle = new KeyBinding() { keyCode = KeyCode.Z };
			Settings.mapFollow = new KeyBinding() { modifiers = 1, keyCode = KeyCode.Z };
			Settings.mapRecenter = new KeyBinding() { modifiers = 2, keyCode = KeyCode.Z };

			// Reset boolean settings
			Settings.DoubleClick = false;
			Settings.ShowTurntableMarkers = true;
			Settings.EnableTurntableControl = true;

			Settings.ShowRoadCrossingMarkers = true;
			Settings.CrossingMarkerScale = 0.3f;

			// Reset scale and size settings
			Settings.FlareScale = 0.6f;
			Settings.JunctionMarkerScale = 0.6f;
			Settings.MarkerCutoff = 0.12f;
			Settings.WindowSizeMin = 800f;
			Settings.MapZoomMin = 50f;
			Settings.MapZoomMax = 10000f;
			Settings.TrackLineThickness = 1.25f;
			Settings.MSAA = MsaaQuality._4x;

			// Reset track colors
			Settings.TrackColorMainline = MapEnhancerSettings.TrackColorMainlineOrig;
			Settings.TrackColorBranch = MapEnhancerSettings.TrackColorBranchOrig;
			Settings.TrackColorIndustrial = MapEnhancerSettings.TrackColorIndustrialOrig;
			Settings.TrackColorUnavailable = MapEnhancerSettings.TrackColorUnavailableOrig;
			Settings.TrackColorPax = MapEnhancerSettings.TrackColorPaxOrig;
			Settings.TrackColorUnreachable = MapEnhancerSettings.TrackColorUnreachableOrig;

			// Reset feature toggles
			Settings.UseVisualOnlyTrackColors = true;
			Settings.EnablePassengerStopTracking = false;
			Settings.EnableIndustryAreaColors = true;
			Settings.EnableModdedSpawnPoints = false;
			Settings.ShowAdvancedMapWindowSettings = true;

			// Reset turntable marker color
			Settings.TurntableMarkerColor = new Color(0.8f, 0.5f, 0.2f, 0.4f);

			// Reset grade indicators settings
			Settings.ShowGradeOnHover = true;
			Settings.ShowGradeMarkers = false;
			Settings.GradeMarkerMinIntensity = 1.0f;
			Settings.Overlay = TrackOverlayMode.Routes;
			Settings.GradeColorFlat = MapEnhancerSettings.GradeColorFlatOrig;
			Settings.GradeColor0to1 = MapEnhancerSettings.GradeColor0to1Orig;
			Settings.GradeColor1to2 = MapEnhancerSettings.GradeColor1to2Orig;
			Settings.GradeColor2to3 = MapEnhancerSettings.GradeColor2to3Orig;
			Settings.GradeColorAbove3 = MapEnhancerSettings.GradeColorAbove3Orig;

			// Reset train hover info settings
			Settings.ShowTrainHoverDetails = true;
			Settings.ShowTrainDriver       = true;
			Settings.ShowTrainStats        = true;

			// Reset cargo intelligence settings
			Settings.ShowFuel                 = true;
			Settings.ShowTrainCargoSummary    = true;
			Settings.ShowDestinations         = true;
			Settings.ShowIndividualCarTooltip = true;
			Settings.EnableDebugLogging       = false;

			Log("All settings have been reset to their default values.");
		}

		static bool DrawColor(ref Color color)
		{
			bool changed = false;
			using (new GUILayout.HorizontalScope())
			{
				using (new GUILayout.HorizontalScope("box"))
				{
					float r, g, b, a;
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"R: {color.r * 255f}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(SettingsUi.Scale(133))))
						{
							r = (int)GUILayout.HorizontalSlider(color.r * 255f, 0f, 255f, GUILayout.Width(SettingsUi.Scale(128))) / 255f;
						}
					}
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"G: {color.g * 255f}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(SettingsUi.Scale(133))))
						{
							g = (int)GUILayout.HorizontalSlider(color.g * 255f, 0f, 255f, GUILayout.Width(SettingsUi.Scale(128))) / 255f;
						}
					}
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"B: {color.b * 255f}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(SettingsUi.Scale(133))))
						{
							b = (int)GUILayout.HorizontalSlider(color.b * 255f, 0f, 255f, GUILayout.Width(SettingsUi.Scale(128))) / 255f;
						}
					}
					using (new GUILayout.VerticalScope())
					{
						GUILayout.Label($"A: {color.a}");
						using (new GUILayout.HorizontalScope(GUILayout.Width(SettingsUi.Scale(133))))
						{
							a = (float)Math.Round(GUILayout.HorizontalSlider(color.a, 0f, 1f, GUILayout.Width(SettingsUi.Scale(128))), 2, MidpointRounding.AwayFromZero);
						}
					}
					if (color.r != r || color.g != g || color.b != b || color.a != a)
					{
						color = new Color(r, g, b, a);
						changed = true;
					}
				}
				GUILayout.FlexibleSpace();
			}

			return changed;
		}
	}

	public static void Log(string str)
	{
		Context?.Log.Info(str);
	}

	public static void LogDebug(string str)
	{
		if (Settings?.EnableDebugLogging == true)
		{
			Context?.Log.Info(str);
		}
#if DEBUG
		else
		{
			Context?.Log.Info(str);
		}
#endif
	}
}
