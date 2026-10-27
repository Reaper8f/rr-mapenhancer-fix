# Map Enhancer

Map Enhancer adds map controls and overlays for **Railroader 0.6.0 Experimental**. This branch uses the game's native DLL mod loader.

Based on [Vanguard's Map Enhancer](https://github.com/mricher-git/rr-mapenhancer-mod) and the [Community Fix Edition](https://github.com/Reaper8f/rr-mapenhancer-fix). Original credits and the MIT license are preserved.

## Features

- Resizable map, extended zoom, follow mode, and configurable keyboard shortcuts.
- Route colors, industry-area colors, passenger-stop colors, and grade indicators.
- Track-class, condition, and vegetation-clearance overlays with a legend. Unavailable and unfinished track retain the game's status colors.
- Train and freight details on hover, destination shortcuts, switch markers, and signal colors.
- Turntable controls that submit target angles to the native game controller.
- Map marker visibility without changing the game's culling camera.
- Optional [A Pretty Convenient Menu](https://github.com/crazybmanp/PrettyConvenientMenu) support: with both mods enabled, left-click an industry's spotting or service track on the map to open its Production and Facility Monitor. Dragging, modifier clicks, map markers, and track-location selection keep their existing behavior. The menu mod is not required or bundled; absent, disabled, or incompatible versions leave normal map clicks available.

## Install

1. Enable **Allow Unsafe DLL Mods** under the game's experimental feature preferences.
2. Extract the release ZIP into your Railroader **Mods** directory. The default is Documents/Railroader/Mods, with the files in a MapEnhancer subfolder.
3. Remove older Map Enhancer installations before enabling this version.
4. Start the game and enable Map Enhancer in the mod list.

See the [official installation guide](https://docs.railroadergame.com/modding/installing-mods/) and [DLL mod documentation](https://docs.railroadergame.com/modding/dll-mods/).

Press **Ctrl+F8** to open settings. Settings are saved to Documents/Railroader/ModSettings/map-enhancer.json. A malformed settings file is preserved.

Default shortcuts: **Z** toggles map size, **Ctrl+Z** toggles follow mode, and **Shift+Z** recenters. The map's overlay dropdown selects Off, Grade, Track Class, Track Condition, or Track Cleanliness. Vegetation clearance is the game's right-of-way clearing value; condition is the remaining track-condition fraction.

Turntable markers use **Ctrl+click** for the next clockwise connection, **Alt+click** for the next counterclockwise connection, and **Shift+click** for a half turn. The game owns movement and obstruction checks.

## Build and check

Use a current .NET SDK and reference your public Railroader 0.6.0 installation. Do not copy game assemblies into this repository.

```powershell
dotnet build MapEnhancer/MapEnhancer.csproj -c Release -p:RrManagedDir="C:/path/to/Railroader/Railroader_Data/Managed"
dotnet run --project tests/MapEnhancer.Checks.csproj -- "C:/path/to/Railroader/Railroader_Data/Managed" "artifacts/bin/Release/MapEnhancer.dll"
./package.ps1
```

Builds write to artifacts and do not install or publish the mod. The package script includes an explicit list of runtime files. It excludes game assemblies, debug symbols, source files, and local configuration.

Offline checks validate Harmony targets, game-member references, the native entry point, and overlay boundaries. Gameplay acceptance must separately cover loading a world, map controls, each overlay, world reload, mod disable/re-enable, and multiplayer turntable commands.

## License

MIT; see LICENSE. The bundled Harmony dependency is covered by THIRD-PARTY-NOTICES.txt.
