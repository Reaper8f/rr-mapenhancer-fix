using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object? left, Object? right)
        {
            bool a = left is null || left.Destroyed, b = right is null || right.Destroyed;
            return a || b ? a == b : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public static class Time { public static float unscaledTime; }
}
namespace Core
{
    public static class EventBus
    {
        private static readonly Dictionary<(Type, object), Delegate> Handlers = new();
        public static int Count => Handlers.Count;
        public static void Subscribe<T>(object owner, Action<T> handler) => Handlers.Add((typeof(T), owner), handler);
        public static void Unsubscribe<T>(object owner) => Handlers.Remove((typeof(T), owner));
        public static void Publish<T>(T value)
        {
            foreach (var handler in Handlers.Where(h => h.Key.Item1 == typeof(T)).Select(h => h.Value).ToArray())
                ((Action<T>)handler)(value);
        }
    }
}
namespace Game.Events
{
    public struct MapDidLoadEvent { }
    public struct MapWillUnloadEvent { }
    public struct IndustriesDidChange { }
}
namespace Track
{
    public struct GraphDidRebuildCollections { }
    public sealed class Graph
    {
        public static Graph? Shared;
        public bool HasPopulatedCollections;
    }
}
namespace Game.State
{
    public sealed class StateManager : UnityEngine.Object
    {
        public static StateManager? Shared;
        public bool IsSessionReady;
        public static bool IsUnloading;
    }
}
namespace UI.Windows
{
    public sealed class Window : UnityEngine.Object
    {
        public event Action<bool>? OnShownDidChange;
        public int Subscribers => OnShownDidChange?.GetInvocationList().Length ?? 0;
        public void Show() => OnShownDidChange?.Invoke(true);
    }
    public sealed class WindowManager : UnityEngine.Object
    {
        public static WindowManager? Shared;
        public UI.Map.MapWindow? Map;
        public T? GetComponentInChildren<T>(bool includeInactive) where T : class
        {
            if (!includeInactive) throw new Exception("Closed map windows must be discoverable.");
            return Map as T;
        }
    }
}
namespace UI.Map
{
    public sealed class MapBuilder : UnityEngine.Object
    {
        public static MapBuilder? Shared;
        public UnityEngine.Object? mapCamera;
    }
    public sealed class MapWindow : UnityEngine.Object
    {
        public static MapWindow instance => throw new Exception("Unsafe singleton getter was called.");
        public UI.Windows.Window? _window;
        public UnityEngine.Object? _renderTexture;
        public object? _toolbarPanel;
        public UnityEngine.Object? mapDrag;
        public MapBuilder? mapBuilder;
    }
}
namespace MapEnhancer.Native
{
    public static class Loader { public static Context? Context = new(); }
    public sealed class Context { public Log Log = new(); }
    public sealed class Log
    {
        public readonly List<string> Messages = new();
        public void Info(string message) => Messages.Add(message);
    }
}
namespace MapEnhancer
{
    // Only rendering is substituted. Production subscriptions, readiness checks,
    // retry logic and teardown orchestration run unchanged.
    public partial class MapEnhancer
    {
        public enum MapStates { MAINMENU, MAPLOADED, MAPUNLOADING }
        public static MapStates MapState { get; private set; }
        private bool _visualRefreshPending;
        public int Initializations, Cleanups, Markers, Coroutines, ShownCalls;
        public bool FailInitialization;
        public void Begin() => Start();
        public void Tick() => UpdateMapLifecycle();
        public void Stop() => StopMapLifecycle();
        public bool Pending => _mapLoadPending;
        public bool RefreshPending => _visualRefreshPending;
        private void OnMapWindowShown(bool shown) => ShownCalls++;
        private void InitializeMap(UI.Map.MapWindow window)
        {
            Initializations++;
            MapState = MapStates.MAPLOADED;
            Markers++;
            Coroutines++;
            if (FailInitialization) throw new InvalidOperationException("Rendering setup failed.");
        }
        private void CleanupMap() { Cleanups++; Markers = 0; Coroutines = 0; }
    }
}