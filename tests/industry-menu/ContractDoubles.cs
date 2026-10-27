using System.Collections;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    // Only the public contracts reached by the linked production click handler are modeled.
    public class Object
    {
        public bool Destroyed { get; set; }
        public static bool operator ==(Object? left, Object? right)
        {
            bool leftNull = left is null || left.Destroyed;
            bool rightNull = right is null || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    public readonly record struct Vector2(float x, float y);
    public readonly record struct Vector3(float x, float y, float z);
    public readonly record struct Ray(Vector3 ViewportPoint);
    public readonly struct RaycastHit { }
    public sealed class Transform { public Vector3 position = new(0, 100, 0); }
    public sealed class Camera : Object
    {
        public float orthographicSize = 80;
        public Transform transform = new();
        public Vector3? LastViewportPoint { get; private set; }
        public Ray ViewportPointToRay(Vector3 point)
        {
            LastViewportPoint = point;
            return new Ray(point);
        }
    }

    public enum KeyCode { LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt }
    public static class Input
    {
        public static HashSet<KeyCode> Pressed { get; } = [];
        public static bool GetKey(KeyCode key) => Pressed.Contains(key);
    }
    public static class Physics
    {
        public static bool HasMapIcon { get; set; }
        public static int Calls { get; private set; }
        public static int LastLayerMask { get; private set; }
        public static float LastDistance { get; private set; }
        public static bool Raycast(Ray ray, out RaycastHit hit, float distance, int layerMask)
        {
            Calls++;
            LastDistance = distance;
            LastLayerMask = layerMask;
            hit = default;
            return HasMapIcon;
        }
        public static void Reset() { HasMapIcon = false; Calls = 0; LastLayerMask = 0; LastDistance = 0; }
    }
}

namespace UnityEngine.EventSystems
{
    public sealed class PointerEventData
    {
        public enum InputButton { Left, Right, Middle }
        public InputButton button;
    }
}

namespace Track
{
    // Position labels distinguish points on the same segment; this fixture implements no rail geometry.
    public readonly record struct Location(string SegmentId, float Position);
    public sealed class TrackSpan
    {
        public Func<Location, bool> ContainsResult { get; set; } = _ => false;
        public List<Location> Queries { get; } = [];
        public bool Contains(Location location)
        {
            Queries.Add(location);
            return ContainsResult(location);
        }
    }
    public sealed class Graph
    {
        public static Graph? Shared { get; set; }
        public Location Result { get; set; }
        public bool HasLocation { get; set; } = true;
        public bool ThrowOnQuery { get; set; }
        public int Queries { get; private set; }
        public UnityEngine.Vector3 LastPoint { get; private set; }
        public float LastRadius { get; private set; }
        public bool TryGetLocationFromGamePoint(UnityEngine.Vector3 point, float radius, out Location location)
        {
            Queries++;
            LastPoint = point;
            LastRadius = radius;
            if (ThrowOnQuery) throw new InvalidOperationException("Track query unavailable.");
            location = Result;
            return HasLocation;
        }
    }
}

namespace Model.Ops
{
    public sealed class Industry
    {
        public List<IndustryComponent?> Components { get; } = [];
    }
    public sealed class IndustryComponent
    {
        public bool IsProgressionEnabled { get; set; } = true;
        public List<Track.TrackSpan?> TrackSpans { get; } = [];
    }
    public sealed class OpsController
    {
        public static OpsController? Shared { get; set; }
        public IEnumerable<Industry> AllIndustries { get; set; } = [];
    }
}

namespace UI
{
    public sealed class Window { public bool IsShown { get; set; } = true; }
    public static class GameInput
    {
        public static bool MouseOverWindow { get; set; } = true;
        public static bool IsMouseOverGameWindow(Window window) => MouseOverWindow;
    }
    public sealed class TrackLocationPicker
    {
        public static TrackLocationPicker? Shared { get; set; }
        public bool IsPicking { get; set; }
    }
}

namespace UI.Map
{
    public sealed class MapDrag : UnityEngine.Object
    {
        public bool _isDragging;
        public float RectHeight { get; set; } = 240;
        public UnityEngine.Vector2 ViewportPoint { get; set; } = new(0.4f, 0.6f);
        public UnityEngine.Vector2 NormalizedMousePosition() => ViewportPoint;
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData data) { }
    }
    public sealed class MapBuilder { public UnityEngine.Camera? mapCamera = new(); }
    public sealed class MapWindow : UnityEngine.Object
    {
        public static MapWindow? Current { get; set; }
        public static bool SingletonThrows { get; set; }
        public static MapWindow? instance => SingletonThrows
            ? throw new NullReferenceException("WindowManager is unavailable.") : Current;
        public MapDrag? mapDrag;
        public UI.Window? _window = new();
        public MapBuilder? mapBuilder = new();
        public bool HasGamePoint { get; set; } = true;
        public UnityEngine.Vector3 GamePoint { get; set; } = new(12, 0, 34);
        public UnityEngine.Vector2? LastViewportPoint { get; private set; }
        public bool TryGetGamePointForViewportNormalizedPoint(UnityEngine.Vector2 point, out UnityEngine.Vector3 gamePoint)
        {
            LastViewportPoint = point;
            gamePoint = GamePoint;
            return HasGamePoint;
        }
    }
}

namespace Helpers { public static class Layers { public const int Map = 7; } }

namespace MapEnhancer
{
    public sealed class MapEnhancer : UnityEngine.Object
    {
        public enum MapStates { UNLOADED, MAPLOADED }
        public static MapEnhancer? Instance { get; set; }
        public static MapStates MapState { get; set; }
    }
}

namespace MapEnhancer.Native
{
    public static class Loader { public static ModContext? Context { get; set; } }
    public sealed class ModContext
    {
        public bool Enabled { get; set; }
        public string? LastDependency { get; private set; }
        public ModLog Log { get; } = new();
        public bool CheckDependency(string dependency) { LastDependency = dependency; return Enabled; }
    }
    public sealed class ModLog
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
    }
    public static class SettingsWindow { public static bool IsOpen { get; set; } }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }
}

internal sealed class ObservedIndustries(IEnumerable<Model.Ops.Industry> industries) : IEnumerable<Model.Ops.Industry>
{
    public int Enumerations { get; private set; }
    public bool Invalidated { get; set; }
    public IEnumerator<Model.Ops.Industry> GetEnumerator()
    {
        if (Invalidated) throw new InvalidOperationException("The previous world's industries are no longer valid.");
        Enumerations++;
        return industries.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
