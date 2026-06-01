# Changelog

All notable changes to this package are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

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
