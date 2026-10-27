using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using UnityEngine;

namespace MapEnhancer.Native;

public sealed class NativeEntryPoint : IModEntryPoint
{
    public void OnModLoad(IModContext context) => Loader.LoadNative(context);
    public void OnModUnload() => Loader.UnloadNative();
}

public static partial class Loader
{
    public static IModContext Context { get; private set; } = null!;
    public static Harmony HarmonyInstance { get; private set; } = null!;
    public static MapEnhancer Instance { get; private set; } = null!;
    public static MapEnhancerSettings Settings = new();
    private static GameObject? _root;
    private static bool _settingsUnreadable;
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Converters = { new ColorSettingsConverter() }
    };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Railroader", "ModSettings", "map-enhancer.json");

    public static void LoadNative(IModContext context)
    {
        if (_root != null) throw new InvalidOperationException("Map Enhancer is already loaded.");
        Context = context;
        SettingsUi.Reset();
        Settings = new MapEnhancerSettings();
        _settingsUnreadable = false;
        if (File.Exists(SettingsPath))
        {
            try { Settings = JsonConvert.DeserializeObject<MapEnhancerSettings>(File.ReadAllText(SettingsPath), JsonSettings) ?? new(); }
            catch (Exception ex)
            {
                _settingsUnreadable = true;
                context.Log.Info("Could not read Map Enhancer settings; the original file will be preserved. " + ex.Message);
            }
        }
        Settings.UseVisualOnlyTrackColors = true;
        if (!Enum.IsDefined(typeof(TrackOverlayMode), Settings.Overlay)) Settings.Overlay = TrackOverlayMode.Routes;
        if (float.IsNaN(Settings.MapZoomMin) || float.IsInfinity(Settings.MapZoomMin)) Settings.MapZoomMin = 50f;
        if (float.IsNaN(Settings.MapZoomMax) || float.IsInfinity(Settings.MapZoomMax)) Settings.MapZoomMax = 10000f;
        Settings.MapZoomMin = Mathf.Clamp(Settings.MapZoomMin, 10f, 1000f);
        Settings.MapZoomMax = Mathf.Clamp(Settings.MapZoomMax, Settings.MapZoomMin + 1f, 20000f);
        Settings.mapToggle ??= new KeyBinding { keyCode = KeyCode.Z };
        Settings.mapFollow ??= new KeyBinding { keyCode = KeyCode.Z, modifiers = 1 };
        Settings.mapRecenter ??= new KeyBinding { keyCode = KeyCode.Z, modifiers = 2 };
        HarmonyInstance = new Harmony(context.ModIdentifier);
        try
        {
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            _root = new GameObject("Map Enhancer");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            Instance = _root.AddComponent<MapEnhancer>();
            Instance.Settings = Settings;
            _root.AddComponent<SettingsWindow>();
            MapToolbarIntegration.Acquire();
            context.Log.Info("Map Enhancer loaded. Press Ctrl+F8 for settings.");
        }
        catch
        {
            CleanupNative();
            throw;
        }
    }

    public static void UnloadNative()
    {
        try { if (_root != null) SaveSettings(); }
        finally { CleanupNative(); }
    }

    private static void CleanupNative()
    {
        IndustryMapClick.Reset();
        NativeCleanup.Run(MapToolbarIntegration.Release, ReportCleanupFailure);
        NativeCleanup.Run(() => HarmonyInstance?.UnpatchAll(HarmonyInstance.Id), ReportCleanupFailure);
        NativeCleanup.Run(() =>
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }, ReportCleanupFailure);
        _root = null;
        Instance = null!;
        SettingsUi.Reset();
        Context = null!;
        HarmonyInstance = null!;
    }

    private static void ReportCleanupFailure(Exception error) =>
        Context?.Log.Info("Could not complete Map Enhancer cleanup: " + error.Message);

    public static void SaveSettings()
    {
        if (_settingsUnreadable || Context == null) return;
        string path = SettingsPath;

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonConvert.SerializeObject(Settings, JsonSettings));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (Exception ex) { Context.Log.Info("Could not save Map Enhancer settings: " + ex.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }

    private sealed class ColorSettingsConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(Color);
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            Color color = (Color)value!;
            writer.WriteStartObject();
            writer.WritePropertyName("r"); writer.WriteValue(color.r);
            writer.WritePropertyName("g"); writer.WriteValue(color.g);
            writer.WritePropertyName("b"); writer.WriteValue(color.b);
            writer.WritePropertyName("a"); writer.WriteValue(color.a);
            writer.WriteEndObject();
        }
        public override object ReadJson(JsonReader reader, Type type, object? value, JsonSerializer serializer)
        {
            JObject data = JObject.Load(reader);
            return new Color((float?)data["r"] ?? 0f, (float?)data["g"] ?? 0f,
                (float?)data["b"] ?? 0f, (float?)data["a"] ?? 1f);
        }
    }
}

public sealed class SettingsWindow : MonoBehaviour
{
    private bool _visible;
    public static bool IsOpen { get; private set; }
    private void OnDestroy() => IsOpen = false;
    private Rect _bounds = new(80, 80, 650, 700);
    private Vector2 _scroll;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8) &&
            (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            _visible = !_visible; IsOpen = _visible;
            if (!_visible) Loader.SaveSettings();
        }
    }

    private void OnGUI()
    {
        if (!_visible) return;
        _bounds.x = Mathf.Clamp(_bounds.x, 0f, Mathf.Max(0f, Screen.width - _bounds.width));
        _bounds.y = Mathf.Clamp(_bounds.y, 0f, Mathf.Max(0f, Screen.height - _bounds.height));
        _bounds.width = Mathf.Min(650f, Screen.width);
        _bounds.height = Mathf.Min(700f, Screen.height);
        _bounds = GUILayout.Window(GetInstanceID(), _bounds, Draw, "Map Enhancer");
    }

    private void Draw(int id)
    {
        if (GUILayout.Button("Save and close")) { Loader.SaveSettings(); _visible = false; IsOpen = false; }
        _scroll = GUILayout.BeginScrollView(_scroll);
        Loader.DrawSettings();
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }
}
