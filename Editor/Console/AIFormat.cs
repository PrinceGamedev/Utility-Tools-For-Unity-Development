#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// Token-lean text formats for AI consumers: status line, issues digest (Markdown) and one JSON
    /// object per entry. Shared by the bridge files and the window's "Copy for AI".
    /// </summary>
    internal static class AIFormat
    {
        public const int SchemaVersion = 1;
        private const int MaxMessageChars = 2000;
        private const int MaxDigestLineChars = 200;
        private const int MaxJsonFrames = 8;

        public static string Iso(long ticks) =>
            new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        public static string IsoNow() => Iso(DateTime.UtcNow.Ticks);

        // ---------- status.txt ----------
        public static string Status(bool editorRunning)
        {
            var compile = ConsoleStore.Compile;
            string state =
                !editorRunning ? "closed" :
                compile == ConsoleStore.CompileState.Compiling ? "compiling" :
                compile == ConsoleStore.CompileState.Failed ? "compile-failed" :
                ConsoleStore.ErrorCount > 0 ? "errors" : "ok";

            var sb = new StringBuilder(200);
            sb.Append("state=").Append(state);
            sb.Append(" mode=").Append(EditorApplication.isPlaying ? (EditorApplication.isPaused ? "paused" : "play") : "edit");
            sb.Append(" compile=").Append(CompileName(compile));
            if (ConsoleStore.LastCompileTicks > 0) sb.Append(" compiled_at=").Append(Iso(ConsoleStore.LastCompileTicks));
            sb.Append(" compile_errors=").Append(ConsoleStore.CompileErrorCount);
            sb.Append(" errors=").Append(ConsoleStore.ErrorCount);
            sb.Append(" warnings=").Append(ConsoleStore.WarningCount);
            sb.Append(" last_id=").Append(ConsoleStore.LastId);
            sb.Append(" unity=").Append(Application.unityVersion);
            sb.Append(" updated=").Append(IsoNow());
            sb.Append('\n');
            return sb.ToString();
        }

        private static string CompileName(ConsoleStore.CompileState s)
        {
            switch (s)
            {
                case ConsoleStore.CompileState.Compiling: return "compiling";
                case ConsoleStore.CompileState.Failed: return "failed";
                case ConsoleStore.CompileState.Ok: return "ok";
                default: return "unknown";
            }
        }

        // ---------- errors.md ----------
        public static string IssuesDigest(int topGroups, int userFrames)
        {
            var sb = new StringBuilder(2048);
            sb.Append("# AI Console — issues\n");
            sb.Append("updated ").Append(IsoNow())
                .Append(" · mode ").Append(EditorApplication.isPlaying ? "play" : "edit")
                .Append(" · last_id ").Append(ConsoleStore.LastId).Append('\n');

            var compileErrors = ConsoleStore.CompileIssues.Where(e => e.kind == EntryKind.CompileError).ToList();
            if (compileErrors.Count > 0)
            {
                sb.Append("\n## Compile errors (").Append(compileErrors.Count).Append(")\n");
                foreach (var e in compileErrors.Take(topGroups))
                    sb.Append("- #").Append(e.id).Append(' ').Append(e.file).Append(':').Append(e.line)
                        .Append(':').Append(e.column).Append(" — ").Append(Clip(CompilerText(e), MaxDigestLineChars)).Append('\n');
            }

            AppendGroups(sb, "Errors", g => g.Latest.IsError && !g.Latest.IsCompile, topGroups, userFrames);
            AppendGroups(sb, "Warnings", g => g.Latest.IsWarning && !g.Latest.IsCompile, Math.Max(3, topGroups / 2), 1);

            if (compileErrors.Count == 0 && ConsoleStore.ErrorCount == 0 && ConsoleStore.WarningCount == 0)
                sb.Append("\nNo errors or warnings.\n");
            return sb.ToString();
        }

        private static void AppendGroups(StringBuilder sb, string title, Func<EntryGroup, bool> filter, int top, int userFrames)
        {
            var groups = ConsoleStore.Groups.Values.Where(filter).ToList();
            if (groups.Count == 0) return;

            int total = groups.Sum(g => g.Count);
            sb.Append("\n## ").Append(title).Append(" (").Append(groups.Count).Append(" groups, ")
                .Append(total).Append(" total, newest first)\n");

            foreach (var g in groups.OrderByDescending(g => g.Latest.id).Take(top))
            {
                var e = g.Latest;
                sb.Append("- #").Append(e.id);
                if (g.Count > 1) sb.Append(" ×").Append(g.Count);
                sb.Append(' ').Append(e.KindName);
                if (e.system != null) sb.Append(" [").Append(e.system).Append(']');
                sb.Append(' ').Append(Clip(e.FirstLine, MaxDigestLineChars)).Append('\n');

                AppendFrames(sb, e, userFrames, "  at ");

                var extra = new List<string>();
                if (e.FieldCount > 0) extra.Add("fields: " + FieldsInline(e));
                if (g.Count > 1) extra.Add("first #" + g.FirstId);
                if (!string.IsNullOrEmpty(e.context)) extra.Add("ctx " + e.context);
                if (extra.Count > 0) sb.Append("  ").Append(string.Join(" · ", extra)).Append('\n');
            }

            if (groups.Count > top) sb.Append("- … ").Append(groups.Count - top).Append(" more groups in session.jsonl\n");
        }

        private static void AppendFrames(StringBuilder sb, ConsoleEntry e, int max, string prefix)
        {
            int written = 0;
            foreach (var f in e.Frames)
            {
                if (!f.IsUser) continue;
                sb.Append(prefix).Append(f.File).Append(':').Append(f.Line).Append(' ').Append(f.Method).Append('\n');
                if (++written >= max) return;
            }

            if (written == 0)
            {
                // No frames in your code: show the top engine frame as a hint.
                foreach (var f in e.Frames)
                {
                    if (f.IsSeparator) continue;
                    sb.Append(prefix).Append(f.Method);
                    if (f.File != null) sb.Append(" (").Append(f.File).Append(':').Append(f.Line).Append(')');
                    sb.Append(" [engine]\n");
                    return;
                }
            }
        }

        private static string FieldsInline(ConsoleEntry e)
        {
            var parts = new string[e.FieldCount];
            for (int i = 0; i < parts.Length; i++) parts[i] = e.fieldKeys[i] + "=" + e.fieldValues[i];
            return string.Join(", ", parts);
        }

        /// <summary>Compiler message without the "path(line,col): " prefix (file/line are listed separately).</summary>
        private static string CompilerText(ConsoleEntry e)
        {
            string m = e.message ?? "";
            int i = m.IndexOf("): ", StringComparison.Ordinal);
            return i > 0 && i < 260 ? m.Substring(i + 3) : m;
        }

        // ---------- Copy for AI ----------
        public static string EntryMarkdown(ConsoleEntry e)
        {
            var sb = new StringBuilder(512);
            var g = ConsoleStore.GroupOf(e);
            sb.Append("#").Append(e.id).Append(' ').Append(e.KindName);
            if (e.system != null) sb.Append(" [").Append(e.system).Append(']');
            if (g != null && g.Count > 1) sb.Append(" ×").Append(g.Count);
            sb.Append(" @ ").Append(Iso(e.ticks)).Append(e.play ? " (play" : " (edit");
            if (e.frame >= 0) sb.Append(", frame ").Append(e.frame);
            if (!string.IsNullOrEmpty(e.scene)) sb.Append(", scene ").Append(e.scene);
            sb.Append(")\n");

            sb.Append(e.IsCompile ? CompilerText(e) : Clip(e.message, MaxMessageChars)).Append('\n');
            if (e.IsCompile) sb.Append("  at ").Append(e.file).Append(':').Append(e.line).Append(':').Append(e.column).Append('\n');
            else AppendFrames(sb, e, 6, "  at ");

            if (e.FieldCount > 0) sb.Append("fields: ").Append(FieldsInline(e)).Append('\n');
            if (!string.IsNullOrEmpty(e.context)) sb.Append("context: ").Append(e.context).Append('\n');
            return sb.ToString();
        }

        // ---------- session.jsonl ----------
        public static string JsonLine(ConsoleEntry e)
        {
            var w = new StringBuilder(256);
            w.Append("{\"id\":").Append(e.id);
            Prop(w, "t", Iso(e.ticks));
            Prop(w, "k", e.KindName);
            if (e.system != null) Prop(w, "sys", e.system);
            Prop(w, "msg", e.IsCompile ? CompilerText(e) : Clip(e.message, MaxMessageChars));

            if (e.kind != EntryKind.Marker)
            {
                if (e.fingerprint != null) Prop(w, "fp", e.fingerprint);
                Prop(w, "mode", e.play ? "play" : "edit");
                if (!string.IsNullOrEmpty(e.scene)) Prop(w, "scene", e.scene);
                if (e.frame >= 0) w.Append(",\"frame\":").Append(e.frame);
            }

            if (!string.IsNullOrEmpty(e.context)) Prop(w, "ctx", e.context);

            if (e.FieldCount > 0)
            {
                w.Append(",\"f\":{");
                for (int i = 0; i < e.FieldCount; i++)
                {
                    if (i > 0) w.Append(',');
                    Str(w, e.fieldKeys[i]);
                    w.Append(':');
                    Str(w, e.fieldValues[i]);
                }

                w.Append('}');
            }

            if (e.IsCompile)
            {
                if (e.file != null) Prop(w, "file", e.file);
                w.Append(",\"line\":").Append(e.line).Append(",\"col\":").Append(e.column);
            }
            else if (e.kind != EntryKind.Marker)
            {
                var at = new List<string>();
                int engine = 0;
                foreach (var f in e.Frames)
                {
                    if (f.IsSeparator) { at.Add(f.Method); continue; }
                    if (!f.IsUser) { engine++; continue; }
                    if (at.Count < MaxJsonFrames) at.Add(f.File + ":" + f.Line + " " + f.Method);
                }

                if (at.Count > 0)
                {
                    w.Append(",\"at\":[");
                    for (int i = 0; i < at.Count; i++)
                    {
                        if (i > 0) w.Append(',');
                        Str(w, at[i]);
                    }

                    w.Append(']');
                }

                if (engine > 0) w.Append(",\"engine_frames\":").Append(engine);
            }

            w.Append('}');
            return w.ToString();
        }

        private static void Prop(StringBuilder w, string key, string value)
        {
            w.Append(",\"").Append(key).Append("\":");
            Str(w, value);
        }

        internal static void Str(StringBuilder w, string s)
        {
            if (s == null)
            {
                w.Append("null");
                return;
            }

            w.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': w.Append("\\\""); break;
                    case '\\': w.Append("\\\\"); break;
                    case '\n': w.Append("\\n"); break;
                    case '\r': break;
                    case '\t': w.Append("\\t"); break;
                    default:
                        if (c < 0x20) w.Append("\\u").Append(((int)c).ToString("x4"));
                        else w.Append(c);
                        break;
                }
            }

            w.Append('"');
        }

        internal static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "…";
        }

        // ---------- AI-README.md ----------
        public static string Readme(string folder) =>
$@"# AI Console — read me first (schema v{SchemaVersion})
Live data from the Unity Editor, written by Utility Tools. Cheapest first:

1. `{folder}/status.txt` — one line: state (ok|errors|compiling|compile-failed|closed), mode, compile,
   compile_errors, errors, warnings, last_id, updated. Check this before anything else.
2. `{folder}/errors.md` — only when status shows problems: compile errors, then error/warning groups
   (newest first). `#id` = entry id, `×N` = repeats, `at` lines are frames in project code only.
3. `{folder}/session.jsonl` — one JSON object per entry, for details: search for `""id"":812` or read
   lines after a known last_id. Keys: id, t (UTC), k (log|warning|error|exception|assert|
   compile-error|compile-warning|marker), sys (system/channel), msg, fp (same fp = same issue),
   mode, scene, frame, ctx (object path), f (fields), at (project frames), engine_frames, file/line/col.
   `session.prev.jsonl` holds the previous editor session.

Ids are shared with the AI Console window: the user may say ""look at #812"".
After editing scripts, wait for status `compile=ok` with a newer compiled_at before judging results.
";
    }
}
#endif
