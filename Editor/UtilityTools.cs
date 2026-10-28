using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using System.Reflection;

namespace UtilityTools
{
    #region Scene Switcher
    public class SceneSwitcherOverlay : EditorWindow
    {
        const string WINDOW_TITLE = "Scene Switcher";
        const float ROW_HEIGHT = 24f;
        const float PADDING = 10f;
        const float MAX_HEIGHT = 360f;
        const float WIDTH = 420f;

        static SceneSwitcherOverlay Instance;          // <- keep one window
        static int highlightedIndex = -1;
        static Vector2 scroll;

        static GUIStyle rowStyle, rowSelectedStyle, sceneNameStyle;
        static Texture2D sceneIcon;

        const string PREF_KEY = "SceneSwitcherOverlay.lastIndex";
        static bool closeOnActionKeyUp;               // close + open on Ctrl/Cmd release

        static EditorBuildSettingsScene[] EnabledScenes =>
            EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();

        // ---------- OPEN / SHOW ----------
        // Creates once; afterwards only updates selection (no reposition = no jitter)
        static void ShowOrCreate(int delta, bool fromShortcut)
        {
            if (Instance == null)
            {
                Instance = CreateInstance<SceneSwitcherOverlay>();
                Instance.titleContent = new GUIContent(WINDOW_TITLE);
                Instance.minSize = new Vector2(WIDTH, 120f);
                Instance.maxSize = new Vector2(WIDTH, MAX_HEIGHT);
                CenterOnMainWin(Instance);
                Instance.ShowUtility();  // utility window; doesn't steal full dock focus
                Instance.Focus();
                LoadStyles();
                highlightedIndex = -1;   // force init below
            }

            var scenes = EnabledScenes;
            if (scenes.Length > 0)
            {
                if (highlightedIndex < 0 || highlightedIndex >= scenes.Length)
                    highlightedIndex = Mathf.Clamp(EditorPrefs.GetInt(PREF_KEY, 0), 0, scenes.Length - 1);

                highlightedIndex = (highlightedIndex + delta + scenes.Length) % scenes.Length;
                EditorPrefs.SetInt(PREF_KEY, highlightedIndex);
            }
            else highlightedIndex = -1;

            closeOnActionKeyUp = fromShortcut; // arm close-on-release only if opened via shortcut
            Instance.Repaint();                // <- just repaint, no recreate
        }

        [MenuItem("Tools/Utility Tools/Scene Switcher UI")]
        static void ShowFromMenu() => ShowOrCreate(0, false);

        // The switcher binds the physical Ctrl key on every platform: on macOS, Action resolves to
        // Cmd and Cmd+Q quits Unity. ShortcutModifiers.Control only exists from 2022.1, so older
        // macOS editors fall back to Option (Alt).
#if UNITY_EDITOR_OSX && UNITY_2022_1_OR_NEWER
        const ShortcutModifiers SWITCH_MOD = ShortcutModifiers.Control;
#elif UNITY_EDITOR_OSX
        const ShortcutModifiers SWITCH_MOD = ShortcutModifiers.Alt;
#else
        const ShortcutModifiers SWITCH_MOD = ShortcutModifiers.Action; // = Ctrl on Windows/Linux
#endif

        // Shortcuts (Ctrl + Q/W) – only update; do NOT recreate window.
        // IDs are unchanged so existing user rebinds keep working.
        [Shortcut("Scenes/Show & Next (Ctrl+Q)", KeyCode.Q, SWITCH_MOD)]
        static void ShortcutNext() => ShowOrCreate(+1, true);

        [Shortcut("Scenes/Show & Prev (Ctrl+W)", KeyCode.W, SWITCH_MOD)]
        static void ShortcutPrev() => ShowOrCreate(-1, true);

        void OnEnable()
        {
            Instance = this;   // cache running window
            LoadStyles();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        static void LoadStyles()
        {
            if (rowStyle != null) return;

            rowStyle = new GUIStyle("RL Background")
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                padding = new RectOffset(8, 8, 0, 0)
            };
            rowSelectedStyle = new GUIStyle(rowStyle);
            rowSelectedStyle.normal.background = MakeTex(1, 1, new Color(0.24f, 0.48f, 0.90f, 0.35f));
            rowSelectedStyle.active = rowSelectedStyle.normal;

            sceneNameStyle = new GUIStyle(EditorStyles.label) { fontSize = 13 };
            sceneIcon = EditorGUIUtility.IconContent("SceneAsset Icon").image as Texture2D;
        }

        void OnGUI()
        {
            // If we opened via shortcut: releasing the switcher modifier should open highlighted scene
            if (closeOnActionKeyUp && Event.current.type == EventType.KeyUp && IsSwitchModifierKeyUp(Event.current))
            {
                if (TryOpenHighlighted()) Close();
                GUIUtility.ExitGUI();
                return;
            }

            var scenes = EnabledScenes;

            GUILayout.Space(PADDING);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(PADDING);
                GUILayout.Label("Enabled Scenes (" + SWITCH_KEY_NAME + "+Q next • " + SWITCH_KEY_NAME +
                                "+W prev • Enter open • Esc cancel)", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Build Settings", GUILayout.Height(20)))
                    EditorWindow.GetWindow(Type.GetType("UnityEditor.BuildPlayerWindow,UnityEditor"));
                GUILayout.Space(PADDING);
            }
            GUILayout.Space(6);

            if (scenes.Length == 0)
            {
                EditorGUILayout.HelpBox("No enabled scenes in Build Settings.", MessageType.Info);
                return;
            }

            float totalHeight = scenes.Length * ROW_HEIGHT + PADDING * 2 + 40f;
            maxSize = new Vector2(WIDTH, Mathf.Clamp(totalHeight, 160f, MAX_HEIGHT));

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < scenes.Length; i++)
                DrawRow(i, scenes[i]);
            EditorGUILayout.EndScrollView();

            HandleKeyboard(scenes);

            GUILayout.FlexibleSpace();
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(PADDING);
                GUILayout.Label("Tip: Rebind in Edit → Shortcuts… (search “Scenes/”).", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Space(PADDING);
            }
            GUILayout.Space(PADDING);
        }

#if UNITY_EDITOR_OSX && !UNITY_2022_1_OR_NEWER
        const string SWITCH_KEY_NAME = "Option";
#else
        const string SWITCH_KEY_NAME = "Ctrl";
#endif

        static bool IsSwitchModifierKeyUp(Event e)
        {
#if UNITY_EDITOR_OSX && !UNITY_2022_1_OR_NEWER
            return e.keyCode == KeyCode.LeftAlt || e.keyCode == KeyCode.RightAlt;
#else
            return e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl;
#endif
        }

        void DrawRow(int index, EditorBuildSettingsScene scene)
        {
            bool selected = index == highlightedIndex;
            var style = selected ? rowSelectedStyle : rowStyle;

            Rect r = EditorGUILayout.BeginHorizontal(style, GUILayout.Height(ROW_HEIGHT));
            {
                GUILayout.Label(sceneIcon, GUILayout.Width(20), GUILayout.Height(20));
                string name = Path.GetFileNameWithoutExtension(scene.path);
                GUILayout.Label(name, sceneNameStyle);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();

            if (!r.Contains(Event.current.mousePosition)) return;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { highlightedIndex = index; Repaint(); }
            else if (Event.current.type == EventType.MouseDown && Event.current.button == 2)
            { if (TryOpenByIndex(index)) Close(); Event.current.Use(); }
            else if (Event.current.type == EventType.MouseUp && Event.current.button == 0 && Event.current.clickCount == 2)
            { if (TryOpenByIndex(index)) Close(); Event.current.Use(); }
        }

        void HandleKeyboard(EditorBuildSettingsScene[] scenes)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (e.keyCode == KeyCode.DownArrow)
            { highlightedIndex = (highlightedIndex + 1) % scenes.Length; e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.UpArrow)
            { highlightedIndex = (highlightedIndex - 1 + scenes.Length) % scenes.Length; e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            { if (TryOpenHighlighted()) Close(); e.Use(); }
            else if (e.keyCode == KeyCode.Escape)
            { Close(); e.Use(); }
        }

        static bool TryOpenHighlighted() => TryOpenByIndex(highlightedIndex);

        static bool TryOpenByIndex(int index)
        {
            var scenes = EnabledScenes;
            if (scenes == null || index < 0 || index >= scenes.Length) return false;

            string path = scenes[index].path;
            string name = Path.GetFileNameWithoutExtension(path);

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false; // cancelled

            var opened = EditorSceneManager.OpenScene(path);
            if (opened.IsValid())
            {
                EditorPrefs.SetInt(PREF_KEY, index);
                Debug.Log($"[SceneSwitcher] Opened: {name}");
                return true;
            }
            return false;
        }

        static void CenterOnMainWin(EditorWindow wnd)
        {
            var main = GetEditorMainWindowPos();
            var size = new Vector2(WIDTH, Mathf.Min(MAX_HEIGHT, 360f));
            var pos = new Vector2(main.x + (main.width - size.x) * 0.5f, main.y + (main.height - size.y) * 0.3f);
            wnd.position = new Rect(pos, size);
        }

        static Rect GetEditorMainWindowPos()
        {
            var type = Type.GetType("UnityEditor.ContainerWindow,UnityEditor");
            if (type == null) return new Rect(100, 100, 800, 600);

            var showModeField = type.GetField("m_ShowMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var positionProperty = type.GetProperty("position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            var windows = Resources.FindObjectsOfTypeAll(type);
            foreach (var win in windows)
            {
                int showmode = (int)showModeField.GetValue(win);
                if (showmode == 4) return (Rect)positionProperty.GetValue(win, null);
            }
            return new Rect(100, 100, 800, 600);
        }

        static Texture2D MakeTex(int w, int h, Color c)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; ++i) pix[i] = c;
            var tex = new Texture2D(w, h) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixels(pix); tex.Apply();
            return tex;
        }
    }
    #endregion

    #region Inspector Lock
    public class InspectorLock : EditorWindow
    {
        [MenuItem("Tools/Utility Tools/Toggle Lock %e")]
        private static void ToggleViaMenu() => ToggleInspectorLock();

        private static void ToggleInspectorLock()
        {
            var inspectorType = Type.GetType("UnityEditor.InspectorWindow, UnityEditor");
            if (inspectorType == null)
            {
                Debug.LogError("InspectorLockToggle: Couldn't find UnityEditor.InspectorWindow type.");
                return;
            }

            // Prefer the focused Inspector (if any); otherwise toggle all Inspector windows.
            var focused = EditorWindow.focusedWindow;
            UnityEngine.Object[] targets;

            if (focused != null && inspectorType.IsInstanceOfType(focused))
            {
                targets = new UnityEngine.Object[] { focused };
            }
            else
            {
                targets = Resources.FindObjectsOfTypeAll(inspectorType);
                if (targets == null || targets.Length == 0)
                {
                    // Ensure at least one Inspector exists (opens one) then try again
                    EditorApplication.ExecuteMenuItem("Window/General/Inspector");
                    targets = Resources.FindObjectsOfTypeAll(inspectorType);
                    if (targets == null || targets.Length == 0)
                    {
                        Debug.LogWarning("InspectorLockToggle: No Inspector windows found.");
                        return;
                    }
                }
            }

            var isLockedProp = inspectorType.GetProperty("isLocked",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (isLockedProp == null)
            {
                Debug.LogError("InspectorLockToggle: 'isLocked' property not found on InspectorWindow.");
                return;
            }

            foreach (var t in targets)
            {
                var win = (EditorWindow)t;
                bool current = (bool)isLockedProp.GetValue(win, null);
                isLockedProp.SetValue(win, !current, null);
                win.Repaint();
                // Optional: tell you which one changed
                // Debug.Log($"Inspector lock → {(!current ? "ON" : "OFF")} ({win.titleContent.text})");
            }

            // Make sure everything reflects the new state
            EditorApplication.RepaintHierarchyWindow();
            EditorApplication.RepaintProjectWindow();
        }
    }
    #endregion

    #region Open Package Manager
    public class OpenPackageManager : EditorWindow
    {

        [MenuItem("Tools/Utility Tools/Open Package Manager #x")]
        private static void OpenViaMenu() => OpenPM();

        private static void OpenPM()
        {
            // First try the official menu path (works across most versions)
            if (EditorApplication.ExecuteMenuItem("Window/Package Manager"))
                return;

            // Fallback: try known internal window types across Unity versions
            var candidates = new[]
            {
            "UnityEditor.PackageManager.UI.Internal.PackageManagerWindow, UnityEditor", // 2020+
            "UnityEditor.PackageManager.UI.PackageManagerWindow, UnityEditor",          // older
        };

            foreach (var qn in candidates)
            {
                var t = Type.GetType(qn, throwOnError: false);
                if (t == null) continue;

                var win = GetWindow(t);  // EditorWindow.GetWindow(Type)
                if (win != null)
                {
                    win.Show();
                    win.Focus();
                    return;
                }
            }

            // If everything fails:
            Debug.LogWarning("Could not open Package Manager. Check Window ▶ Package Manager or Unity version.");
        }
    }
    #endregion

    #region Open In Explorer
    public class OpenInExplorer : EditorWindow
    {
        [MenuItem("Tools/Utility Tools/Open In Explorer %#e")] // Ctrl+Shift+E fallback
        private static void OpenViaMenu() => OpenSelected();

        private static void OpenSelected()
        {
            string path = null;

            // If something is selected in Project view → open its folder
            var obj = Selection.activeObject;
            if (obj != null)
            {
                path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    path = Path.GetFullPath(path);
                    if (File.Exists(path))
                        path = Path.GetDirectoryName(path); // file → folder
                }
            }

            // If nothing is selected, open project root
            if (string.IsNullOrEmpty(path))
                path = Path.GetFullPath(Application.dataPath + "/..");

#if UNITY_EDITOR_WIN
        System.Diagnostics.Process.Start("explorer.exe", path.Replace("/", "\\"));
#elif UNITY_EDITOR_OSX
        System.Diagnostics.Process.Start("open", path);
#else
            UnityEngine.Debug.LogWarning("OpenInExplorer not supported on this platform.");
#endif
        }
    }
    #endregion

    #region Open Project Settings
    public static class OpenProjectSettings
    {
        [MenuItem("Tools/Utility Tools/Open Project Settings %#o")] // Ctrl+Shift+O
        private static void Open() => EditorApplication.ExecuteMenuItem("Edit/Project Settings...");
    }
    #endregion

    #region Open Player Settings
    public static class OpenPlayerSettings
    {
        // Ctrl+Shift+Alt+P — Ctrl+Shift+P is Unity's built-in Pause shortcut.
        [MenuItem("Tools/Utility Tools/Open Player Settings %#&p")]
        private static void Open() => SettingsService.OpenProjectSettings("Project/Player");
    }
    #endregion

    #region Clear All PlayerPrefs
    public static class ClearAllPlayerPrefs
    {
        [MenuItem("Tools/Utility Tools/Clear All PlayerPrefs %#d")] // Ctrl+Shift+D
        private static void Clear()
        {
            if (!EditorUtility.DisplayDialog(
                    "Clear All PlayerPrefs",
                    "This permanently deletes every saved PlayerPref. This cannot be undone.",
                    "Delete All", "Cancel"))
                return;

            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("[UtilityTools] All PlayerPrefs cleared.");
        }
    }
    #endregion
}

