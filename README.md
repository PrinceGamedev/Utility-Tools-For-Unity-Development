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
└── Editor/                       # editor-only
    ├── ButtonMethodEditor.cs     # draws [Button]s on every MonoBehaviour
    ├── UtilityTools.cs           # scene switcher, inspector lock, settings, PlayerPrefs
    ├── PlayFromBootstrap.cs
    ├── TimeScaleWindow.cs
    ├── AssetQuickActions.cs
    └── UtilityTools.Editor.asmdef
```

The Runtime assembly is **Auto-Referenced**, so scripts in the default `Assembly-CSharp` can use
`[Button]` without any extra setup.

---

## ✨ The `[Button]` attribute

Mark any method on a `MonoBehaviour` with `[Button]` to get a clickable button in its Inspector.

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

    // Coroutines run in the editor (advanced once per editor tick).
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

Static methods are supported. `[Button]` is `AllowMultiple`, so a method can appear in more than
one group. Parameter values are cached per object during the editor session; the cache is pruned
automatically when objects are destroyed. Invokes are wrapped in `Undo` and `SetDirty`.

---

## 🧰 Editor tools

All commands live under **`Tools → Utility Tools`** (and **`Assets → Utility Tools`** for asset actions).

| Tool | Shortcut | Notes |
|------|----------|-------|
| Scene Switcher UI | `Ctrl/Cmd+Q` next · `Ctrl/Cmd+W` prev | Overlay of enabled Build scenes; ↑/↓, Enter, Esc, release-to-open |
| Toggle Inspector Lock | `Ctrl+E` | Locks focused inspector, or all if none focused |
| Open Package Manager | `Shift+X` | |
| Open In Explorer / Finder | `Ctrl+Shift+E` | Selected asset's folder, or project root |
| Open Project Settings | `Ctrl+Shift+O` | |
| Open Player Settings | `Ctrl+Shift+P` | Deep-links to the Player tab |
| Clear All PlayerPrefs | `Ctrl+Shift+D` | Confirmation required |
| Play From Bootstrap | `F5` | Enters Play Mode from the first enabled Build scene; restores normal play on exit |
| Force Recompile | `Ctrl+Shift+R` | |
| Time Scale | — | Window: slider + presets + Pause/Step |
| Copy GUID | — | `Assets →` context menu |
| Copy Asset Path | — | `Assets →` context menu |
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

To pin a specific version, append a release tag:

```
https://github.com/PrinceGamedev/Utility-Tools-For-Unity-Development.git#v1.1.0
```

### Option B — Manual

Download/clone the repo and copy the package folder into your project's `Assets/` (or `Packages/`).

---

## 📜 License

MIT — see [LICENSE.md](LICENSE.md).
