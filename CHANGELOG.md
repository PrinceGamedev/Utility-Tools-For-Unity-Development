# Changelog

All notable changes to this package are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [1.2.0] - 2026-10-28

### Added
- `[Button]` now works on **ScriptableObjects** as well as MonoBehaviours.
- `[Button]` finds private methods declared on base classes.
- Define `UTILITYTOOLS_DISABLE_BUTTON_EDITOR` to unregister the `[Button]` editors (e.g. when Odin or
  NaughtyAttributes already provides an editor for every object).
- Editor coroutines follow nested `IEnumerator`s and `CustomYieldInstruction`s
  (`WaitForSecondsRealtime`, `WaitUntil`, `WaitWhile`).
- **Copy GUID / Copy Asset Path** copy every selected asset (one per line).
- "Button Demo" sample (Package Manager → Samples) and EditMode tests.

### Changed
- The `[Button]` inspector is built with UI Toolkit (`CreateInspectorGUI`), so UI Toolkit-only property
  drawers render instead of showing "No GUI Implemented".
- Buttons are listed in declaration order (base-class buttons first) instead of alphabetically.
- `[Button]` coroutines in Play Mode run via `StartCoroutine` on the target, so `WaitForSeconds` etc.
  behave normally. Edit Mode still uses the editor pump.
- The **Run** button for methods with parameters is always enabled; parameters you never expanded
  use their defaults.
- **Open Player Settings** moved from `Ctrl+Shift+P` (Unity's built-in Pause shortcut) to
  `Ctrl+Shift+Alt+P`.
- **Open In Explorer** reveals a selected file (highlighted in its folder), works on Linux, and
  handles paths with spaces.
- **Time Scale** window repaints only when time scale / pause / play state changes instead of every frame.
- `InspectorLock`, `OpenPackageManager`, `OpenInExplorer` are now static classes (they were never windows).

### Fixed
- `[Button]` editor no longer blocks multi-object editing — selecting several objects with the same
  script showed "Multi-object editing not supported" for every MonoBehaviour. Buttons on a
  multi-selection now run on every selected object (static methods run once).
- `[Button]` array parameters: Add / Remove / Size and element edits were lost after the first
  frame. List/array UI state is now keyed per method and parameter, so methods no longer share one list.
- Overloaded `[Button]` methods no longer share parameter values or results.
- Enum parameters with default values no longer fail with an invalid cast.
- Fold-state, result and list caches are now pruned when their objects are destroyed (previously only
  parameter values were).
- Scene Switcher on macOS used `Cmd+Q` / `Cmd+W`; `Cmd+Q` quits Unity. It now uses the physical
  `Ctrl` key (`Option` on macOS editors older than 2022.1). Windows/Linux are unchanged.
- Scene Switcher uses public APIs for the main-window position and the Build Settings button, so it no
  longer breaks if Unity renames internal types (Unity 6 opens Build Profiles).
- **Play From Bootstrap**: if Unity refused to enter Play Mode (e.g. compile errors), the next normal
  Play also started from the bootstrap scene.
- **Copy GUID / Copy Asset Path** were enabled for scene objects and copied an empty string.

## [1.1.0] - 2026-06-01

### Added
- `[Button]`: optional `tooltip`, `color` (hex tint), `confirm` and `confirmMessage` arguments.
- `[Button]`: coroutine support — methods returning `IEnumerator` run in the editor.
- `[Button]`: return values are displayed under the button.
- `[Button]`: static methods are now supported.
- **Open Project Settings** (`Ctrl+Shift+O`) and **Clear All PlayerPrefs** (`Ctrl+Shift+D`).
- **Play From Bootstrap** (`F5`) — enter Play Mode from the first enabled Build scene.
- **Time Scale** window — live `Time.timeScale` slider, presets, Pause / Step.
- **Asset actions** — Copy GUID, Copy Asset Path, Reserialize Selected, Force Recompile (`Ctrl+Shift+R`).
- UPM packaging: `package.json` + Runtime/Editor assembly definitions.

### Changed
- **Open Player Settings** now deep-links to the Player tab and uses `Ctrl+Shift+P` (was a generic
  Project Settings open on `Shift+Q`).
- Param-value cache is now pruned automatically when owning objects are destroyed (was an unbounded leak).

### Backward compatibility
- All existing `[Button(...)]` call sites keep working — new attribute arguments are optional and appended.

## [1.0.0] - 2026-06-01
- Initial set: `[Button]` attribute with parameter editor, Scene Switcher overlay, Inspector Lock,
  Open Package Manager, Open In Explorer.
