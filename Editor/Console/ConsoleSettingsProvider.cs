#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UtilityTools.Logging;

namespace UtilityTools.AIConsole
{
    /// <summary>Project Settings → Utility Tools → AI Console.</summary>
    internal static class ConsoleSettingsProvider
    {
        private const string DefineLogs = "UTILITYTOOLS_LOGS";
        private const string DefineStripErrors = "UTILITYTOOLS_STRIP_ERRORS";
        private const string DefineSilence = "UTILITYTOOLS_SILENCE_UNITY_LOGS";

        private static Vector2 s_Scroll;

        [SettingsProvider]
        private static SettingsProvider Create() =>
            new SettingsProvider("Project/Utility Tools/AI Console", SettingsScope.Project)
            {
                label = "AI Console",
                guiHandler = _ => Draw(),
                keywords = new HashSet<string>(new[]
                {
                    "log", "logging", "ULog", "channel", "system", "console", "AI", "strip", "release", "define"
                })
            };

        private static void Draw()
        {
            var project = ConsoleProjectSettings.instance;
            var user = ConsoleUserSettings.instance;

            s_Scroll = EditorGUILayout.BeginScrollView(s_Scroll);
            EditorGUIUtility.labelWidth = 200;

            EditorGUI.BeginChangeCheck();
            DrawLogging(project);
            bool projectChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            DrawSystems(project, user, out bool userChanged);
            projectChanged |= EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            DrawTags(project);
            projectChanged |= EditorGUI.EndChangeCheck();

            DrawBuild();

            EditorGUI.BeginChangeCheck();
            DrawCapture(project);
            DrawAI(project);
            projectChanged |= EditorGUI.EndChangeCheck();

            if (projectChanged) project.SaveAndNotify();
            if (userChanged) user.SaveAndNotify();

            EditorGUILayout.EndScrollView();
        }

        // ---------- Logging ----------
        private static void DrawLogging(ConsoleProjectSettings s)
        {
            Header("Logging", "Team settings, saved in ProjectSettings/UtilityToolsConsole.asset — commit this file.");
            s.globalMinLevel = (LogLevel)EditorGUILayout.EnumPopup(
                new GUIContent("Minimum level (all systems)", "Logs below this level are dropped. Errors always pass."),
                s.globalMinLevel);
            s.richTextTags = EditorGUILayout.Toggle(
                new GUIContent("Colour tags in Unity Console", "Wrap [System] in its colour in Unity's own Console (Editor only)."),
                s.richTextTags);
        }

        // ---------- Systems ----------
        private static void DrawSystems(ConsoleProjectSettings s, ConsoleUserSettings user, out bool userChanged)
        {
            userChanged = false;
            Header("Systems", "Every LogChannel is listed once it has been used. Errors are logged even when a system is off.");

            var live = LogChannel.All.Where(c => c != ULog.Default).Select(c => c.Name);
            var names = live.Concat(s.channels.Select(c => c.name)).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            if (names.Count == 0)
            {
                EditorGUILayout.HelpBox("No systems yet. Declare a channel, e.g.\n" +
                                        "public static readonly LogChannel Spawn = new LogChannel(\"Spawn\", \"#2ecc71\");\n" +
                                        "then enter Play Mode or press Scan.", MessageType.Info);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("System", EditorStyles.miniBoldLabel, GUILayout.Width(150));
                    GUILayout.Label("On", EditorStyles.miniBoldLabel, GUILayout.Width(28));
                    GUILayout.Label("Min level", EditorStyles.miniBoldLabel, GUILayout.Width(90));
                    GUILayout.Label("Colour", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                    GUILayout.Label(new GUIContent("Only for me", "Personal override, saved in UserSettings/"),
                        EditorStyles.miniBoldLabel, GUILayout.Width(90));
                }

                string remove = null;
                foreach (var name in names)
                {
                    var setting = s.Find(name);
                    bool isLive = LogChannel.All.Any(c => c.Name == name);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(new GUIContent(name, isLive ? name : name + " (not loaded in this session)"),
                            isLive ? EditorStyles.label : EditorStyles.miniLabel, GUILayout.Width(150));

                        bool enabled = setting?.enabled ?? true;
                        bool newEnabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(28));

                        var level = setting?.minLevel ?? LogLevel.Verbose;
                        var newLevel = (LogLevel)EditorGUILayout.EnumPopup(level, GUILayout.Width(90));

                        ColorUtility.TryParseHtmlString(ChannelConfig.ColorFor(name), out var color);
                        var newColor = EditorGUILayout.ColorField(GUIContent.none, color, false, false, false, GUILayout.Width(70));

                        if (newEnabled != enabled || newLevel != level || newColor != color)
                        {
                            var edit = s.GetOrAdd(name);
                            edit.enabled = newEnabled;
                            edit.minLevel = newLevel;
                            if (newColor != color) edit.color = "#" + ColorUtility.ToHtmlStringRGB(newColor);
                        }

                        var mode = user.GetOverride(name);
                        var newMode = (ChannelOverride)EditorGUILayout.EnumPopup(mode, GUILayout.Width(90));
                        if (newMode != mode)
                        {
                            user.SetOverride(name, newMode);
                            userChanged = true;
                        }

                        if (!isLive && setting != null && GUILayout.Button(new GUIContent("×", "Forget this system"), GUILayout.Width(22)))
                            remove = name;
                    }
                }

                if (remove != null)
                {
                    s.channels.RemoveAll(c => c.name == remove);
                    GUI.changed = true;
                }
            }

            if (GUILayout.Button(new GUIContent("Scan project for systems",
                    "Finds static LogChannel fields in your assemblies (runs their static initializers)."), GUILayout.Width(200)))
                ScanForChannels();
        }

        // ---------- Tags ----------
        private static string s_NewTag = "";
        private static Color s_NewTagColor = new Color(0.35f, 0.63f, 1f);

        private static void DrawTags(ConsoleProjectSettings s)
        {
            Header("Tags", "Custom tag colours, e.g. UI → blue. Logs get tags from [UI] prefixes, " +
                           "Tagged(\"UI\") and their system. Also editable in the AI Console (Tags bar → Edit tags).");

            string remove = null;
            foreach (var tag in s.tags)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    ColorUtility.TryParseHtmlString(tag.color, out var c);
                    var next = EditorGUILayout.ColorField(GUIContent.none, c, false, false, false, GUILayout.Width(70));
                    if (next != c) tag.color = "#" + ColorUtility.ToHtmlStringRGB(next);
                    GUILayout.Label(tag.name, GUILayout.Width(150));
                    if (GUILayout.Button(new GUIContent("×", "Remove this tag colour"), GUILayout.Width(22))) remove = tag.name;
                }
            }

            if (remove != null)
            {
                s.tags.RemoveAll(t => t.name == remove);
                GUI.changed = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                s_NewTagColor = EditorGUILayout.ColorField(GUIContent.none, s_NewTagColor, false, false, false, GUILayout.Width(70));
                s_NewTag = EditorGUILayout.TextField(s_NewTag, GUILayout.Width(150));
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(s_NewTag)))
                {
                    if (GUILayout.Button("Add tag", GUILayout.Width(80)))
                    {
                        s.SetTagColor(s_NewTag.Trim().Trim('[', ']'), "#" + ColorUtility.ToHtmlStringRGB(s_NewTagColor));
                        s_NewTag = "";
                        GUI.changed = true;
                    }
                }
            }
        }

        private static void ScanForChannels()
        {
            int found = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (n.StartsWith("Unity", StringComparison.Ordinal) || n.StartsWith("System", StringComparison.Ordinal) ||
                    n.StartsWith("Mono", StringComparison.Ordinal) || n == "mscorlib" || n == "netstandard" ||
                    n.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Bee.", StringComparison.Ordinal) ||
                    n == "UtilityTools.Runtime")
                    continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                {
                    if (type.ContainsGenericParameters) continue;
                    foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (field.FieldType != typeof(LogChannel)) continue;
                        try
                        {
                            if (field.GetValue(null) != null) found++;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[UtilityTools] Could not read {type.FullName}.{field.Name}: {e.Message}");
                        }
                    }
                }
            }

            ChannelConfig.ApplyAll();
            Debug.Log($"[UtilityTools] Found {found} LogChannel field(s).");
        }

        // ---------- Build ----------
        private static void DrawBuild()
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            Header("Release builds — " + target.TargetName,
                "Verbose / Info / Warning / Assert are removed from release builds by the compiler. " +
                "These switches edit the Scripting Define Symbols of the active build target (scripts recompile).");

            DefineToggle(target, DefineLogs, "Keep all logs in release builds",
                "Defines UTILITYTOOLS_LOGS: Verbose/Info/Warning/Assert stay in non-development builds.");
            DefineToggle(target, DefineStripErrors, "Strip errors in release builds",
                "Defines UTILITYTOOLS_STRIP_ERRORS: ULog errors/exceptions become no-ops in non-development builds.");
            DefineToggle(target, DefineSilence, "Silence Debug.Log in release builds",
                "Defines UTILITYTOOLS_SILENCE_UNITY_LOGS: plain Debug.Log/LogWarning (e.g. third-party code) are filtered " +
                "in non-development builds; errors and exceptions still pass.");
        }

        private static void DefineToggle(NamedBuildTarget target, string define, string label, string tooltip)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';')
                .Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
            bool on = defines.Contains(define);
            bool next = EditorGUILayout.Toggle(new GUIContent(label, tooltip), on);
            if (next == on) return;

            if (next) defines.Add(define);
            else defines.Remove(define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        }

        // ---------- Capture ----------
        private static void DrawCapture(ConsoleProjectSettings s)
        {
            Header("Console", "Personal options (Error Pause, Clear on Play, Collapse…) live in the AI Console toolbar.");
            s.maxEntries = Mathf.Clamp(EditorGUILayout.DelayedIntField(
                new GUIContent("Max entries in session", "Oldest entries are trimmed beyond this. 500 – 200000."),
                s.maxEntries), 500, 200000);
        }

        // ---------- AI ----------
        private static void DrawAI(ConsoleProjectSettings s)
        {
            Header("AI files", "Token-lean files an AI agent reads directly: status.txt → errors.md → session.jsonl.");
            s.aiFilesEnabled = EditorGUILayout.Toggle("Write AI files", s.aiFilesEnabled);
            using (new EditorGUI.DisabledScope(!s.aiFilesEnabled))
            {
                s.aiFolder = EditorGUILayout.DelayedTextField(
                    new GUIContent("Folder (project-relative)", "Logs/ is git-ignored in Unity's default .gitignore."), s.aiFolder);
                s.aiIncludeInfoLogs = EditorGUILayout.Toggle(
                    new GUIContent("Include info logs in session.jsonl", "Off = only warnings, errors, compile messages and markers."),
                    s.aiIncludeInfoLogs);
                s.aiTopGroups = Mathf.Clamp(EditorGUILayout.IntField(
                    new GUIContent("Groups in errors.md", "Error groups listed (warnings get half)."), s.aiTopGroups), 1, 100);
                s.aiUserFrames = Mathf.Clamp(EditorGUILayout.IntField(
                    new GUIContent("Frames per group", "Project-code stack frames shown per error group."), s.aiUserFrames), 1, 10);
                s.aiMaxFileMB = Mathf.Clamp(EditorGUILayout.IntField(
                    new GUIContent("Rotate session.jsonl at (MB)"), s.aiMaxFileMB), 1, 512);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open folder", GUILayout.Width(120))) AIBridge.Reveal();
                    if (GUILayout.Button("Write now", GUILayout.Width(120))) AIBridge.Flush(true);
                }
            }
        }

        private static void Header(string title, string help)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2);
        }
    }
}
#endif
