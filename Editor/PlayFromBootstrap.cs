#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UtilityTools
{
    /// <summary>
    /// Enter Play Mode from the first enabled Build Settings scene (the "bootstrap" / loader scene)
    /// no matter which scene you're editing. On returning to Edit Mode the start scene override is
    /// cleared, so normal Play behaviour is restored. Pressing the shortcut while playing stops play.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromBootstrap
    {
        static PlayFromBootstrap() => EditorApplication.playModeStateChanged += OnStateChanged;

        [MenuItem("Tools/Utility Tools/Play From Bootstrap _F5")] // F5
        private static void Play()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            string bootPath = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path;
            if (string.IsNullOrEmpty(bootPath))
            {
                Debug.LogWarning("[UtilityTools] No enabled scenes in Build Settings to boot from.");
                return;
            }

            var bootScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(bootPath);
            if (bootScene == null)
            {
                Debug.LogWarning($"[UtilityTools] Could not load boot scene at '{bootPath}'.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.playModeStartScene = bootScene;
            Debug.Log($"[UtilityTools] Play From Bootstrap → {bootScene.name}");
            EditorApplication.isPlaying = true;

            // If Unity refuses to enter Play Mode (e.g. compile errors) no state change fires, so the
            // override would leak into the next normal Play. Watch until the request settles.
            EditorApplication.delayCall += ClearIfPlayRefused;
        }

        private static void ClearIfPlayRefused()
        {
            if (EditorApplication.isPlaying) return;                // entered: EnteredEditMode clears later
            if (EditorApplication.isPlayingOrWillChangePlaymode)    // still transitioning: check next tick
                EditorApplication.delayCall += ClearIfPlayRefused;
            else
                EditorSceneManager.playModeStartScene = null;       // refused
        }

        private static void OnStateChanged(PlayModeStateChange change)
        {
            // Restore default behaviour once we're back in Edit Mode.
            if (change == PlayModeStateChange.EnteredEditMode)
                EditorSceneManager.playModeStartScene = null;
        }
    }
}
#endif
