#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UtilityTools.Logging;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// Puts the "Logs in builds" choice for this kind of build (Development or Release) into the player as
    /// a Resources asset that <see cref="LogFilter"/> reads on start. The asset lives in a temporary folder
    /// that is removed when the build ends.
    /// </summary>
    internal sealed class LogFilterBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        internal const string Folder = "Assets/UtilityToolsBuildTemp";
        private const string ResourcesFolder = Folder + "/Resources";

        public int callbackOrder => 0;

        internal static BuildLogRules RulesFor(bool development)
        {
            var s = ConsoleProjectSettings.instance;
            return development ? s.devBuildLogs : s.releaseBuildLogs;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            RemoveTempFolder();
            bool development = (report.summary.options & BuildOptions.Development) != 0;
            var rules = RulesFor(development);
            if (rules == null || !rules.filter) return;

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UtilityToolsBuildTemp");
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder(Folder, "Resources");

            var data = ScriptableObject.CreateInstance<LogFilterData>();
            data.tags = rules.tags.ToArray();
            data.keepUntagged = rules.keepUntagged;
            data.keepErrors = rules.keepErrors;
            AssetDatabase.CreateAsset(data, ResourcesFolder + "/" + LogFilterData.ResourceName + ".asset");
            AssetDatabase.SaveAssets();

            Debug.Log($"[UtilityTools] {(development ? "Development" : "Release")} build keeps these logs: {rules.Describe()}.");
        }

        public void OnPostprocessBuild(BuildReport report) => RemoveTempFolder();

        // A failed build skips OnPostprocessBuild; tidy up on the next script reload instead.
        [InitializeOnLoadMethod]
        private static void RemoveLeftovers()
        {
            EditorApplication.delayCall += () =>
            {
                if (!BuildPipeline.isBuildingPlayer) RemoveTempFolder();
            };
        }

        private static void RemoveTempFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }
    }
}
#endif
