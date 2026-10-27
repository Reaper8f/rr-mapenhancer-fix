using System.Reflection;
using MapEnhancer;
using MapEnhancer.Native;
using UI.Map;

int checks = 0;
void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

var prefix = typeof(MapToolbarIntegration)
    .GetNestedType("DisplayOptionsButtonPatch", BindingFlags.NonPublic)!
    .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
bool AllowsNativeButton() => (bool)prefix.Invoke(null, null)!;

Require(AllowsNativeButton(), "Native controls must start enabled.");
try
{
    MapToolbarIntegration.Acquire();
    Require(!AllowsNativeButton(), "Ownership must suppress future toolbar construction before UI exists.");
    MapToolbarIntegration.Release();
    Require(AllowsNativeButton(), "Release before UI exists must restore the native construction gate.");
}
catch (Exception error)
{
    throw new InvalidOperationException("Cold startup and release must not access the map singleton.", error);
}

var preStart = new MapWindow();
UnityEngine.Object.Existing = [preStart];
MapToolbarIntegration.Acquire();
MapToolbarIntegration.Release();
Require(AllowsNativeButton(), "Windows without toolbar fields must be safe during startup and unload.");

var active = new MapWindow { _displayMenu = new(), _toolbarPanel = new() };
var inactive = new MapWindow { Active = false, _displayMenu = new(), _toolbarPanel = new() };
var destroyed = new MapWindow { Destroyed = true, _displayMenu = new(), _toolbarPanel = new() };
UnityEngine.Object.Existing = [active, inactive, destroyed];
MapToolbarIntegration.Acquire();
Require(!AllowsNativeButton(), "Acquire must suppress native buttons.");
Require(active._displayMenu.Dismissals == 1 && inactive._displayMenu.Dismissals == 1,
    "Acquire must dismiss existing menus, including inactive windows.");
Require(active._toolbarPanel.Rebuilds == 1 && inactive._toolbarPanel.Rebuilds == 1,
    "Acquire must refresh both existing toolbars.");
Require(destroyed._displayMenu.Dismissals == 0 && destroyed._toolbarPanel.Rebuilds == 0,
    "Destroyed Unity objects must be skipped.");
MapToolbarIntegration.Release();
Require(AllowsNativeButton(), "Release must restore native buttons.");
Require(active._toolbarPanel.Rebuilds == 2 && inactive._toolbarPanel.Rebuilds == 2,
    "Release must refresh both existing toolbars.");
Require(active._displayMenu.Dismissals == 1 && inactive._displayMenu.Dismissals == 1,
    "Release must not dismiss unrelated menus.");
Require(MapWindow.SingletonReads == 0, "Toolbar ownership must never read the unsafe singleton.");

// Execute the production cleanup boundary with failures in both cleanup and reporting.
var events = new List<string>();
var original = new InvalidOperationException("Original load failure.");
Exception? observed = null;
active._toolbarPanel.ThrowOnRebuild = true;
try
{
    try { throw original; }
    catch
    {
        NativeCleanup.Run(MapToolbarIntegration.Release, error =>
        {
            events.Add("toolbar failure reported");
            throw new InvalidOperationException("Diagnostic failure.");
        });
        NativeCleanup.Run(() => events.Add("unpatch"), _ => throw new Exception("Unexpected report."));
        NativeCleanup.Run(() => { events.Add("destroy"); throw new InvalidOperationException("Destroy failure."); },
            _ => events.Add("destroy failure reported"));
        NativeCleanup.Run(() => events.Add("reset"), _ => throw new Exception("Unexpected report."));
        throw;
    }
}
catch (Exception error) { observed = error; }
Require(ReferenceEquals(observed, original), "Cleanup must preserve the original load exception.");
Require(events.SequenceEqual(new[] { "toolbar failure reported", "unpatch", "destroy", "destroy failure reported", "reset" }),
    "Cleanup failures or diagnostic failures must not skip later cleanup steps.");
Require(AllowsNativeButton(), "Release must drop ownership before any toolbar refresh can fail.");
Require(MapWindow.SingletonReads == 0, "Failure cleanup must remain independent of UI initialization.");
Console.WriteLine($"PASS: {checks} startup assertions; production toolbar ownership and cleanup with public-contract doubles.");
Console.WriteLine("These offline checks do not launch Unity or establish in-game behavior.");
