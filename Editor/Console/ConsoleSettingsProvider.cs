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

        internal const string SettingsPath = "Project/Utility Tools/AI Console";

        private static Vector2 s_Scroll;

        [SettingsProvider]
        private static SettingsProvider Create() =>
            new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "AI Console",
                guiHandler = _ => Draw(),
                keywords = new HashSet<string>(new[]
                {
                    "log", "logging", "ULog", "channel", "system", "console", "AI", "strip", "release", "define",
                    "agent", "CLAUDE.md", "AGENTS.md", "Cursor", "Copilot", "commands"
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
            DrawAgents(project);
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
        private const string TagExample =
            "using UtilityTools.Logging;\n\n" +
            "public static class Tags\n" +
            "{\n" +
            "    public static readonly LogTag UI = new LogTag(\"UI\", \"#4FC3F7\");\n" +
            "    public static readonly LogTag GAEvents = new LogTag(\"GAEvents\", \"#F2C14E\");\n" +
            "}\n\n" +
            "// Anywhere:\n" +
            "Debug.Log($\"{Tags.UI}{Tags.GAEvents} Level start sent\");\n" +
            "Systems.Ads.Tagged(Tags.UI).Info(\"Banner shown\");";

        private static void DrawTags(ConsoleProjectSettings s)
        {
            Header("Tags", "Tags and their colours are defined once in code (LogTag) and used anywhere. " +
                           "The AI Console finds them automatically and lists them in its tags bar.");

            var tags = LogTag.All;
            if (tags.Count == 0)
            {
                EditorGUILayout.HelpBox("No LogTags defined yet. Put them in one static class:", MessageType.None);
            }
            else
            {
                foreach (var tag in tags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var rect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
                        rect.y += 2;
                        ColorUtility.TryParseHtmlString(tag.ColorHex, out var c);
                        EditorGUI.DrawRect(rect, c);
                        GUILayout.Label(tag.Name, EditorStyles.boldLabel, GUILayout.Width(160));
                        GUILayout.Label(tag.ColorHex, EditorStyles.miniLabel, GUILayout.Width(70));
                        LogTagDiscovery.Declarations.TryGetValue(tag.Name, out var where);
                        GUILayout.Label(where ?? "", EditorStyles.miniLabel);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Copy example", "Copy a Tags class to paste into your project"), GUILayout.Width(120)))
                    EditorGUIUtility.systemCopyBuffer = TagExample;
                if (GUILayout.Button(new GUIContent("Find tags", "Look for LogTags in the project's code again"), GUILayout.Width(120)))
                    LogTagDiscovery.Run();
            }

            if (s.tags.Count == 0) return;

            // Colours picked in the window by version 1.3.0. Tags defined in code take priority.
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Older colours set in the window (a LogTag with the same name wins)", EditorStyles.miniLabel);
            string remove = null;
            foreach (var tag in s.tags)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
                    rect.y += 2;
                    ColorUtility.TryParseHtmlString(tag.color, out var c);
                    EditorGUI.DrawRect(rect, c);
                    GUILayout.Label(tag.name, GUILayout.Width(160));
                    GUILayout.Label(tag.color, EditorStyles.miniLabel, GUILayout.Width(70));
                    if (GUILayout.Button(new GUIContent("×", "Forget this colour"), GUILayout.Width(22))) remove = tag.name;
                }
            }

            if (remove != null)
            {
                s.tags.RemoveAll(t => t.name == remove);
                GUI.changed = true;
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

        // ---------- AI agents ----------
        private static AgentFileState[] s_AgentStates;
        private static double s_AgentStatesAt = -10;
        private static bool s_ShowSection;
        private static GUIStyle s_OkStyle, s_WarnStyle;

        private static void DrawAgents(ConsoleProjectSettings s)
        {
            Header("AI agents",
                "A short managed section in agent instruction files tells AI agents how to read these files and drive Unity " +
                "through commands.txt, so you don't have to explain it. Only text between the Utility Tools markers is written. " +
                "Commit the files to share them with the team.");

            if (s_AgentStates == null || EditorApplication.timeSinceStartup - s_AgentStatesAt > 2) RefreshAgentStates();
            if (s_OkStyle == null)
            {
                s_OkStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.30f, 0.76f, 0.54f) } };
                s_WarnStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.76f, 0.31f) } };
            }

            for (int i = 0; i < AgentSetup.Targets.Length; i++)
            {
                var t = AgentSetup.Targets[i];
                var state = s_AgentStates[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(t.Path, "Read by " + t.Agents), GUILayout.Width(250));
                    switch (state)
                    {
                        case AgentFileState.Installed: GUILayout.Label("installed", s_OkStyle, GUILayout.Width(110)); break;
                        case AgentFileState.Outdated: GUILayout.Label("outdated", s_WarnStyle, GUILayout.Width(110)); break;
                        case AgentFileState.NotInstalled: GUILayout.Label("not added", EditorStyles.miniLabel, GUILayout.Width(110)); break;
                        default: GUILayout.Label("no file", EditorStyles.miniLabel, GUILayout.Width(110)); break;
                    }

                    bool has = state == AgentFileState.Installed || state == AgentFileState.Outdated;
                    if (state == AgentFileState.Outdated && GUILayout.Button("Update", GUILayout.Width(80))) Act(() => AgentSetup.Install(t));
                    if (!has && GUILayout.Button(new GUIContent(state == AgentFileState.NoFile ? "Create" : "Add",
                            "Write the section into " + t.Path), GUILayout.Width(80)))
                        Act(() => AgentSetup.Install(t));
                    if (has && GUILayout.Button(new GUIContent("Remove", "Remove only the managed section"), GUILayout.Width(80)))
                        Act(() => AgentSetup.Uninstall(t));
                    GUILayout.FlexibleSpace();
                }
            }

            EditorGUILayout.Space(4);
            s.agentAutoUpdate = EditorGUILayout.Toggle(new GUIContent("Keep sections up to date",
                "After a package update or a folder change, refresh sections that are already there. " +
                "Files without the section are never touched."), s.agentAutoUpdate);
            s.aiCommandsEnabled = EditorGUILayout.Toggle(new GUIContent("Accept commands (commands.txt)",
                "Agents can import and compile, clear, enter and leave Play Mode, add markers, show entries and ask you for help."),
                s.aiCommandsEnabled);
            using (new EditorGUI.DisabledScope(!s.aiCommandsEnabled))
                s.aiAllowMenuCommands = EditorGUILayout.Toggle(new GUIContent("Allow menu commands",
                    "`menu <path>` runs any Editor menu item. Off by default."), s.aiAllowMenuCommands);

            s_ShowSection = EditorGUILayout.Foldout(s_ShowSection, "Preview section", true);
            if (s_ShowSection)
            {
                string text = AgentBlock.Build(AIBridge.Folder);
                var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 11 };
                float width = EditorGUIUtility.currentViewWidth - 40;
                EditorGUILayout.SelectableLabel(text, style, GUILayout.Height(style.CalcHeight(new GUIContent(text), width)));
                if (GUILayout.Button("Copy", GUILayout.Width(80))) EditorGUIUtility.systemCopyBuffer = text;
            }

            if (ConsoleUserSettings.instance.agentSetupDismissed &&
                GUILayout.Button("Show the setup card in the AI Console again", GUILayout.Width(300)))
            {
                ConsoleUserSettings.instance.agentSetupDismissed = false;
                ConsoleUserSettings.instance.SaveAndNotify();
            }
        }

        private static void Act(Func<bool> action)
        {
            action();
            RefreshAgentStates();
            GUIUtility.ExitGUI();
        }

        private static void RefreshAgentStates()
        {
            s_AgentStates = new AgentFileState[AgentSetup.Targets.Length];
            for (int i = 0; i < s_AgentStates.Length; i++) s_AgentStates[i] = AgentSetup.StateOf(AgentSetup.Targets[i]);
            s_AgentStatesAt = EditorApplication.timeSinceStartup;
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
