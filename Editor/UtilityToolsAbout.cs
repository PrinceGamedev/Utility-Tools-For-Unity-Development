#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UtilityTools
{
    /// <summary>Project Settings → Utility Tools: package and feature versions, docs links.</summary>
    internal static class UtilityToolsAbout
    {
        private const string SettingsPath = "Project/Utility Tools";

        [MenuItem("Tools/Utility Tools/About", false, 1000)]
        private static void Open() => SettingsService.OpenProjectSettings(SettingsPath);

        [SettingsProvider]
        private static SettingsProvider Create() =>
            new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Utility Tools",
                guiHandler = _ => Draw(),
                keywords = new HashSet<string>(new[] { "version", "about", "changelog", "utility tools" })
            };

        private static void Draw()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Utility Tools " + UtilityToolsInfo.PackageVersion, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Package version = the release you install. Each feature also has its own version, bumped only when " +
                "that feature changes (see VERSIONING.md).", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Features", EditorStyles.boldLabel);
            foreach (var f in UtilityToolsInfo.AllFeatures)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(f.Key, GUILayout.Width(180));
                    EditorGUILayout.SelectableLabel(f.Value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var doc in new[] { "README.md", "CHANGELOG.md", "VERSIONING.md" })
                    if (GUILayout.Button(doc, GUILayout.Width(120))) OpenDoc(doc);
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Copy versions", GUILayout.Width(120)))
                EditorGUIUtility.systemCopyBuffer = VersionsText();
        }

        /// <summary>"Utility Tools 1.3.0 (AI Console 1.0.0, ULog 1.0.0, …)" for bug reports.</summary>
        internal static string VersionsText()
        {
            var parts = new List<string>();
            foreach (var f in UtilityToolsInfo.AllFeatures) parts.Add(f.Key + " " + f.Value);
            return "Utility Tools " + UtilityToolsInfo.PackageVersion + " (" + string.Join(", ", parts) + ")";
        }

        private static void OpenDoc(string file)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UtilityToolsAbout).Assembly);
            string path = info != null ? Path.Combine(info.resolvedPath, file) : null;
            if (path != null && File.Exists(path)) Application.OpenURL(new System.Uri(path).AbsoluteUri);
            else Application.OpenURL("https://github.com/PrinceGamedev/Utility-Tools-For-Unity-Development/blob/main/" + file);
        }
    }
}
#endif
