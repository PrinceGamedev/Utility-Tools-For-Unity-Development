# Changelog

All notable changes to this package are documented here. Both the package and each feature follow
[Semantic Versioning](https://semver.org/) — see [VERSIONING.md](VERSIONING.md).

`## [x.y.z]` is the **package** release you install. Inside each release, `### Feature x.y.z` is that
**feature's** own version; features not listed are unchanged.

## [1.3.0] - 2026-10-28

### AI Console 1.0.0 — new
- **AI Console window** (`Tools → Utility Tools → AI Console`, `Ctrl+Shift+Alt+C`) — a replacement for
  Unity's Console, built with UI Toolkit, dark and light themes:
  - Rows with system colour strip, type icon, local time, system chip, structured fields, `×count` and `#id`.
  - Search language: words, `"phrases"`, `-exclude`, `type:`, `sys:`, `#id`, `fp:`, `file:`, `/regex/`.
  - Type toggles with live counts, system chips (click to filter, right-click to mute for yourself),
    Collapse, Follow, Error Pause, Clear on Play, sort by time / newest / severity / count / system / message.
  - Pinned compile errors with a banner; "Compiling…" banner while scripts build.
  - Clear behaves like Unity's: logs and compile warnings go (until the next compile reports them
    again); compile errors stay until fixed.
  - Session timeline of errors / warnings / logs — click a bar to jump there.
  - Details pane: selectable message, fields, context object (click to select it), stack trace with
    your code highlighted and engine frames folded, exception chains, and inline source preview.
  - Double-click opens the code; Copy / Copy for AI / Copy JSON / Show Similar; Ctrl/Cmd+F search,
    Ctrl/Cmd+C copy, Ctrl/Cmd+Shift+C copy for AI.
  - Status bar shows AI file state; the window title shows the error count.
  - Panels: the details pane docks **bottom or right** (Auto picks right on wide windows) with a drag
    divider (double-click to hide); Systems, Timeline and Details collapse with ▾/▸ arrows; all of it
    is remembered per user.
  - Slim icon strip with one-click buttons for every option (no dropdown menus): Follow, Error Pause,
    Clear on Play, Sort and Dock (cycle; Shift+click = back), Details / Systems / Timeline panels,
    engine frames, row density, copy issues, AI folder, Settings, About. Tooltips name each option
    and its state; options that are on show an accent marker; the chevron hides the strip.
  - Responsive: on narrow windows row fields hide; on very narrow windows search moves to its own
    row and `#id` hides.
  - Compile errors/warnings show a `File.cs:line` chip instead of the full path; Unity's re-printed
    copies of compiler messages are not counted twice.
- **AI bridge** (file schema v1) — token-lean files an AI agent reads directly (no MCP server, no tool
  schemas in context), in `Logs/AIConsole/` (configurable):
  - `status.txt` — one line: state, mode, compile result, error/warning counts, last id (~30 tokens).
  - `errors.md` — compile errors plus error/warning groups (repeats collapsed, project frames only).
  - `session.jsonl` — one JSON object per entry for drill-down by `#id`; previous editor session kept
    as `session.prev.jsonl`; size-capped with rotation.
  - `AI-README.md` — how to read the files, cheapest first, with package / feature versions.
  - Captures logs from every thread, compiler errors/warnings, and Play / compile / clear markers.
  - Plain `Debug.Log("[Tag] …")` messages are tagged like ULog channels.
  - Writes are batched, atomic (no half-written files) and only happen when something changed.
- **Project Settings → Utility Tools → AI Console**:
  - Per-system on/off, minimum level and colour (team settings in `ProjectSettings/UtilityToolsConsole.asset`).
  - "Only for me" per-system overrides (personal, in `UserSettings/`), also from the system chips.
  - Release-build switches that edit the active target's scripting defines (keep logs, strip errors,
    silence `Debug.Log`).
  - AI file options (folder, info logs, digest size, rotation) and console history size.
  - "Scan project for systems" finds static `LogChannel` fields.
- Known limits: Unity's built-in Console keeps working alongside it; Error Pause applies on the next
  editor tick (not synchronously like Unity's); logs written very early in a domain reload, before the
  package's scripts load, are only in Unity's Console.

### ULog 1.0.0 — new
- `UtilityTools.Logging`: `LogChannel` per game system with its own colour, `Enabled` switch and `MinLevel`.
- `Verbose` / `Info` / `Warning` / `Error` / `Exception` / `Assert`, `InfoOnce` / `WarningOnce`,
  lazy `Func<string>` overloads, structured fields: `Spawn.Info("Spawned", ("count", 3))`.
- Verbose / Info / Warning / Assert calls are compiled out of release builds (arguments included).
  `UTILITYTOOLS_LOGS` keeps them; `UTILITYTOOLS_STRIP_ERRORS` also drops errors;
  `UTILITYTOOLS_SILENCE_UNITY_LOGS` filters plain `Debug.Log` / `LogWarning` in release builds.
- Extension points: `ULog.Logged` event (custom outputs) and `ULog.Formatter` (line format).
- Logging methods are hidden from stack traces (Unity 2022.2+), so double-click opens the caller.

### Package
- **Versioning**: each feature now has its own version (`UtilityToolsInfo`, `VERSIONING.md`), shown in
  **Project Settings → Utility Tools** (`Tools → Utility Tools → About`), with a *Copy versions* button for
  bug reports. Tests keep `package.json`, the code and `VERSIONING.md` in sync.
- "Logging Demo" sample; EditMode tests for the parser, AI formats, search language, ULog and versions.

## [1.2.0] - 2026-10-28

### Breaking
- **[Button] 2.0.0**: buttons are listed in declaration order instead of alphabetically; the editor also
  applies to ScriptableObjects and builds the inspector with UI Toolkit.
- **Scene Switcher 2.0.0**: on macOS the shortcuts use the physical `Ctrl` key instead of `Cmd`.
- **Editor Shortcuts 3.0.0**: Open Player Settings moved from `Ctrl+Shift+P` to `Ctrl+Shift+Alt+P`.

### [Button] 2.0.0
- Added: works on **ScriptableObjects** as well as MonoBehaviours.
- Added: finds private methods declared on base classes.
- Added: define `UTILITYTOOLS_DISABLE_BUTTON_EDITOR` to unregister the editors (e.g. when Odin or
  NaughtyAttributes already provides an editor for every object).
- Added: editor coroutines follow nested `IEnumerator`s and `CustomYieldInstruction`s
  (`WaitForSecondsRealtime`, `WaitUntil`, `WaitWhile`).
- Changed: the inspector is built with UI Toolkit (`CreateInspectorGUI`), so UI Toolkit-only property
  drawers render instead of showing "No GUI Implemented".
- Changed: buttons are listed in declaration order (base-class buttons first) instead of alphabetically.
- Changed: coroutines in Play Mode run via `StartCoroutine` on the target, so `WaitForSeconds` etc.
  behave normally. Edit Mode still uses the editor pump.
- Changed: the **Run** button for methods with parameters is always enabled; parameters you never
  expanded use their defaults.
- Fixed: no longer blocks multi-object editing — selecting several objects with the same script showed
  "Multi-object editing not supported" for every MonoBehaviour. Buttons on a multi-selection now run on
  every selected object (static methods run once).
- Fixed: array parameters — Add / Remove / Size and element edits were lost after the first frame.
  List/array UI state is now keyed per method and parameter, so methods no longer share one list.
- Fixed: overloaded methods no longer share parameter values or results.
- Fixed: enum parameters with default values no longer fail with an invalid cast.
- Fixed: fold-state, result and list caches are pruned when their objects are destroyed.

### Scene Switcher 2.0.0
- Changed: on macOS the shortcuts use the physical `Ctrl` key — `Cmd+Q` quit Unity (`Option` on macOS
  editors older than 2022.1). Windows/Linux are unchanged.
- Fixed: uses public APIs for the main-window position and the Build Settings button, so it no longer
  breaks if Unity renames internal types (Unity 6 opens Build Profiles).

### Editor Shortcuts 3.0.0
- Changed: **Open Player Settings** moved from `Ctrl+Shift+P` (Unity's built-in Pause shortcut) to
  `Ctrl+Shift+Alt+P`.
- Changed: **Open In Explorer** reveals a selected file (highlighted in its folder), works on Linux, and
  handles paths with spaces.
- Changed: `OpenPackageManager` and `OpenInExplorer` are static classes (they were never windows).

### Inspector Lock 1.0.1
- Changed: `InspectorLock` is a static class (it was never a window).

### Play From Bootstrap 1.0.1
- Fixed: if Unity refused to enter Play Mode (e.g. compile errors), the next normal Play also started
  from the bootstrap scene.

### Time Scale 1.0.1
- Fixed: the window repaints only when time scale / pause / play state changes instead of every frame.

### Asset Actions 1.1.0
- Added: **Copy GUID / Copy Asset Path** copy every selected asset (one per line).
- Fixed: Copy GUID / Copy Asset Path were enabled for scene objects and copied an empty string.

### Package
- "Button Demo" sample (Package Manager → Samples) and EditMode tests.

## [1.1.0] - 2026-06-01

### Breaking
- **Editor Shortcuts 2.0.0**: Open Player Settings moved from `Shift+Q` to `Ctrl+Shift+P`.

### [Button] 1.1.0
- Added: optional `tooltip`, `color` (hex tint), `confirm` and `confirmMessage` arguments.
- Added: coroutine support — methods returning `IEnumerator` run in the editor.
- Added: return values are displayed under the button.
- Added: static methods are supported.
- Fixed: param-value cache is pruned when owning objects are destroyed (was an unbounded leak).
- All existing `[Button(...)]` call sites keep working — new arguments are optional and appended.

### Editor Shortcuts 2.0.0
- Added: **Open Project Settings** (`Ctrl+Shift+O`) and **Clear All PlayerPrefs** (`Ctrl+Shift+D`).
- Changed: **Open Player Settings** deep-links to the Player tab and uses `Ctrl+Shift+P` (was a generic
  Project Settings open on `Shift+Q`).

### Play From Bootstrap 1.0.0 — new
- `F5` enters Play Mode from the first enabled Build scene.

### Time Scale 1.0.0 — new
- Live `Time.timeScale` slider, presets, Pause / Step.

### Asset Actions 1.0.0 — new
- Copy GUID, Copy Asset Path, Reserialize Selected, Force Recompile (`Ctrl+Shift+R`).

### Package
- UPM packaging: `package.json` + Runtime/Editor assembly definitions.

## [1.0.0] - 2026-06-01

### [Button] 1.0.0 — new
- `[Button]` attribute with parameter editor.

### Scene Switcher 1.0.0 — new
### Inspector Lock 1.0.0 — new
### Editor Shortcuts 1.0.0 — new
- Open Package Manager, Open In Explorer, Open Player Settings.
