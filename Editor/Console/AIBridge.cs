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
    /// Writes the AI-readable files (status.txt, errors.md, session.jsonl, AI-README.md) under the
    /// configured folder. Writes are batched (≤ 2 per second) and happen only when something changed;
    /// status is written immediately when compilation starts, before Unity blocks.
    /// </summary>
    [InitializeOnLoad]
    internal static class AIBridge
    {
        private const double MinInterval = 0.5;

        private static readonly List<ConsoleEntry> s_PendingLines = new List<ConsoleEntry>();
        private static bool s_Dirty;
        private static double s_LastWrite;
        private static ConsoleStore.CompileState s_LastCompile;
        private static bool s_WarnedIo;
        private static bool s_Flushing;
        private static bool s_RotatePending;
        private static bool s_ReadmeChecked;

        public static string Folder
        {
            get
            {
                string f = ConsoleProjectSettings.instance.aiFolder;
                return string.IsNullOrWhiteSpace(f) ? "Logs/AIConsole" : f.Trim().Replace('\\', '/').TrimEnd('/');
            }
        }

        public static string FullFolder => Path.GetFullPath(Folder);

        public static bool Enabled => ConsoleProjectSettings.instance.aiFilesEnabled;

        public static DateTime LastWriteUtc { get; private set; }

        static AIBridge()
        {
            // Touch ConsoleCapture first so IsNewEditorSession and the snapshot are ready.
            // Rotation runs inside the first flush, before anything of this session is appended —
            // a separate delayed rotation could run after an early flush and move this session's
            // first entries into session.prev.jsonl.
            s_RotatePending = ConsoleCapture.IsNewEditorSession;

            ConsoleStore.Added += OnAdded;
            ConsoleStore.Changed += OnChanged;
            ConsoleStore.Cleared += () => s_Dirty = true;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += () => Flush(true);
            EditorApplication.quitting += OnQuitting;
            ConsoleProjectSettings.Changed += () => { s_Dirty = true; Flush(true); };

            EditorApplication.delayCall += () =>
            {
                s_Dirty = true;
                Flush(true);
            };
        }

        private static void OnAdded(ConsoleEntry e)
        {
            if (!Enabled) return;
            if (e.kind == EntryKind.Log && !ConsoleProjectSettings.instance.aiIncludeInfoLogs) return;
            s_PendingLines.Add(e);
            s_Dirty = true;
        }

        private static void OnChanged()
        {
            s_Dirty = true;
            // Unity blocks the main thread while compiling — publish "compiling" right away.
            if (ConsoleStore.Compile != s_LastCompile && ConsoleStore.Compile == ConsoleStore.CompileState.Compiling)
                Flush(true);
        }

        private static void Tick()
        {
            if (s_Dirty && EditorApplication.timeSinceStartup - s_LastWrite >= MinInterval) Flush(false);
        }

        private static void OnQuitting()
        {
            Flush(true);
            if (!Enabled) return;
            TryWrite("status.txt", AIFormat.Status(editorRunning: false), atomic: true);
        }

        /// <summary>Writes pending lines and refreshes status / digest.</summary>
        public static void Flush(bool force)
        {
            if (!Enabled)
            {
                s_PendingLines.Clear();
                s_Dirty = false;
                return;
            }

            if (!force && !s_Dirty) return;
            if (s_Flushing) return; // Drain → Changed → Flush re-entry
            s_Flushing = true;

            try
            {
                ConsoleCapture.Drain();
                Directory.CreateDirectory(FullFolder);
                if (s_RotatePending)
                {
                    s_RotatePending = false;
                    StartNewSessionFiles();
                }

                if (!s_ReadmeChecked)
                {
                    // Once per domain: keeps the read-me (versions, schema) current after package updates.
                    s_ReadmeChecked = true;
                    string readme = AIFormat.Readme(Folder);
                    string path = Path.Combine(FullFolder, "AI-README.md");
                    if (!File.Exists(path) || File.ReadAllText(path) != readme) TryWrite("AI-README.md", readme, atomic: true);
                }

                AppendSession();
                var settings = ConsoleProjectSettings.instance;
                TryWrite("errors.md", AIFormat.IssuesDigest(Mathf.Max(1, settings.aiTopGroups),
                    Mathf.Clamp(settings.aiUserFrames, 1, 10)), atomic: true);
                TryWrite("status.txt", AIFormat.Status(editorRunning: true), atomic: true);

                s_LastCompile = ConsoleStore.Compile;
                s_LastWrite = EditorApplication.timeSinceStartup;
                LastWriteUtc = DateTime.UtcNow;
                s_Dirty = false;
            }
            catch (Exception e)
            {
                WarnOnce(e);
            }
            finally
            {
                s_Flushing = false;
            }
        }

        private static void AppendSession()
        {
            if (s_PendingLines.Count == 0) return;

            string path = Path.Combine(FullFolder, "session.jsonl");
            RotateIfLarge(path);

            var sb = new StringBuilder(s_PendingLines.Count * 200);
            foreach (var e in s_PendingLines) sb.Append(AIFormat.JsonLine(e)).Append('\n');
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            s_PendingLines.Clear();
        }

        private static void RotateIfLarge(string path)
        {
            long max = Math.Max(1, ConsoleProjectSettings.instance.aiMaxFileMB) * 1024L * 1024L;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < max) return;
            MoveOver(path, Path.Combine(FullFolder, "session.prev.jsonl"));
        }

        private static void StartNewSessionFiles()
        {
            try
            {
                Directory.CreateDirectory(FullFolder);
                string session = Path.Combine(FullFolder, "session.jsonl");
                if (File.Exists(session)) MoveOver(session, Path.Combine(FullFolder, "session.prev.jsonl"));
                TryWrite("AI-README.md", AIFormat.Readme(Folder), atomic: true);
            }
            catch (Exception e)
            {
                WarnOnce(e);
            }
        }

        /// <summary>Opens the AI folder in the OS file browser.</summary>
        public static void Reveal()
        {
            Directory.CreateDirectory(FullFolder);
            if (!File.Exists(Path.Combine(FullFolder, "AI-README.md")))
                TryWrite("AI-README.md", AIFormat.Readme(Folder), atomic: true);
            EditorUtility.RevealInFinder(Path.Combine(FullFolder, "AI-README.md"));
        }

        private static void TryWrite(string name, string content, bool atomic)
        {
            string path = Path.Combine(FullFolder, name);
            try
            {
                if (!atomic)
                {
                    File.WriteAllText(path, content, new UTF8Encoding(false));
                    return;
                }

                // Write next to the target, then swap, so readers never see a half-written file.
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, content, new UTF8Encoding(false));
                MoveOver(tmp, path);
            }
            catch (Exception e)
            {
                WarnOnce(e);
            }
        }

        private static void MoveOver(string from, string to)
        {
            if (File.Exists(to)) File.Replace(from, to, null);
            else File.Move(from, to);
        }

        private static void WarnOnce(Exception e)
        {
            if (s_WarnedIo) return;
            s_WarnedIo = true;
            Debug.LogWarning($"[UtilityTools] AI Console could not write to '{Folder}': {e.Message}");
        }
    }
}
#endif
