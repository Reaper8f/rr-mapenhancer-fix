using System.Reflection;
using MapEnhancer;
using Model.Ops;
using Track;
using UI;
using UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using Mod = MapEnhancer.MapEnhancer;
using Native = MapEnhancer.Native;

int checks = 0;
void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var absent = new ClickFixture { Enabled = false, WindowType = null };
Require(!absent.Menu.IsAvailable && !absent.Menu.TryOpen(absent.Industry), "An absent provider must be unavailable.");
Require(absent.Resolutions == 0 && absent.ReadyChecks == 0, "Absence must skip readiness, type resolution, and provider access.");

var disabled = new ClickFixture { Enabled = false };
Require(!disabled.Menu.TryOpen(disabled.Industry), "An installed but disabled provider must stay inactive.");
Require(disabled.Resolutions == 0 && SuccessfulWindow.Calls == 0, "A resident provider type must not override the native dependency gate.");
disabled.Enabled = true;
Require(disabled.Menu.TryOpen(disabled.Industry) && SuccessfulWindow.Calls == 1, "Enabling the provider later must permit opening without restarting the bridge.");
Require(disabled.Resolutions == 1, "The first enabled use must resolve the provider.");
disabled.Enabled = false;
Require(!disabled.Menu.IsAvailable, "Disabling after success must immediately close the availability gate.");
disabled.WindowType = typeof(MissingSignatureWindow);
disabled.Enabled = true;
Require(!disabled.Menu.IsAvailable && disabled.Resolutions == 2, "Re-enabling must not reuse a method cached before disable.");

var readiness = new ClickFixture();
Require(readiness.Menu.TryOpen(readiness.Industry), "The initial ready provider must open.");
readiness.Ready = false;
Require(!readiness.Menu.IsAvailable && !readiness.Menu.TryOpen(readiness.Industry), "A dead provider host must invalidate an already cached method.");
Require(readiness.Resolutions == 1 && SuccessfulWindow.Calls == 1 && readiness.Messages.Count == 0,
    "An unready provider must fail silently without resolving or invoking its API.");
readiness.WindowType = typeof(ReplacementWindow);
readiness.Ready = true;
Require(readiness.Menu.TryOpen(readiness.Industry) && ReplacementWindow.Calls == 1 && readiness.Resolutions == 2,
    "A recreated provider host must resolve again and use the current API.");

var late = new ClickFixture { WindowType = null };
Require(!late.Menu.IsAvailable && !late.Menu.IsAvailable && late.Messages.Count == 1,
    "A missing API must retain fallback and avoid repeated diagnostics.");
late.WindowType = typeof(SuccessfulWindow);
Require(late.Menu.TryOpen(late.Industry), "A provider API that appears later must be retried.");
late.Menu.Reset();
Require(late.Menu.TryOpen(late.Industry) && late.Resolutions == 4, "Explicit unload reset must clear cached provider methods.");

foreach (Type invalid in new[] { typeof(MissingSignatureWindow), typeof(WrongReturnWindow), typeof(GenericWindow<>) })
{
    var incompatible = new ClickFixture { WindowType = invalid };
    Require(!incompatible.Menu.IsAvailable && !incompatible.Menu.TryOpen(incompatible.Industry),
        $"Incompatible API {invalid.Name} must not be invoked.");
    Require(incompatible.Messages.Count == 1, "Repeated incompatible API checks must log only once.");
}

var nullResult = new ClickFixture { WindowType = typeof(NullWindow) };
Require(!nullResult.Menu.TryOpen(nullResult.Industry) && NullWindow.Calls == 1,
    "A provider returning no window must preserve the native click.");
Require(!nullResult.Menu.TryOpen(null) && NullWindow.Calls == 1, "A missing industry must not call the provider.");

var throwing = new ClickFixture { WindowType = typeof(RecoveringWindow) };
RecoveringWindow.Throw = true;
Require(!throwing.Menu.TryOpen(throwing.Industry) && !throwing.Menu.TryOpen(throwing.Industry),
    "Provider exceptions must not escape into native map input.");
Require(throwing.Messages.Count == 1 && throwing.Messages[0].Contains("Provider failure."),
    "Reflection failures must report the underlying provider error only once.");
RecoveringWindow.Throw = false;
Require(throwing.Menu.TryOpen(throwing.Industry), "A transient provider failure must recover.");
RecoveringWindow.Throw = true;
Require(!throwing.Menu.TryOpen(throwing.Industry) && throwing.Messages.Count == 2,
    "A successful open must reset diagnostic suppression for a later failure.");

var resolverFailure = new OptionalIndustryMenu(() => true, () => throw new InvalidOperationException("Resolution failure."), _ => { });
Require(!resolverFailure.IsAvailable, "Type resolution failures must remain optional.");
var dependencyFailure = new OptionalIndustryMenu(() => throw new InvalidOperationException("Dependency failure."), () => typeof(SuccessfulWindow), _ => { });
Require(!dependencyFailure.IsAvailable, "Native dependency query failures must preserve fallback.");
var readinessFailure = new OptionalIndustryMenu(() => true, () => typeof(SuccessfulWindow), _ => { },
    () => throw new InvalidOperationException("Readiness failure."));
Require(!readinessFailure.IsAvailable, "Provider readiness failures must preserve fallback.");
var diagnosticFailure = new OptionalIndustryMenu(() => true, () => typeof(RecoveringWindow),
    _ => throw new InvalidOperationException("Diagnostic failure."));
Require(!diagnosticFailure.TryOpen(throwing.Industry), "A failing logger must not convert an optional provider failure into a click exception.");

var success = new ClickFixture();
Require(success.Click(), "A plain left click on eligible industry track must be consumed when the window opens.");
Require(SuccessfulWindow.Calls == 1 && ReferenceEquals(SuccessfulWindow.LastIndustry, success.Industry),
    "The actual clicked industry's object must reach the provider exactly once.");
Require(success.Camera.LastViewportPoint == new Vector3(0.4f, 0.6f, 0) && success.Window.LastViewportPoint == success.Drag.ViewportPoint,
    "Ray and game-point lookup must use the same normalized pointer position.");
Require(success.Graph.LastPoint == success.Window.GamePoint && Math.Abs(success.Graph.LastRadius - 4f) < 0.00001f,
    "Track lookup must use the projected game point and the six-pixel radius at the current scale.");
Require(Physics.LastLayerMask == 1 << Helpers.Layers.Map && Physics.LastDistance == success.Camera.transform.position.y,
    "Marker priority must use the map layer and camera height.");

var guards = new (string Name, Action<ClickFixture> Arrange)[]
{
    ("right click", f => f.Data.button = PointerEventData.InputButton.Right),
    ("middle click", f => f.Data.button = PointerEventData.InputButton.Middle),
    ("drag", f => f.Drag._isDragging = true),
    ("hidden map", f => f.Window._window!.IsShown = false),
    ("different map drag", f => f.Window.mapDrag = new MapDrag()),
    ("settings window", _ => Native.SettingsWindow.IsOpen = true),
    ("another foreground window", _ => GameInput.MouseOverWindow = false),
    ("native track picker", _ => TrackLocationPicker.Shared = new() { IsPicking = true }),
    ("disabled provider", f => f.Enabled = false),
    ("provider host unavailable", f => f.Ready = false),
    ("map enhancer absent", _ => Mod.Instance = null),
    ("map not loaded", _ => Mod.MapState = Mod.MapStates.UNLOADED),
    ("destroyed map", f => f.Window.Destroyed = true),
    ("no native window", f => f.Window._window = null),
    ("no builder", f => f.Window.mapBuilder = null),
    ("no camera", f => f.Window.mapBuilder!.mapCamera = null),
    ("destroyed camera", f => f.Camera.Destroyed = true),
    ("no graph", _ => Graph.Shared = null),
    ("no operations", _ => OpsController.Shared = null),
    ("empty map rectangle", f => f.Drag.RectHeight = 0),
    ("left of viewport", f => f.Drag.ViewportPoint = new(-0.01f, 0.5f)),
    ("right of viewport", f => f.Drag.ViewportPoint = new(1.01f, 0.5f)),
    ("below viewport", f => f.Drag.ViewportPoint = new(0.5f, -0.01f)),
    ("above viewport", f => f.Drag.ViewportPoint = new(0.5f, 1.01f)),
    ("map icon", _ => Physics.HasMapIcon = true),
    ("no projected point", f => f.Window.HasGamePoint = false),
    ("no nearby track", f => f.Graph.HasLocation = false),
    ("track query failure", f => f.Graph.ThrowOnQuery = true),
    ("map singleton unavailable", _ => MapWindow.SingletonThrows = true)
};
foreach (var (name, arrange) in guards)
{
    var fixture = new ClickFixture();
    arrange(fixture);
    Require(!fixture.Click() && SuccessfulWindow.Calls == 0, $"{name} must preserve native map behavior without opening an industry window.");
}
foreach (KeyCode modifier in Enum.GetValues<KeyCode>())
{
    var fixture = new ClickFixture();
    Input.Pressed.Add(modifier);
    Require(!fixture.Click() && fixture.Graph.Queries == 0 && SuccessfulWindow.Calls == 0,
        $"{modifier} must retain its native map gesture.");
}
var nullInput = new ClickFixture();
Require(!IndustryMapClick.TryOpen(null!, nullInput.Data, nullInput.Menu) &&
    !IndustryMapClick.TryOpen(nullInput.Drag, null!, nullInput.Menu), "Missing pointer or drag data must retain native behavior.");
var icon = new ClickFixture();
Physics.HasMapIcon = true;
Require(!icon.Click() && icon.Graph.Queries == 0 && icon.Span.Queries.Count == 0,
    "A map icon must win before querying track or industry spans.");
var failedOpen = new ClickFixture { WindowType = typeof(NullWindow) };
Require(!failedOpen.Click() && NullWindow.Calls == 1, "The click handler must fall through when the provider declines to create a window.");
failedOpen.WindowType = typeof(RecoveringWindow);
failedOpen.Menu.Reset();
RecoveringWindow.Throw = true;
Require(!failedOpen.Click(), "The click handler must fall through when provider opening throws.");

// The real TrackSpan owns boundary semantics. Configure responses and verify that the exact
// clicked Location is forwarded, without substituting segment-wide, midpoint, or distance checks.
var firstBoundary = new Location("shared-segment", 3.25f);
var lastBoundary = new Location("shared-segment", 6.5f);
var beforeBoundary = new Location("shared-segment", 3.249f);
var afterBoundary = new Location("shared-segment", 6.501f);
var exactSpan = new TrackSpan { ContainsResult = p => p == firstBoundary || p == lastBoundary };
var exactIndustry = ClickFixture.MakeIndustry(exactSpan);
Require(ReferenceEquals(IndustryMapClick.FindIndustry([exactIndustry], firstBoundary), exactIndustry),
    "The first boundary decision from TrackSpan.Contains must be honored.");
Require(ReferenceEquals(IndustryMapClick.FindIndustry([exactIndustry], lastBoundary), exactIndustry),
    "The last boundary decision from TrackSpan.Contains must be honored.");
Require(IndustryMapClick.FindIndustry([exactIndustry], beforeBoundary) == null &&
    IndustryMapClick.FindIndustry([exactIndustry], afterBoundary) == null,
    "Nearby rejected locations on the same segment must not match an industry.");
Require(exactSpan.Queries.SequenceEqual(new[] { firstBoundary, lastBoundary, beforeBoundary, afterBoundary }),
    "TrackSpan.Contains must receive every exact queried Location.");

var otherSpan = new TrackSpan { ContainsResult = p => p == afterBoundary };
var otherIndustry = ClickFixture.MakeIndustry(otherSpan);
Require(ReferenceEquals(IndustryMapClick.FindIndustry([exactIndustry, otherIndustry], afterBoundary), otherIndustry),
    "Two industries sharing a segment must be distinguished by the actual location.");
var overlap = ClickFixture.MakeIndustry(new TrackSpan { ContainsResult = p => p == firstBoundary });
Require(IndustryMapClick.FindIndustry([exactIndustry, overlap], firstBoundary) == null,
    "Track belonging to multiple industries must preserve native behavior.");
exactIndustry.Components.Add(new IndustryComponent { TrackSpans = { new TrackSpan { ContainsResult = _ => true } } });
Require(ReferenceEquals(IndustryMapClick.FindIndustry([exactIndustry], firstBoundary), exactIndustry),
    "Multiple matching components of one industry must not be treated as ambiguous.");
var inactiveSpan = new TrackSpan { ContainsResult = _ => true };
var inactiveIndustry = new Industry();
inactiveIndustry.Components.Add(new IndustryComponent { IsProgressionEnabled = false, TrackSpans = { inactiveSpan } });
inactiveIndustry.Components.Add(null);
inactiveIndustry.Components.Add(new IndustryComponent { TrackSpans = { null } });
Require(IndustryMapClick.FindIndustry([null!, inactiveIndustry], firstBoundary) == null && inactiveSpan.Queries.Count == 0,
    "Missing entries and progression-disabled components must not create matches.");

var ambiguousClick = new ClickFixture();
OpsController.Shared!.AllIndustries = [ambiguousClick.Industry,
    ClickFixture.MakeIndustry(new TrackSpan { ContainsResult = _ => true })];
Require(!ambiguousClick.Click() && SuccessfulWindow.Calls == 0,
    "An ambiguous industry result must not open either provider window.");

var reload = new ClickFixture();
var oldIndustries = new ObservedIndustries([reload.Industry]);
OpsController.Shared!.AllIndustries = oldIndustries;
Require(reload.Click() && ReferenceEquals(SuccessfulWindow.LastIndustry, reload.Industry), "The initial world must open its current industry.");
oldIndustries.Invalidated = true;
var replacementIndustry = ClickFixture.MakeIndustry(new TrackSpan { ContainsResult = p => p == reload.Graph.Result });
var newIndustries = new ObservedIndustries([replacementIndustry]);
OpsController.Shared = new() { AllIndustries = newIndustries };
Require(reload.Click() && ReferenceEquals(SuccessfulWindow.LastIndustry, replacementIndustry),
    "A reloaded world must open its new industry object, even at the same track location.");
Require(oldIndustries.Enumerations == 1 && newIndustries.Enumerations == 1,
    "Every click must use the current operations collection and never enumerate a stale world cache.");

var nativeFallback = new ClickFixture();
Native.Loader.Context = new Native.ModContext { Enabled = false };
var prefix = typeof(IndustryMapClick).GetNestedType("MapTrackClickPatch", BindingFlags.NonPublic)!
    .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
Require((bool)prefix.Invoke(null, new object[] { nativeFallback.Drag, nativeFallback.Data })!,
    "The actual Harmony prefix must permit the native click when the optional mod is disabled.");
Require(Native.Loader.Context.LastDependency == "crzbp/a-pretty-convenient-menu ^0.1.0",
    "The native dependency gate must check the supported provider version.");
IndustryMapClick.Reset();

Console.WriteLine($"PASS: {checks} optional industry-menu assertions; both production bridge and click handler are linked.");
Console.WriteLine("Contract doubles exercise offline interaction and fallback behavior; no game or Unity runtime was launched.");

internal sealed class ClickFixture
{
    public bool Enabled { get; set; } = true;
    public bool Ready { get; set; } = true;
    public Type? WindowType { get; set; } = typeof(SuccessfulWindow);
    public int Resolutions { get; private set; }
    public int ReadyChecks { get; private set; }
    public List<string> Messages { get; } = [];
    public OptionalIndustryMenu Menu { get; }
    public MapDrag Drag { get; } = new();
    public PointerEventData Data { get; } = new() { button = PointerEventData.InputButton.Left };
    public MapWindow Window { get; }
    public Camera Camera { get; }
    public Graph Graph { get; }
    public TrackSpan Span { get; }
    public Industry Industry { get; }

    public ClickFixture()
    {
        SuccessfulWindow.Reset();
        ReplacementWindow.Calls = 0;
        NullWindow.Calls = 0;
        RecoveringWindow.Throw = false;
        Input.Pressed.Clear();
        Physics.Reset();
        MapWindow.SingletonThrows = false;
        GameInput.MouseOverWindow = true;
        TrackLocationPicker.Shared = null;
        Native.SettingsWindow.IsOpen = false;
        Native.Loader.Context = null;
        IndustryMapClick.Reset();
        Mod.Instance = new Mod();
        Mod.MapState = Mod.MapStates.MAPLOADED;
        Window = new MapWindow { mapDrag = Drag };
        MapWindow.Current = Window;
        Camera = Window.mapBuilder!.mapCamera!;
        Graph = new Graph { Result = new Location("industry-track", 2.5f) };
        Track.Graph.Shared = Graph;
        Span = new TrackSpan { ContainsResult = p => p == Graph.Result };
        Industry = MakeIndustry(Span);
        OpsController.Shared = new() { AllIndustries = new[] { Industry } };
        Menu = new OptionalIndustryMenu(() => Enabled, () => { Resolutions++; return WindowType; },
            Messages.Add, () => { ReadyChecks++; return Ready; });
    }

    public bool Click() => IndustryMapClick.TryOpen(Drag, Data, Menu);
    public static Industry MakeIndustry(TrackSpan span)
    {
        var industry = new Industry();
        industry.Components.Add(new IndustryComponent { TrackSpans = { span } });
        return industry;
    }
}

public sealed class SuccessfulWindow
{
    public static int Calls { get; private set; }
    public static Industry? LastIndustry { get; private set; }
    public static SuccessfulWindow Open(Industry industry)
    {
        Calls++;
        LastIndustry = industry;
        return new();
    }
    public static void Reset() { Calls = 0; LastIndustry = null; }
}
public sealed class ReplacementWindow
{
    public static int Calls { get; set; }
    public static ReplacementWindow Open(Industry industry) { Calls++; return new(); }
}
public sealed class MissingSignatureWindow { public static MissingSignatureWindow Open(string industry) => new(); }
public sealed class WrongReturnWindow { public static object Open(Industry industry) => new(); }
public sealed class GenericWindow<T> { public static GenericWindow<T> Open(Industry industry) => new(); }
public sealed class NullWindow
{
    public static int Calls { get; set; }
    public static NullWindow? Open(Industry industry) { Calls++; return null; }
}
public sealed class RecoveringWindow
{
    public static bool Throw { get; set; }
    public static RecoveringWindow Open(Industry industry) => Throw
        ? throw new InvalidOperationException("Provider failure.") : new();
}
