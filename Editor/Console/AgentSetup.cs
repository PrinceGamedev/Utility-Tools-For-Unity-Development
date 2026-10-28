#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// Installs and maintains the managed AI Console section (<see cref="AgentBlock"/>) in the project's
    /// agent instruction files, so agents know how to read Unity's state and send commands without being told.
    /// Files that never got the section are not touched by automatic updates.
    /// </summary>
    [InitializeOnLoad]
    internal static class AgentSetup
    {
        internal sealed class Target
        {
            /// <summary>Project-relative path.</summary>
            public readonly string Path;
            public readonly string Agents;
            /// <summary>Written above the section when the file is created (e.g. Cursor rule front matter).</summary>
            public readonly string Preamble;
            /// <summary>The whole file belongs to the AI Console; removing the section deletes it.</summary>
            public readonly bool Dedicated;

            public Target(string path, string agents, string preamble = null, bool dedicated = false)
            {
                Path = path;
                Agents = agents;
                Preamble = preamble;
                Dedicated = dedicated;
            }
        }

        internal static readonly Target[] Targets =
        {
            new Target("CLAUDE.md", "Claude Code"),
            new Target("AGENTS.md", "Codex, Cursor, GitHub Copilot, Jules, Aider and other AGENTS.md readers"),
            new Target("GEMINI.md", "Gemini CLI"),
            new Target(".github/copilot-instructions.md", "GitHub Copilot"),
            new Target(".cursor/rules/unity-ai-console.mdc", "Cursor (rule file)",
                "---\ndescription: Unity Editor console, compile state and Editor control (Utility Tools AI Console)\nalwaysApply: true\n---\n\n",
                dedicated: true),
        };

        /// <summary>Raised after any file was installed, updated or removed.</summary>
        public static event Action Changed;

        static AgentSetup()
        {
            EditorApplication.delayCall += AutoUpdate;
        }

        private static string Block => AgentBlock.Build(AIBridge.Folder);

        private static string FullPath(Target t) => System.IO.Path.GetFullPath(t.Path);

        public static AgentFileState StateOf(Target t)
        {
            try
            {
                return AgentBlock.StateOf(ReadOrNull(FullPath(t), out _), Block);
            }
            catch (Exception)
            {
                return AgentFileState.NoFile;
            }
        }

        /// <summary>True when at least one file carries the section (current or not).</summary>
        public static bool AnyInstalled()
        {
            foreach (var t in Targets)
            {
                var s = StateOf(t);
                if (s == AgentFileState.Installed || s == AgentFileState.Outdated) return true;
            }

            return false;
        }

        /// <summary>
        /// One-click setup: the agent files the project already has, or CLAUDE.md + AGENTS.md when it has none.
        /// Returns the paths written.
        /// </summary>
        public static List<string> InstallDefault()
        {
            var written = new List<string>();
            foreach (var t in DefaultTargets())
                if (Install(t)) written.Add(t.Path);
            return written;
        }

        /// <summary>Paths <see cref="InstallDefault"/> would write.</summary>
        public static List<string> DefaultTargetPaths()
        {
            var paths = new List<string>();
            foreach (var t in DefaultTargets()) paths.Add(t.Path);
            return paths;
        }

        private static List<Target> DefaultTargets()
        {
            var chosen = new List<Target>();
            foreach (var t in Targets)
                if (!t.Dedicated && StateOf(t) != AgentFileState.NoFile) chosen.Add(t);
            if (chosen.Count == 0)
            {
                chosen.Add(Targets[0]);
                chosen.Add(Targets[1]);
            }

            return chosen;
        }

        /// <summary>Adds or refreshes the section. Returns false (and logs) when the file could not be written.</summary>
        public static bool Install(Target t)
        {
            try
            {
                string path = FullPath(t);
                string text = ReadOrNull(path, out bool bom);
                string next = text == null && t.Preamble != null
                    ? t.Preamble + AgentBlock.Merge("", Block)
                    : AgentBlock.Merge(text, Block);
                if (next != text) Write(path, next, bom);
                Changed?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UtilityTools] Could not update {t.Path}: {e.Message}");
                return false;
            }
        }

        /// <summary>Removes the section; a dedicated file is deleted.</summary>
        public static bool Uninstall(Target t)
        {
            try
            {
                string path = FullPath(t);
                string text = ReadOrNull(path, out bool bom);
                if (text == null || !AgentBlock.Contains(text)) return true;

                string next = AgentBlock.Remove(text);
                if (t.Dedicated && (t.Preamble == null || next.Replace("\r\n", "\n").Trim() == t.Preamble.Trim()))
                    File.Delete(path);
                else
                    Write(path, next, bom);
                Changed?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UtilityTools] Could not update {t.Path}: {e.Message}");
                return false;
            }
        }

        /// <summary>Refreshes outdated sections after a package update or a folder change.</summary>
        internal static void AutoUpdate()
        {
            if (!ConsoleProjectSettings.instance.agentAutoUpdate) return;
            foreach (var t in Targets)
                if (StateOf(t) == AgentFileState.Outdated)
                    Install(t);
        }

        private static string ReadOrNull(string path, out bool bom)
        {
            bom = false;
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        }

        private static void Write(string path, string text, bool bom)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text, new UTF8Encoding(bom));
        }
    }
}
#endif
