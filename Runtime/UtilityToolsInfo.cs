using System.Collections.Generic;

namespace UtilityTools
{
    /// <summary>
    /// Version of the whole package and of each feature inside it (rules in VERSIONING.md).
    /// The package version must match package.json — a test enforces it.
    /// </summary>
    public static class UtilityToolsInfo
    {
        /// <summary>The installable release (package.json, git tag "v" + version).</summary>
        public const string PackageVersion = "1.4.0";

        /// <summary>Each feature's own SemVer; bumped only when that feature changes.</summary>
        public static class Features
        {
            public const string AIConsole = "1.1.0";
            public const string ULog = "1.0.0";
            public const string Button = "2.0.0";
            public const string SceneSwitcher = "2.0.0";
            public const string InspectorLock = "1.0.1";
            public const string EditorShortcuts = "3.0.0";
            public const string PlayFromBootstrap = "1.0.1";
            public const string TimeScale = "1.0.1";
            public const string AssetActions = "1.1.0";
        }

        /// <summary>Display name → version for every feature, in menu order.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> AllFeatures { get; } = new[]
        {
            new KeyValuePair<string, string>("AI Console", Features.AIConsole),
            new KeyValuePair<string, string>("ULog", Features.ULog),
            new KeyValuePair<string, string>("[Button]", Features.Button),
            new KeyValuePair<string, string>("Scene Switcher", Features.SceneSwitcher),
            new KeyValuePair<string, string>("Inspector Lock", Features.InspectorLock),
            new KeyValuePair<string, string>("Editor Shortcuts", Features.EditorShortcuts),
            new KeyValuePair<string, string>("Play From Bootstrap", Features.PlayFromBootstrap),
            new KeyValuePair<string, string>("Time Scale", Features.TimeScale),
            new KeyValuePair<string, string>("Asset Actions", Features.AssetActions)
        };
    }
}
