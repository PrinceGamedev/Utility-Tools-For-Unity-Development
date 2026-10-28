#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UtilityTools
{
    /// <summary>
    /// Small asset/workflow shortcuts: copy the selected assets' GUIDs or project paths to the
    /// clipboard (one per line), force a script recompile, or force-reserialize the selected assets.
    /// </summary>
    public static class AssetQuickActions
    {
        // Selection.assetGUIDs only contains project assets (incl. folders), never scene objects.
        private static bool HasAssetSelection() => Selection.assetGUIDs.Length > 0;

        // ---------- Copy GUID ----------
        [MenuItem("Assets/Utility Tools/Copy GUID", false, 20)]
        private static void CopyGuid()
        {
            var guids = Selection.assetGUIDs;
            EditorGUIUtility.systemCopyBuffer = string.Join("\n", guids);
            Debug.Log(guids.Length == 1
                ? $"[UtilityTools] Copied GUID: {guids[0]}  ({AssetDatabase.GUIDToAssetPath(guids[0])})"
                : $"[UtilityTools] Copied {guids.Length} GUIDs.");
        }

        [MenuItem("Assets/Utility Tools/Copy GUID", true)]
        private static bool CopyGuidValidate() => HasAssetSelection();

        // ---------- Copy Asset Path ----------
        [MenuItem("Assets/Utility Tools/Copy Asset Path", false, 21)]
        private static void CopyPath()
        {
            var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).ToArray();
            EditorGUIUtility.systemCopyBuffer = string.Join("\n", paths);
            Debug.Log(paths.Length == 1
                ? $"[UtilityTools] Copied path: {paths[0]}"
                : $"[UtilityTools] Copied {paths.Length} paths.");
        }

        [MenuItem("Assets/Utility Tools/Copy Asset Path", true)]
        private static bool CopyPathValidate() => HasAssetSelection();

        // ---------- Reserialize Selected ----------
        [MenuItem("Assets/Utility Tools/Reserialize Selected", false, 40)]
        private static void ReserializeSelected()
        {
            var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).ToArray();
            AssetDatabase.ForceReserializeAssets(paths);
            Debug.Log($"[UtilityTools] Reserialized {paths.Length} asset(s).");
        }

        [MenuItem("Assets/Utility Tools/Reserialize Selected", true)]
        private static bool ReserializeSelectedValidate() => HasAssetSelection();

        // ---------- Force Recompile ----------
        [MenuItem("Tools/Utility Tools/Force Recompile %#r")] // Ctrl+Shift+R
        private static void ForceRecompile()
        {
            Debug.Log("[UtilityTools] Requesting script recompilation…");
            CompilationPipeline.RequestScriptCompilation();
        }
    }
}
#endif
