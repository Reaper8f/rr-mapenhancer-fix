using Core;
using Game.Events;
using Game.State;
using Track;
using UI.Map;
using UI.Windows;
using UnityEngine;
using Mod = MapEnhancer.MapEnhancer;

int checks = 0;
void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
MapWindow ReadyWindow()
{
    MapBuilder.Shared = new MapBuilder { mapCamera = new UnityEngine.Object() };
    return new MapWindow
    {
        _window = new Window(), _renderTexture = new UnityEngine.Object(), _toolbarPanel = new object(),
        mapDrag = new UnityEngine.Object(), mapBuilder = MapBuilder.Shared
    };
}
void ClearWorld()
{
    StateManager.Shared = null;
    StateManager.IsUnloading = false;
    WindowManager.Shared = null;
    Graph.Shared = null;
    MapBuilder.Shared = null;
    Time.unscaledTime = 0;
}

ClearWorld();
var mod = new Mod();
mod.Begin();
for (int i = 0; i < 100; i++) mod.Tick();
Check(mod.Pending && mod.Initializations == 0, "Boot must remain pending with no state/window manager.");
Check(EventBus.Count == 4, "Lifecycle subscriptions must survive delayed startup.");
StateManager.Shared = new StateManager { IsSessionReady = true };
Graph.Shared = new Graph { HasPopulatedCollections = true };
mod.Tick();
Check(mod.Initializations == 0, "A missing manager is not an exception or successful initialization.");
WindowManager.Shared = new WindowManager();
EventBus.Publish(new MapDidLoadEvent());
mod.Tick();
Check(mod.Initializations == 0 && mod.Pending, "A manager with no map must continue retrying.");
var first = new MapWindow();
WindowManager.Shared.Map = first;
mod.Tick();
Check(mod.Initializations == 0, "Registration alone is insufficient before MapWindow.Start.");
first._window = new Window();
first._toolbarPanel = new object();
first.mapDrag = new UnityEngine.Object();
first.mapBuilder = new MapBuilder { mapCamera = new UnityEngine.Object() };
MapBuilder.Shared = first.mapBuilder;
mod.Tick();
Check(mod.Initializations == 0, "Wait for the map render texture.");
first._renderTexture = new UnityEngine.Object();
first._toolbarPanel = null;
mod.Tick();
Check(mod.Initializations == 0, "Wait for toolbar setup.");
first._toolbarPanel = new object();
first.mapDrag = null;
mod.Tick();
Check(mod.Initializations == 0, "Wait for map input.");
first.mapDrag = new UnityEngine.Object();
first.mapBuilder.mapCamera = null;
mod.Tick();
Check(mod.Initializations == 0, "Wait for the map camera.");
first.mapBuilder.mapCamera = new UnityEngine.Object();
MapBuilder.Shared = new MapBuilder();
mod.Tick();
Check(mod.Initializations == 0, "Do not bind a mismatched builder.");
MapBuilder.Shared = first.mapBuilder;
Graph.Shared.HasPopulatedCollections = false;
mod.Tick();
Check(mod.Initializations == 0, "Wait for graph population.");
Graph.Shared.HasPopulatedCollections = true;
StateManager.Shared.IsSessionReady = false;
mod.Tick();
Check(mod.Initializations == 0, "Wait until the world has finished population.");
StateManager.Shared.IsSessionReady = true;
mod.Tick();
Check(mod.Initializations == 1 && !mod.Pending && Mod.MapState == Mod.MapStates.MAPLOADED,
    "Delayed startup must eventually initialize without another event.");
Check(first._window.Subscribers == 1, "Subscribe once to the actual initialized window.");
EventBus.Publish(new MapDidLoadEvent());
EventBus.Publish(new MapDidLoadEvent());
mod.Tick(); mod.Tick();
Check(mod.Initializations == 1 && mod.Markers == 1 && mod.Coroutines == 1 && first._window.Subscribers == 1,
    "Duplicate events must not duplicate markers, workers or handlers.");
first._window.Show();
Check(mod.ShownCalls == 1, "Window handler remains active after delayed initialization.");
EventBus.Publish(new GraphDidRebuildCollections());
Check(mod.RefreshPending, "Graph refresh subscriptions remain intact.");

// Replacing the manager without an unload callback must release the old window.
var second = ReadyWindow();
WindowManager.Shared = new WindowManager { Map = second };
mod.Tick();
Check(mod.Cleanups == 1 && mod.Initializations == 2, "Recover when the map window is replaced.");
Check(first._window.Subscribers == 0 && second._window!.Subscribers == 1,
    "Unsubscribe from the captured old window, not the new singleton.");
Check(!mod.RefreshPending, "Old pending visual work must not cross window lifetimes.");
Graph.Shared = new Graph { HasPopulatedCollections = true };
mod.Tick();
Check(mod.Cleanups == 2 && mod.Initializations == 3 && second._window!.Subscribers == 1,
    "A replacement graph gets fresh resources even when its window survives.");

// The manager can disappear before the mod receives unload.
WindowManager.Shared = null;
mod.Tick();
Check(mod.Pending && mod.Cleanups == 3 && second._window!.Subscribers == 0,
    "Manager disappearance releases ownership and waits without singleton access.");
EventBus.Publish(new MapWillUnloadEvent());
var third = ReadyWindow();
WindowManager.Shared = new WindowManager { Map = third };
mod.Tick();
Check(!mod.Pending && mod.Initializations == 3, "Unload cancels waiting even if old state looks ready.");
EventBus.Publish(new MapDidLoadEvent());
mod.Tick();
Check(mod.Initializations == 4 && third._window!.Subscribers == 1, "A second map load initializes again.");
StateManager.IsUnloading = true;
mod.Tick(); mod.Tick();
Check(mod.Cleanups == 4 && mod.Markers == 0 && mod.Coroutines == 0 && !mod.Pending,
    "Native unloading also cancels and cleans exactly once.");
StateManager.IsUnloading = false;
mod.Tick();
Check(mod.Initializations == 4, "Unloading state clearing alone must not resurrect resources.");
EventBus.Publish(new MapDidLoadEvent()); mod.Tick();
Check(mod.Initializations == 5, "A subsequent load notification rearms initialization.");
mod.Stop(); mod.Stop(); mod.Tick();
EventBus.Publish(new MapDidLoadEvent());
Check(mod.Cleanups == 5 && EventBus.Count == 0 && third._window!.Subscribers == 0 && !mod.Pending,
    "Mod destruction must detach all handlers, cancel retries, and be idempotent.");

// Unity destroyed-object equality must work, not just CLR null checks.
ClearWorld();
StateManager.Shared = new StateManager { IsSessionReady = true };
Graph.Shared = new Graph { HasPopulatedCollections = true };
var destroyed = ReadyWindow(); destroyed.Destroyed = true;
WindowManager.Shared = new WindowManager { Map = destroyed };
mod = new Mod(); mod.Begin(); mod.Tick();
Check(mod.Initializations == 0 && mod.Pending, "Destroyed Unity windows are unavailable.");
mod.Stop();
WindowManager.Shared.Map = ReadyWindow();
mod.Tick();
Check(mod.Initializations == 0 && EventBus.Count == 0, "Stopping before initialization cancels it permanently.");

// Late enabling requires no new MapDidLoad event.
mod = new Mod(); mod.Begin(); mod.Tick();
Check(mod.Initializations == 1 && !mod.Pending, "Late enable initializes from current ready world state.");
mod.Stop();

// An initialization fault must neither leave partial resources nor claim success.
mod = new Mod { FailInitialization = true }; mod.Begin(); mod.Tick();
Check(mod.Initializations == 1 && mod.Cleanups == 1 && mod.Markers == 0 && mod.Coroutines == 0 && mod.Pending,
    "Failed initialization releases partial resources and remains pending.");
Check(Mod.MapState != Mod.MapStates.MAPLOADED && WindowManager.Shared.Map!._window!.Subscribers == 0,
    "Failure must not leave the map loaded or subscribed.");
Check(MapEnhancer.Native.Loader.Context!.Log.Messages.Count == 1, "Initialization failure is diagnosed.");
for (int i = 0; i < 100; i++) mod.Tick();
Check(mod.Initializations == 1, "Failure retries must be throttled.");
Time.unscaledTime = 1; mod.Tick();
Check(mod.Initializations == 2 && mod.Cleanups == 2, "Retry a fault instead of silently giving up.");
Check(MapEnhancer.Native.Loader.Context.Log.Messages.Count == 1, "Identical errors should not flood logs.");
mod.FailInitialization = false;
Time.unscaledTime = 2; mod.Tick();
Check(mod.Initializations == 3 && !mod.Pending && mod.Markers == 1 && mod.Coroutines == 1,
    "Recovery must produce one complete initialization.");
mod.Stop();
Check(EventBus.Count == 0, "No event subscribers remain after all lifecycle scenarios.");
Console.WriteLine($"PASS: {checks} map lifecycle assertions.");