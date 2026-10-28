# Utility Tools for Unity 🎮

A lightweight collection of Unity Editor productivity tools — an inspector **`[Button]`** attribute
plus a set of editor shortcuts for scene switching, settings, time control, and asset workflow.

Tested on Unity 6 (6000.1). APIs target Unity **2021.3+**.

---

## 📦 Structure

```
<repo root>/
├── package.json                 # UPM manifest
├── Runtime/                      # ships in builds (attribute only)
│   ├── ButtonAttribute.cs
│   └── UtilityTools.Runtime.asmdef   (Auto-Referenced)
├── Editor/                       # editor-only
│   ├── ButtonMethodEditor.cs     # draws [Button]s on every MonoBehaviour / ScriptableObject
│   ├── UtilityTools.cs           # scene switcher, inspector lock, settings, PlayerPrefs
│   ├── PlayFromBootstrap.cs
│   ├── TimeScaleWindow.cs
│   ├── AssetQuickActions.cs
│   └── UtilityTools.Editor.asmdef
├── Tests/Editor/                 # EditMode tests (see "Running the tests")
└── Samples~/ButtonDemo/          # importable from Package Manager → Samples
```

The Runtime assembly is **Auto-Referenced**, so scripts in the default `Assembly-CSharp` can use
`[Button]` without any extra setup.

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
https://github.com/PrinceGamedev/Utility-Tools-For-Unity-Development.git#v1.2.0
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
