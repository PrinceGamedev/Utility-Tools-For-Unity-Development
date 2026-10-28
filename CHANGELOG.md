# Changelog

All notable changes to this package are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [1.3.0] - 2026-10-28

### Added
- **ULog logging library** (`UtilityTools.Logging`):
  - `LogChannel` — one per game system, with its own colour, `Enabled` switch and `MinLevel`.
  - `Verbose` / `Info` / `Warning` / `Error` / `Exception` / `Assert`, `InfoOnce` / `WarningOnce`,
    lazy `Func<string>` overloads, structured fields: `Spawn.Info("Spawned", ("count", 3))`.
  - Verbose / Info / Warning / Assert calls are compiled out of release builds (arguments included).
    `UTILITYTOOLS_LOGS` keeps them; `UTILITYTOOLS_STRIP_ERRORS` also drops errors;
    `UTILITYTOOLS_SILENCE_UNITY_LOGS` filters plain `Debug.Log` / `LogWarning` in release builds.
  - Extension points: `ULog.Logged` event (custom outputs) and `ULog.Formatter` (line format).
  - Logging methods are hidden from stack traces (Unity 2022.2+), so double-click opens the caller.
- **AI bridge** — token-lean files an AI agent reads directly (no MCP server, no tool schemas in context),
  in `Logs/AIConsole/` (configurable):
  - `status.txt` — one line: state, mode, compile result, error/warning counts, last id (~30 tokens).
  - `errors.md` — compile errors plus error/warning groups (repeats collapsed, project frames only).
  - `session.jsonl` — one JSON object per entry for drill-down by `#id`; previous editor session kept
    as `session.prev.jsonl`; size-capped with rotation.
  - `AI-README.md` — how to read the files, cheapest first.
  - Captures logs from every thread, compiler errors/warnings, and Play / compile / clear markers.
  - Plain `Debug.Log("[Tag] …")` messages are tagged like ULog channels.
  - Writes are batched, atomic (no half-written files) and only happen when something changed.
- **AI Console window** (`Tools → Utility Tools → AI Console`, `Ctrl+Shift+Alt+C`) — a replacement for
  Unity's Console, built with UI Toolkit, dark and light themes:
  - Rows with system colour strip, type icon, local time, system chip, structured fields, `×count` and `#id`.
  - Search language: words, `"phrases"`, `-exclude`, `type:`, `sys:`, `#id`, `fp:`, `file:`, `/regex/`.
  - Type toggles with live counts, system chips (click to filter, right-click to mute for yourself),
    Collapse, Follow, Error Pause, Clear on Play, sort by time / newest / severity / count / system / message.
  - Pinned compile errors with a banner; "Compiling…" banner while scripts build.
  - Session timeline of errors / warnings / logs — click a bar to jump there.
  - Details pane: selectable message, fields, context object (click to select it), stack trace with
    your code highlighted and engine frames folded, exception chains, and inline source preview.
  - Double-click opens the code; Copy / Copy for AI / Copy JSON / Show Similar; Ctrl/Cmd+F search,
    Ctrl/Cmd+C copy, Ctrl/Cmd+Shift+C copy for AI.
  - Status bar shows AI file state; the window title shows the error count.
- **Project Settings → Utility Tools → AI Console**:
  - Per-system on/off, minimum level and colour (team settings in `ProjectSettings/UtilityToolsConsole.asset`).
  - "Only for me" per-system overrides (personal, in `UserSettings/`), also from the system chips.
  - Release-build switches that edit the active target's scripting defines (keep logs, strip errors,
    silence `Debug.Log`).
  - AI file options (folder, info logs, digest size, rotation) and console history size.
  - "Scan project for systems" finds static `LogChannel` fields.
- "Logging Demo" sample and EditMode tests for the parser, AI formats, search language and ULog.

### Notes
- The AI Console window does not replace Unity's built-in Console, which keeps working alongside it.
- Pause on error is applied on the next editor tick, not synchronously like Unity's Error Pause.
- Logs written before the editor finishes loading scripts (very early in a domain reload) are only in
  Unity's Console.

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
