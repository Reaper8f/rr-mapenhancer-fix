using Character;
using Core;

using Game;
using Game.AccessControl;
using Game.Events;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Helpers;
using Map.Runtime;
using MapEnhancer.Native;
using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Ops;
using Network;
using RollingStock;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using TMPro;
using Track;
using Track.Signals;
using UI;
using UI.Builder;
using UI.CarInspector;
using UI.Common;
using UI.Console.Commands;
using UI.Map;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MapEnhancer;

public partial class MapEnhancer : MonoBehaviour
{
	public enum MapStates { MAINMENU, MAPLOADED, MAPUNLOADING }
	public static MapStates MapState { get; private set; } = MapStates.MAINMENU;
	internal Loader.MapEnhancerSettings Settings = new Loader.MapEnhancerSettings();
	public GameObject? Junctions;
	public GameObject? JunctionsBranch;
	public GameObject? JunctionsMainline;
	public GameObject? Turntables;
	public GameObject? Crossings;
	public GameObject? GradeMarkers;
	private List<Entry> junctionMarkers = new List<Entry>();
	private CullingGroup? cullingGroup;
	private BoundingSphere[] cullingSpheres = Array.Empty<BoundingSphere>();
	private MapResizer? resizer;
	private bool mapFollowMode;
	private UnityEngine.Component? mapCameraTarget;
	public RectTransform? mapSettings;
	private TMP_Dropdown? _overlayDropdown;
	private static readonly TrackOverlayMode[] OverlayModes =
	{
		TrackOverlayMode.Routes,
		TrackOverlayMode.Grade,
		TrackOverlayMode.TrackClass,
		TrackOverlayMode.Condition,
		TrackOverlayMode.Cleanliness
	};
	private Sprite? dropdownSprite;
	private HashSet<string> moddedSpawnPointNames = new HashSet<string>(); // Track modded spawn points
	private static Dictionary<string, List<string>> _spawnPointCategories = new Dictionary<string, List<string>>(); // Category -> spawn point names
	private bool _modSpawnPointsLoaded = false;
	private readonly HashSet<string> _spawnPointSummaryLogKeys = new HashSet<string>();

	// Holder stops "prefab" from going active immediately
	private static GameObject? _prefabHolder;
	internal static GameObject prefabHolder
	{
		get
		{
			if (_prefabHolder == null)
			{
				_prefabHolder = new GameObject("Prefab Holder");
				_prefabHolder.hideFlags = HideFlags.HideAndDontSave;
				_prefabHolder.SetActive(false);
			}
			return _prefabHolder;
		}
	}

	private static MapIcon? _traincarPrefab;
	public static MapIcon? traincarPrefab
	{
		get
		{
			if (_traincarPrefab == null) CreateTraincarPrefab();
			return _traincarPrefab;
		}
	}

	private static MapIcon? _flarePrefab;
	public static MapIcon? flarePrefab
	{
		get
		{
			if (_flarePrefab == null) CreateFlarePrefab();
			return _flarePrefab;
		}
	}

	private static MapIcon? _turntablePrefab;
	public static MapIcon? turntablePrefab
	{
		get
		{
			if (_turntablePrefab == null) CreateTurntablePrefab();
			return _turntablePrefab;
		}
	}

	private static MapIcon? _crossingPrefab;
	public static MapIcon? crossingPrefab
	{
		get
		{
			if (_crossingPrefab == null) CreateCrossingPrefab();
			return _crossingPrefab;
		}
	}

	private Coroutine? traincarColorUpdater;
	private static List<TurntableHelper> _turntableHelpers = new List<TurntableHelper>();
	private readonly List<MapIcon> _crossingMarkers = new List<MapIcon>();
	private GameObject? _switchResetAuditStorageGo;
	private SwitchResetAuditStorage? _switchResetAuditStorage;
	private IDisposable? _switchResetAuditObserver;
	private readonly HashSet<string> _processedSwitchResetRequestIds = new HashSet<string>();
	private readonly HashSet<string> _processedSwitchResetLogIds = new HashSet<string>();
	private DateTime _sessionStartTime;
	private bool _hasLoggedOldSwitchResetReplayIgnored;
	private int _switchResetRequestSlot;
	private int _switchResetLogSlot;
	private int _hostSwitchResetActionCount = 0;
	private int _hostSwitchesResetTotal = 0;
	private Coroutine? _turntableMapRefreshCoroutine;
	private const string SwitchResetAuditRequestPrefix = "request:";
	private const string SwitchResetAuditLogPrefix = "log:";
	private const int MaxSwitchResetAuditEntries = 200;
	private const int MaxProcessedSwitchResetIds = 1000;


	private static HashSet<string>? _mainlineSegments;
	private static HashSet<string> _industrialSegments = new HashSet<string>();
	private static HashSet<string> _passengerStopSegments = new HashSet<string>();
	private static Dictionary<string, Color> _industrialSegmentColors = new Dictionary<string, Color>();
	private static Dictionary<string, GradeInfo> _segmentGrades = new Dictionary<string, GradeInfo>();
	private static Dictionary<string, string> _industrialSegmentNames = new Dictionary<string, string>();
	private static Dictionary<string, string> _passengerStopSegmentNames = new Dictionary<string, string>();

	// Train Intelligence Layer
	private Dictionary<string, TrainInfo> _trainInfoCache = new Dictionary<string, TrainInfo>();
	private readonly Dictionary<string, string> _consistRefreshLogSignatures = new Dictionary<string, string>(StringComparer.Ordinal);
	private Coroutine? _trainInfoUpdater;
	private GameObject? _trainTooltipGo;
	private TextMeshProUGUI? _trainTooltipText;
	private RectTransform? _trainTooltipRect;
	private bool _lastShowAdvancedMapWindowSettings = true;

	public static HashSet<string> mainlineSegments
	{
		get
		{
			if (_mainlineSegments == null)
				populateSegmentsAndSwitches();

			return _mainlineSegments!;
		}
	}

	private static HashSet<string>? _mainlineSwitches;
	public static HashSet<string> mainlineSwitches
	{
		get
		{
			if (_mainlineSwitches == null)
				populateSegmentsAndSwitches();

			return _mainlineSwitches!;
		}
	}

	private static void populateSegmentsAndSwitches()
	{
		_mainlineSegments = new HashSet<string>();
		_mainlineSwitches = new HashSet<string>();
		var ctcBlocks = FindObjectsByType<CTCBlock>(FindObjectsInactive.Include, FindObjectsSortMode.None);

		Loader.LogDebug($"Found {ctcBlocks.Length} CTC blocks for mainline identification");

		foreach (var span in ctcBlocks.SelectMany(block => block.Spans))
		{

			foreach (var seg in span.GetSegments())
			{
				_mainlineSegments.Add(seg.id);
				_mainlineSwitches.Add(seg.a.id);
				_mainlineSwitches.Add(seg.b.id);
			}
		}

		Loader.LogDebug($"Identified {_mainlineSegments.Count} mainline segments and {_mainlineSwitches.Count} mainline switches");
	}

	private float ComputeSegmentGrade(TrackSegment segment)
	{
		if (segment == null || segment.a == null || segment.b == null) return 0f;

		Vector3 aPos = segment.a.Position;
		Vector3 bPos = segment.b.Position;

		float rise = bPos.y - aPos.y;
		float run = Vector3.Distance(new Vector3(aPos.x, 0f, aPos.z), new Vector3(bPos.x, 0f, bPos.z));

		if (run < 0.1f) return 0f;

		return (rise / run) * 100f;
	}

	private void ComputeSegmentGrades()
	{
		_segmentGrades.Clear();
		foreach (var segment in Graph.Shared.Segments)
		{
			if (segment == null || segment.a == null || segment.b == null) continue;

			Vector3 a = segment.a.Position;
			Vector3 b = segment.b.Position;

			float rise = b.y - a.y;
			float run = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

			if (run < 1f) continue;

			float grade = (rise / run) * 100f;
			float length = Vector3.Distance(a, b);

			_segmentGrades[segment.id] = new GradeInfo
			{
				Grade = grade,
				Midpoint = (a + b) / 2f,
				Length = length
			};
		}
		Loader.LogDebug($"Computed grades for {_segmentGrades.Count} segments");
	}

	private float DistanceToLineSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
	{
		float l2 = (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
		if (l2 == 0f) return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(a.x, a.z));
		float t = ((p.x - a.x) * (b.x - a.x) + (p.z - a.z) * (b.z - a.z)) / l2;
		t = Mathf.Clamp01(t);
		float dx = p.x - (a.x + t * (b.x - a.x));
		float dz = p.z - (a.z + t * (b.z - a.z));
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	private GameObject? _gradeTooltipGo;
	private TextMeshProUGUI? _gradeTooltipText;
	private RectTransform? _gradeTooltipRect;

	private void CreateGradeTooltip()
	{
		if (_gradeTooltipGo != null) return;

		_gradeTooltipGo = new GameObject("Grade Tooltip", typeof(RectTransform));
		_gradeTooltipRect = _gradeTooltipGo.GetComponent<RectTransform>();
		_gradeTooltipRect.SetParent(MapWindow.instance._window.transform, false);

		_gradeTooltipRect.anchorMin = new Vector2(0f, 1f);
		_gradeTooltipRect.anchorMax = new Vector2(0f, 1f);
		_gradeTooltipRect.pivot = new Vector2(0f, 1f);
		_gradeTooltipRect.anchoredPosition = new Vector2(10f, -40f);

		var bgImage = _gradeTooltipGo.AddComponent<Image>();
		bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

		var textGo = new GameObject("Text", typeof(RectTransform));
		textGo.transform.SetParent(_gradeTooltipGo.transform, false);
		_gradeTooltipText = textGo.AddComponent<TextMeshProUGUI>();
		_gradeTooltipText.fontSize = 12f;
		_gradeTooltipText.color = Color.white;
		_gradeTooltipText.alignment = TextAlignmentOptions.Center;
		_gradeTooltipText.raycastTarget = false;

		var fitter = textGo.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		var verticalLayout = _gradeTooltipGo.AddComponent<VerticalLayoutGroup>();
		verticalLayout.childAlignment = TextAnchor.MiddleCenter;
		verticalLayout.padding = new RectOffset(10, 10, 8, 8);
		verticalLayout.childControlWidth = true;
		verticalLayout.childControlHeight = true;

		var tooltipFitter = _gradeTooltipGo.AddComponent<ContentSizeFitter>();
		tooltipFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		tooltipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		_gradeTooltipGo.SetActive(false);
	}

	private void CreateTrainTooltip()
	{
		if (_trainTooltipGo != null) return;

		_trainTooltipGo = new GameObject("Train Tooltip", typeof(RectTransform));
		_trainTooltipRect = _trainTooltipGo.GetComponent<RectTransform>();
		_trainTooltipRect.SetParent(MapWindow.instance._window.transform, false);

		_trainTooltipRect.anchorMin = new Vector2(0f, 1f);
		_trainTooltipRect.anchorMax = new Vector2(0f, 1f);
		_trainTooltipRect.pivot = new Vector2(0f, 1f);
		_trainTooltipRect.anchoredPosition = new Vector2(10f, -110f);

			var bgImage = _trainTooltipGo.AddComponent<Image>();
			// Plain tooltip background (no custom coloring)
			bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

		var textGo = new GameObject("Text", typeof(RectTransform));
		textGo.transform.SetParent(_trainTooltipGo.transform, false);
			_trainTooltipText = textGo.AddComponent<TextMeshProUGUI>();
			_trainTooltipText.fontSize = 12f;
			// Plain white text for tooltips
			_trainTooltipText.color = Color.white;
		_trainTooltipText.alignment = TextAlignmentOptions.Left;
		_trainTooltipText.raycastTarget = false;

		var fitter = textGo.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		var verticalLayout = _trainTooltipGo.AddComponent<VerticalLayoutGroup>();
		verticalLayout.childAlignment = TextAnchor.MiddleCenter;
		verticalLayout.padding = new RectOffset(10, 10, 8, 8);
		verticalLayout.childControlWidth = true;
		verticalLayout.childControlHeight = true;

		var tooltipFitter = _trainTooltipGo.AddComponent<ContentSizeFitter>();
		tooltipFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		tooltipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		_trainTooltipGo.SetActive(false);
	}

	// private void ShowTrainTooltip(TrainInfo info)
	// {
	// 	if (_trainTooltipGo == null)
	// 	{
	// 		CreateTrainTooltip();
	// 	}

	// 	if (_trainTooltipText != null)
	// 	{
	// 		string FormatLabelValue(string label, string value, bool newline = true)
	// 		{
	// 			if (string.IsNullOrEmpty(value)) return "";
	// 			return (newline ? "\n" : "") + $"{label}: {value}";
	// 		}

	// 		var sb = new System.Text.StringBuilder();
	// 		// First line: Train name (no leading newline)
	// 		sb.Append($"Train: {info.TrainName}");

	// 		if (Settings.ShowTrainDriver)
	// 		{
	// 			sb.Append(FormatLabelValue("Driver", info.DriverName));
	// 			if (!string.IsNullOrEmpty(info.DriveMode))
	// 				sb.Append(FormatLabelValue("Mode", info.DriveMode));
	// 		}

	// 		// --- Fuel ---
	// 		if (Settings.ShowFuel && info.Fuel != null)
	// 		{
	// 			if (info.Fuel.IsSteam)
	// 			{
	// 				var fuelParts = new System.Text.StringBuilder();
	// 				if (info.Fuel.CoalPercent >= 0f)
	// 					fuelParts.Append($"Coal {info.Fuel.CoalPercent:F0}%");
	// 				if (info.Fuel.WaterPercent >= 0f)
	// 				{
	// 					if (fuelParts.Length > 0) fuelParts.Append(" | ");
	// 					fuelParts.Append($"Water {info.Fuel.WaterPercent:F0}%");
	// 				}
	// 				if (fuelParts.Length > 0)
	// 					sb.Append(FormatLabelValue("Fuel", fuelParts.ToString()));
	// 			}
	// 			else
	// 			{
	// 				if (info.Fuel.DieselPercent >= 0f)
	// 					sb.Append(FormatLabelValue("Fuel", $"Diesel {info.Fuel.DieselPercent:F0}%"));
	// 			}
	// 		}

	// 		if (Settings.ShowTrainStats)
	// 		{
	// 			sb.Append(FormatLabelValue("Speed", $"{info.SpeedMph:F0} mph"));
	// 			sb.Append(FormatLabelValue("Length", $"{info.LengthFt:F0} ft"));
	// 			sb.Append(FormatLabelValue("Weight", $"{info.WeightTons:F0} T"));
	// 			sb.Append(FormatLabelValue("Cars", info.CarCount.ToString()));
	// 			if (info.PassengerCapacity > 0)
	// 			{
	// 				sb.Append(FormatLabelValue("Passengers", $"{info.PassengerCount}/{info.PassengerCapacity}"));
	// 			}
	// 			else if (info.PassengerCount > 0)
	// 			{
	// 				sb.Append(FormatLabelValue("Passengers", info.PassengerCount.ToString()));
	// 			}
	// 		}

	// 		// --- Consist Summary ---
	// 		if (Settings.ShowTrainCargoSummary && info.HasFreightCars && info.CargoSummary.Count > 0)
	// 		{
	// 			sb.Append("\n\nConsist");
	// 			foreach (var group in info.CargoSummary)
	// 			{
	// 				sb.Append($"\n{group.Count}x {group.Label}");
	// 			}
	// 		}

	// 		// --- Destination Summary ---
	// 		if (Settings.ShowDestinations && info.HasFreightCars && info.DestinationSummary.Count > 0)
	// 		{
	// 			sb.Append("\n\nDestinations");
	// 			foreach (var dest in info.DestinationSummary)
	// 			{
	// 				sb.Append($"\n{dest}");
	// 			}
	// 		}

	// 		_trainTooltipText.text = sb.ToString();
	// 	}

	// 	if (_trainTooltipGo != null && !_trainTooltipGo.activeSelf)
	// 	{
	// 		_trainTooltipGo.SetActive(true);
	// 	}
	// }

	private void ShowTrainTooltip(TrainInfo info)
	{
		if (_trainTooltipGo == null)
		{
			CreateTrainTooltip();
		}

		if (_trainTooltipText != null)
		{
			var sb = new System.Text.StringBuilder();

			const string HeaderColor = "#9CDCFE";

			const string Green = "#66BB6A";
			const string Yellow = "#FFD54F";
			const string Red = "#EF5350";

			string Header(string text) => $"<color={HeaderColor}>{text}</color>";

			string FuelColor(float pct)
			{
				if (pct >= 60f) return Green;
				if (pct >= 30f) return Yellow;
				return Red;
			}

			// ==========================================
			// Train Name
			// ==========================================

			sb.Append($"<b>{info.TrainName}</b>");

			// ==========================================
			// Driver
			// ==========================================

			if (Settings.ShowTrainDriver)
			{
				if (!string.IsNullOrWhiteSpace(info.DriverName))
				{
					sb.Append($"\n\n{Header("Driver")}");
					sb.Append($"\n{info.DriverName}");
				}

				if (!string.IsNullOrWhiteSpace(info.DriveMode))
				{
					sb.Append($"\n\n{Header("Mode")}");
					sb.Append($"\n{info.DriveMode}");
				}
			}

			// ==========================================
			// Fuel
			// ==========================================

			if (Settings.ShowFuel && info.Fuel != null)
			{
				sb.Append($"\n\n{Header("Fuel")}");

				if (info.Fuel.IsSteam)
				{
					var fuel = new List<string>();

					if (info.Fuel.CoalPercent >= 0f)
					{
						fuel.Add(
							$"Coal <color={FuelColor(info.Fuel.CoalPercent)}>{info.Fuel.CoalPercent:F0}%</color>");
					}

					if (info.Fuel.WaterPercent >= 0f)
					{
						fuel.Add(
							$"Water <color={FuelColor(info.Fuel.WaterPercent)}>{info.Fuel.WaterPercent:F0}%</color>");
					}

					if (fuel.Count > 0)
						sb.Append("\n" + string.Join("  |  ", fuel));
				}
				else
				{
					if (info.Fuel.DieselPercent >= 0f)
					{
						sb.Append(
							$"\nDiesel <color={FuelColor(info.Fuel.DieselPercent)}>{info.Fuel.DieselPercent:F0}%</color>");
					}
				}
			}

			// ==========================================
			// Statistics
			// ==========================================

			if (Settings.ShowTrainStats)
			{
				sb.Append($"\n\n{Header("Statistics")}");

				sb.Append($"\nSpeed      {info.SpeedMph:F0} mph");
				sb.Append($"\nLength     {info.LengthFt:F0} ft");
				sb.Append($"\nWeight     {info.WeightTons:F0} T");
				sb.Append($"\nCars       {info.CarCount}");

				if (info.PassengerCapacity > 0)
				{
					float pct = (float)info.PassengerCount / info.PassengerCapacity * 100f;

					sb.Append(
						$"\nPassengers {info.PassengerCount}/{info.PassengerCapacity} ({pct:F0}%)");
				}
				else if (info.PassengerCount > 0)
				{
					sb.Append(
						$"\nPassengers {info.PassengerCount}");
				}
			}

			// ==========================================
			// Consist
			// ==========================================

			if (Settings.ShowTrainCargoSummary &&
				info.HasFreightCars &&
				info.CargoSummary.Count > 0)
			{
				sb.Append($"\n\n{Header("Consist")}");

				foreach (var group in info.CargoSummary)
				{
					sb.Append($"\n• {group.Count}x {group.Label}");
				}
			}

			// ==========================================
			// Destinations
			// ==========================================

			if (Settings.ShowDestinations &&
				info.HasFreightCars &&
				info.DestinationSummary.Count > 0)
			{
				sb.Append($"\n\n{Header("Destinations")}");

				foreach (string destination in info.DestinationSummary)
				{
					sb.Append($"\n• {destination}");
				}
			}

			_trainTooltipText.richText = true;
			_trainTooltipText.text = sb.ToString();
		}

		if (_trainTooltipGo != null && !_trainTooltipGo.activeSelf)
		{
			_trainTooltipGo.SetActive(true);
		}
	}

	private void HideTrainTooltip()
	{
		if (_trainTooltipGo != null && _trainTooltipGo.activeSelf)
		{
			_trainTooltipGo.SetActive(false);
		}
	}

	// private void ShowFreightCarTooltip(FreightCarInfo fci)
	// {
	// 	if (_trainTooltipGo == null)
	// 	{
	// 		CreateTrainTooltip();
	// 	}

	// 	if (_trainTooltipText != null)
	// 	{
	// 		var sb = new System.Text.StringBuilder();

	// 		sb.Append($"Status\n{(string.IsNullOrEmpty(fci.Status) ? "Unknown" : fci.Status)}");

	// 		if (!string.IsNullOrEmpty(fci.DestinationName))
	// 		{
	// 			sb.Append($"\n\nDestination\n{fci.DestinationName}");
	// 		}

	// 		if (!string.IsNullOrEmpty(fci.CargoName))
	// 		{
	// 			sb.Append($"\n\nCargo\n{fci.CargoName}");
	// 		}

	// 		sb.Append($"\n\nCapacity\n{fci.LoadWeightTons:F0} / {fci.CapacityTons:F0} T");
	// 		float pct = fci.CapacityTons > 0f ? (fci.LoadWeightTons / fci.CapacityTons) * 100f : 0f;
	// 		sb.Append($"\n{pct:F0}%");

	// 		sb.Append($"\n\nCar Type\n{fci.CarTypeName}");

	// 		var warnings = new List<string>();
	// 		if (fci.HasHotbox) warnings.Add("Hotbox");
	// 		if (fci.HandBrakeApplied) warnings.Add("Hand Brake Applied");

	// 		if (warnings.Count > 0)
	// 		{
	// 			sb.Append($"\n\nOther\n{string.Join("\n", warnings)}");
	// 		}

	// 		_trainTooltipText.text = sb.ToString();
	// 	}

	// 	if (_trainTooltipGo != null && !_trainTooltipGo.activeSelf)
	// 	{
	// 		_trainTooltipGo.SetActive(true);
	// 	}
	// }

	private void ShowFreightCarTooltip(FreightCarInfo fci)
	{
		if (_trainTooltipGo == null)
		{
			CreateTrainTooltip();
		}

		if (_trainTooltipText != null)
		{
			var sb = new System.Text.StringBuilder();

				const string headerColor = "#9CDCFE";

			// -----------------------------
			// Car Name
			// -----------------------------

			sb.Append($"<b><color={headerColor}>{fci.CarTypeName}</color></b>");

			if (!string.IsNullOrWhiteSpace(fci.CarNumber))
			{
				sb.Append($"\n{fci.CarNumber}");
			}

			// -----------------------------
			// Status
			// -----------------------------
			string status = string.IsNullOrWhiteSpace(fci.Status)
				? "None"
				: fci.Status;

			string statusColor = status switch
			{
				"Delivered" => "#4CAF50",   // Green
				"Pending"   => "#FFC107",   // Amber
				"None"      => "#9E9E9E",   // Grey
				_           => "#FFFFFF"
			};

			sb.Append($"\n\n<color={headerColor}>Status</color>\n");
			sb.Append($"<color={statusColor}>{status}</color>");

			// -----------------------------
			// Destination
			// -----------------------------
			sb.Append($"\n\n<color={headerColor}>Destination</color>\n");

			if (!string.IsNullOrWhiteSpace(fci.DestinationName))
				sb.Append(fci.DestinationName);
			else
				sb.Append("<color=#9E9E9E>None</color>");

			// -----------------------------
			// Cargo
			// -----------------------------
			sb.Append($"\n\n<color={headerColor}>Cargo</color>\n");

			string cargoName = string.IsNullOrWhiteSpace(fci.CargoName)
				? "<color=#9E9E9E>Empty</color>"
				: fci.CargoName;

			sb.Append(cargoName);

			// -----------------------------
			// Capacity
			// -----------------------------
			sb.Append($"\n\n<color={headerColor}>Capacity</color>\n");

			if (fci.CapacityTons > 0f)
			{
				if (fci.LoadWeightTons <= 0f)
				{
					sb.Append($"Empty (0 / {fci.CapacityTons:F0} T)");
				}
				else
				{
					float pct = (fci.LoadWeightTons / fci.CapacityTons) * 100f;
					sb.Append($"{fci.LoadWeightTons:F0} / {fci.CapacityTons:F0} T ({pct:F0}%)");
				}
			}
			else
			{
				sb.Append("<color=#9E9E9E>Unknown</color>");
			}

			// -----------------------------
			// Car Type
			// -----------------------------
			sb.Append($"\n\n<color={headerColor}>Car Type</color>\n");

			if (!string.IsNullOrWhiteSpace(fci.CarTypeName))
				sb.Append(fci.CarTypeName);
			else
				sb.Append("<color=#9E9E9E>Unknown</color>");

			// -----------------------------
			// Health
			// -----------------------------
			sb.Append($"\n\n<color={headerColor}>Health</color>\n");
			string oilText = fci.JournalOilPercent >= 0 ? $"{fci.JournalOilPercent}%" : "Unknown";
			sb.Append($"Journal Oil    {oilText}\n");
			// sb.Append($"Hand Brake     {(fci.HandBrakeApplied ? "Applied" : "Released")}");

			// -----------------------------
			// Alerts
			// -----------------------------
			var alerts = new List<string>();

			if (fci.HasHotbox)
			{
				alerts.Add("<color=#EF5350>• Hotbox Detected</color>");
			}
			else if (fci.JournalOilPercent >= 0)
			{
				if (fci.JournalOilPercent < 10)
				{
					alerts.Add($"<color=#EF5350>• Hotbox Risk ({fci.JournalOilPercent}% oil)</color>");
				}
				else if (fci.JournalOilPercent < 25)
				{
					alerts.Add($"<color=#FF9800>• Critical Journal Oil ({fci.JournalOilPercent}%)</color>");
				}
				else if (fci.JournalOilPercent < 50)
				{
					alerts.Add($"<color=#FFD54F>• Low Journal Oil ({fci.JournalOilPercent}%)</color>");
				}
			}

			if (fci.HandBrakeApplied)
			{
				alerts.Add("<color=#FFD54F>• Hand Brake Applied</color>");
			}

			if (alerts.Count > 0)
			{
				sb.Append($"\n\n<color={headerColor}>Alerts</color>");

				foreach (string alert in alerts)
				{
					sb.Append($"\n{alert}");
				}
			}

			_trainTooltipText.richText = true;
			_trainTooltipText.text = sb.ToString();
		}

		if (_trainTooltipGo != null && !_trainTooltipGo.activeSelf)
		{
			_trainTooltipGo.SetActive(true);
		}
	}

	private GameObject? _gradeLegendGo;

	private void CreateGradeLegend()
	{
		if (_gradeLegendGo != null) return;

		_gradeLegendGo = new GameObject("Grade Legend", typeof(RectTransform));
		var rect = _gradeLegendGo.GetComponent<RectTransform>();
		rect.SetParent(MapWindow.instance._window.transform, false);

		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(0f, 0f);
		rect.pivot = new Vector2(0f, 0f);
		rect.anchoredPosition = new Vector2(10f, 10f);

		var bgImage = _gradeLegendGo.AddComponent<Image>();
		bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

		var layout = _gradeLegendGo.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(10, 10, 10, 10);
		layout.spacing = 6f;
		layout.childAlignment = TextAnchor.MiddleLeft;
		layout.childControlWidth = true;
		layout.childControlHeight = true;

		var fitter = _gradeLegendGo.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		var titleGo = new GameObject("Title", typeof(RectTransform));
		titleGo.transform.SetParent(_gradeLegendGo.transform, false);
		var titleText = titleGo.AddComponent<TextMeshProUGUI>();
		titleText.text = "<b>" + (Settings.Overlay == TrackOverlayMode.Cleanliness ? "Vegetation clearance" : Settings.Overlay.ToString()) + "</b>";
		titleText.fontSize = 11f;
		titleText.color = Color.white;
		titleText.alignment = TextAlignmentOptions.Left;

		var titleFitter = titleGo.AddComponent<ContentSizeFitter>();
		titleFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		titleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		AddOverlayLegendItems();

		_gradeLegendGo.SetActive(false);
	}

	private void AddLegendItem(string label, Color color)
	{
		if (_gradeLegendGo == null) return;

		var itemGo = new GameObject(label, typeof(RectTransform));
		itemGo.transform.SetParent(_gradeLegendGo.transform, false);

		var layout = itemGo.AddComponent<HorizontalLayoutGroup>();
		layout.spacing = 8f;
		layout.childAlignment = TextAnchor.MiddleLeft;
		layout.childControlWidth = false;
		layout.childControlHeight = false;

		var boxGo = new GameObject("ColorBox", typeof(RectTransform));
		boxGo.transform.SetParent(itemGo.transform, false);
		var img = boxGo.AddComponent<Image>();
		img.color = color;
		var boxRect = boxGo.GetComponent<RectTransform>();
		boxRect.sizeDelta = new Vector2(12f, 12f);

		var textGo = new GameObject("Text", typeof(RectTransform));
		textGo.transform.SetParent(itemGo.transform, false);
		var text = textGo.AddComponent<TextMeshProUGUI>();
		text.text = label;
		text.fontSize = 10f;
		text.color = Color.white;
		text.alignment = TextAlignmentOptions.Left;

		var textFitter = textGo.AddComponent<ContentSizeFitter>();
		textFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		var itemFitter = itemGo.AddComponent<ContentSizeFitter>();
		itemFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		itemFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	private void UpdateGradeLegendColors()
	{
		if (_gradeLegendGo != null)
		{
			Destroy(_gradeLegendGo);
			_gradeLegendGo = null;
		}

		if (Settings.Overlay != TrackOverlayMode.Routes && MapWindow.instance != null && MapWindow.instance._window.IsShown)
		{
			CreateGradeLegend();
			_gradeLegendGo!.SetActive(true);
		}
	}

	private Color GetGradeMarkerColor(float grade) => OverlayMath.GradeBand(grade) switch
    {
        0 => Settings.GradeColorFlat,
        1 => Settings.GradeColor0to1,
        2 => Settings.GradeColor1to2,
        3 => Settings.GradeColor2to3,
        4 => Settings.GradeColorAbove3,
        _ => Color.gray
    };

	private readonly List<MapIcon> _gradeMarkers = new List<MapIcon>();

	private void GatherGradeMarkers()
	{
		DestroyGradeMarkers();

        if (!Settings.ShowGradeMarkers || GradeMarkers == null)
            return;

		const float minSpacing = 400f; // minimum distance between markers
        var spawnedPositions = new List<Vector3>();

        // Collect qualified mainline segments
        var qualifiedSegments = new List<(TrackSegment segment, GradeInfo info)>();
        foreach (var segment in Graph.Shared.Segments)
        {
            if (segment == null || segment.a == null || segment.b == null)
                continue;
            if (!mainlineSegments.Contains(segment.id))
                continue;
            if (!_segmentGrades.TryGetValue(segment.id, out var info))
                continue;
            if (Mathf.Abs(info.Grade) < Settings.GradeMarkerMinIntensity)
                continue;
            qualifiedSegments.Add((segment, info));
        }

        // Build adjacency map (node -> segments)
        var nodeToSegments = new Dictionary<string, List<TrackSegment>>();
        foreach (var (segment, _) in qualifiedSegments)
        {
            if (!nodeToSegments.TryGetValue(segment.a.id, out var listA))
            {
                listA = new List<TrackSegment>();
                nodeToSegments[segment.a.id] = listA;
            }
            listA.Add(segment);
            if (!nodeToSegments.TryGetValue(segment.b.id, out var listB))
            {
                listB = new List<TrackSegment>();
                nodeToSegments[segment.b.id] = listB;
            }
            listB.Add(segment);
        }

        var visited = new HashSet<string>();
        foreach (var (startSeg, _) in qualifiedSegments)
        {
            if (visited.Contains(startSeg.id)) continue;

            // BFS to collect connected cluster
            var clusterSegments = new List<TrackSegment>();
            var queue = new Queue<TrackSegment>();
            queue.Enqueue(startSeg);
            visited.Add(startSeg.id);
            while (queue.Count > 0)
            {
                var seg = queue.Dequeue();
                clusterSegments.Add(seg);
                // gather neighbors via shared nodes
                var neighbors = new List<TrackSegment>();
                if (seg.a != null && nodeToSegments.TryGetValue(seg.a.id, out var na)) neighbors.AddRange(na);
                if (seg.b != null && nodeToSegments.TryGetValue(seg.b.id, out var nb)) neighbors.AddRange(nb);
                foreach (var nbr in neighbors)
                {
                    if (visited.Contains(nbr.id)) continue;
                    if (!qualifiedSegments.Any(q => q.segment.id == nbr.id)) continue;
					var currentGrade = Mathf.Abs(_segmentGrades[seg.id].Grade);
					var neighborGrade = Mathf.Abs(_segmentGrades[nbr.id].Grade);
					if (Mathf.Abs(currentGrade - neighborGrade) > 0.5f) continue;
                    visited.Add(nbr.id);
                    queue.Enqueue(nbr);
                }
            }

            float totalLength = 0f;
			float totalSignedGrade = 0f;
			float peakAbsGrade = 0f;
            foreach (var seg in clusterSegments)
            {
                var info = _segmentGrades[seg.id];
                totalLength += info.Length;
				totalSignedGrade += info.Grade * info.Length;
				peakAbsGrade = Mathf.Max(peakAbsGrade, Mathf.Abs(info.Grade));
            }

            // Find midpoint along cumulative length
            float target = totalLength / 2f;
            float acc = 0f;
            Vector3 markerPos = _segmentGrades[clusterSegments[0].id].Midpoint;
			Vector3 markerSide = Vector3.zero;
            foreach (var seg in clusterSegments)
            {
                var info = _segmentGrades[seg.id];
                if (acc + info.Length >= target)
                {
                    float t = (target - acc) / info.Length;
                    var aPos = WorldTransformer.WorldToGame(WorldTransformer.GameToWorld(seg.a.Position));
                    var bPos = WorldTransformer.WorldToGame(WorldTransformer.GameToWorld(seg.b.Position));
                    markerPos = Vector3.Lerp(aPos, bPos, t);
					markerSide = Vector3.Cross(Vector3.up, (bPos - aPos).normalized);
					markerSide.y = 0f;
					if (markerSide.sqrMagnitude > 0f)
					{
						markerSide.Normalize();
					}
                    break;
                }
                acc += info.Length;
            }

            if (IsTooClose(markerPos, spawnedPositions, minSpacing))
                continue;

			markerPos += markerSide * 25f;
			var worldPos = WorldTransformer.GameToWorld(markerPos);
			var netGrade = totalLength > 0f ? totalSignedGrade / totalLength : 0f;
			var gradeSign = Mathf.Abs(netGrade) > 0.01f ? Mathf.Sign(netGrade) : Mathf.Sign(_segmentGrades[clusterSegments[0].id].Grade);
			if (gradeSign == 0f)
				gradeSign = 1f;
			var displayGrade = peakAbsGrade * gradeSign;

			CreateMarkerAt(worldPos, displayGrade);

			spawnedPositions.Add(markerPos);
        }

		bool IsTooClose(Vector3 candidate, List<Vector3> existing, float spacing)
		{
			foreach (var p in existing)
			{
				if (Vector3.Distance(candidate, p) < spacing)
					return true;
			}
			return false;
		}

		void CreateMarkerAt(Vector3 worldPos, float grade)
		{
			string FormatGradeText(float grade)
			{
				var absGrade = Mathf.Abs(grade);
				var symbolCount = absGrade >= 2.5f ? 3 : absGrade >= 1.7f ? 2 : 1;
				var symbols = new string('^', symbolCount);
				return $"{symbols} {grade:+0.0;-0.0}%";
			}

			var mapIcon = Instantiate<MapIcon>(
				TrainController.Shared.locomotiveMapIconPrefab,
				GradeMarkers.transform);

			mapIcon.name = $"GradeMarker_{worldPos}";

			mapIcon.transform.position = worldPos + Vector3.up * 2200f;

			// Keep icon flat on map
			mapIcon.transform.localRotation =
				Quaternion.Euler(90f,0f,0f);

			var image = mapIcon.GetComponentInChildren<Image>();
			if(image != null)
				image.enabled = false;

			mapIcon.SetText(FormatGradeText(grade));

			if(mapIcon.Text != null)
			{
				mapIcon.Text.color = GetGradeMarkerColor(grade);
				mapIcon.Text.fontSize = 14f;
				mapIcon.Text.textWrappingMode = TextWrappingModes.NoWrap;
				mapIcon.Text.overflowMode = TextOverflowModes.Overflow;

				// Rotate ONLY the text
				mapIcon.Text.transform.rotation =
					Quaternion.LookRotation(
						-MapBuilder.Shared.mapCamera.transform.forward,
						MapBuilder.Shared.mapCamera.transform.up
					);
				// Flip text face
				mapIcon.Text.transform.Rotate(0f,180f,0f);
			}

			_gradeMarkers.Add(mapIcon);
		}
	}

	private void DestroyGradeMarkers()
	{
		foreach (var marker in _gradeMarkers)
		{
			if (marker != null)
			{
				DestroyImmediate(marker.gameObject);
			}
		}
		_gradeMarkers.Clear();
	}

	private IEnumerator UpdateTrainInfoCoroutine()
	{
		while (true)
		{
			try
			{
				RefreshTrainInfoCache();
			}
			catch (Exception ex)
			{
				Loader.Log($"Error updating train info: {ex}");
			}
			yield return new WaitForSeconds(1f);
		}
	}

	private void RefreshTrainInfoCache()
	{
		_trainInfoCache.Clear();
		var processedTrainsets = new HashSet<object>();

		foreach (var car in TrainController.Shared.Cars)
		{
			if (car == null) continue;
			if (!car.CarType.IsLocomotive) continue;

			var trainset = car.set;
			if (trainset != null)
			{
				if (!processedTrainsets.Add(trainset))
				{
					continue;
				}
			}

			List<Car> allCars = GetConsistCars(car);
			if (allCars.Count == 0) continue;

			var info = new TrainInfo();
			info.LeadLoco = car;
			info.TrainName = car.DisplayName ?? car.Ident.RoadNumber ?? car.id ?? "Unknown";

			info.SpeedMph = car.VelocityMphAbs;

			float lengthMeters = allCars.Sum(c => c.carLength) + 1.0f * Mathf.Max(0, allCars.Count - 1);
			info.LengthFt = Mathf.CeilToInt(lengthMeters * 3.28084f);
			info.WeightTons = allCars.Sum(c => c.Weight) / 2000f;
			info.CarCount = allCars.Count(c => !c.CarType.IsLocomotive);
			int paxCurrent = 0;
			int paxCapacity = 0;
			TryGetPassengerCounts(allCars, out paxCurrent, out paxCapacity);
			info.PassengerCount = paxCurrent;
			info.PassengerCapacity = paxCapacity;
			info.DriverName = GetDriverName(car);
			info.DriveMode = GetDriveMode(car);
			// DriveType removed: not shown in tooltip

			// --- Cargo Intelligence ---
			info.Fuel = TryGetFuelInfo(car);
			GatherFreightData(info, allCars);

			LogConsistRefreshIfChanged("Consist", info, allCars, includeFuel: true);

			foreach (var c in allCars)
			{
				if (c != null && c.id != null)
				{
					_trainInfoCache[c.id] = info;
				}
			}
		}

		// Second loop: process any remaining uncached cars (e.g. loose cars/consists without an engine)
		foreach (var car in TrainController.Shared.Cars)
		{
			if (car == null) continue;
			if (_trainInfoCache.ContainsKey(car.id)) continue;

			var trainset = car.set;
			if (trainset != null)
			{
				if (!processedTrainsets.Add(trainset))
				{
					continue;
				}
			}

			// Gather consist/list of cars in this engine-less trainset
			List<Car> allCars;
			if (trainset != null && trainset.Cars != null)
			{
				allCars = new List<Car>();
				foreach (var c in trainset.Cars)
				{
					if (c != null) allCars.Add(c);
				}
			}
			else
			{
				allCars = new List<Car> { car };
			}

			if (allCars.Count == 0) continue;

			var info = new TrainInfo();
			info.LeadLoco = null!; // No locomotive

			// For engine-less consists, name it using the first car or similar
			var leadCar = allCars[0];
			info.TrainName = leadCar.DisplayName ?? leadCar.Ident.RoadNumber ?? leadCar.id ?? "Unknown";

			info.SpeedMph = leadCar.VelocityMphAbs;

			float lengthMeters = allCars.Sum(c => c.carLength) + 1.0f * Mathf.Max(0, allCars.Count - 1);
			info.LengthFt = Mathf.CeilToInt(lengthMeters * 3.28084f);
			info.WeightTons = allCars.Sum(c => c.Weight) / 2000f;
			info.CarCount = allCars.Count(c => !c.CarType.IsLocomotive);

			int paxCurrent = 0;
			int paxCapacity = 0;
			TryGetPassengerCounts(allCars, out paxCurrent, out paxCapacity);
			info.PassengerCount = paxCurrent;
			info.PassengerCapacity = paxCapacity;

			info.DriverName = "";
			info.DriveMode = "";

			info.Fuel = null;
			GatherFreightData(info, allCars);

			LogConsistRefreshIfChanged("Loose consist", info, allCars, includeFuel: false);

			foreach (var c in allCars)
			{
				if (c != null && c.id != null)
				{
					_trainInfoCache[c.id] = info;
				}
			}
		}

		// Waypoint marker feature removed; no per-selected-loco marker update
	}

	private void LogConsistRefreshIfChanged(string label, TrainInfo info, List<Car> allCars, bool includeFuel)
	{
		try
		{
			var lines = new List<string>
			{
				$"[MapEnhancer] {label} cache refresh: Name={info.TrainName}, Length={info.LengthFt} ft, Cars={allCars.Count}, Weight={info.WeightTons:F1} tons"
			};

			if (includeFuel && info.Fuel != null)
			{
				if (info.Fuel.IsSteam)
				{
					lines.Add($"[MapEnhancer]   Steam Fuel - Coal: {info.Fuel.CoalPercent:F1}%, Water: {info.Fuel.WaterPercent:F1}%");
				}
				else
				{
					lines.Add($"[MapEnhancer]   Diesel Fuel - Fuel: {info.Fuel.DieselPercent:F1}%");
				}
			}

			if (info.HasFreightCars)
			{
				foreach (var fci in info.FreightCars)
				{
					lines.Add($"[MapEnhancer]   Car {fci.Car.id} ({fci.CarTypeName}): Cargo={fci.CargoName}, Load={fci.LoadWeightTons:F1}T/{fci.CapacityTons:F1}T, Status={fci.Status}, Handbrake={fci.HandBrakeApplied}, Hotbox={fci.HasHotbox}");
				}
			}

			var cacheKey = $"{label}:{BuildConsistLogKey(allCars)}";
			var signature = string.Join("\n", lines);
			if (_consistRefreshLogSignatures.TryGetValue(cacheKey, out var previousSignature) && previousSignature == signature)
			{
				return;
			}

			_consistRefreshLogSignatures[cacheKey] = signature;
			foreach (var line in lines)
			{
				Loader.LogDebug(line);
			}
		}
		catch (Exception ex)
		{
			Loader.LogDebug($"[MapEnhancer]   {label} debug logging error: {ex.Message}");
		}
	}

	private static string BuildConsistLogKey(List<Car> allCars)
	{
		var ids = allCars
			.Where(car => car != null && !string.IsNullOrEmpty(car.id))
			.Select(car => car.id!)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToArray();

		if (ids.Length > 0)
		{
			return string.Join("|", ids);
		}

		var fallbackCar = allCars.Count > 0 ? allCars[0] : null;
		return fallbackCar?.id ?? fallbackCar?.DisplayName ?? "unknown";
	}

	private List<Car> GetConsistCars(Car loco)
	{
		var result = new List<Car>();
		var trainset = loco.set;
		if (trainset != null && trainset.Cars != null)
		{
			foreach (var car in trainset.Cars)
			{
				if (car != null)
				{
					result.Add(car);
				}
			}
		}
		else
		{
			result.Add(loco);
		}
		return result;
	}

	private int TryGetPassengerCount(List<Car> cars)
	{
		int total = 0;
		foreach (var car in cars)
		{
			if (car == null) continue;
			try
			{
				if (car.IsPassengerCar())
				{
					var pm = car.GetPassengerMarker();
					if (pm.HasValue)
					{
						total += pm.Value.TotalPassengers;
					}
				}
			}
			catch
			{
				// Graceful ignore
			}
		}
		return total;
	}

	private void TryGetPassengerCounts(List<Car> cars, out int current, out int capacity)
	{
		current = 0;
		capacity = 0;
		foreach (var car in cars)
		{
			if (car == null) continue;
			try
			{
				if (car.IsPassengerCar())
				{
					var pm = car.GetPassengerMarker();
					if (pm.HasValue)
					{
						current += pm.Value.TotalPassengers;
						// Try to extract capacity from Car.PassengerCountString if available (format: "cur/total")
						try
						{
							string countStr = car.PassengerCountString(pm.Value);
							if (!string.IsNullOrEmpty(countStr))
							{
								var parts = countStr.Split('/');
								if (parts.Length >= 2)
								{
									var m = Regex.Match(parts[1], "(\\d+)");
									if (m.Success)
									{
										capacity += int.Parse(m.Groups[1].Value);
									}
								}
							}
						}
						catch { }
					}
				}
			}
			catch { }
		}
		// If we couldn't determine capacity but have current pax, set capacity = current to avoid showing 0/0
		if (capacity == 0 && current > 0) capacity = current;
	}


	private string GetDriverName(Car loco)
	{
		try
		{
			var pm = StateManager.Shared?._playersManager;
			string? preferredName = null;
			// If the loco is the selected car/locomotive, prefer the local player's name
			if (TrainController.Shared != null && (TrainController.Shared.SelectedCar == loco || TrainController.Shared.SelectedLocomotive == loco))
			{
				if (pm != null)
				{
					var localPlayer = pm.LocalPlayer;
					if (!string.IsNullOrEmpty(localPlayer.Name))
						return localPlayer.Name;
					preferredName = !string.IsNullOrEmpty(localPlayer.Name) ? localPlayer.Name : null;
				}
			}

			if (pm != null)
			{
				// Try to find a player who is occupying this loco (preferred) or within proximity (fallback)
				if (pm.AllPlayers != null)
				{
					foreach (var player in pm.AllPlayers)
					{
						if (player == null) continue;
						try
						{
							// Some player types expose an OccupiedCar or OccupiedVehicle property
							var occupied = player.GetType().GetProperty("OccupiedCar", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(player)
								?? player.GetType().GetProperty("occupiedCar", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(player);
							if (occupied is Car oc && oc == loco)
								return player.Name ?? preferredName ?? "Player";
						}
						catch { }

						// Fallback: proximity check (existing behaviour)
						try
						{
							Vector3 playerPosGame = player.GamePosition;
							Vector3 locoPosGame = WorldTransformer.WorldToGame(loco.transform.position);
							if (Vector3.Distance(playerPosGame, locoPosGame) < 20f)
							{
								return player.Name ?? preferredName ?? "Player";
							}
						}
						catch { }
					}
				}
			}
		}
		catch (Exception ex)
		{
			Loader.LogDebug($"Error getting driver name: {ex}");
		}
		return "AI";
	}

	private string GetDriveMode(Car loco)
	{
		if (loco is BaseLocomotive bl)
		{
			try
			{
				// Prefer reading the AutoEngineerPlanner orders when available (most reliable)
				try
				{
					var planner = bl.GetComponent<Model.AI.AutoEngineerPlanner>();
					if (planner != null)
					{
						// Try property 'Orders' then field '_orders'
						var ordProp = planner.GetType().GetProperty("Orders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
						var ordField = planner.GetType().GetField("_orders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
						var ordersObj = ordProp != null ? ordProp.GetValue(planner) : ordField != null ? ordField.GetValue(planner) : null;
						if (ordersObj != null)
						{
							var modeProp = ordersObj.GetType().GetProperty("Mode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
							var modeField = ordersObj.GetType().GetField("Mode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
											?? ordersObj.GetType().GetField("mode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
							var modeVal = modeProp != null ? modeProp.GetValue(ordersObj) : modeField != null ? modeField.GetValue(ordersObj) : null;
							if (modeVal != null)
							{
								return MapDriveMode(modeVal.ToString() ?? "");
							}
						}
					}
				}
				catch { }
				// Try several likely field/property names to be resilient across game versions
				string? raw = null;
				var t = bl.GetType();
				var candidates = new[] { "autoEngineerMode", "AutoEngineerMode", "driveMode", "DriveMode", "mode", "Mode" };
				foreach (var name in candidates)
				{
					var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
					if (f != null)
					{
						raw = f.GetValue(bl)?.ToString();
						break;
					}
					var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
					if (p != null)
					{
						raw = p.GetValue(bl)?.ToString();
						break;
					}
				}

				// Try nested autoEngineer object
				if (string.IsNullOrEmpty(raw))
				{
					var aeField = t.GetField("autoEngineer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
						?? t.GetField("AutoEngineer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
					if (aeField != null)
					{
						var ae = aeField.GetValue(bl);
						if (ae != null)
						{
							var aeType = ae.GetType();
							var nestedCandidates = new[] { "mode", "Mode", "currentMode" };
							foreach (var n in nestedCandidates)
							{
								var nf = aeType.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
								if (nf != null)
								{
									raw = nf.GetValue(ae)?.ToString();
									break;
								}
								var np = aeType.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
								if (np != null)
								{
									raw = np.GetValue(ae)?.ToString();
									break;
								}
							}
						}
					}
				}

				if (!string.IsNullOrEmpty(raw))
					return MapDriveMode(raw ?? "");
			}
			catch (Exception ex)
			{
				Loader.LogDebug($"Error getting drive mode: {ex}");
			}
		}
		return "Manual";
	}

	// DriveType removed per user request.

	private string MapDriveMode(string rawMode)
	{
		if (string.IsNullOrEmpty(rawMode)) return "Manual";
		if (rawMode.Equals("Road", StringComparison.OrdinalIgnoreCase)) return "AE Road";
		if (rawMode.Equals("Yard", StringComparison.OrdinalIgnoreCase)) return "AE Yard";
		if (rawMode.Equals("Off", StringComparison.OrdinalIgnoreCase) || rawMode.Equals("None", StringComparison.OrdinalIgnoreCase)) return "Manual";
		return rawMode;
	}

	private FuelInfo? TryGetFuelInfo(Car loco)
	{
		if (loco == null) return null;
		try
		{
			if (loco is SteamLocomotive steamLoco)
			{
				var info = new FuelInfo { IsSteam = true };
				var fuelCar = steamLoco.FuelCar();
				if (fuelCar != null && fuelCar.Definition != null)
				{
					// Coal slot
					int coalIdx = fuelCar.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == Model.Definition.Data.LoadIdentifier.Coal);
					if (coalIdx >= 0)
					{
						var loadOpt = Model.Ops.CarOpsExtensions.GetLoadInfo(fuelCar, coalIdx);
						float qty = loadOpt.HasValue ? loadOpt.Value.Quantity : 0f;
						float cap = fuelCar.Definition.LoadSlots[coalIdx].MaximumCapacity;
						info.CoalPercent = cap > 0f ? (qty / cap) * 100f : 0f;
					}
					else
					{
						info.CoalPercent = -1f;
					}

					// Water slot
					int waterIdx = fuelCar.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == Model.Definition.Data.LoadIdentifier.Water);
					if (waterIdx >= 0)
					{
						var loadOpt = Model.Ops.CarOpsExtensions.GetLoadInfo(fuelCar, waterIdx);
						float qty = loadOpt.HasValue ? loadOpt.Value.Quantity : 0f;
						float cap = fuelCar.Definition.LoadSlots[waterIdx].MaximumCapacity;
						info.WaterPercent = cap > 0f ? (qty / cap) * 100f : 0f;
					}
					else
					{
						info.WaterPercent = -1f;
					}
					return info;
				}
			}
			else if (loco is DieselLocomotive dieselLoco)
			{
				var info = new FuelInfo { IsSteam = false };
				if (dieselLoco.Definition != null)
				{
					// Diesel fuel slot
					int dieselIdx = dieselLoco.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == Model.Definition.Data.LoadIdentifier.DieselFuel);
					if (dieselIdx >= 0)
					{
						var loadOpt = Model.Ops.CarOpsExtensions.GetLoadInfo(dieselLoco, dieselIdx);
						float qty = loadOpt.HasValue ? loadOpt.Value.Quantity : 0f;
						float cap = dieselLoco.Definition.LoadSlots[dieselIdx].MaximumCapacity;
						info.DieselPercent = cap > 0f ? (qty / cap) * 100f : 0f;
						return info;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Loader.LogDebug($"TryGetFuelInfo error: {ex.Message}");
		}
		return null;
	}

	private static string GetAreaDisplayName(Area area)
	{
		if (area == null || string.IsNullOrEmpty(area.identifier)) return "";

		string id = area.identifier.ToLowerInvariant();
		switch (id)
		{
			case "sylva": return "Sylva";
			case "whittier": return "Whittier";
			case "bryson": return "Bryson";
			case "dillsboro": return "Dillsboro";
			case "ela": return "Ela";
			case "murphy": return "Murphy";
			case "andrews": return "Andrews";
			case "barkers-creek":
			case "barkerscreek": return "Barkers Creek";
			default:
				var parts = area.identifier.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < parts.Length; i++)
				{
					if (parts[i].Length > 0)
					{
						parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
					}
				}
				return string.Join(" ", parts);
		}
	}

	/// <summary>
	/// Single-pass consist scan that populates all freight-related fields on <paramref name="info"/>.
	/// Called from RefreshTrainInfoCache; never called per-frame.
	/// </summary>
	private void GatherFreightData(TrainInfo info, List<Car> allCars)
	{
		info.HasFreightCars = false;
		info.LoadedCarCount = 0;
		info.EmptyCarCount  = 0;
		info.CargoSummary.Clear();
		info.DestinationSummary.Clear();
		info.FreightCars.Clear();

		// Temporary structures for grouping
		var cargoGroupOrder  = new List<string>();
		var cargoGroupCounts = new Dictionary<string, int>();
		var seenDestinations = new HashSet<string>(StringComparer.Ordinal);

		var opsController = OpsController.Shared;

		foreach (var car in allCars)
		{
			if (car == null) continue;
			if (!car.CarType.IsFreight) continue;

			info.HasFreightCars = true;

			var fci = new FreightCarInfo();
			fci.Car = car;
			fci.CarTypeName = GetCarTypeName(car);
			fci.CarNumber = car.DisplayName ?? $"{car.Ident.ReportingMark} {car.Ident.RoadNumber}";

			// --- Destination + Status via OpsController ---
			try
			{
				if (opsController != null)
				{
					string destText;
					bool iconEnabled;
					Vector3 destPos;
					OpsCarPosition opsPos;
					if (CanQueryDestinationInfo(opsController, car) && opsController.TryGetDestinationInfo(car, out destText, out iconEnabled, out destPos, out opsPos))
					{
						if (!string.IsNullOrEmpty(destText))
						{
							fci.DestinationName = destText;

							// Extract area name for consist summary
							var area = opsController.AreaForCarPosition(opsPos);
							string areaName = GetAreaDisplayName(area);
							if (string.IsNullOrEmpty(areaName))
							{
								areaName = destText;
								int spaceIdx = areaName.IndexOf(' ');
								if (spaceIdx > 0)
								{
									if (areaName.StartsWith("Barkers Creek", StringComparison.OrdinalIgnoreCase))
									{
										areaName = "Barkers Creek";
									}
									else
									{
										areaName = areaName.Substring(0, spaceIdx);
									}
								}
							}

							if (!string.IsNullOrEmpty(areaName))
							{
								if (seenDestinations.Add(areaName))
								{
									info.DestinationSummary.Add(areaName);
								}
							}
						}
						// iconEnabled = true means the car is at the destination (Delivered)
						fci.Status = iconEnabled ? "Delivered" : "Pending";
					}
				}
			}
			catch (Exception ex)
			{
				Loader.LogDebug($"GatherFreightData DestinationInfo error for {car.id}: {ex.Message}");
			}

			// --- Cargo name ---
			try
			{
				fci.CargoName = TryGetCargoName(car);
			}
			catch (Exception ex)
			{
				Loader.LogDebug($"GatherFreightData CargoName error for {car.id}: {ex.Message}");
			}

			// --- Weight / capacity ---
			try
			{
				(fci.LoadWeightTons, fci.CapacityTons) = TryGetCargoWeight(car);
			}
			catch (Exception ex)
			{
				Loader.LogDebug($"GatherFreightData Weight error for {car.id}: {ex.Message}");
			}

			// --- Handbrake ---
			try
			{
				fci.HandBrakeApplied = TryGetHandbrake(car);
			}
			catch { }

			// --- Hotbox ---
			try
			{
				fci.HasHotbox = car.HasHotbox;
			}
			catch { }

			// --- Journal Oil ---
			try
			{
				fci.JournalOilPercent = car.EnableOiling ? Mathf.RoundToInt(car.Oiled * 100f) : -1;
			}
			catch
			{
				fci.JournalOilPercent = -1;
			}

			// --- Loaded / empty classification ---
			bool isLoaded = fci.LoadWeightTons > 0.01f || fci.Status == "Pending";
			if (isLoaded) info.LoadedCarCount++;
			else          info.EmptyCarCount++;

			// --- Build cargo group label ---
			string groupLabel = BuildCargoGroupLabel(car, fci.CargoName, isLoaded);
			if (!cargoGroupCounts.ContainsKey(groupLabel))
			{
				cargoGroupCounts[groupLabel] = 0;
				cargoGroupOrder.Add(groupLabel);
			}
			cargoGroupCounts[groupLabel]++;

			info.FreightCars.Add(fci);
		}

		// Convert group dict ordered list
		foreach (var label in cargoGroupOrder)
		{
			info.CargoSummary.Add(new CargoGroupEntry { Label = label, Count = cargoGroupCounts[label] });
		}
	}

	/// <summary>Returns a human-readable car type name (e.g. "Boxcar", "Hopper").</summary>
	private static string GetCarTypeName(Car car)
	{
		if (car == null) return "Freight Car";
		try
		{
			if (car.Definition != null && !string.IsNullOrEmpty(car.Definition.CarType))
			{
				string ct = car.Definition.CarType;
				if (ct.Length > 0)
				{
					return char.ToUpper(ct[0]) + ct.Substring(1);
				}
				return ct;
			}
			var arch = car.CarType.ToString();
			if (arch.StartsWith("Freight", StringComparison.OrdinalIgnoreCase))
				arch = arch.Substring(7);
			return string.IsNullOrEmpty(arch) ? "Freight Car" : arch;
		}
		catch { return "Freight Car"; }
	}

	/// <summary>Attempts to read the loaded cargo type name from the car using strongly-typed definitions.</summary>
	private static string TryGetCargoName(Car car)
	{
		if (car == null || car.Definition == null) return "";
		try
		{
			for (int i = 0; i < car.Definition.LoadSlots.Count; i++)
			{
				var loadInfoOpt = Model.Ops.CarOpsExtensions.GetLoadInfo(car, i);
				if (loadInfoOpt.HasValue)
				{
					string loadId = loadInfoOpt.Value.LoadId;
					if (!string.IsNullOrEmpty(loadId))
					{
						var loadDef = Model.Ops.Definition.CarLoadLibrary.GetLoad(loadId);
						if (loadDef != null)
						{

							if (loadDef != null && !string.IsNullOrEmpty(loadDef.description))
							{
								return loadDef.description;
							}
						}
						return loadId;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Loader.LogDebug($"TryGetCargoName error: {ex.Message}");
		}
		return "";
	}

	/// <summary>
	/// Returns (loadWeightTons, capacityTons) for a freight car.
	/// loadWeightTons is the weight of cargo only (total – empty weight).
	/// capacityTons is the max rated capacity (0 if unknown).
	/// </summary>
	private static (float loadWeight, float capacity) TryGetCargoWeight(Car car)
	{
		if (car == null || car.Definition == null) return (0f, 0f);
		try
		{
			float totalLoadWeightTons = 0f;
			float totalCapacityTons = 0f;

			for (int i = 0; i < car.Definition.LoadSlots.Count; i++)
			{
				var loadInfoOpt = Model.Ops.CarOpsExtensions.GetLoadInfo(car, i);
				if (loadInfoOpt.HasValue)
				{
					string loadId = loadInfoOpt.Value.LoadId;
					if (!string.IsNullOrEmpty(loadId))
					{
						var loadDef = Model.Ops.Definition.CarLoadLibrary.GetLoad(loadId);
						if (loadDef != null)
						{

							if (loadDef != null)
							{
								var qtyCap = Model.Ops.CarOpsExtensions.QuantityCapacityOfLoad(car, loadDef);
								float qtyLbs = loadDef.Pounds(qtyCap.Item1);
								float capLbs = loadDef.Pounds(qtyCap.Item2);
								totalLoadWeightTons += qtyLbs / 2000f;
								totalCapacityTons += capLbs / 2000f;
							}
						}
					}
				}
			}

			if (totalLoadWeightTons > 0f || totalCapacityTons > 0f)
			{
				return (totalLoadWeightTons, totalCapacityTons);
			}
		}
		catch (Exception ex)
		{
			Loader.LogDebug($"TryGetCargoWeight error: {ex.Message}");
		}

		// Fallback: total weight - empty weight
		try
		{
			float totalWeightLbs = car.Weight;
			float emptyWeightLbs = car.Definition.WeightEmpty;
			float loadWeightLbs = Mathf.Max(0f, totalWeightLbs - emptyWeightLbs);
			return (loadWeightLbs / 2000f, 0f);
		}
		catch
		{
			return (0f, 0f);
		}
	}

	/// <summary>Reads the handbrake state via car.air.handbrakeApplied.</summary>
	private static bool TryGetHandbrake(Car car)
	{
		if (car == null || car.air == null) return false;
		return car.air.handbrakeApplied;
	}

	/// <summary>Reads the hotbox / overheated journal state via car.HasHotbox.</summary>
	private static bool TryGetHotbox(Car car)
	{
		if (car == null) return false;
		return car.HasHotbox;
	}

	/// <summary>
	/// Builds the consist-summary group label for a freight car,
	/// e.g. "Loaded Lumber Cars", "Empty Boxcars".
	/// </summary>
	private static string BuildCargoGroupLabel(Car car, string cargoName, bool isLoaded)
	{
		var carType = GetCarTypeName(car);
		// Pluralise simply by appending 's' if not already ending with 's'
		string pluralType = carType.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? carType : carType + "s";
		if (isLoaded)
		{
			string cargoDesc = !string.IsNullOrEmpty(cargoName) ? cargoName : "Cargo";
			return $"Loaded {cargoDesc} {pluralType}";
		}
		else
		{
			return $"Empty {pluralType}";
		}
	}

	private static void AttachTrainMarkerData(Car car, MapIcon marker)
	{
		if (marker == null || car == null) return;
		var tmd = marker.gameObject.GetComponent<TrainMarkerData>();
		if (tmd == null)
		{
			tmd = marker.gameObject.AddComponent<TrainMarkerData>();
		}
		tmd.Car = car;
	}

	public static MapEnhancer Instance
	{
		get { return Loader.Instance; }
	}

	void Update() {
        UpdateMapLifecycle();
        UpdateVisualData();
		if (MapState == MapStates.MAPLOADED && Junctions != null && _mapWindow != null)
		{
			bool mapWindowShown = _mapWindow._window.IsShown;
			if (Junctions.activeSelf != mapWindowShown)
			{
				Junctions.SetActive(mapWindowShown);
			}

			if (mapWindowShown)
			{
				if (mapSettings == null) CreateMapSettings();
				RefreshOverlayDropdown();
			}
		}
	}

	void OnDestroy()
	{
		Loader.LogDebug("OnDestroy");
        var window = _mapWindow;
        StopMapLifecycle();
		_overlayDropdown = null;

		if (JunctionMarker.matJunctionGreen != null) Destroy(JunctionMarker.matJunctionGreen);
		if (JunctionMarker.matJunctionRed != null) Destroy(JunctionMarker.matJunctionRed);

		if (_prefabHolder != null)
		{
			//TODO cleanup sprite/tex
			DestroyImmediate(_prefabHolder);
		}

		if (window != null)
		{
			if (window._window != null && window._window.IsShown && window.mapBuilder != null)
                window.mapBuilder.Rebuild();

			if (_traincarPrefab != null) Destroy(_traincarPrefab);
			_traincarPrefab = null;
			if (_turntablePrefab != null) Destroy(_turntablePrefab);
			_turntablePrefab = null;
			if (_crossingPrefab != null) Destroy(_crossingPrefab);
			_crossingPrefab = null;

			DestroyTraincarMarkers();
			DestroyFlareMarkers();
			DestroyTurntableMarkers();
		}
		ResetStaticState();
        MapState = MapStates.MAINMENU;
	}

	private void InitializeMap(MapWindow window)
	{
		Loader.LogDebug("InitializeMap");
		_sessionStartTime = DateTime.UtcNow;

		MapState = MapStates.MAPLOADED;
		CleanupIconsAndLabels();
		CaptureNativeMapSettings();
        JunctionMarker.CreatePrefab();

		var junctionRoot = new GameObject("Junctions");
		Junctions = junctionRoot;
		junctionRoot.SetActive(window._window.IsShown);
		JunctionsMainline = new GameObject("Mainline Junctions");
		JunctionsMainline.transform.SetParent(junctionRoot.transform, false);
		JunctionsBranch = new GameObject("Branch Junctions");
		JunctionsBranch.transform.SetParent(junctionRoot.transform, false);
		Turntables = new GameObject("Turntables");
		Turntables.transform.SetParent(junctionRoot.transform, false);
		Crossings = new GameObject("Road Crossings");
		Crossings.transform.SetParent(junctionRoot.transform, false);
		junctionRoot.SetActive(window._window.IsShown);

		if (window._window.IsShown)
		{
			junctionRoot.SetActive(true);
		}

		GatherTraincarMarkers();
		traincarColorUpdater = StartCoroutine(TraincarColorUpdater());
		_trainInfoUpdater = StartCoroutine(UpdateTrainInfoCoroutine());

		GatherFlareMarkers();

		InitializeSwitchResetAuditStorage();
		GatherTurntables();
		GatherCrossingMarkers();

		EventBus.Subscribe<WorldDidMoveEvent>(this, new Action<WorldDidMoveEvent>(this.WorldDidMove));
		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		junctionRoot.transform.position = worldPos;



		// Now that the map is fully loaded, populate mainline segments and classify all existing tracks
		// Force re-population to ensure we have the most up-to-date CTC blocks from all mods
		_mainlineSegments = null;
		_mainlineSwitches = null;



		// Initialize GradeMarkers parent
		GradeMarkers = new GameObject("Grade Markers");
		GradeMarkers.transform.SetParent(junctionRoot.transform, false);

		// Compute segment grades
		RefreshTrackCategories();
        ComputeSegmentGrades();

		// Spawn grade markers
		GatherGradeMarkers();

		Rebuild();
		resizer = MapResizer.Create();
		OnSettingsChanged();
		if (window._window.IsShown)
		{
			MapBuilder.Shared.Rebuild();
			OnMapWindowShown(true);
		}
	}

	private void CleanupIconsAndLabels()
	{
	}

	private void CleanupMap()
	{
		Loader.LogDebug("CleanupMap");

		MapState = MapStates.MAPUNLOADING;
        _visualRefreshPending = false;
        StopAllCoroutines();
        ReleaseMapOwnership();

		// Clear tracking sets
		_passengerStopSegments.Clear();
		_industrialSegments.Clear();
		_industrialSegmentColors.Clear();
		_segmentGrades.Clear();
		_industrialSegmentNames.Clear();
		_passengerStopSegmentNames.Clear();
		_spawnPointCategories.Clear();

		EventBus.Unsubscribe<WorldDidMoveEvent>(this);
		if (cullingGroup != null)
		{
			cullingGroup.Dispose();
		}
		cullingGroup = null;

		if (Junctions != null) Destroy(Junctions);
		Junctions = null;
		junctionMarkers.Clear();

		traincarColorUpdater = null;
		_trainInfoUpdater = null;
		_turntableMapRefreshCoroutine = null;
		_trainInfoCache.Clear();
		_consistRefreshLogSignatures.Clear();

		if (resizer != null)
			DestroyImmediate(resizer.gameObject);
		resizer = null;


		DestroySwitchResetAuditStorage();
		DestroyCrossingMarkers();
		DestroyGradeMarkers();

		if (_gradeTooltipGo != null) Destroy(_gradeTooltipGo);
		_gradeTooltipGo = null;
		_gradeTooltipText = null;
		_gradeTooltipRect = null;

		if (_trainTooltipGo != null) Destroy(_trainTooltipGo);
		_trainTooltipGo = null;
		_trainTooltipText = null;
		_trainTooltipRect = null;

		if (_gradeLegendGo != null) Destroy(_gradeLegendGo);
		_gradeLegendGo = null;



		DestroyMapSettings();
		if (dropdownSprite != null)
		{
			Destroy(dropdownSprite.texture);
			Destroy(dropdownSprite);
		}
	}

	private void WorldDidMove(WorldDidMoveEvent evt)
	{
		Loader.LogDebug("WorldDidMove");

		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		if (Junctions != null) Junctions.transform.position = worldPos;
		UpdateCullingSpheres();
	}

	private void OnMapWindowShown(bool shown)
	{
		Junctions?.SetActive(shown);

		if (shown)
		{
			if (mapSettings == null)
			{
				CreateMapSettings();
			}
			RefreshOverlayDropdown();
			UpdateGradeLegendColors();
		}
		else
		{
			if (_gradeLegendGo != null)
			{
				_gradeLegendGo.SetActive(false);
			}
			if (_gradeTooltipGo != null)
			{
				_gradeTooltipGo.SetActive(false);
			}
			if (_trainTooltipGo != null)
			{
				_trainTooltipGo.SetActive(false);
			}
		}
	}

	private static bool IsCrossingMarkerName(string name)
	{
		return name.IndexOf("crossing", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private sealed class CrossingMarkerCluster
	{
		public readonly List<TrackMarker> Members = new List<TrackMarker>();
		public Vector3 GamePositionSum;

		public Vector3 Center => Members.Count == 0 ? Vector3.zero : GamePositionSum / Members.Count;
	}

	private void GatherCrossingMarkers()
	{
		_crossingMarkers.Clear();

		if (!Settings.ShowRoadCrossingMarkers || Crossings == null)
		{
			return;
		}

		var trackMarkers = FindObjectsByType<TrackMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		Loader.Log($"Found {trackMarkers.Length} track marker(s) while searching for road crossings");

		var clusters = new List<CrossingMarkerCluster>();
		const float clusterRadius = 25f;

		foreach (var trackMarker in trackMarkers)
		{
			if (trackMarker == null)
			{
				continue;
			}

			if (!IsCrossingMarkerName(trackMarker.gameObject.name))
			{
				continue;
			}

			var positionRotation = trackMarker.PositionRotation;
			if (!positionRotation.HasValue)
			{
				continue;
			}

			var gamePosition = positionRotation.Value.Position;
			var cluster = clusters.FirstOrDefault(candidate => Vector3.Distance(candidate.Center, gamePosition) <= clusterRadius);
			if (cluster == null)
			{
				cluster = new CrossingMarkerCluster();
				clusters.Add(cluster);
			}

			cluster.Members.Add(trackMarker);
			cluster.GamePositionSum += gamePosition;
		}

		foreach (var cluster in clusters)
		{
			var mapIcon = AddCrossingMarker(cluster);
			if (mapIcon != null)
			{
				_crossingMarkers.Add(mapIcon);
			}
		}

		Loader.Log($"Created {_crossingMarkers.Count} road crossing marker(s) from {clusters.Count} clustered location(s)");
	}

	private void DestroyCrossingMarkers()
	{
		foreach (var mapIcon in _crossingMarkers)
		{
			if (mapIcon == null)
			{
				continue;
			}

			DestroyImmediate(mapIcon.gameObject);
		}

		_crossingMarkers.Clear();
	}

	private static MapIcon? AddCrossingMarker(CrossingMarkerCluster cluster)
	{
		if (cluster == null || cluster.Members.Count == 0 || Instance == null || Instance.Crossings == null)
		{
			return null;
		}

		var prefab = crossingPrefab;
        if (prefab == null) return null;
        var mapIcon = Instantiate<MapIcon>(prefab, Instance.Crossings.transform);
		var gamePosition = cluster.Center;
		mapIcon.transform.position = WorldTransformer.GameToWorld(gamePosition) + Vector3.up * 1200f;
		mapIcon.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		mapIcon.SetText("");

		var scaleOverride = mapIcon.gameObject.AddComponent<CrossingIconScaleOverride>();
		var image = mapIcon.GetComponentInChildren<Image>();
		if (image != null)
		{
			image.enabled = Loader.Settings.ShowRoadCrossingMarkers;
		}

		var rectTransform = mapIcon.GetComponent<RectTransform>();
		if (rectTransform != null)
		{
			rectTransform.sizeDelta = new Vector2(220f, 220f);
		}

		return scaleOverride != null ? mapIcon : null;
	}

	private static Sprite? LoadCrossingSprite()
	{
		return LoadTexture("level-crossing.png", "MapCrossingIcon");
	}





	private void InitializeSwitchResetAuditStorage()
	{
		if (_switchResetAuditStorage != null)
		{
			return;
		}

		_switchResetAuditStorageGo = new GameObject("MapEnhancer_SwitchResetAuditStorage");
		_switchResetAuditStorageGo.hideFlags = HideFlags.HideAndDontSave;
		_switchResetAuditStorageGo.transform.SetParent(transform, false);

		var keyValueObject = _switchResetAuditStorageGo.AddComponent<KeyValueObject>();
		_switchResetAuditStorage = new SwitchResetAuditStorage(keyValueObject);
		_switchResetAuditObserver = _switchResetAuditStorage.ObserveRequests(OnSwitchResetAuditRequest);
		_processedSwitchResetRequestIds.Clear();
		_processedSwitchResetLogIds.Clear();
		_hasLoggedOldSwitchResetReplayIgnored = false;
		_switchResetRequestSlot = 0;
		_switchResetLogSlot = 0;
		_hostSwitchResetActionCount = 0;
		_hostSwitchesResetTotal = 0;
		Loader.LogDebug($"Initialized switch reset audit storage. Ignoring entries older than {_sessionStartTime:yyyy-MM-dd HH:mm:ss} UTC");
	}

	private void DestroySwitchResetAuditStorage()
	{
		_switchResetAuditObserver?.Dispose();
		_switchResetAuditObserver = null;
		_switchResetAuditStorage?.Dispose();
		_switchResetAuditStorage = null;
		if (_switchResetAuditStorageGo != null)
		{
			DestroyImmediate(_switchResetAuditStorageGo);
			_switchResetAuditStorageGo = null;
		}
		_processedSwitchResetRequestIds.Clear();
		_processedSwitchResetLogIds.Clear();
		_switchResetRequestSlot = 0;
		_switchResetLogSlot = 0;
	}

	internal void RefreshMapAfterTurntableRotation()
	{
		if (_turntableMapRefreshCoroutine != null)
		{
			StopCoroutine(_turntableMapRefreshCoroutine);
		}

		_turntableMapRefreshCoroutine = StartCoroutine(RefreshMapAfterTurntableRotationCoroutine());
	}

	private IEnumerator RefreshMapAfterTurntableRotationCoroutine()
	{
		yield return null;

		if (MapState != MapStates.MAPLOADED)
		{
			_turntableMapRefreshCoroutine = null;
			yield break;
		}

		Loader.LogDebug("Refreshing map after turntable rotation");
		Rebuild();
		if (MapWindow.instance != null && MapWindow.instance._window.IsShown)
		{
			MapBuilder.Shared.Rebuild();
		}

		yield return null;

		if (MapState == MapStates.MAPLOADED && MapWindow.instance != null && MapWindow.instance._window.IsShown)
		{
			MapBuilder.Shared.Rebuild();
		}

		_turntableMapRefreshCoroutine = null;
	}

	private int GetOverlayDropdownIndex()
	{
		int index = Array.IndexOf(OverlayModes, Settings.Overlay);
		return index >= 0 ? index : 0;
	}

	private void SelectOverlay(int index)
	{
		if (index < 0 || index >= OverlayModes.Length) return;
		Settings.Overlay = OverlayModes[index];
		Settings.Save();
		RefreshOverlayDropdown();

		var mapBuilder = MapBuilder.Shared;
		if (MapState != MapStates.MAPLOADED || mapBuilder == null) return;
		UpdateGradeLegendColors();
		var renderer = mapBuilder._meshRenderer;
		var indices = mapBuilder._segmentIndexMap;
		if (renderer != null && renderer._mesh != null &&
			indices != null && indices.Count == renderer.SegmentCount)
			mapBuilder.RefreshSegmentColors();
		else
			mapBuilder.Rebuild();
	}

	private void RefreshOverlayDropdown()
	{
		if (mapSettings == null || _overlayDropdown == null)
		{
			_overlayDropdown = null;
			return;
		}

		int index = GetOverlayDropdownIndex();
		if (_overlayDropdown.value != index) _overlayDropdown.SetValueWithoutNotify(index);
	}

	private void DestroyMapSettings()
	{
		_overlayDropdown = null;
		if (mapSettings != null) Destroy(mapSettings.gameObject);
		mapSettings = null;
	}

	private void CreateMapSettings()
	{
		var settingsGo = new GameObject("Map Settings", typeof(RectTransform));
		mapSettings = settingsGo.GetComponent<RectTransform>();
		mapSettings.SetParent(MapWindow.instance._window.transform, false);
		var settingsPanelHeight = Settings.ShowAdvancedMapWindowSettings ? 210f : 134f;
		mapSettings.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 27, settingsPanelHeight);
		mapSettings.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 4, 145);

		settingsGo.AddComponent<GraphicRaycaster>();

		var panel = UIPanel.Create(mapSettings, UIBuilderAssets.Shared, builder =>
		{
			builder.FieldLabelWidth = 105f;
			builder.AddField("Follow Mode", builder.AddToggle(() => mapFollowMode, (x) => mapFollowMode = x));

			if (Settings.ShowAdvancedMapWindowSettings)
			{
				builder.AddField("Hover Grade", builder.AddToggle(() => Settings.ShowGradeOnHover, (x) => {
					Settings.ShowGradeOnHover = x;
					Settings.Save();
				}));

				builder.AddField("Grade Markers", builder.AddToggle(() => Settings.ShowGradeMarkers, (x) => {
					Settings.ShowGradeMarkers = x;
					Settings.Save();
					OnSettingsChanged();
				}));
			}

			_overlayDropdown = builder.AddDropdown(
				new List<string> { "Overlay: Off", "Grade", "Track Class", "Track Condition", "Track Cleanliness" },
				GetOverlayDropdownIndex(), SelectOverlay).GetComponent<TMP_Dropdown>();

			TMP_Dropdown? dropdown = null;
			var teleportLocations = GetTeleportLocations();
		dropdown = builder.AddDropdown(teleportLocations, 0, (index) =>
		{
			// Skip if separator or header clicked
			if (index == 0 || teleportLocations[index].text.StartsWith("---"))
			{
				dropdown!.SetValueWithoutNotify(0);
				return;
			}

			SpawnPoint spawnPoint = TeleportCommand.SpawnPointFor(teleportLocations[index].text);
			if (spawnPoint != null)
			{
				var mapCamera = MapBuilder.Shared.mapCamera.transform;
				ValueTuple<Vector3, Quaternion> gamePositionRotation = spawnPoint.GamePositionRotation;
				Vector3 item = gamePositionRotation.Item1;
				item.y = mapCamera.position.y;
				mapCamera.position = WorldTransformer.GameToWorld(item);
				dropdown!.SetValueWithoutNotify(0);
			}
		}).GetComponent<TMP_Dropdown>();
		AddLocoSelectorDropdown(builder);
		if (Settings.ShowAdvancedMapWindowSettings)
		{
			AddResetSwitchesDropdown(builder);
		}
	});
	settingsGo.AddComponent<Image>().color = new Color(0.1098f, 0.1098f, 0.1098f, 1f);
	_lastShowAdvancedMapWindowSettings = Settings.ShowAdvancedMapWindowSettings;

	void AddResetSwitchesDropdown(UIPanelBuilder builder)
	{
		TMP_Dropdown? resetDropdown = null;
		var resetOptions = new List<TMP_Dropdown.OptionData>()
		{
			new TMP_Dropdown.OptionData("Switch Reset..."),
			new TMP_Dropdown.OptionData("All to Normal"),
			new TMP_Dropdown.OptionData("All to Thrown")
		};

		resetDropdown = builder.AddDropdown(resetOptions, 0, (index) =>
		{
			if (index == 0) return; // Skip default option

			if (index == 1)
			{
				// Reset all switches to normal (straight)
				ResetAllSwitchesToNormal();
			}
			else if (index == 2)
			{
				// Set all switches to thrown (diverging)
				ResetAllSwitchesToThrown();
			}

			resetDropdown!.SetValueWithoutNotify(0); // Reset dropdown to default
		}).GetComponent<TMP_Dropdown>();
	}

	void AddLocoSelectorDropdown(UIPanelBuilder builder)
		{
			RectTransform rectTransform = builder.CreateRectView("DropDown", 0, 0);

			TMP_Dropdown? dropdown = null;
			var mapCamera = MapBuilder.Shared.mapCamera.transform;
			var locoList = TrainController.Shared.Cars.Where((Car car) => car.IsLocomotive).OrderBy(car => car.SortName).ToList();
			dropdown = builder.AddDropdown(new List<string>() { "Locomotive..." }, 0, index =>
			{
				if (index == 0) return;
				var cars = dropdown!.GetComponent<DropdownClickHandler>().cars;
				var car = cars[index - 1];
				if (car == null) return;

				var position = WorldTransformer.GameToWorld(car.GetCenterPosition(Graph.Shared));
				position.y = mapCamera.position.y;
				mapCamera.position = position;
				dropdown!.SetValueWithoutNotify(0);
				mapCameraTarget = car;
				MapWindow.instance.mapDrag._isDragging = false;
			}).GetComponent<TMP_Dropdown>();
			dropdown.gameObject.AddComponent<DropdownClickHandler>();
			DestroyImmediate(dropdown.template.transform.Find("Viewport/Content/Item/Item Checkmark").gameObject);
			dropdown.template.transform.Find("Viewport/Content/Item/Item Label").GetComponent<RectTransform>().offsetMin = new Vector2(10f, 1f);
			dropdown.template.sizeDelta = new Vector2(0f, 15 * 20f + 8f);
		}

		List<TMP_Dropdown.OptionData> GetTeleportLocations()
		{
			var POIs = GameObject.Find("World/POIs").transform;

			// Load spawn points from whitelisted mods
			LoadModSpawnPoints();

			if (!POIs.Find("Connelly"))
			{
				var sp = new GameObject("Connelly", typeof(SpawnPoint)).transform;
				sp.SetParent(POIs, false);
				sp.position = WorldTransformer.GameToWorld(new Vector3(11896.60f, 608.67f, 2875.93f));
				sp.eulerAngles = new Vector3(0, 255f, 0);
			}

			var logCamp = POIs.Find("LogCamp1");
			if (logCamp) logCamp.name = "Walker";

			var _teleportLocations = new List<TMP_Dropdown.OptionData>() { new TMP_Dropdown.OptionData("Location...") };
			var areas = OpsController.Shared.Areas;
			Dictionary<string, Color> locationColorLookup = new Dictionary<string, Color>();
			locationColorLookup = SpawnPoint.All.Where(sp => sp.name.ToLower() != "ds").Select(sp =>
			{
				var area = OpsController.Shared.ClosestAreaForGamePosition(sp.GamePositionRotation.Item1);
				var color = area != null ? area.tagColor : Color.grey;
				return new { sp.name, color };
			}).ToDictionary(kvp => kvp.name, kvp => kvp.color);

			var tex = new Texture2D(20, 20);
			dropdownSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector3.zero);

			// Separate vanilla and modded spawn points
			var allSpawnPoints = SpawnPoint.All.Where(sp => sp.name.ToLower() != "ds").ToList();
			var vanillaSpawnPoints = allSpawnPoints.Where(sp => !moddedSpawnPointNames.Contains(sp.name)).ToList();
			var moddedSpawnPoints = allSpawnPoints.Where(sp => moddedSpawnPointNames.Contains(sp.name)).ToList();

			// Add vanilla locations first (sorted by color hue)
			_teleportLocations.AddRange(vanillaSpawnPoints.Select(sp => new TMP_Dropdown.OptionData(sp.name, dropdownSprite, locationColorLookup[sp.name])).OrderBy(sp =>
			{
				float h, s, v;
				Color.RGBToHSV(sp.color, out h, out s, out v);
				return h;
			}));

			// Add modded locations grouped by category if any exist and feature is enabled
			if (Settings.EnableModdedSpawnPoints && moddedSpawnPoints.Count > 0)
			{
				// Sort categories alphabetically
				var sortedCategories = _spawnPointCategories.Keys.OrderBy(k => k).ToList();

				foreach (var category in sortedCategories)
				{
					var categorySpawnPoints = _spawnPointCategories[category];
					// Only add category if it has spawn points that were actually loaded
					var validSpawnPoints = moddedSpawnPoints.Where(sp => categorySpawnPoints.Contains(sp.name)).ToList();
					if (validSpawnPoints.Count > 0)
					{
						// Add category separator
						_teleportLocations.Add(new TMP_Dropdown.OptionData($"--- {category} ---"));
						// Add spawn points in this category (sorted alphabetically)
						_teleportLocations.AddRange(validSpawnPoints.Select(sp => new TMP_Dropdown.OptionData(sp.name, dropdownSprite, locationColorLookup[sp.name])).OrderBy(sp => sp.text));
					}
				}
			}
			return _teleportLocations;
		}
	}

	private void LoadModSpawnPoints()
	{
		if (_modSpawnPointsLoaded)
		{
			return;
		}

		// Clear previous modded spawn points tracking
		moddedSpawnPointNames.Clear();
		_spawnPointCategories.Clear();

		// Skip if feature is disabled
		if (!Settings.EnableModdedSpawnPoints)
		{
			Loader.LogDebug("Additional locations from mods disabled in settings");
			return;
		}

		// Get the Mods directory (assuming Railroader/Mods/)
		string railroaderPath = Path.GetDirectoryName(Application.dataPath);
		string modsPath = Path.Combine(railroaderPath, "Mods");

		if (!Directory.Exists(modsPath))
		{
			Loader.Log($"Mods directory not found at: {modsPath}");
			return;
		}

		var POIs = GameObject.Find("World/POIs").transform;
		int totalLoaded = 0;

		// Auto-discover all mods with spawn-points.json files
		var modFolders = Directory.GetDirectories(modsPath);
		Loader.LogDebug($"Scanning {modFolders.Length} mod folders for spawn-points.json files");

	foreach (string modFolderPath in modFolders)
	{
		string modFolderName = Path.GetFileName(modFolderPath);
		string spawnPointsFile = Path.Combine(modFolderPath, "spawn-points.json");

		if (!File.Exists(spawnPointsFile))
		{
			Loader.LogDebug($"No spawn-points.json found in {modFolderName}");
			continue;
		}

		try
		{
			string json = File.ReadAllText(spawnPointsFile);

			// Extract title (optional field, defaults to mod folder name)
			string categoryTitle = modFolderName;
			var titleMatch = Regex.Match(json, @"""title""\s*:\s*""([^""]+)""");
			if (titleMatch.Success)
			{
				categoryTitle = titleMatch.Groups[1].Value;
			}

			// Simple regex-based parsing for the spawn-points.json format
			// Updated pattern to handle more float formats including scientific notation
			var nameMatches = Regex.Matches(json, @"""name""\s*:\s*""([^""]+)""");
			var posMatches = Regex.Matches(json, @"""position""\s*:\s*\{\s*""x""\s*:\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*,\s*""y""\s*:\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*,\s*""z""\s*:\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)");

			if (nameMatches.Count == 0 || posMatches.Count == 0)
			{
				Loader.Log($"Invalid spawn-points.json format in {modFolderName}: no spawn points found");
				continue;
			}

				int spawnPointCount = Math.Min(nameMatches.Count, posMatches.Count);

				for (int i = 0; i < spawnPointCount; i++)
				{
					string spawnName = nameMatches[i].Groups[1].Value;

					if (string.IsNullOrEmpty(spawnName))
					{
						Loader.Log($"Invalid spawn point data in {modFolderName}: missing name");
						continue;
					}

					// Parse floats using InvariantCulture to handle different decimal separators
					if (!float.TryParse(posMatches[i].Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
						!float.TryParse(posMatches[i].Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) ||
						!float.TryParse(posMatches[i].Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
					{
						Loader.Log($"Invalid position data for spawn point '{spawnName}' in {modFolderName}: could not parse coordinates");
						continue;
					}

					// Check if spawn point already exists
					if (POIs.Find(spawnName))
					{
						Loader.LogDebug($"Spawn point '{spawnName}' already exists, skipping");
						continue;
					}

					// Create the spawn point
					var sp = new GameObject(spawnName, typeof(SpawnPoint)).transform;
					sp.SetParent(POIs, false);
					sp.position = WorldTransformer.GameToWorld(new Vector3(x, y, z));
					sp.eulerAngles = Vector3.zero; // Default rotation

					// Track this as a modded spawn point with its category
					moddedSpawnPointNames.Add(spawnName);
					// Store category mapping for dropdown grouping
					if (!_spawnPointCategories.ContainsKey(categoryTitle))
					{
						_spawnPointCategories[categoryTitle] = new List<string>();
					}
					_spawnPointCategories[categoryTitle].Add(spawnName);

					totalLoaded++;
					Loader.LogDebug($"Loaded spawn point '{spawnName}' from {modFolderName} at ({x}, {y}, {z})");
				}
			var perModLogKey = $"{modFolderName}:{spawnPointCount}";
			if (_spawnPointSummaryLogKeys.Add(perModLogKey))
			{
				Loader.Log($"Loaded {spawnPointCount} spawn point(s) from {modFolderName}");
			}
		}
		catch (Exception ex)
		{
			Loader.Log($"Error loading spawn points from {modFolderName}: {ex.Message}");
		}
	}

		_modSpawnPointsLoaded = true;

		if (totalLoaded > 0)
		{
			var totalLogKey = $"total:{totalLoaded}";
			if (_spawnPointSummaryLogKeys.Add(totalLogKey))
			{
				Loader.Log($"Total spawn points loaded from mods: {totalLoaded}");
			}
		}
	}

	private void ResetAllSwitchesToNormal()
	{
		int switchesReset = 0;

		foreach (var kvp in TrackObjectManager.Instance._descriptors.switches)
		{
			var switchNode = kvp.Value.node;

			// Only reset if switch is thrown (not in normal position)
			if (switchNode.isThrown)
			{
				StateManager.ApplyLocal(new RequestSetSwitch(switchNode.id, false));
				switchesReset++;
			}
		}

		LogSwitchResetAction("Normal", switchesReset);
	}

	private void ResetAllSwitchesToThrown()
	{
		int switchesReset = 0;

		foreach (var kvp in TrackObjectManager.Instance._descriptors.switches)
		{
			var switchNode = kvp.Value.node;

			// Only set if switch is in normal position (not thrown)
			if (!switchNode.isThrown)
			{
				StateManager.ApplyLocal(new RequestSetSwitch(switchNode.id, true));
				switchesReset++;
			}
		}

		LogSwitchResetAction("Thrown", switchesReset);
	}

	private void LogSwitchResetAction(string action, int switchCount)
	{
		string userName = GetCurrentPlayerName();
		string actionText = action.Equals("Normal", StringComparison.OrdinalIgnoreCase) ? "Normal" : "Reversed";

		if (IsHostMultiplayer() || !IsClientMultiplayer())
		{
			if (IsHostMultiplayer() && PublishSwitchResetAuditLog(userName, actionText, switchCount, "host-local"))
			{
				return;
			}

			WriteSwitchResetAuditLocal(userName, actionText, switchCount, "singleplayer", 0, 0);
			return;
		}

		if (TrySendSwitchResetAuditRequest(userName, actionText, switchCount))
		{
			Loader.Log($"Switch reset request sent to host by '{userName}' for action '{actionText}' ({switchCount} switch(es))");
			return;
		}

		Loader.Log("Switch reset host-sync unavailable, falling back to local audit log");
		WriteSwitchResetAuditLocal(userName, actionText, switchCount, "local-fallback", 0, 0);
	}

	private string GetCurrentPlayerName()
	{
		string userName = Environment.UserName.ToLower();
		try
		{
			var playersManager = StateManager.Shared?._playersManager;
			if (playersManager != null)
			{
				var localPlayer = playersManager.LocalPlayer;
				if (!string.IsNullOrEmpty(localPlayer.Name))
				{
					userName = localPlayer.Name.ToLower();
				}
			}
		}
		catch (Exception ex)
		{
			Loader.Log($"Could not get Steam player name: {ex.Message}");
		}

		return userName;
	}

	private void OnSwitchResetAuditRequest(string key, SwitchResetAuditState? request)
	{
		if (request == null)
		{
			return;
		}

		// Filter out old entries from previous sessions by timestamp
		if (DateTime.TryParse(request.TimestampUtc, out var requestTimestamp))
		{
			if (requestTimestamp < _sessionStartTime)
			{
				if (!_hasLoggedOldSwitchResetReplayIgnored)
				{
					Loader.LogDebug($"Ignoring restored switch reset entries from before session start ({_sessionStartTime:yyyy-MM-dd HH:mm:ss} UTC)");
					_hasLoggedOldSwitchResetReplayIgnored = true;
				}
				return;
			}
		}

		if (key.StartsWith(SwitchResetAuditLogPrefix, StringComparison.Ordinal))
		{
			if (string.IsNullOrWhiteSpace(request.RequestId) || !_processedSwitchResetLogIds.Add(request.RequestId))
			{
				return;
			}

			MaybeTrimProcessedSwitchResetIds();

			WriteSwitchResetAuditLocal(
				request.RequesterName,
				request.Action,
				request.SwitchCount,
				"host-sync",
				request.HostSequence,
				request.HostTotal);
			return;
		}

		if (!key.StartsWith(SwitchResetAuditRequestPrefix, StringComparison.Ordinal) || !IsHostMultiplayer())
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(request.RequestId) || !_processedSwitchResetRequestIds.Add(request.RequestId))
		{
			return;
		}

		MaybeTrimProcessedSwitchResetIds();

		PublishSwitchResetAuditLog(request.RequesterName, request.Action, request.SwitchCount, "client-request");
	}

	private bool TrySendSwitchResetAuditRequest(string userName, string actionText, int switchCount)
	{
		if (_switchResetAuditStorage == null)
		{
			return false;
		}

		var requestId = SwitchResetAuditRequestPrefix + Guid.NewGuid().ToString("N");
		var request = new SwitchResetAuditState(
			requestId,
			userName,
			actionText,
			switchCount,
			DateTime.UtcNow.ToString("O"));
		var key = GetNextSwitchResetAuditKey(SwitchResetAuditRequestPrefix, isLogKey: false);

		StateManager.ApplyLocal(new PropertyChange(
			SwitchResetAuditStorage.ObjectId,
			key,
			PropertyValueConverter.RuntimeToSnapshot(request.ToPropertyValue())));

		return true;
	}

	private bool PublishSwitchResetAuditLog(string userName, string actionText, int switchCount, string source)
	{
		if (_switchResetAuditStorage == null)
		{
			return false;
		}

		_hostSwitchResetActionCount++;
		_hostSwitchesResetTotal += Mathf.Max(0, switchCount);
		var key = GetNextSwitchResetAuditKey(SwitchResetAuditLogPrefix, isLogKey: true);
		var requestId = SwitchResetAuditLogPrefix + Guid.NewGuid().ToString("N");
		var state = new SwitchResetAuditState(
			requestId,
			userName,
			actionText,
			switchCount,
			DateTime.UtcNow.ToString("O"),
			_hostSwitchResetActionCount,
			_hostSwitchesResetTotal);

		_switchResetAuditStorage.Write(key, state);
		return true;
	}

	private string GetNextSwitchResetAuditKey(string prefix, bool isLogKey)
	{
		if (isLogKey)
		{
			_switchResetLogSlot = (_switchResetLogSlot % MaxSwitchResetAuditEntries) + 1;
			return prefix + _switchResetLogSlot.ToString("D4");
		}

		_switchResetRequestSlot = (_switchResetRequestSlot % MaxSwitchResetAuditEntries) + 1;
		return prefix + _switchResetRequestSlot.ToString("D4");
	}

	private void MaybeTrimProcessedSwitchResetIds()
	{
		if (_processedSwitchResetRequestIds.Count > MaxProcessedSwitchResetIds * 2)
		{
			_processedSwitchResetRequestIds.Clear();
		}

		if (_processedSwitchResetLogIds.Count > MaxProcessedSwitchResetIds * 2)
		{
			_processedSwitchResetLogIds.Clear();
		}
	}

	private void WriteSwitchResetAuditLocal(string userName, string actionText, int switchCount, string source, int hostSequence, int hostTotal)
	{
		string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		var sequence = hostSequence > 0 ? hostSequence : ++_hostSwitchResetActionCount;
		if (hostTotal > 0)
		{
			_hostSwitchesResetTotal = Math.Max(_hostSwitchesResetTotal, hostTotal);
		}
		else
		{
			_hostSwitchesResetTotal += Mathf.Max(0, switchCount);
			hostTotal = _hostSwitchesResetTotal;
		}

		string logMessage =
			$"[SwitchReset#{sequence}] {userName} reset switches to {actionText} " +
			$"(count: {switchCount}, host total: {hostTotal})";

		global::Console.Log(logMessage);
		Loader.Log(logMessage);

		try
		{
			string logPath = Path.Combine(Loader.Context.ModDirectory, "MapEnhancer_SwitchResets.log");
			string fileLogMessage = $"[{timestamp}] [source:{source}] {logMessage}";
			File.AppendAllText(logPath, fileLogMessage + Environment.NewLine);
		}
		catch (Exception ex)
		{
			Loader.Log($"Failed to write switch reset log to file: {ex.Message}");
		}
	}

	private static bool IsHostMultiplayer()
	{
		return GetMultiplayerFlag("IsHost");
	}

	private static bool IsClientMultiplayer()
	{
		return GetMultiplayerFlag("IsClient");
	}

	private static bool GetMultiplayerFlag(string memberName)
	{
		var type = typeof(Multiplayer);
		var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		if (property != null && property.PropertyType == typeof(bool))
		{
			return (bool)(property.GetValue(null) ?? false);
		}

		var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null && field.FieldType == typeof(bool))
		{
			return (bool)(field.GetValue(null) ?? false);
		}

		return false;
	}

	private static bool CanQueryDestinationInfo(OpsController opsController, Car car)
    {
        if (car.set != null) return true;

        // Coupling arrival checks require an integration set. Other destinations
        // remain available while a single car is waiting to enter one.
        try
        {
            OpsCarPosition destination;
            if (car.TryGetOverrideDestination(OverrideDestination.Repair, opsController, out var repairDestination)
                && repairDestination.HasValue)
            {
                destination = repairDestination.Value.Item1;
            }
            else
            {
                var waybill = car.GetWaybill(opsController);
                if (!waybill.HasValue) return false;
                destination = waybill.Value.Destination;
            }

            return !CoupledToDestination.IsCoupledToDestination(destination.Identifier);
        }
        catch (Exception ex)
        {
            Loader.LogDebug($"Unable to resolve map destination for {car.id}: {ex.Message}");
            return false;
        }
    }

    private IEnumerator TraincarColorUpdater()
	{
		for (;;)
		{
			foreach (var marker in MapBuilder.Shared._mapIcons.ToArray())
			{
				if (marker == null || marker.transform == null || marker.transform.parent == null)
				{
					continue;
				}

				Car car = marker.transform.parent.GetComponent<Car>();
				if (car == null)
				{
					continue;
				}

				var image = marker.GetComponentInChildren<Image>(true);
				if (image == null || marker.Text == null)
				{
					continue;
				}

				bool visible = !car.IsInBardo;
				marker.Text.gameObject.SetActive(visible);
				image.gameObject.SetActive(visible);
                if (!visible) continue;

				if (!car.CarType.IsFreight)
				{
					continue;
				}

				string text;
				bool iconEnabled;
				Vector3 destination;
				OpsCarPosition opsCarPosition;
				var opsController = OpsController.Shared;
				Color color = Color.white;

				if (opsController != null && CanQueryDestinationInfo(opsController, car) && opsController.TryGetDestinationInfo(car, out text, out iconEnabled, out destination, out opsCarPosition))
				{
					Area area = opsController.AreaForCarPosition(opsCarPosition);
					if (area != null)
					{
						color = area.tagColor;
					}

					float maxComponent = color.maxColorComponent;
					bool isModdedArea = maxComponent >= 0.99f;
					bool shouldDarken = isModdedArea ? iconEnabled : !iconEnabled;

					if (shouldDarken)
					{
						if (maxComponent < 0.99f)
						{
							var intensity = 1f / Mathf.Max(maxComponent, 0.0001f);
							color *= intensity;
						}
						else
						{
							Color.RGBToHSV(color, out float h, out float s, out float v);
							v *= 0.6f;
							color = Color.HSVToRGB(h, s, v);
							color.a = 1f;
						}
					}
				}

				image.color = color;
				yield return null;
			}

			yield return null;
		}
	}
	public void Rebuild()
	{
		Loader.LogDebug("Rebuild");
		if (cullingGroup != null)
		{
			cullingGroup.Dispose();
		}

		CreateSwitches();

		Camera mapCamera = MapBuilder.Shared.mapCamera;
		cullingGroup = new CullingGroup();
		cullingGroup.targetCamera = mapCamera;

		cullingGroup.onStateChanged = new CullingGroup.StateChanged(CullingGroupStateChanged);
		cullingGroup.SetBoundingDistances(new float[] { float.PositiveInfinity });
		cullingGroup.SetDistanceReferencePoint(mapCamera.transform);
		cullingSpheres = new BoundingSphere[junctionMarkers.Count];
		UpdateCullingSpheres();
		cullingGroup.SetBoundingSpheres(cullingSpheres);
		cullingGroup.SetBoundingSphereCount(cullingSpheres.Length);
	}

	private void CullingGroupStateChanged(CullingGroupEvent sphere)
	{
		int index = sphere.index;
        if (index < 0 || index >= junctionMarkers.Count) return;

		var sd = junctionMarkers[index].SwitchDescriptor;

		if (sphere.isVisible && !sphere.wasVisible)
		{
			junctionMarkers[index].JunctionMarker.SetActive(true);
		}
		else if (!sphere.isVisible && sphere.wasVisible)
		{
			junctionMarkers[index].JunctionMarker.SetActive(false);
		}
	}

	private void UpdateCullingSpheres()
	{
		for (int i = 0; i < junctionMarkers.Count && i < cullingSpheres.Length; i++)
		{
			var geo = junctionMarkers[i].SwitchDescriptor.geometry;
			Vector3 vector = WorldTransformer.GameToWorld(geo.switchHome);
			this.cullingSpheres[i] = new BoundingSphere(vector, 1f);
		}
	}

	private void CreateSwitches()
	{
		Loader.LogDebug("CreateSwitches");

		if (JunctionsMainline == null || JunctionsBranch == null ||
            JunctionMarker.junctionMarkerPrefabL == null || JunctionMarker.junctionMarkerPrefabR == null ||
            TrackObjectManager.Instance == null || TrackObjectManager.Instance._descriptors.switches == null)
		{
			return;
		}

		foreach (var oldMarker in junctionMarkers) if (oldMarker.JunctionMarker != null) Destroy(oldMarker.JunctionMarker);
        junctionMarkers.Clear();
        int switchCount = TrackObjectManager.Instance._descriptors.switches.Count;
		if (switchCount == 0)
		{
			return;
		}

		foreach (var jm in junctionMarkers) Destroy(jm.JunctionMarker);
		junctionMarkers.Clear();
		foreach (var kvp in TrackObjectManager.Instance._descriptors.switches)
		{
			var sd = kvp.Value;
			TrackNode node = sd.node;

			var junctionMarker = new GameObject($"JunctionMarker ({node.id})");
			junctionMarker.SetActive(false);
			if (mainlineSwitches.Contains(node.id))
				junctionMarker.transform.SetParent(JunctionsMainline.transform, false);
			else
				junctionMarker.transform.SetParent(JunctionsBranch.transform, false);
			junctionMarkers.Add(new Entry(sd, junctionMarker));
			junctionMarker.transform.localPosition = sd.geometry.switchHome + Vector3.up * 2000f;
			junctionMarker.transform.localRotation = sd.geometry.aPointRail.Points.First().Rotation;
			JunctionMarker jm = sd.geometry.aPointRail.hand == Hand.Right ?
				JunctionMarker.junctionMarkerPrefabL :
				JunctionMarker.junctionMarkerPrefabR;

			jm = GameObject.Instantiate(jm, junctionMarker.transform);
			jm.junction = node;
		}
	}

	public void OnSettingsChanged()
	{
		RefreshOverlayDropdown();
        if (Junctions == null || JunctionMarker.junctionMarkerPrefabL == null || JunctionMarker.junctionMarkerPrefabR == null) return;
		if (MapState != MapStates.MAPLOADED || MapBuilder.Shared == null || MapWindow.instance == null) return;

		if (_lastShowAdvancedMapWindowSettings != Settings.ShowAdvancedMapWindowSettings)
		{
			DestroyMapSettings();

			if (MapWindow.instance != null && MapWindow.instance._window.IsShown)
			{
				CreateMapSettings();
			}
		}

		foreach (var junctionMarker in JunctionMarker.junctionMarkerPrefabL.GetComponentsInChildren<CanvasRenderer>(true))
		{
			junctionMarker.transform.localScale = Vector3.one * Settings.JunctionMarkerScale;
		}
		foreach (var junctionMarker in JunctionMarker.junctionMarkerPrefabR.GetComponentsInChildren<CanvasRenderer>(true))
		{
			junctionMarker.transform.localScale = Vector3.one * Settings.JunctionMarkerScale;
		}
		foreach (var junctionMarker in Junctions.GetComponentsInChildren<CanvasRenderer>(true))
		{
			junctionMarker.transform.localScale = Vector3.one * Settings.JunctionMarkerScale;
		}

		if (_flarePrefab != null)
			_flarePrefab.GetComponentInChildren<Image>(true).transform.localScale = Vector3.one * Settings.FlareScale;
		foreach (var flare in FlareManager.Shared._instances.Values)
		{
			var icon = flare.GetComponentInChildren<Image>(true);
			if (icon != null)
			{
				icon.transform.localScale = Vector3.one * Settings.FlareScale;
			}
		}

		var mapBuilder = MapBuilder.Shared;
		mapBuilder.segmentLineWidthMin = Settings.TrackLineThickness;
		mapBuilder.segmentLineWidthMax = Settings.MapZoomMax / 5000f * 20;

		var renderTex = mapBuilder.mapCamera.targetTexture;
		if (renderTex && renderTex.antiAliasing != (int)Settings.MSAA)
		{
			renderTex.Release();
			renderTex.antiAliasing = (int)Settings.MSAA;
		}

		var mapWindow = MapWindow.instance;
		if (mapWindow != null && mapWindow._window.IsShown)
		{
			mapBuilder.mapCamera.orthographicSize =
				Mathf.Clamp(MapBuilder.Shared.mapCamera.orthographicSize, Settings.MapZoomMin, Settings.MapZoomMax);
			mapBuilder.UpdateForZoom();
		}

		if (Crossings != null)
		{
			Crossings.SetActive(Settings.ShowRoadCrossingMarkers && mapBuilder.NormalizedScale <= Loader.Settings.MarkerCutoff);
			if (Settings.ShowRoadCrossingMarkers && _crossingMarkers.Count == 0)
			{
				GatherCrossingMarkers();
			}
		}

		if (GradeMarkers != null)
		{
			GradeMarkers.SetActive(Settings.ShowGradeMarkers && mapBuilder.NormalizedScale <= Loader.Settings.MarkerCutoff);
			GatherGradeMarkers();
		}

		UpdateGradeLegendColors();

		if (MapWindow.instance != null && MapWindow.instance._window.IsShown)
		{
			MapBuilder.Shared.Rebuild();
		}

		if (resizer != null) resizer.SetMinimumSize(Settings.WindowSizeMin);
	}

	private static void CreateTraincarPrefab()
	{
		var sprite = LoadTexture("traincar.png", "MapTraincarIcon");
        _traincarPrefab = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, prefabHolder.transform);
		GameObject trainCarMarker = _traincarPrefab.gameObject;
		trainCarMarker.hideFlags = HideFlags.HideAndDontSave;
		trainCarMarker.name = "Map Icon Traincar";
		_traincarPrefab.GetComponentInChildren<Image>().sprite = sprite;

		trainCarMarker.AddComponent<CanvasCuller>();
	}

	private static void CreateFlarePrefab()
	{
		var scale = Instance?.Settings.FlareScale ?? 0.6f;
		var sprite = LoadTexture("flare.png", "MapFlareIcon");
		_flarePrefab = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, prefabHolder.transform);
		GameObject flareMarker = _flarePrefab.gameObject;
		flareMarker.hideFlags = HideFlags.HideAndDontSave;
		flareMarker.name = "Map Icon Flare";
		if (_flarePrefab.Text) DestroyImmediate(_flarePrefab.Text.gameObject);
		var image = _flarePrefab.GetComponentInChildren<Image>();
		image.sprite = sprite;
		image.transform.localScale = Vector3.one * scale;
	}

	private static void CreateTurntablePrefab()
	{
		var scale = 2.0f;
		var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
		var pixels = new Color[128 * 128];
		var markerColor = Native.Loader.Settings.TurntableMarkerColor;
		if (markerColor.a < 0.2f)
		{
			markerColor.a = 1f;
		}

		for (int y = 0; y < 128; y++)
		{
			for (int x = 0; x < 128; x++)
			{
				float dx = (x - 64f) / 64f;
				float dy = (y - 64f) / 64f;
				float dist = Mathf.Sqrt(dx * dx + dy * dy);

				if (dist < 0.9f)
				{
					pixels[y * 128 + x] = new Color(
						markerColor.r,
						markerColor.g,
						markerColor.b,
						markerColor.a * 0.75f
					);
				}
				else if (dist < 1.0f)
				{
					pixels[y * 128 + x] = new Color(
						markerColor.r * 0.6f,
						markerColor.g * 0.6f,
						markerColor.b * 0.6f,
						markerColor.a * 0.9f
					);
				}
				else
				{
					pixels[y * 128 + x] = Color.clear;
				}
			}
		}

		tex.SetPixels(pixels);
		tex.Apply();
		tex.name = "MapTurntableIcon";
		tex.wrapMode = TextureWrapMode.Clamp;
		var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
		sprite.name = "MapTurntableIcon";

		_turntablePrefab = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, prefabHolder.transform);
		GameObject turntableMarker = _turntablePrefab.gameObject;
		turntableMarker.hideFlags = HideFlags.HideAndDontSave;
		turntableMarker.name = "Map Icon Turntable";
		var image = _turntablePrefab.GetComponentInChildren<Image>();
		image.sprite = sprite;
		image.transform.localScale = Vector3.one * scale;
	}

	private static void CreateCrossingPrefab()
	{
		var scale = 0.5f;
		var sprite = LoadCrossingSprite();
		if (sprite == null)
		{
			var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
			var pixels = new Color[128 * 128];
			var fill = new Color(0.12f, 0.1f, 0.06f, 0.9f);
			var border = new Color(0.95f, 0.8f, 0.15f, 1f);
			var cross = new Color(0.98f, 0.96f, 0.9f, 1f);

			for (int y = 0; y < 128; y++)
			{
				for (int x = 0; x < 128; x++)
				{
					bool isBorder = x < 10 || x >= 118 || y < 10 || y >= 118;
					bool diagonalA = Math.Abs(x - y) <= 10;
					bool diagonalB = Math.Abs((127 - x) - y) <= 10;
					pixels[y * 128 + x] = isBorder ? border : (diagonalA || diagonalB ? cross : fill);
				}
			}

			tex.SetPixels(pixels);
			tex.Apply();
			tex.name = "MapCrossingIconFallback";
			tex.wrapMode = TextureWrapMode.Clamp;
			sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
			sprite.name = "MapCrossingIconFallback";
		}

		_crossingPrefab = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, prefabHolder.transform);
		GameObject crossingMarker = _crossingPrefab.gameObject;
		crossingMarker.hideFlags = HideFlags.HideAndDontSave;
		crossingMarker.name = "Map Icon Crossing";
		if (_crossingPrefab.Text != null) DestroyImmediate(_crossingPrefab.Text.gameObject);
		var image = _crossingPrefab.GetComponentInChildren<Image>();
		image.sprite = sprite;
		image.color = Color.white;
		image.preserveAspect = true;
		image.transform.localScale = Vector3.one * scale;
	}

	public static Sprite? LoadTexture(string fileName, string name)
	{
		string iconPath = Path.Combine(Loader.Context.ModDirectory, fileName);
		if (!File.Exists(iconPath))
		{
			Loader.Log($"Icon file not found: {iconPath}");
			return null;
		}

		var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
		tex.name = name;
		tex.wrapMode = TextureWrapMode.Clamp;
		byte[] bytes;
		try
		{
			bytes = File.ReadAllBytes(iconPath);
		}
		catch (Exception ex)
		{
			Loader.Log($"Failed reading icon '{fileName}': {ex.Message}");
			return null;
		}

		if (!ImageConversion.LoadImage(tex, bytes))
		{
			Loader.Log($"Unable to decode icon PNG '{fileName}'");
			return null;
		}

		tex.filterMode = FilterMode.Bilinear;
		Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
		sprite.name = name;
        OwnedAssets.Add(tex); OwnedAssets.Add(sprite);
		return sprite;
	}

	private void GatherTraincarMarkers()
	{
		foreach (Car car in TrainController.Shared.Cars)
		{
			var marker = car.GetComponentInChildren<MapIcon>(true);
            if (car.CarType.IsLocomotive && marker == null) continue;

			if (car.CarType.IsLocomotive)
			{
				void OnClick()
				{
					if (Instance == null)
					{
						marker.OnClick = delegate { CarPickable.HandleShowInspector(car); };
						CarPickable.HandleShowInspector(car);
						return;
					}

					Instance.mapCameraTarget = car;
					MapWindow.instance.mapDrag._isDragging = false;

					if (GameInput.IsShiftDown) TrainController.Shared.SelectedCar = car;
					if (GameInput.IsControlDown) CameraSelector.shared.FollowCar(car);

					CarInspector.Show(car);
				}
				RememberClickHandler(marker);
                marker.OnClick = OnClick;
			}
			else
			{
				if (!marker)
					AddTraincarMarker(car);
			}

			var actualMarker = car.GetComponentInChildren<MapIcon>(true);
			if (actualMarker != null)
			{
				AttachTrainMarkerData(car, actualMarker);
			}
		}
	}

	private void DestroyTraincarMarkers()
    {
        foreach (var entry in _ownedCarIcons)
        {
            if (entry.Key != null && entry.Key.MapIcon == entry.Value) entry.Key.MapIcon = null!;
            if (entry.Value != null) DestroyImmediate(entry.Value.gameObject);
        }
        _ownedCarIcons.Clear();
    }

	internal static void AddTraincarMarker(Car car)
	{
		if (Instance == null || car == null || car.CarType.IsLocomotive || car.MapIcon != null) return;
        car.MapIcon = Instantiate<MapIcon>(traincarPrefab!, car.transform);
        Instance._ownedCarIcons[car] = car.MapIcon;
		if (car.CarType.IsTender)
			car.MapIcon.SetText(car.Ident.RoadNumber);
		else
			car.MapIcon.SetText($"<line-height=70%>{car.Ident.ReportingMark}\n{car.Ident.RoadNumber}");
		var image = car.MapIcon.GetComponentInChildren<Image>();
		var scale = image.transform.localScale;
		scale.y = (car.carLength / 11) * scale.y;
		image.transform.localScale = scale;
		var text = car.MapIcon.GetComponentInChildren<TMP_Text>();
		text.horizontalAlignment = HorizontalAlignmentOptions.Left;
		text.enableAutoSizing = false;
		text.fontSizeMin = 19;
		text.fontSize = 19;
		text.autoSizeTextContainer = true;
		text.transform.localPosition = Vector3.zero;


		car.MapIcon.OnClick = delegate
		{
			if (Instance) Instance.mapCameraTarget = car;
			MapWindow.instance.mapDrag._isDragging = false;

			if (GameInput.IsShiftDown) TrainController.Shared.SelectedCar = car;
			if (GameInput.IsControlDown) CameraSelector.shared.FollowCar(car);

			CarInspector.Show(car);
		};
		car.UpdateMapIconPosition(car._mover.Position, car._mover.Rotation);
		AttachTrainMarkerData(car, car.MapIcon);
	}

	private void GatherFlareMarkers()
	{
		foreach (GameObject flareGO in FlareManager.Shared._instances.Values)
		{
			var flare = flareGO.GetComponentInChildren<FlarePickable>();
			var marker = flare.GetComponentInChildren<MapIcon>();
			if (!marker)
				AddFlareMarker(flare);
		}
	}

	private void DestroyFlareMarkers()
    {
        foreach (var icon in _ownedFlareIcons.Values)
            if (icon != null) DestroyImmediate(icon.gameObject);
        _ownedFlareIcons.Clear();
    }

	internal static void AddFlareMarker(FlarePickable flare)
    {
        if (Instance == null || flare == null || MapState != MapStates.MAPLOADED) return;
        if (Instance._ownedFlareIcons.TryGetValue(flare, out var existing) && existing != null) return;
        if (flare.GetComponentInChildren<MapIcon>(true) != null) return;
        var icon = Instantiate(flarePrefab!, flare.transform);
        icon.transform.position = flare.transform.position + Vector3.up * 1000f;
        icon.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        icon.OnClick = () => { if (flare != null) flare.Activate(ObjectPicker.CreateEvent(PickableActivation.Primary)); };
        Instance._ownedFlareIcons[flare] = icon;
    }

	private void GatherTurntables()
	{
		_turntableHelpers.Clear();

		var turntableControllers = FindObjectsByType<TurntableController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		Loader.Log($"Found {turntableControllers.Length} turntable(s) in scene");

		foreach (var controller in turntableControllers)
		{
			if (controller == null || controller.turntable == null)
				continue;

			if (!controller.isActiveAndEnabled) continue;
            var helper = controller.GetComponent<TurntableHelper>();
			if (helper == null)
			{
				helper = controller.gameObject.AddComponent<TurntableHelper>();
			}
			helper.EnsureInitialized();

		// Set callback to rebuild map after rotation
		helper.OnRotationComplete = () =>
		{
			Instance?.RefreshMapAfterTurntableRotation();
		};

			_turntableHelpers.Add(helper);
		}

		// Defer marker creation to allow Awake() to complete on all helpers
		if (_turntableHelpers.Count > 0)
			StartCoroutine(AddTurntableMarkersNextFrame());
	}

	private System.Collections.IEnumerator AddTurntableMarkersNextFrame()
	{
		// Wait for all TurntableHelper Awake() calls to complete
		// Inactive turntables that were just activated may take several frames to initialize
		const int maxWaitFrames = 120;
		int frameCount = 0;

		while (frameCount < maxWaitFrames)
		{
			yield return null;
			frameCount++;

			foreach (var helper in _turntableHelpers)
			{
				helper?.EnsureInitialized();
			}

			// Check if all helpers are fully initialized
			if (_turntableHelpers.All(h => h != null && h.IsInitialized))
			{
				Loader.Log($"All {_turntableHelpers.Count} turntable helper(s) initialized after {frameCount} frame(s)");
				break;
			}

			// Log status every 5 frames to help debug slow initialization
			if (frameCount % 5 == 0)
			{
				int initializedCount = _turntableHelpers.Count(h => h != null && h.IsInitialized);
				Loader.Log($"Frame {frameCount}: {initializedCount}/{_turntableHelpers.Count} turntable(s) initialized");
			}
		}

		// Log any helpers that failed to initialize
		for (int i = 0; i < _turntableHelpers.Count; i++)
		{
			var helper = _turntableHelpers[i];
			if (helper == null)
			{
				Loader.Log($"ERROR: Turntable helper #{i} is null");
			}
			else if (!helper.IsInitialized)
			{
				Loader.Log($"ERROR: Turntable helper #{i} failed to initialize after {maxWaitFrames} frames");
			}
		}

		int successCount = 0;
		foreach (var helper in _turntableHelpers)
		{
			if (helper != null && helper.IsInitialized && helper.Controller != null && helper.Controller.turntable != null)
			{
				var pos = WorldTransformer.GameToWorld(helper.Controller.turntable.Position);
				AddTurntableMarker(helper, helper.Controller);
				successCount++;
				Loader.Log($"Created marker for turntable at ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
			}
		}

		Loader.Log($"Successfully created {successCount}/{_turntableHelpers.Count} turntable marker(s)");
		if (successCount < _turntableHelpers.Count)
			Loader.Log($"WARNING: {_turntableHelpers.Count - successCount} turntable marker(s) failed to create");
	}

	private void DestroyTurntableMarkers()
	{
		foreach (var helper in _turntableHelpers)
		{
			if (helper != null && helper.Controller != null)
			{
				var marker = helper.Controller.GetComponentInChildren<MapIcon>();
				if (marker)
					DestroyImmediate(marker.gameObject);
			}
		}
		_turntableHelpers.Clear();
	}

	internal static void AddTurntableMarker(TurntableHelper helper, TurntableController controller)
	{
		if (helper == null || controller == null || controller.turntable == null)
		{
			Loader.LogDebug("AddTurntableMarker: Invalid helper or controller");
			return;
		}

		var prefab = turntablePrefab;
        if (prefab == null || Instance == null || Instance.Turntables == null) return;
        var mapIcon = Instantiate<MapIcon>(prefab, Instance.Turntables.transform);
		var turntablePos = controller.turntable.Position;
		mapIcon.transform.position = WorldTransformer.GameToWorld(turntablePos) + Vector3.up * 2500f;
		mapIcon.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

		// Remove text label for cleaner look
		mapIcon.SetText("");

		// Add component to override zoom scaling and keep it large
		var scaleOverride = mapIcon.gameObject.AddComponent<TurntableIconScaleOverride>();

		// Control visibility based on settings
		var image = mapIcon.GetComponentInChildren<Image>();
		if (image != null)
		{
			image.enabled = Native.Loader.Settings.ShowTurntableMarkers;
		}

		// Increase the RectTransform size for the clickable area
		var rectTransform = mapIcon.GetComponent<RectTransform>();
		if (rectTransform != null)
		{
			rectTransform.sizeDelta = new Vector2(300f, 300f);
		}

		mapIcon.OnClick = delegate
		{
			// Detect Shift modifier for 180-degree rotation
			if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
			{
				ShowTurntable180Control(helper);
				return;
			}

			// Detect Alt modifier for counterclockwise rotation
			bool clockwise = !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt);
			ShowTurntableControl(helper, clockwise);
		};
	}

	private static void ShowTurntableControl(TurntableHelper helper, bool clockwise)
	{
		if (helper == null || helper.Controller == null)
			return;

		if (IsClientMultiplayer() && !IsHostMultiplayer())
		{
			Loader.LogDebug("Turntable map controls are host-only in multiplayer.");
			return;
		}

		// Check if turntable control is disabled in settings
		if (!Native.Loader.Settings.EnableTurntableControl)
		{
			Loader.LogDebug("Turntable control is disabled in settings. Enable it in the mod settings to rotate turntables.");
			return;
		}

		// Only rotate if Ctrl or Alt is held
		if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) &&
		    !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
		{
			Loader.LogDebug("Turntable controls: Ctrl+Click = clockwise, Alt+Click = counterclockwise, Shift+Click = 180°. Plain click shows this hint only.");
			return;
		}

		var activeIndexes = helper.ActiveTrackIndexes;
		if (activeIndexes.Count == 0)
		{
			Loader.LogDebug("No active tracks connected to turntable");
			return;
		}

		// Use the helper's rotation methods
		bool rotated = clockwise
			? helper.RotateClockwise()
			: helper.RotateCounterClockwise();

		if (rotated)
		{
			var direction = clockwise ? "clockwise" : "counterclockwise";
			Loader.LogDebug($"Rotating turntable {direction}. Hold Ctrl+Click for clockwise, Alt+Click for counterclockwise, or Shift+Click for 180° rotation");
		}
	}

	private static void ShowTurntable180Control(TurntableHelper helper)
	{
		if (helper == null || helper.Controller == null)
			return;

		if (IsClientMultiplayer() && !IsHostMultiplayer())
		{
			Loader.LogDebug("Turntable map controls are host-only in multiplayer.");
			return;
		}

		// Check if turntable control is disabled in settings
		if (!Native.Loader.Settings.EnableTurntableControl)
		{
			Loader.LogDebug("Turntable control is disabled in settings. Enable it in the mod settings to rotate turntables.");
			return;
		}

		// Rotate 180 degrees using the instance method
		if (helper.Rotate180())
		{
			Loader.LogDebug("Rotating turntable 180 degrees to reverse engine");
		}
	}

	void LateUpdate()
	{
		if (MapState != MapStates.MAPLOADED || MapBuilder.Shared == null || MapWindow.instance == null) return;

		var mapCamera = MapBuilder.Shared.mapCamera;

		if (!SettingsWindow.IsOpen && GameInput.Shared._gameActionMap.enabled)
		{
			if (Settings.mapToggle.Down())
			{
				if (resizer != null) resizer.Toggle();
				return;
			}
			else if (Settings.mapFollow.Down())
			{
				mapFollowMode = !mapFollowMode;
			}
			else if (Settings.mapRecenter.Down())
			{
				mapCameraTarget = mapFollowMode ? Camera.main : null;
				MapWindow.instance.mapDrag._isDragging = false;
				Vector3 currentCameraPosition = CameraSelector.shared.CurrentCameraPosition;
				currentCameraPosition.y = 5000f;
				mapCamera.transform.localPosition = currentCameraPosition;
			}
		}

		if (mapFollowMode == true && mapCameraTarget != null)
		{
			if (mapCameraTarget is Car car)
			{
				Vector3 position = WorldTransformer.GameToWorld(Vector3.Lerp(car.LastBodyPosition[0], car.LastBodyPosition[1], 0.5f));

				position.y = mapCamera.transform.position.y;
				mapCamera.transform.position = position;
			}
			else if (mapCameraTarget is Camera cam)
			{
				Vector3 position = Camera.main.transform.position;
				position.y = mapCamera.transform.position.y;
				mapCamera.transform.position = position;
			}
		}

		var mapWindow = MapWindow.instance;
		var mapDrag = MapWindow.instance.mapDrag;
		if (mapDrag._isDragging) mapCameraTarget = null;

		if (!mapDrag._pointerOver || !GameInput.IsMouseOverGameWindow(mapWindow._window))
		{
			if (_gradeTooltipGo != null && _gradeTooltipGo.activeSelf)
			{
				_gradeTooltipGo.SetActive(false);
			}
			if (_trainTooltipGo != null && _trainTooltipGo.activeSelf)
			{
				_trainTooltipGo.SetActive(false);
			}
			return;
		}

		if (Settings.ShowGradeOnHover)
		{
			Vector2 viewportNormalizedPoint = mapDrag.NormalizedMousePosition();
			Ray ray = mapWindow.mapBuilder.mapCamera.ViewportPointToRay(viewportNormalizedPoint);
			Vector3 mousePosWorld = ray.origin;

			float pixelRadius = 20f;
			float viewHeight = (mapCamera.targetTexture != null) ? mapCamera.targetTexture.height : Screen.height;
			float gameRadius = pixelRadius * (2f * mapCamera.orthographicSize) / viewHeight;

			TrackSegment? closestSegment = null;
			float minDistance = float.MaxValue;

			foreach (var segment in Graph.Shared.Segments)
			{
				if (segment == null || segment.a == null || segment.b == null) continue;

				float dist = DistanceToLineSegmentXZ(mousePosWorld, WorldTransformer.GameToWorld(segment.a.Position), WorldTransformer.GameToWorld(segment.b.Position));
				if (dist < minDistance)
				{
					minDistance = dist;
					closestSegment = segment;
				}
			}

			if (closestSegment != null && minDistance <= gameRadius)
			{
				if (_gradeTooltipGo == null)
				{
					CreateGradeTooltip();
				}

				float grade = 0f;
				if (_segmentGrades.TryGetValue(closestSegment.id, out var info))
				{
					grade = info.Grade;
				}
				else
				{
					grade = ComputeSegmentGrade(closestSegment);
				}

				string lineType = "Branch Line";
				if (mainlineSegments.Contains(closestSegment.id))
				{
					lineType = "Main Line";
				}
				else if (_passengerStopSegments.Contains(closestSegment.id) && _passengerStopSegmentNames.TryGetValue(closestSegment.id, out string stationName))
				{
					lineType = stationName;
				}
				else if (_industrialSegments.Contains(closestSegment.id) && _industrialSegmentNames.TryGetValue(closestSegment.id, out string indName))
				{
					lineType = indName;
				}

				string sign = "";
				string arrow = " ≈";
				if (grade > 0.05f)
				{
					sign = "+";
					arrow = " ▲";
				}
				else if (grade < -0.05f)
				{
					sign = "-";
					arrow = " ▼";
				}

				// if (_gradeTooltipText != null)
				// {
				// 	_gradeTooltipText.text = $"{lineType}\nGrade: {sign}{Mathf.Abs(grade):F1}%{arrow}";
				// }

				if (_gradeTooltipText != null)
				{


					// Grade color based on absolute value
					float absGrade = Mathf.Abs(grade);
					string gradeColor;

					if (absGrade < 0.1f)
						gradeColor = "#66BB6A";      // Flat - Green
					else if (absGrade < 1.0f)
						gradeColor = "#8BC34A";      // Easy
					else if (absGrade < 2.0f)
						gradeColor = "#FFD54F";      // Moderate
					else if (absGrade < 3.0f)
						gradeColor = "#FF9800";      // Steep
					else
						gradeColor = "#EF5350";      // Very Steep

					// Color the track type using the actual track colors from settings
					Color trackColor = Color.white;
					if (Settings != null)
					{
						// Default to branch
						trackColor = Settings.TrackColorBranch;

						if (mainlineSegments.Contains(closestSegment.id))
						{
							trackColor = Settings.TrackColorMainline;
						}

						if (_industrialSegments.Contains(closestSegment.id)
							&& !mainlineSegments.Contains(closestSegment.id)
							&& !_passengerStopSegments.Contains(closestSegment.id))
						{
							if (Settings.EnableIndustryAreaColors && _industrialSegmentColors.TryGetValue(closestSegment.id, out Color segmentColor))
							{
								trackColor = segmentColor;
							}
							else
							{
								trackColor = Settings.TrackColorIndustrial;
							}
						}

						if (Settings.EnablePassengerStopTracking && _passengerStopSegments.Contains(closestSegment.id))
						{
							trackColor = Settings.TrackColorPax;
						}
					}
					else
					{
						trackColor = new Color(0.74f, 0.74f, 0.74f); // Default branch
						if (mainlineSegments.Contains(closestSegment.id))
						{
							trackColor = new Color(0.31f, 0.76f, 0.97f);
						}
						if (_industrialSegments.Contains(closestSegment.id)
							&& !mainlineSegments.Contains(closestSegment.id)
							&& !_passengerStopSegments.Contains(closestSegment.id))
						{
							if (_industrialSegmentColors.TryGetValue(closestSegment.id, out Color segmentColor))
							{
								trackColor = segmentColor;
							}
							else
							{
								trackColor = new Color(0.51f, 0.78f, 0.52f);
							}
						}
						if (_passengerStopSegments.Contains(closestSegment.id))
						{
							trackColor = new Color(0.73f, 0.41f, 0.78f);
						}
					}
					string lineColor = $"#{ColorUtility.ToHtmlStringRGB(trackColor)}";

					// _gradeTooltipText.richText = true;
					// _gradeTooltipText.text =
					// 	$"<color={headerColor}>Track</color>\n" +
					// 	$"<color={lineColor}>{lineType}</color>\n\n" +
					// 	$"<color={headerColor}>Grade</color>\n" +
					// 	$"<color={gradeColor}>{sign}{Mathf.Abs(grade):F1}%{arrow}</color>";

					_gradeTooltipText.richText = true;
					_gradeTooltipText.text =
						$"<b><color={lineColor}>{lineType}</color></b> • " +
						$"<color={gradeColor}>{sign}{Mathf.Abs(grade):F1}%{arrow}</color>";
				}

				if (_gradeTooltipGo != null)
				{
					if (!_gradeTooltipGo.activeSelf)
					{
						_gradeTooltipGo.SetActive(true);
					}
				}
			}
			else
			{
				if (_gradeTooltipGo != null && _gradeTooltipGo.activeSelf)
				{
					_gradeTooltipGo.SetActive(false);
				}
			}
		}
		else
		{
			if (_gradeTooltipGo != null && _gradeTooltipGo.activeSelf)
			{
				_gradeTooltipGo.SetActive(false);
			}
		}

		if (Settings!.ShowTrainHoverDetails)
		{
			Vector2 viewportNormalizedPoint = mapDrag.NormalizedMousePosition();
			Ray ray = mapWindow.mapBuilder.mapCamera.ViewportPointToRay(viewportNormalizedPoint);

			if (Physics.Raycast(ray, out RaycastHit hit, 10000f))
			{
				var tmd = hit.collider.GetComponentInParent<TrainMarkerData>();
				if (tmd != null && tmd.Car != null && _trainInfoCache.TryGetValue(tmd.Car.id, out var info))
				{
					if (tmd.Car.CarType.IsLocomotive || !Settings!.ShowIndividualCarTooltip)
					{
						ShowTrainTooltip(info);
					}
					else
					{
						var fci = info.FreightCars.FirstOrDefault(f => f.Car == tmd.Car);
						if (fci != null)
						{
							ShowFreightCarTooltip(fci);
						}
						else
						{
							ShowTrainTooltip(info);
						}
					}
				}
				else
				{
					HideTrainTooltip();
				}
			}
			else
			{
				HideTrainTooltip();
			}
		}
		else
		{
			HideTrainTooltip();
		}

		if (GameInput.Shared.PlaceFlare)
		{
			Vector2 viewportNormalizedPoint = mapDrag.NormalizedMousePosition();
			Ray ray = mapWindow.mapBuilder.mapCamera.ViewportPointToRay(viewportNormalizedPoint);
			if (!mapWindow.TryGetGamePointForViewportNormalizedPoint(viewportNormalizedPoint, out var vector)) return;
			Location? location = LocationFromGamePoint(vector, 50f);
			if (location != null)
			{
				FlareManager.Shared.PlaceFlare(location.Value);
			}
		}
	}

	public Location? LocationFromGamePoint(Vector3 gamePosition, float radius)
	{
		List<Location> locations = new List<Location>();
		foreach (TrackSegment trackSegment in Graph.Shared.Segments)
		{
			Location loc;
			bool result = Graph.Shared.TryGetLocationFromPoint(trackSegment, gamePosition, radius, out loc);
			if (result && loc.IsValid)
			{
				locations.Add((Location)loc);
			}
		}

		if (locations.Count > 0)
			return locations.OrderBy(a => Vector3.Magnitude(a.GetPosition() - gamePosition)).First();

		return null;
	}

	private class Entry
	{
		public Entry(TrackObjectManager.SwitchDescriptor switchDescriptor, GameObject junctionMarker)
		{
			SwitchDescriptor = switchDescriptor;
			JunctionMarker = junctionMarker;
		}

		public readonly TrackObjectManager.SwitchDescriptor SwitchDescriptor;
		public readonly GameObject JunctionMarker;
	}

	[HarmonyPatch(typeof(TrackObjectManager), nameof(TrackObjectManager.Rebuild))]
	private static class TrackObjectManagerRebuildPatch
	{
		private static void Postfix()
		{
			if (MapState != MapStates.MAPLOADED || MapBuilder.Shared == null || MapWindow.instance == null) return;

			Instance?.Rebuild();

			if (MapWindow.instance != null && MapWindow.instance._window.IsShown) MapWindow.instance.mapBuilder.Rebuild();
		}
	}

	// TODO: Remove this entire patch when cleaning up non-visual-only mode code
	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.ColorForSegment))]
	private static class ColorForSegmentPatch
    {
        private static void Postfix(TrackSegment segment, ref Color __result)
        {
            if (Instance == null || MapState != MapStates.MAPLOADED) return;
            __result = Instance.OverlayColor(segment, __result);
        }
    }

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.Add), new[] { typeof(MapIcon) })]
	private static class MapBuilderAddPatch
	{
		private static void Postfix(MapIcon icon)
		{
			// Check if this is a signal's map icon
			var signal = icon.transform.parent?.GetComponent<CTCSignal>();
			if (signal == null) return;

			// Find the icon's image
			var image = icon.GetComponentInChildren<Image>(true);
			if (image == null) return;

			// Start monitoring this signal icon
			if (!icon.gameObject.GetComponent<SignalIconColorizer>())
			{
				var colorizer = icon.gameObject.AddComponent<SignalIconColorizer>();
				colorizer.Setup(signal, image);
			}
		}
	}

	private class TurntableIconScaleOverride : MonoBehaviour
	{
		private const float FIXED_SCALE = 0.5f;
		private Image? _image;

		void Start()
		{
			_image = GetComponentInChildren<Image>();
		}

		void LateUpdate()
		{
			// Override any zoom-based scaling by forcing a large fixed scale
			if (_image != null)
			{
				_image.transform.localScale = Vector3.one * FIXED_SCALE;
			}
			// Also scale the root for good measure
			transform.localScale = Vector3.one * FIXED_SCALE;
		}
	}

	private class CrossingIconScaleOverride : MonoBehaviour
	{
		private Image? _image;

		void Start()
		{
			_image = GetComponentInChildren<Image>();
		}

		void LateUpdate()
		{
			var scale = Mathf.Clamp(Loader.Settings.CrossingMarkerScale, 0.1f, 1.0f);
			if (_image != null)
			{
				_image.transform.localScale = Vector3.one * scale;
			}
			transform.localScale = Vector3.one * scale;
		}
	}

	private class SignalIconColorizer : MonoBehaviour
	{
		private CTCSignal? signal;
		private Image? icon;
		private SignalAspect lastAspect;
        private Color originalColor;
        private void OnDestroy() { if (icon != null) icon.color = originalColor; }

		public void Setup(CTCSignal ctcSignal, Image iconImage)
		{
			signal = ctcSignal;
			icon = iconImage; originalColor = iconImage.color;
			lastAspect = signal.CurrentAspect;
			UpdateColor();
		}

		void Update() {
			if (signal != null && signal.CurrentAspect != lastAspect)
			{
				lastAspect = signal.CurrentAspect;
				UpdateColor();
			}
		}

		private void UpdateColor()
		{
			Color signalColor = lastAspect switch
			{
				SignalAspect.Stop => Color.red,
				SignalAspect.Approach => Color.yellow,
				SignalAspect.Clear => Color.green,
				SignalAspect.DivergingApproach => Color.yellow,
				SignalAspect.DivergingClear => Color.green,
				SignalAspect.Restricting => new Color(1f, 0.5f, 0f), // Orange
				_ => Color.white
			};

			signalColor.a = 0.8f;
			if (icon != null) icon.color = signalColor;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.UpdateForZoom))]
	private static class MapBuilderZoomPatch
	{
		private static void Postfix(MapBuilder __instance)
		{
			Instance?.JunctionsBranch?.SetActive(__instance.NormalizedScale <= Loader.Settings.MarkerCutoff);
			Instance?.Turntables?.SetActive(__instance.NormalizedScale <= Loader.Settings.MarkerCutoff);
			Instance?.Crossings?.SetActive(__instance.NormalizedScale <= Loader.Settings.MarkerCutoff && Loader.Settings.ShowRoadCrossingMarkers);
			Instance?.GradeMarkers?.SetActive(__instance.NormalizedScale <= Loader.Settings.MarkerCutoff && Loader.Settings.ShowGradeMarkers);
		}
	}

	[HarmonyPatch(typeof(Car), nameof(Car.FinishSetup))]
	private static class CarFinishSetupPatch
	{
		private static void Postfix(Car __instance)
		{
			if (Instance != null && MapState == MapStates.MAPLOADED && !__instance.CarType.IsLocomotive) AddTraincarMarker(__instance);
		}
	}

	[HarmonyPatch(typeof(BaseLocomotive), nameof(Car.FinishSetup))]
	private static class BaseLocomotiveFinishSetupPatch
	{
		private static void Postfix(Car __instance)
		{
			var marker = __instance.GetComponentInChildren<MapIcon>();
			if (marker == null) return;
			void OnClick()
			{
				if (Instance == null)
				{
					marker.OnClick = delegate { CarPickable.HandleShowInspector(__instance); };
					CarPickable.HandleShowInspector(__instance);
					return;
				}

				Instance.mapCameraTarget = __instance;

				if (GameInput.IsShiftDown) TrainController.Shared.SelectedCar = __instance;
				if (GameInput.IsControlDown) CameraSelector.shared.FollowCar(__instance);

				CarInspector.Show(__instance);
			}
			RememberClickHandler(marker);
                marker.OnClick = OnClick;
			AttachTrainMarkerData(__instance, marker);
		}
	}

	[HarmonyPatch(typeof(Car), nameof(Car.UpdateMapIconPosition))]
	private static class CarUpdatePositionPatch
	{
		private static bool Prefix(Car __instance, Vector3 position, Quaternion rotation)
		{
			if (__instance.MapIcon == null)
			{
				return false;
			}
			if (__instance.CarType.IsLocomotive)
				__instance.MapIcon.transform.SetPositionAndRotation(position + Vector3.up * 4000f, Quaternion.Euler(-90f, rotation.eulerAngles.y, 0f));
			else
				__instance.MapIcon.transform.SetPositionAndRotation(position + Vector3.up * 3000f, Quaternion.Euler(-90f, rotation.eulerAngles.y, 0f));
			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.Zoom))]
	public static class ChangeMinMaxMapZoom
    {
        private static float MinimumZoom() => Loader.Settings.MapZoomMin;
        private static float MaximumZoom() => Loader.Settings.MapZoomMax;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = instructions.ToList();
            var minimum = result.Where(i => i.opcode == OpCodes.Ldc_R4 && Equals(i.operand, 50f)).ToList();
            var maximum = result.Where(i => i.opcode == OpCodes.Ldc_R4 && Equals(i.operand, 10000f)).ToList();
            if (minimum.Count != 1 || maximum.Count != 1)
                throw new InvalidOperationException("Unsupported map zoom implementation.");
            minimum[0].opcode = OpCodes.Call;
            minimum[0].operand = AccessTools.Method(typeof(ChangeMinMaxMapZoom), nameof(MinimumZoom));
            maximum[0].opcode = OpCodes.Call;
            maximum[0].operand = AccessTools.Method(typeof(ChangeMinMaxMapZoom), nameof(MaximumZoom));
            return result;
        }
    }

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.NormalizedScale), MethodType.Getter)]
	private static class ChangeMinMaxMapZoom2
	{
		private static bool Prefix(Camera ___mapCamera, ref float __result)
		{
			__result = Mathf.InverseLerp(Loader.Settings.MapZoomMin,
				Loader.Settings.MapZoomMax, ___mapCamera.orthographicSize);
			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.IconScale), MethodType.Getter)]
	private static class ChangeMinMaxMapZoom3
	{
		private static bool Prefix(MapBuilder __instance, ref float __result)
		{
			if (Instance == null) return true;

			var min = Mathf.LerpUnclamped(0.2f, 4f, InverseLerpUnclamped(50f, 10000f, Loader.Settings.MapZoomMin));
			var max = Mathf.LerpUnclamped(0.2f, 4f, InverseLerpUnclamped(50f, 10000f, Loader.Settings.MapZoomMax));
			__result = Mathf.Lerp(min, max, __instance.NormalizedScale); ;
			return false;
		}

		public static float InverseLerpUnclamped(float a, float b, float value)
		{
			if (a != b)
			{
				return (value - a) / (b - a);
			}

			return 0f;
		}
	}

	[HarmonyPatch(typeof(MapLabel), nameof(MapLabel.SetZoom))]
	private static class ChangeMinMaxMapZoom4
	{
		private static bool Prefix(ref float s)
		{
			s = s / 4f * 3f;
			return true;
		}
	}

	[HarmonyPatch(typeof(FlarePickable), nameof(FlarePickable.Configure))]
	private static class FlareAddUpdatePatch
	{
		private static void Postfix(FlarePickable __instance)
		{
			if (Instance != null && MapState == MapStates.MAPLOADED) AddFlareMarker(__instance);
		}
	}

	[HarmonyPatch(typeof(FlareManager), nameof(FlareManager.PlaceFlare), new[] { typeof(Camera) })]
	public static class PlaceFlareProtectionPatch
	{
		private static bool Prefix(Camera theCamera)
		{
			if (!GameInput.IsMouseOverGameWindow() || theCamera == null)
			{
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(MapIcon), nameof(MapIcon.FixTextRotation))]
	public static class FixTextRotationPatch
	{
		private static bool Prefix(TMP_Text ____text)
		{
			if (____text != null)
			{
				float num = Mathf.DeltaAngle(____text.transform.rotation.eulerAngles.y, 180f);
				if (-90f < num && num < 90f)
				{
					____text.transform.localRotation = Quaternion.Euler(____text.transform.localRotation.eulerAngles + new Vector3(0f, 0f, 180f));
				}
			}
			return false;
		}
	}

	private class GradeInfo
	{
		public float Grade;
		public Vector3 Midpoint;
		public float Length;
	}

	private class TrainInfo
	{
		public Car LeadLoco = null!;
		public string TrainName = "";
		public string DriveMode = "";

		public string DriverName = "";
		public float SpeedMph;
		public float LengthFt;
		public float WeightTons;
		public int CarCount;
		public int PassengerCount;
		public int PassengerCapacity;
		public string Destination = "";

		// --- Cargo Intelligence Layer ---
		public FuelInfo? Fuel;
		public List<CargoGroupEntry> CargoSummary = new List<CargoGroupEntry>();
		public List<string> DestinationSummary = new List<string>();
		public bool HasFreightCars;
		public int LoadedCarCount;
		public int EmptyCarCount;
		public List<FreightCarInfo> FreightCars = new List<FreightCarInfo>();
	}

	/// <summary>Locomotive fuel levels for display in the train tooltip.</summary>
	private class FuelInfo
	{
		public bool IsSteam;
		/// <summary>0-100 percentage of coal remaining (steam only).</summary>
		public float CoalPercent;
		/// <summary>0-100 percentage of water remaining (steam only).</summary>
		public float WaterPercent;
		/// <summary>0-100 percentage of diesel remaining (diesel only).</summary>
		public float DieselPercent;
	}

	/// <summary>A grouped cargo entry for the consist summary (e.g. "5x Loaded Lumber Cars").</summary>
	private class CargoGroupEntry
	{
		public string Label = "";
		public int Count;
	}

	/// <summary>Per-car freight data cached for the individual car hover tooltip.</summary>
	private class FreightCarInfo
	{
		public Car Car = null!;
		/// <summary>Cargo type name (e.g. "Lumber"), or empty if unknown.</summary>
		public string CargoName = "";
		/// <summary>Destination station/area name (e.g. "Sylva Sawmill"), or empty if none.</summary>
		public string DestinationName = "";
		/// <summary>Car type label (e.g. "Boxcar").</summary>
		public string CarTypeName = "";
		public string CarNumber = "";
		/// <summary>Total cargo weight in tons.</summary>
		public float LoadWeightTons;
		/// <summary>Maximum cargo capacity in tons (0 if unknown).</summary>
		public float CapacityTons;
		/// <summary>"Pending", "Delivered", or "Unknown".</summary>
		public string Status = "Unknown";
		public bool HandBrakeApplied;
		public bool HasHotbox;
		public int JournalOilPercent;
	}


	private class TrainMarkerData : MonoBehaviour
	{
		public Car? Car;
	}


	// Waypoint marker feature removed per user request

}

public static class Extensions
{
	public static RectTransform AddDropdown(this UIPanelBuilder builder, List<TMP_Dropdown.OptionData> options, int currentSelectedIndex, Action<int> onSelect)
	{
		TMP_Dropdown tmp_Dropdown = builder.InstantiateInContainer<TMP_Dropdown>(builder._assets.dropdownControl);
		tmp_Dropdown.ClearOptions();
		tmp_Dropdown.AddOptions(options);
		tmp_Dropdown.value = currentSelectedIndex;
		tmp_Dropdown.onValueChanged.AddListener(delegate (int index)
		{
			onSelect(index);
		});

		tmp_Dropdown.template.sizeDelta = new Vector2(0f, options.Count * 20f + 8f);
		var go = new GameObject("Item Image");
		go.transform.SetParent(tmp_Dropdown.template.transform.Find("Viewport/Content/Item"), false);
		var image = go.AddComponent<Image>();
		var rectTransform = image.rectTransform;
		rectTransform.anchorMin = new Vector2(1, 0);
		rectTransform.anchorMax = new Vector2(1, 1);
		rectTransform.pivot = new Vector2(1, 0.5f);
		rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 15);
		rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 15);
		tmp_Dropdown.itemImage = image;

		UnityEngine.Object.DestroyImmediate(tmp_Dropdown.template.transform.Find("Viewport/Content/Item/Item Checkmark").gameObject);
		tmp_Dropdown.template.transform.Find("Viewport/Content/Item/Item Label").GetComponent<RectTransform>().offsetMin = new Vector2(10f, 1f);

		return tmp_Dropdown.GetComponent<RectTransform>();
	}
}
