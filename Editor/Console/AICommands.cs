#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// Lets an AI agent drive the Editor through files: it writes <c>commands.txt</c> in the AI folder,
    /// Unity takes it, runs the commands in order and writes <c>results.txt</c>. A running batch survives
    /// domain reloads (compiles, entering Play Mode) through SessionState.
    /// </summary>
    [InitializeOnLoad]
    internal static class AICommands
    {
        internal const string CommandsFile = "commands.txt";
        internal const string ResultsFile = "results.txt";

        private const string BatchKey = "UtilityTools.AIConsole.CommandBatch";
        private const string SeqKey = "UtilityTools.AIConsole.CommandSeq";
        private const double PollInterval = 0.25;
        /// <summary>A command file younger than this may still be being written.</summary>
        private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(150);
        /// <summary>After refresh/compile: how long to wait for a compile to start before "nothing to compile".</summary>
        private static readonly TimeSpan CompileGrace = TimeSpan.FromSeconds(2.5);

        [Serializable]
        private sealed class Command
        {
            public int n;
            public string verb;
            public string arg;
            public string text;
            public string state = "queued"; // queued | running | done | failed
            public string result = "";
            public long startTicks;
            public long baselineTicks; // LastCompileTicks when a refresh/compile started
            public bool compileSeen;
            public int fromId;         // ask: first entry id written while waiting
            public double seconds;
        }

        [Serializable]
        private sealed class Batch
        {
            public int number;
            public long pickedUpTicks;
            public List<Command> commands = new List<Command>();
        }

        private static Batch s_Batch;
        private static double s_LastPoll;
        private static bool s_WarnedIo;

        /// <summary>Raised when a batch starts, a command changes state or an ask is answered.</summary>
        public static event Action Changed;

        public static bool Busy => s_Batch != null;

        static AICommands()
        {
            string json = SessionState.GetString(BatchKey, "");
            if (json.Length > 0)
            {
                try { s_Batch = JsonUtility.FromJson<Batch>(json); }
                catch (Exception) { s_Batch = null; }
                if (s_Batch != null && s_Batch.commands.Count == 0) s_Batch = null;
            }

            EditorApplication.update += Tick;
        }

        // ---------- Ask (shown by the AI Console window) ----------

        public readonly struct AskRequest
        {
            public readonly int Number;
            public readonly string Text;
            public readonly DateTime StartedUtc;

            public AskRequest(int number, string text, DateTime startedUtc)
            {
                Number = number;
                Text = text;
                StartedUtc = startedUtc;
            }
        }

        /// <summary>The question waiting for the user, if any.</summary>
        public static bool TryGetPendingAsk(out AskRequest request)
        {
            var c = Current();
            if (c != null && c.verb == "ask" && c.state == "running")
            {
                request = new AskRequest(c.n, c.arg, new DateTime(c.startTicks, DateTimeKind.Utc));
                return true;
            }

            request = default;
            return false;
        }

        /// <summary>The user's answer to the pending ask: done, or could not do it, with an optional note.</summary>
        public static void Answer(bool done, string note)
        {
            var c = Current();
            if (c == null || c.verb != "ask" || c.state != "running") return;

            note = (note ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            ConsoleCapture.Drain();
            int to = ConsoleStore.LastId;
            string range = to >= c.fromId ? $"logs=#{c.fromId}..#{to} ({Summarize(c.fromId, to)})" : "logs=none";
            string quoted = note.Length > 0 ? " note=\"" + note.Replace("\"", "'") + "\"" : "";
            ConsoleCapture.AddMarker((done ? "◆ User: done" : "◆ User: could not do it") + (note.Length > 0 ? " — " + note : ""));
            Finish(c, done, (done ? "user=done" : "user=could-not") + quoted + " " + range);
        }

        // ---------- Loop ----------

        private static void Tick()
        {
            if (s_Batch != null)
            {
                Advance();
                return;
            }

            if (!Enabled) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - s_LastPoll < PollInterval) return;
            s_LastPoll = now;
            TryPickUp();
        }

        private static bool Enabled => ConsoleProjectSettings.instance.aiCommandsEnabled && AIBridge.Enabled;

        private static void TryPickUp()
        {
            string path = Path.Combine(AIBridge.FullFolder, CommandsFile);
            string text;
            try
            {
                if (!File.Exists(path)) return;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < SettleTime) return;
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                return; // still being written; next poll
            }
            catch (Exception e)
            {
                WarnOnce(e);
                return;
            }

            var lines = CommandLine.Parse(text);
            int number = SessionState.GetInt(SeqKey, 0) + 1;
            SessionState.SetInt(SeqKey, number);
            s_Batch = new Batch { number = number, pickedUpTicks = DateTime.UtcNow.Ticks };
            for (int i = 0; i < lines.Count; i++)
                s_Batch.commands.Add(new Command { n = i + 1, verb = lines[i].Verb, arg = lines[i].Arg, text = lines[i].Text });

            // results.txt says "running" before commands.txt disappears, so an agent that sees the file gone
            // never reads the previous batch's "done".
            WriteResults();
            try { File.Delete(path); }
            catch (Exception e) { WarnOnce(e); }

            if (s_Batch.commands.Count == 0)
            {
                CompleteBatch();
                return;
            }

            Save();
            Changed?.Invoke();
            Advance();
        }

        private static void Advance()
        {
            // Several instant commands (mark, clear…) may finish in one tick.
            for (int guard = 0; guard < 32 && s_Batch != null; guard++)
            {
                var c = Current();
                if (c == null)
                {
                    CompleteBatch();
                    return;
                }

                string before = c.state;
                if (c.state == "queued") Start(c);
                else Check(c);
                if (c.state == before || c.state == "running") return;
            }
        }

        private static Command Current()
        {
            if (s_Batch == null) return null;
            foreach (var c in s_Batch.commands)
                if (c.state == "queued" || c.state == "running") return c;
            return null;
        }

        // ---------- Commands ----------

        private static void Start(Command c)
        {
            c.startTicks = DateTime.UtcNow.Ticks;
            c.state = "running";
            try
            {
                switch (c.verb)
                {
                    case "refresh":
                        c.baselineTicks = ConsoleStore.LastCompileTicks;
                        Save();
                        AssetDatabase.Refresh();
                        break;

                    case "compile":
                        c.baselineTicks = ConsoleStore.LastCompileTicks;
                        Save();
                        // "compile all": clean rebuild, so every assembly reports its warnings again.
                        if (string.Equals(c.arg, "all", StringComparison.OrdinalIgnoreCase))
                            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
                        else
                            CompilationPipeline.RequestScriptCompilation();
                        break;

                    case "clear":
                        ConsoleCapture.ClearAll("AI");
                        Finish(c, true, "cleared");
                        return;

                    case "play":
                        if (EditorApplication.isPlaying) { Finish(c, true, "already in Play Mode"); return; }
                        if (ConsoleStore.CompileErrorCount > 0 || EditorUtility.scriptCompilationFailed)
                        {
                            Finish(c, false, "compile errors block Play Mode — read errors.md");
                            return;
                        }
                        Save();
                        EditorApplication.isPlaying = true;
                        break;

                    case "stop":
                        if (!EditorApplication.isPlaying) { Finish(c, true, "not in Play Mode"); return; }
                        Save();
                        EditorApplication.isPlaying = false;
                        break;

                    case "pause":
                    case "resume":
                        if (!EditorApplication.isPlaying) { Finish(c, false, "not in Play Mode"); return; }
                        EditorApplication.isPaused = c.verb == "pause";
                        Finish(c, true, c.verb == "pause" ? "paused" : "resumed");
                        return;

                    case "wait":
                        if (!double.TryParse(c.arg.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out double secs) ||
                            secs < 0)
                        {
                            Finish(c, false, "usage: wait <seconds> (max 300)");
                            return;
                        }
                        c.seconds = Math.Min(secs, 300);
                        break;

                    case "mark":
                        ConsoleCapture.AddMarker("◆ " + (c.arg.Length > 0 ? c.arg : "AI mark"));
                        ConsoleCapture.Drain();
                        Finish(c, true, "#" + ConsoleStore.LastId);
                        return;

                    case "show":
                        Show(c);
                        return;

                    case "ask":
                        if (c.arg.Length == 0) { Finish(c, false, "usage: ask <what the user should do>"); return; }
                        ConsoleCapture.AddMarker("◆ AI asks: " + c.arg);
                        ConsoleCapture.Drain();
                        c.fromId = ConsoleStore.LastId + 1;
                        Save();
                        AIConsoleWindow.ShowAsk(c.arg);
                        break;

                    case "status":
                        Finish(c, true, "see status line below");
                        return;

                    case "menu":
                        if (!ConsoleProjectSettings.instance.aiAllowMenuCommands)
                        {
                            Finish(c, false, "menu commands are off (Project Settings › Utility Tools › AI Console › AI agents)");
                            return;
                        }
                        bool ran = EditorApplication.ExecuteMenuItem(c.arg);
                        Finish(c, ran, ran ? "ran" : "no such menu item");
                        return;

                    case "help":
                        Finish(c, true, "verbs: " + string.Join(" ", CommandLine.Verbs));
                        return;

                    default:
                        Finish(c, false, "unknown command — send `help`");
                        return;
                }
            }
            catch (Exception e)
            {
                Finish(c, false, e.GetType().Name + ": " + e.Message);
                return;
            }

            WriteResults();
            Changed?.Invoke();
        }

        private static void Check(Command c)
        {
            var elapsed = DateTime.UtcNow - new DateTime(c.startTicks, DateTimeKind.Utc);
            switch (c.verb)
            {
                case "refresh":
                case "compile":
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        if (!c.compileSeen) { c.compileSeen = true; Save(); }
                        return;
                    }

                    if (ConsoleStore.LastCompileTicks > c.baselineTicks)
                    {
                        int errors = ConsoleStore.CompileErrorCount;
                        Finish(c, errors == 0, (errors == 0 ? "compiled" : $"compile failed: {errors} error(s) — read errors.md") + Took(elapsed));
                    }
                    else if (!c.compileSeen && elapsed > CompileGrace)
                    {
                        Finish(c, true, (c.verb == "refresh" ? "imported; no script changes" : "nothing to compile") + Took(elapsed));
                    }
                    else if (elapsed > TimeSpan.FromMinutes(10))
                    {
                        Finish(c, false, "timed out waiting for the compile");
                    }
                    return;

                case "play":
                    if (EditorApplication.isPlaying && !EditorApplication.isCompiling)
                        Finish(c, true, "mode=play" + Took(elapsed));
                    else if (!EditorApplication.isPlayingOrWillChangePlaymode && elapsed > TimeSpan.FromSeconds(3))
                        Finish(c, false, "Unity did not enter Play Mode — read errors.md");
                    else if (elapsed > TimeSpan.FromMinutes(2))
                        Finish(c, false, "timed out entering Play Mode");
                    return;

                case "stop":
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                        Finish(c, true, "mode=edit" + Took(elapsed));
                    else if (elapsed > TimeSpan.FromMinutes(1))
                        Finish(c, false, "timed out leaving Play Mode");
                    return;

                case "wait":
                    if (elapsed.TotalSeconds >= c.seconds) Finish(c, true, "waited " + c.seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s");
                    return;

                case "ask":
                    return; // answered from the window

                default:
                    Finish(c, false, "interrupted");
                    return;
            }
        }

        private static void Show(Command c)
        {
            int id = CommandLine.ParseId(c.arg);
            ConsoleCapture.Drain();
            var e = id >= 0 ? ConsoleStore.FindById(id) : null;
            if (e == null)
            {
                Finish(c, false, id < 0 ? "usage: show <id>" : $"no entry #{id} in this session");
                return;
            }

            AIConsoleWindow.Reveal(e);
            Finish(c, true, "shown #" + id);
        }

        private static string Took(TimeSpan t) => string.Format(CultureInfo.InvariantCulture, " ({0:0.0}s)", t.TotalSeconds);

        private static string Summarize(int from, int to)
        {
            int errors = 0, warnings = 0, logs = 0;
            foreach (var e in ConsoleStore.Entries)
            {
                if (e.id < from || e.id > to || e.kind == EntryKind.Marker) continue;
                if (e.IsError) errors++;
                else if (e.IsWarning) warnings++;
                else logs++;
            }

            return $"{errors} errors, {warnings} warnings, {logs} logs";
        }

        private static void Finish(Command c, bool ok, string result)
        {
            c.state = ok ? "done" : "failed";
            c.result = result;
            Save();
            WriteResults();
            Changed?.Invoke();
        }

        private static void CompleteBatch()
        {
            // Not busy any more before status is written, or it would still say commands=running.
            var batch = s_Batch;
            s_Batch = null;
            SessionState.EraseString(BatchKey);
            ConsoleCapture.Drain();
            AIBridge.Flush(true); // status / errors.md current before the agent reads them
            WriteResults(batch, done: true);
            Changed?.Invoke();
        }

        // ---------- Files ----------

        private static void Save()
        {
            if (s_Batch != null) SessionState.SetString(BatchKey, JsonUtility.ToJson(s_Batch));
        }

        private static void WriteResults() => WriteResults(s_Batch, done: false);

        private static void WriteResults(Batch batch, bool done)
        {
            if (batch == null) return;
            try
            {
                Directory.CreateDirectory(AIBridge.FullFolder);
                string path = Path.Combine(AIBridge.FullFolder, ResultsFile);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, FormatResults(batch, done), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                WarnOnce(e);
            }
        }

        private static string FormatResults(Batch batch, bool done)
        {
            var sb = new StringBuilder(256);
            sb.Append("state=").Append(done ? "done" : "running")
              .Append(" batch=").Append(batch.number)
              .Append(" picked_up=").Append(new DateTime(batch.pickedUpTicks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
              .Append('\n');
            if (batch.commands.Count == 0) sb.Append("(no commands — write one per line, e.g. `refresh`)\n");
            foreach (var c in batch.commands)
            {
                sb.Append('#').Append(c.n).Append(' ').Append(c.text).Append(" -> ").Append(c.state);
                if (!string.IsNullOrEmpty(c.result)) sb.Append(": ").Append(c.result);
                sb.Append('\n');
            }

            if (done) sb.Append("status: ").Append(AIFormat.Status(editorRunning: true));
            return sb.ToString();
        }

        private static void WarnOnce(Exception e)
        {
            if (s_WarnedIo) return;
            s_WarnedIo = true;
            Debug.LogWarning($"[UtilityTools] AI Console commands: {e.Message}");
        }
    }
}
#endif
