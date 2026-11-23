#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UtilityTools.AIConsole
{
    /// <summary><c>scene</c> lists the open scenes; <c>scene open &lt;path&gt; [force]</c> opens one.</summary>
    internal sealed class SceneVerb : IAgentVerb
    {
        public string Name => "scene";
        public string Usage => "scene [open <path> [force]]";
        public bool ReadOnly => false;

        public AgentVerbResult Start(AgentVerbCall call)
        {
            string[] words = call.Arg.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return AgentVerbResult.Success(DescribeOpenScenes());
            if (words[0] != "open" || words.Length < 2) return AgentVerbResult.Failure("usage: " + Usage);

            bool force = words.Length > 2 && words[words.Length - 1] == "force";
            string path = string.Join(" ", words, 1, words.Length - (force ? 2 : 1));
            return Open(path, force);
        }

        public AgentVerbResult Check(AgentVerbCall call) => AgentVerbResult.Failure("interrupted");

        private static AgentVerbResult Open(string path, bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return AgentVerbResult.Failure("stop Play Mode first");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return AgentVerbResult.Failure("no scene at " + path);

            var dirty = DirtyScenes();
            if (dirty.Count > 0 && !force)
                return AgentVerbResult.Failure("unsaved changes in " + string.Join(", ", dirty) +
                                               " — save them, or `scene open <path> force` to discard");

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return AgentVerbResult.Success("opened " + path + (dirty.Count > 0 ? " (discarded changes in " + string.Join(", ", dirty) + ")" : ""));
        }

        private static List<string> DirtyScenes()
        {
            var list = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) list.Add(string.IsNullOrEmpty(scene.path) ? "(untitled)" : scene.path);
            }

            return list;
        }

        private static string DescribeOpenScenes()
        {
            var parts = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                string name = string.IsNullOrEmpty(scene.path) ? "(untitled)" : scene.path;
                parts.Add(name + (scene.isDirty ? " (unsaved)" : "") + (scene == SceneManager.GetActiveScene() ? " (active)" : ""));
            }

            return "open: " + string.Join(", ", parts);
        }
    }
}
#endif
