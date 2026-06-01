#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UtilityTools
{
    /// <summary>
    /// Small asset/workflow shortcuts: copy the selected asset's GUID or project path to the
    /// clipboard, force a script recompile, or force-reserialize the selected assets.
    /// </summary>
    public static class AssetQuickActions
    {
        // ---------- Copy GUID ----------
        [MenuItem("Assets/Utility Tools/Copy GUID", false, 20)]
        private static void CopyGuid()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            string guid = AssetDatabase.AssetPathToGUID(path);
            EditorGUIUtility.systemCopyBuffer = guid;
            Debug.Log($"[UtilityTools] Copied GUID: {guid}  ({path})");
        }

        [MenuItem("Assets/Utility Tools/Copy GUID", true)]
        private static bool CopyGuidValidate() => Selection.activeObject != null;

        // ---------- Copy Asset Path ----------
        [MenuItem("Assets/Utility Tools/Copy Asset Path", false, 21)]
        private static void CopyPath()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            EditorGUIUtility.systemCopyBuffer = path;
            Debug.Log($"[UtilityTools] Copied path: {path}");
        }

        [MenuItem("Assets/Utility Tools/Copy Asset Path", true)]
        private static bool CopyPathValidate() => Selection.activeObject != null;

        // ---------- Reserialize Selected ----------
        [MenuItem("Assets/Utility Tools/Reserialize Selected", false, 40)]
        private static void ReserializeSelected()
        {
            var guids = Selection.assetGUIDs;
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);

            AssetDatabase.ForceReserializeAssets(paths);
            Debug.Log($"[UtilityTools] Reserialized {paths.Length} asset(s).");
        }

        [MenuItem("Assets/Utility Tools/Reserialize Selected", true)]
        private static bool ReserializeSelectedValidate() => Selection.assetGUIDs.Length > 0;

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
