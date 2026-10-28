# Versioning

Utility Tools has **two kinds of version**. Both follow [Semantic Versioning](https://semver.org/)
(`MAJOR.MINOR.PATCH`).

| | What it is | Where it lives |
|---|---|---|
| **Package version** | The release you install and pin (`…git#v1.4.0`) | `package.json`, git tag `v<version>`, `UtilityToolsInfo.PackageVersion`, `## [x.y.z]` headings in the changelog |
| **Feature version** | The history of one tool inside the package | `UtilityToolsInfo.Features`, `### Feature x.y.z` headings in the changelog, the About section of the settings page |

## Current features

| Feature | Version | Covers |
|---|---|---|
| AI Console | 1.1.0 | Console window, capture, AI bridge files, agent setup, commands, settings page |
| ULog | 1.0.0 | `ULog`, `LogChannel`, `LogRecord`, build-stripping defines |
| [Button] | 2.0.0 | `ButtonAttribute` and the inspector that draws it |
| Scene Switcher | 2.0.0 | `Ctrl+Q` / `Ctrl+W` overlay |
| Inspector Lock | 1.0.1 | `Ctrl+E` |
| Editor Shortcuts | 3.0.0 | Package Manager, Explorer, Project / Player Settings, Clear PlayerPrefs |
| Play From Bootstrap | 1.0.1 | `F5` |
| Time Scale | 1.0.1 | Time Scale window |
| Asset Actions | 1.1.0 | Copy GUID / Path, Reserialize, Force Recompile |

## Bumping a feature

For the feature you changed:

- **PATCH** — fixes only; behaviour users rely on is unchanged.
- **MINOR** — something new, backward compatible (new option, new menu item, new API member).
- **MAJOR** — something breaks for users of *that feature*: a public API removed or changed, a default
  shortcut changed, output format or on-disk format changed, behaviour people relied on changed.

AI Console also has `AIFormat.SchemaVersion` for its files. Bump it with the AI Console MAJOR version
when the AI files change incompatibly (keys renamed or removed); added keys don't need a schema bump.

## Bumping the package

Every release bumps the package version once, by the largest feature change in it:

| Largest feature change in the release | Package bump |
|---|---|
| Only PATCH | PATCH |
| Any MINOR, or a feature MAJOR | MINOR — list the feature MAJOR under **Breaking** in the changelog |
| A new feature added | MINOR |
| The package itself changes shape: minimum Unity version raised, a feature removed, assemblies renamed | MAJOR |

A feature MAJOR only breaks code that uses that one tool, so the package doesn't need a MAJOR bump;
people pin the package and read the changelog entry for the features they use.

## Release checklist

1. Bump the changed features in `Runtime/UtilityToolsInfo.cs` and in the table above.
2. Bump `PackageVersion` there **and** `version` in `package.json` (the `PackageVersionMatches` test fails
   if they differ).
3. In `CHANGELOG.md`, turn `## [Unreleased]` into `## [x.y.z] - YYYY-MM-DD` with one `### Feature x.y.z`
   section per changed feature.
4. Update the pinned example in `README.md`.
5. Merge to `main`, then tag: `git tag vX.Y.Z` and `git push origin vX.Y.Z`.
