#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UtilityTools
{
    /// <summary>
    /// A small window to scrub <see cref="Time.timeScale"/> live, jump to common presets, and
    /// pause / step a single frame while in Play Mode. Handy for inspecting fast animations.
    /// </summary>
    public class TimeScaleWindow : EditorWindow
    {
        private static readonly float[] Presets = { 0f, 0.25f, 0.5f, 1f, 2f, 4f, 8f };

        [MenuItem("Tools/Utility Tools/Time Scale")]
        private static void Open()
        {
            var win = GetWindow<TimeScaleWindow>("Time Scale");
            win.minSize = new Vector2(300, 110);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            EditorGUI.BeginChangeCheck();
            float scale = EditorGUILayout.Slider("Time Scale", Time.timeScale, 0f, 8f);
            if (EditorGUI.EndChangeCheck()) Time.timeScale = scale;

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (float p in Presets)
                {
                    bool active = Mathf.Approximately(Time.timeScale, p);
                    var prev = GUI.backgroundColor;
                    if (active) GUI.backgroundColor = new Color(0.24f, 0.48f, 0.90f, 1f);
                    if (GUILayout.Button(p.ToString("0.##") + "x")) Time.timeScale = p;
                    GUI.backgroundColor = prev;
                }
            }

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button(EditorApplication.isPaused ? "Resume" : "Pause", GUILayout.Height(24)))
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                if (GUILayout.Button("Step Frame", GUILayout.Height(24)))
                    EditorApplication.Step();
            }

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Pause / Step apply in Play Mode. Time Scale persists into play.",
                    MessageType.None);

            Repaint();
        }
    }
}
#endif
