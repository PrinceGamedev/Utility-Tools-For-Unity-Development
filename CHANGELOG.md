# Changelog

All notable changes to this package are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [1.1.1]

### Fixed
- `[Button]` editor no longer blocks multi-object editing — selecting several objects with the same
  script showed "Multi-object editing not supported" for every MonoBehaviour. Buttons on a
  multi-selection now run on every selected object (static methods run once).
- `[Button]` array parameters: Add / Remove / Size and element edits were lost after the first
  frame. List and array parameter UI state is now also keyed per method, so two methods with the
  same parameter name no longer share (and overwrite) one list.
- Scene Switcher on macOS used `Cmd+Q` / `Cmd+W`; `Cmd+Q` quits Unity. It now uses the physical
  `Ctrl` key (`Option` on macOS editors older than 2022.1). Windows/Linux are unchanged.

### Changed
- **Open Player Settings** moved from `Ctrl+Shift+P` (Unity's built-in Pause shortcut) to
  `Ctrl+Shift+Alt+P`.

## [1.1.0]

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

## [1.0.0]
- Initial set: `[Button]` attribute with parameter editor, Scene Switcher overlay, Inspector Lock,
  Open Package Manager, Open In Explorer.
