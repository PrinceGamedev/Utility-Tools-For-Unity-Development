# Utility Tools for Unity 🎮

A lightweight collection of Unity Editor productivity tools:

- **AI Console** — a modern replacement for Unity's Console that people *and* AI agents can read.
- **ULog** — per-system logging with on/off switches that compiles out of release builds.
- **`[Button]`** — clickable inspector buttons for any method.
- Editor shortcuts for scene switching, settings, time control and asset workflow.

Tested on Unity 6 (6000.1). APIs target Unity **2021.3+**.

---

## 📦 Structure

```
<repo root>/
├── package.json                 # UPM manifest
├── Runtime/                      # ships in builds
│   ├── ButtonAttribute.cs
│   ├── Logging/                  # ULog, LogChannel, LogRecord (calls stripped from release builds)
│   └── UtilityTools.Runtime.asmdef   (Auto-Referenced)
├── Editor/                       # editor-only
│   ├── Console/                  # AI Console: capture, AI bridge files, window, settings
│   ├── ButtonMethodEditor.cs     # draws [Button]s on every MonoBehaviour / ScriptableObject
│   ├── UtilityTools.cs           # scene switcher, inspector lock, settings, PlayerPrefs
│   ├── PlayFromBootstrap.cs
│   ├── TimeScaleWindow.cs
│   ├── AssetQuickActions.cs
│   └── UtilityTools.Editor.asmdef
├── Tests/Editor/                 # EditMode tests (see "Running the tests")
└── Samples~/                     # ButtonDemo, LoggingDemo — Package Manager → Samples
```

The Runtime assembly is **Auto-Referenced**, so scripts in the default `Assembly-CSharp` can use
`[Button]` and `ULog` without any extra setup.

---

## 🤖 AI Console

**`Tools → Utility Tools → AI Console`** (`Ctrl+Shift+Alt+C`, rebindable). It's a replacement for
Unity's Console with one capture pipeline and two readers:

| | People — the window | AI agents — files in `Logs/AIConsole/` |
|---|---|---|
| Overview | Type counts, system chips, timeline, error count in the tab title | `status.txt` (one line, ~30 tokens) |
| Problems | Pinned compile errors, coloured rows, Collapse `×count` | `errors.md`: grouped, newest first, project frames only |
| Details | Fields, context object, exception chain, folded engine frames, inline source | `session.jsonl`: one JSON object per entry |
| Shared reference | `#812` on every row | `"id":812` in every line |

**Window features**
- **Search:** `word`, `"phrase"`, `-exclude`, `type:error`, `sys:Network`, `-sys:Audio`, `#812`,
  `fp:<fingerprint>`, `file:Spawner.cs`, `/regex/`.
- **Filtering:** type toggles with live counts, and system chips (click to filter; right-click →
  *Mute for me*).
- **Toolbar:** Collapse, Follow, Error Pause, and sort by time / newest / severity / count / system /
  message. Clear on Play, the timeline, engine frames and row density are under `•••`.
- **Rows:** double-click opens the code. Right-click offers Copy, Copy for AI, Copy JSON, Show Similar
  and Mute. `Ctrl/Cmd+F` searches, `Ctrl/Cmd+C` copies, `Ctrl/Cmd+Shift+C` copies for AI.
- **What's captured:**
  - logs from every thread, including plain `Debug.Log` (a leading `[Tag]` becomes the system);
  - compiler errors and warnings;
  - Play Mode, compile and clear markers.
- History survives script reloads.

### Pointing an AI agent at it

The files are plain text, so an agent reads them with normal file tools. There's no MCP server and
no tool schemas sitting in the agent's context. Add this to your project's `CLAUDE.md` / `AGENTS.md`:

```markdown
## Unity Editor state
Read `Logs/AIConsole/status.txt` first (one line). If it shows errors or compile-failed, read
`Logs/AIConsole/errors.md`. For one entry, search `Logs/AIConsole/session.jsonl` for `"id":<n>`.
After editing scripts, wait until status shows `compile=ok` with a newer `compiled_at`.
Schema: `Logs/AIConsole/AI-README.md`.
```

Example `status.txt`:
```
state=errors mode=play compile=ok compiled_at=2026-10-28T12:01:33.120Z compile_errors=0 errors=2 warnings=5 last_id=812 unity=6000.1.17f1 updated=2026-10-28T12:04:11.300Z
```

Settings: **Project Settings → Utility Tools → AI Console**. This page holds:
- systems and their colours;
- release-build switches;
- the AI files' folder, digest size and rotation;
- the history size.

---

## 📝 ULog — logging by system

```csharp
using UtilityTools.Logging;

public static class Systems
{
    public static readonly LogChannel Spawn   = new LogChannel("Spawn", "#2ecc71");
    public static readonly LogChannel Network = new LogChannel("Network");   // colour generated from name
}

Systems.Spawn.Info("Spawned enemies", this, ("count", 3), ("wave", 2));   // `this` = context object
Systems.Network.Warning("Ping high", ("ms", 240));
Systems.Spawn.Error("Prefab missing");
Systems.Spawn.Exception(e, this);
Systems.Spawn.Info(() => ExpensiveDump());   // lazy: only built when Spawn is on
Systems.Spawn.Assert(wave > 0, "Wave must be positive");
Systems.Spawn.InfoOnce("Only the first time this line runs");
ULog.Info("Quick log without a system");
```

| | Editor | Development build | Release build |
|---|---|---|---|
| Verbose / Info / Warning / Assert / *Once | ✔ per-system switches | ✔ per-system switches | **Removed by the compiler**, arguments included |
| Error / Exception | ✔ always | ✔ always | ✔ kept (crash reports still work) |

Release behaviour switches (Project Settings → AI Console → *Release builds*, or Scripting Define Symbols):

| Define | Effect in release builds |
|---|---|
| `UTILITYTOOLS_LOGS` | Keep Verbose / Info / Warning / Assert |
| `UTILITYTOOLS_STRIP_ERRORS` | ULog errors and exceptions become no-ops |
| `UTILITYTOOLS_SILENCE_UNITY_LOGS` | Filter plain `Debug.Log` / `LogWarning` (third-party code); errors still pass |

**Turning systems on and off:**
- **Project Settings:** on/off, minimum level and colour per system. These are team settings, saved in
  `ProjectSettings/` and committed.
- **"Only for me" overrides:** per-person, saved in `UserSettings/`. Also available from the system chips
  in the window.
- **In code:** `Systems.Spawn.Enabled = false`, `ULog.MinLevel = LogLevel.Warning`.

Errors are always logged, even from a switched-off system.

**Extending it:**
- `ULog.Logged += record => …` adds your own outputs (crash reporter, in-game overlay, file in dev builds).
- `ULog.Formatter` changes how a line looks.

Double-clicking a ULog line in Unity's Console opens *your* code, not the logger (Unity 2022.2+).

Import **Logging Demo** from **Package Manager → Utility Tools → Samples** to try every feature.

---

## ✨ The `[Button]` attribute

Mark any method on a `MonoBehaviour` or `ScriptableObject` with `[Button]` to get a clickable button
in its Inspector.

```csharp
using UtilityTools;

public class Demo : MonoBehaviour
{
    [Button]                                   // label = method name
    void DoThing() { }

    [Button("Reset Score", ButtonAttribute.Mode.PlayModeOnly)]
    void ResetScore() { }

    [Button("Rebuild", position: ButtonAttribute.Position.Top, tooltip: "Rebuilds the grid")]
    void Rebuild() { }

    [Button("Wipe Save", color: "#c0392b", confirm: true,
            confirmMessage: "Delete the local save file?")]
    void WipeSave() { }

    // Parameters get an auto-generated arg editor (primitives, enums, Unity math types,
    // Color/Curve/Gradient/LayerMask, Object refs, arrays, Lists, and [Serializable] classes).
    [Button("Spawn")]
    void Spawn(int count, GameObject prefab, List<Vector3> points) { }

    // Coroutines: StartCoroutine in Play Mode; in Edit Mode an editor pump advances them once per
    // tick (nested IEnumerators and WaitForSecondsRealtime / WaitUntil work, WaitForSeconds doesn't wait).
    [Button("Animate")]
    IEnumerator Animate() { yield return null; }

    // Return values are displayed under the button.
    [Button("Count Cells")]
    int CountCells() => 42;
}
```

**Options** (all optional, named args recommended):

| Param | Type | Purpose |
|-------|------|---------|
| `label` | `string` | Button text (defaults to method name) |
| `mode` | `Mode` | `Always` / `EditModeOnly` / `PlayModeOnly` |
| `position` | `Position` | `Top` or `Bottom` of the inspector (default `Bottom`) |
| `tooltip` | `string` | Hover tooltip |
| `color` | `string` | Hex tint, e.g. `"#c0392b"` or `"e74c3c"` |
| `confirm` | `bool` | Show a confirmation dialog before running |
| `confirmMessage` | `string` | Custom confirmation text |

- Buttons appear in declaration order, base-class buttons first. Private methods on base classes
  are included; static methods are supported.
- `[Button]` is `AllowMultiple`, so a method can appear in more than one group.
- **Multi-selection:** buttons run on every selected object (static methods run once), using the
  parameter values shown in the Inspector.
- **Parameters** you never expand run with their defaults (the C# default value if declared).
  Values are cached per object for the editor session and pruned when objects are destroyed.
- Invokes are wrapped in `Undo` and `SetDirty`.
- The Inspector is built with UI Toolkit, so UI Toolkit-only property drawers keep working.
- **Conflicts:** this registers a fallback editor for every MonoBehaviour and ScriptableObject. Your own
  `[CustomEditor]`s for specific types still win. If another package (Odin, NaughtyAttributes, …) does
  the same thing, add `UTILITYTOOLS_DISABLE_BUTTON_EDITOR` to **Player → Scripting Define Symbols** to
  turn this one off.

Import **Button Demo** from **Package Manager → Utility Tools → Samples** for a working example.

---

## 🧰 Editor tools

All commands live under **`Tools → Utility Tools`** (and **`Assets → Utility Tools`** for asset actions).

| Tool | Shortcut | Notes |
|------|----------|-------|
| AI Console | `Ctrl+Shift+Alt+C` | See **AI Console** above |
| Scene Switcher UI | `Ctrl+Q` next · `Ctrl+W` prev | Overlay of enabled Build scenes; ↑/↓, Enter, Esc, release-to-open. Physical `Ctrl` on macOS too (`Cmd+Q` would quit Unity); `Option` on macOS editors older than 2022.1 |
| Toggle Inspector Lock | `Ctrl+E` | Locks focused inspector, or all if none focused |
| Open Package Manager | `Shift+X` | |
| Open In Explorer / Finder | `Ctrl+Shift+E` | Reveals the selected file, opens the selected folder, or the project root |
| Open Project Settings | `Ctrl+Shift+O` | |
| Open Player Settings | `Ctrl+Shift+Alt+P` | Deep-links to the Player tab (`Ctrl+Shift+P` is Unity's Pause) |
| Clear All PlayerPrefs | `Ctrl+Shift+D` | Confirmation required |
| Play From Bootstrap | `F5` | Enters Play Mode from the first enabled Build scene; restores normal play on exit |
| Force Recompile | `Ctrl+Shift+R` | |
| Time Scale | — | Window: slider + presets + Pause/Step |
| Copy GUID | — | `Assets →` context menu; one per line for multi-selection |
| Copy Asset Path | — | `Assets →` context menu; one per line for multi-selection |
| Reserialize Selected | — | `Assets →` context menu |

Shortcuts can be rebound via **Edit → Shortcuts…** (search `Scenes/` for the switcher) or by editing
the `[MenuItem]` hotkeys.

---

## 📥 Installation

### Option A — Package Manager (Git URL) ✅ recommended

In Unity: **Window → Package Manager → `+` → Add package from git URL…**, then paste:

```
https://github.com/PrinceGamedev/Utility-Tools-For-Unity-Development.git
```

To pin a specific version, append a release tag (see the repo's
[Tags](https://github.com/PrinceGamedev/Utility-Tools-For-Unity-Development/tags)) or a commit hash:

```
https://github.com/PrinceGamedev/Utility-Tools-For-Unity-Development.git#v1.3.0
```

### Option B — Manual

Download/clone the repo and copy the package folder into your project's `Assets/` (or `Packages/`).

---

## 🧪 Running the tests

Package tests only compile when the consuming project marks the package as testable. Add this to
your project's `Packages/manifest.json`:

```json
"testables": [ "com.gameyogi.utilitytools" ]
```

Then run them from **Window → General → Test Runner → EditMode**.

---

## 📜 License

MIT — see [LICENSE.md](LICENSE.md).
