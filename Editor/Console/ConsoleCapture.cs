#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UtilityTools.Logging;
using Object = UnityEngine.Object;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// Collects every log (any thread), compiler message and Play / compile marker into
    /// <see cref="ConsoleStore"/>. Logs written through ULog keep their channel, fields and context.
    /// </summary>
    [InitializeOnLoad]
    internal static class ConsoleCapture
    {
        private struct Pending
        {
            public string Condition;
            public string StackTrace;
            public LogType Type;
            public LogRecord Record;
            public long Ticks;
            public int Thread;
            public int Frame;
            public string ContextPath;
            public int ContextId;
        }

        private static readonly ConcurrentQueue<Pending> s_Queue = new ConcurrentQueue<Pending>();
        private static readonly List<ConsoleEntry> s_CompileBuffer = new List<ConsoleEntry>();
        private static readonly HashSet<string> s_RebuiltAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Main-thread state cached for logs that arrive on other threads.
        private static volatile bool s_Playing;
        private static volatile string s_Scene;

        private const string SessionKey = "UtilityTools.AIConsole.Session";
        private const string CompileStartKey = "UtilityTools.AIConsole.CompileStart";

        /// <summary>True on the first domain load of this editor session (the AI bridge rotates files).</summary>
        public static bool IsNewEditorSession { get; }

        static ConsoleCapture()
        {
            ULog.MainThreadId = Thread.CurrentThread.ManagedThreadId;

            IsNewEditorSession = !SessionState.GetBool(SessionKey, false);
            if (IsNewEditorSession)
            {
                SessionState.SetBool(SessionKey, true);
                ConsoleStore.DeleteSnapshot();
            }
            else
            {
                ConsoleStore.LoadSnapshot();
            }

            if (ConsoleStore.Compile == ConsoleStore.CompileState.Unknown ||
                ConsoleStore.Compile == ConsoleStore.CompileState.Compiling)
                ConsoleStore.Compile = EditorUtility.scriptCompilationFailed
                    ? ConsoleStore.CompileState.Failed
                    : ConsoleStore.CompileState.Ok;

            UpdateMainThreadState();

            Application.logMessageReceivedThreaded += OnLog;
            EditorApplication.update += Drain;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        // ---------- Logs (any thread) ----------
        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                int thread = Thread.CurrentThread.ManagedThreadId;
                bool main = thread == ULog.MainThreadId;
                var record = ULog.Current;

                var p = new Pending
                {
                    Condition = condition,
                    StackTrace = stackTrace,
                    Type = type,
                    Record = record,
                    Ticks = DateTime.UtcNow.Ticks,
                    Thread = thread,
                    Frame = record?.Frame ?? (main ? Time.frameCount : -1)
                };

                if (main && record != null && record.Context)
                {
                    p.ContextId = record.Context.GetInstanceID();
                    p.ContextPath = DescribeContext(record.Context);
                }

                s_Queue.Enqueue(p);
            }
            catch
            {
                // Never let capture break logging.
            }
        }

        /// <summary>Moves queued logs into the store. Runs every editor tick.</summary>
        internal static void Drain()
        {
            if (s_Queue.IsEmpty) return; // runs every tick: stay allocation-free when idle
            UpdateMainThreadState();

            bool pause = false;
            while (s_Queue.TryDequeue(out var p))
            {
                if (TryHandleCompilerEcho(p)) continue;
                var e = Build(p);
                ConsoleStore.Add(e);
                if (e.IsError && s_Playing && ConsoleUserSettings.instance.pauseOnError) pause = true;
            }

            if (pause && EditorApplication.isPlaying) EditorApplication.isPaused = true;
            ConsoleStore.NotifyChanged();
        }

        /// <summary>
        /// Unity re-prints compiler messages to the Console (e.g. after a domain reload). Those already
        /// captured from CompilationPipeline are dropped; unseen ones (compiled before this package loaded)
        /// become compile issues instead of plain warnings/errors.
        /// </summary>
        private static bool TryHandleCompilerEcho(Pending p)
        {
            if (p.Record != null || (p.Type != LogType.Warning && p.Type != LogType.Error)) return false;
            if (!StackTraceParser.TryParseCompilerLine(p.Condition, out var file, out int line, out int column,
                    out bool isError, out var code))
                return false;

            if (ConsoleStore.HasCompileIssue(file, line, column, code)) return true;

            var kind = isError ? EntryKind.CompileError : EntryKind.CompileWarning;
            var e = new ConsoleEntry
            {
                ticks = p.Ticks,
                kind = kind,
                message = p.Condition.TrimEnd(),
                file = file,
                line = line,
                column = column,
                assembly = AssemblyOfScript(file),
                frame = -1
            };
            e.fingerprint = StackTraceParser.Fingerprint(kind, file, e.message, null);
            ConsoleStore.AddCompileIssue(e);
            return true;
        }

        /// <summary>Assembly a project script compiles into, or null when Unity doesn't know the file.</summary>
        private static string AssemblyOfScript(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            try { return StackTraceParser.AssemblyKey(CompilationPipeline.GetAssemblyNameFromScriptPath(file)); }
            catch (Exception) { return null; }
        }

        private static ConsoleEntry Build(Pending p)
        {
            var e = new ConsoleEntry
            {
                ticks = p.Ticks,
                thread = p.Thread,
                frame = p.Frame,
                play = s_Playing,
                scene = s_Scene,
                stackTrace = p.StackTrace,
                kind = ToKind(p.Type),
                context = p.ContextPath,
                contextId = p.ContextId
            };

            var rec = p.Record;
            if (rec != null)
            {
                if (rec.Channel != ULog.Default)
                {
                    e.system = rec.Channel.Name;
                    e.color = rec.Channel.ColorHex;
                }

                e.message = rec.Exception != null ? StackTraceParser.StripRichText(p.Condition) : rec.Message;
                if (rec.IsAssert) e.kind = EntryKind.Assert;

                // Tags from Tagged(...) plus any "[UI][Boss]" prefix written into the message itself.
                var tags = new List<string>(rec.Tags);
                if (rec.Exception == null && StackTraceParser.TrySplitTags(e.message, out var inline, out var rest))
                {
                    tags.AddRange(inline);
                    e.message = rest;
                }

                if (tags.Count > 0) e.tags = Distinct(tags, e.system);

                int n = rec.Fields.Count;
                if (n > 0)
                {
                    e.fieldKeys = new string[n];
                    e.fieldValues = new string[n];
                    for (int i = 0; i < n; i++)
                    {
                        e.fieldKeys[i] = rec.Fields[i].Key;
                        e.fieldValues[i] = LogRecord.FormatValue(rec.Fields[i].Value);
                    }
                }
            }
            else
            {
                // Plain Debug.Log: every leading "[Tag]" becomes a tag ("[UI][Net] text" → UI, Net).
                string msg = StackTraceParser.StripRichText(p.Condition);
                if (StackTraceParser.TrySplitTags(msg, out var tags, out var rest))
                {
                    e.tags = Distinct(tags, null);
                    msg = rest;
                }

                e.message = msg;
            }

            if (StackTraceParser.TryGetFirstUserFrame(e.Frames, out var top))
            {
                e.file = top.File;
                e.line = top.Line;
            }

            string tagKey = e.TagCount == 0 ? e.system : e.system + "|" + string.Join(",", e.tags);
            e.fingerprint = StackTraceParser.Fingerprint(e.kind, tagKey, e.message, e.Frames);
            return e;
        }

        /// <summary>Case-insensitive de-duplication, dropping the system itself; null when nothing is left.</summary>
        private static string[] Distinct(List<string> tags, string system)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (system != null) seen.Add(system);
            var result = new List<string>(tags.Count);
            foreach (var t in tags)
                if (!string.IsNullOrWhiteSpace(t) && seen.Add(t.Trim())) result.Add(t.Trim());
            return result.Count > 0 ? result.ToArray() : null;
        }

        private static EntryKind ToKind(LogType type)
        {
            switch (type)
            {
                case LogType.Warning: return EntryKind.Warning;
                case LogType.Error: return EntryKind.Error;
                case LogType.Assert: return EntryKind.Assert;
                case LogType.Exception: return EntryKind.Exception;
                default: return EntryKind.Log;
            }
        }

        private static void UpdateMainThreadState()
        {
            s_Playing = EditorApplication.isPlaying;
            s_Scene = SceneManager.GetActiveScene().name;
        }

        internal static string DescribeContext(Object o)
        {
            if (!o) return null;
            if (o is Component c) o = c.gameObject;
            if (o is GameObject go)
            {
                var sb = new StringBuilder(go.name);
                for (var t = go.transform.parent; t != null; t = t.parent) sb.Insert(0, t.name + "/");
                return sb.ToString();
            }

            string assetPath = AssetDatabase.GetAssetPath(o);
            return string.IsNullOrEmpty(assetPath) ? o.name : assetPath;
        }

        /// <summary>Clears the session view and leaves a marker so AI readers know history was reset.</summary>
        internal static void ClearAll(string reason)
        {
            Drain();
            ConsoleStore.Clear();
            AddMarker("⌫ Console cleared (" + reason + ")");
        }

        // ---------- Markers ----------
        internal static void AddMarker(string message)
        {
            Drain(); // keep markers in order with logs already queued
            ConsoleStore.Add(new ConsoleEntry
            {
                ticks = DateTime.UtcNow.Ticks,
                kind = EntryKind.Marker,
                message = message,
                play = s_Playing,
                scene = s_Scene,
                frame = -1
            });
            ConsoleStore.NotifyChanged();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if (ConsoleUserSettings.instance.clearOnPlay) ClearAll("entering Play Mode");
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    UpdateMainThreadState();
                    AddMarker("▶ Entered Play Mode");
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    UpdateMainThreadState();
                    AddMarker("■ Exited Play Mode");
                    break;
            }
        }

        // ---------- Compilation ----------
        private static void OnCompilationStarted(object _)
        {
            s_CompileBuffer.Clear();
            s_RebuiltAssemblies.Clear();
            ConsoleStore.ForgetDismissedCompileIssues();
            ConsoleStore.Compile = ConsoleStore.CompileState.Compiling;
            SessionState.SetString(CompileStartKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            ConsoleStore.NotifyChanged();
        }

        private static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            string asm = StackTraceParser.AssemblyKey(assembly);
            if (asm != null) s_RebuiltAssemblies.Add(asm);
            foreach (var m in messages)
            {
                if (m.type != CompilerMessageType.Error && m.type != CompilerMessageType.Warning) continue;

                var kind = m.type == CompilerMessageType.Error ? EntryKind.CompileError : EntryKind.CompileWarning;
                var e = new ConsoleEntry
                {
                    ticks = DateTime.UtcNow.Ticks,
                    kind = kind,
                    message = m.message,
                    file = StackTraceParser.NormalizePath(m.file),
                    line = m.line,
                    column = m.column,
                    assembly = asm,
                    system = null,
                    frame = -1
                };
                e.fingerprint = StackTraceParser.Fingerprint(kind, e.file, m.message, null);
                s_CompileBuffer.Add(e);
            }
        }

        private static void OnCompilationFinished(object _)
        {
            // Only assemblies that were rebuilt report their messages; keep the others' (like Unity).
            ConsoleStore.UpdateCompileIssues(s_RebuiltAssemblies, s_CompileBuffer, e => AssemblyOfScript(e.file),
                f => System.IO.File.Exists(f));
            s_CompileBuffer.Clear();
            s_RebuiltAssemblies.Clear();

            int errors = ConsoleStore.CompileErrorCount;
            ConsoleStore.Compile = errors > 0 ? ConsoleStore.CompileState.Failed : ConsoleStore.CompileState.Ok;
            ConsoleStore.LastCompileTicks = DateTime.UtcNow.Ticks;

            double seconds = 0;
            if (long.TryParse(SessionState.GetString(CompileStartKey, "0"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long start) && start > 0)
                seconds = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - start).TotalSeconds;
            SessionState.EraseString(CompileStartKey);

            AddMarker(errors > 0
                // Plain text: the editor font has no glyph for ⟳ / ✖.
                ? $"Compilation failed — {errors} error(s)"
                : string.Format(CultureInfo.InvariantCulture, "Scripts compiled ({0:0.0}s)", seconds));
        }

        private static void OnBeforeAssemblyReload()
        {
            Drain();
            ConsoleStore.SaveSnapshot();
        }
    }
}
#endif
