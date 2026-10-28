#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UtilityTools.AIConsole
{
    internal enum AgentFileState
    {
        /// <summary>The file does not exist.</summary>
        NoFile,
        /// <summary>The file exists without the managed section.</summary>
        NotInstalled,
        /// <summary>The managed section is there but differs from the current one.</summary>
        Outdated,
        Installed
    }

    /// <summary>
    /// The managed "how to use the AI Console" section written into agent instruction files
    /// (CLAUDE.md, AGENTS.md, …). Pure text logic; file IO lives in <see cref="AgentSetup"/>.
    /// Only the text between the markers is ever replaced or removed.
    /// </summary>
    internal static class AgentBlock
    {
        /// <summary>Bump when the section's wording changes; installed sections then show as outdated.</summary>
        internal const int Version = 1;

        internal const string BeginPrefix = "<!-- utilitytools:ai-console:begin";
        internal const string End = "<!-- utilitytools:ai-console:end -->";

        internal static string Begin =>
            BeginPrefix + " v" + Version + " — managed by Utility Tools (Project Settings › Utility Tools › AI Console); edits inside are replaced -->";

        /// <summary>The section, '\n' line endings, markers included.</summary>
        internal static string Build(string folder)
        {
            string f = string.IsNullOrWhiteSpace(folder) ? "Logs/AIConsole" : folder.Trim().Replace('\\', '/').TrimEnd('/');
            return Begin + "\n" +
$@"## Unity Editor console (Utility Tools AI Console)

The Unity Editor mirrors its console and compile state into `{f}/`. Read those files instead of asking the user to copy logs.

- `status.txt` — one line: `state=ok|errors|compiling|compile-failed|closed`, `mode`, counts, `last_id`. Read it first, and after every C# change.
- `errors.md` — only when status shows problems: compile errors, then grouped errors/warnings with project stack frames.
- `session.jsonl` — every entry as one JSON line. Search it (`""id"":812`, `""fp"":""…""`, `""tags"":[""UI""`); never read it whole.
- `#812` in the user's words is an entry id in these files.

Drive the Editor: write commands, one per line, to `{f}/commands.txt`. Unity takes the file, runs the commands in order and writes `results.txt` (first line `state=running|done`, then one line per command, then the status line).
`refresh` (import + compile after edits) · `compile` · `clear` · `play` · `stop` · `wait <seconds>` · `mark <label>` · `show <id>` · `ask <what the user should do>` · `status` · `help`

```
f={f}; printf 'refresh\n' > $f/commands.txt; for i in $(seq 120); do [ ! -e $f/commands.txt ] && grep -q '^state=done' $f/results.txt && break; sleep 1; done; cat $f/results.txt
```

- Debug loop: edit → `refresh` → results.txt → `errors.md` if not ok → fix → repeat.
- Check behaviour: `clear`, `play`, `wait 5`, `stop`, then `errors.md` or grep `session.jsonl`.
- Need the user (reproduce a bug, click something, set up a scene)? Send `ask <short instruction>` and say so in chat. It appears in the AI Console with Done / Can't buttons; the result lists the log ids written meanwhile. Re-run the wait loop until it is answered.
- commands.txt not picked up within ~10 s: Unity is closed, busy or throttled in the background. Ask the user to click the Unity window.
- Logging in new code (`using UtilityTools.Logging;`, stripped from release builds): `static readonly LogChannel Log = new LogChannel(""Spawner"", ""#4FC3F7"");` then `Log.Info(""Spawned"", (""count"", n));` or `Log.Tagged(""UI"").Warning(""…"");`.

Full reference: `{f}/AI-README.md`.
" + End + "\n";
        }

        internal static bool Contains(string text) =>
            text != null && text.IndexOf(BeginPrefix, StringComparison.Ordinal) >= 0;

        /// <summary>State of a file's content (<c>null</c> = file missing) against the wanted section.</summary>
        internal static AgentFileState StateOf(string text, string block)
        {
            if (text == null) return AgentFileState.NoFile;
            if (!TryFind(text, out int start, out int end)) return AgentFileState.NotInstalled;
            string current = Normalize(text.Substring(start, end - start));
            return current == Normalize(block).TrimEnd('\n') ? AgentFileState.Installed : AgentFileState.Outdated;
        }

        /// <summary>
        /// Returns <paramref name="text"/> with the section replaced, or appended when absent.
        /// Keeps the file's line endings and everything outside the markers.
        /// </summary>
        internal static string Merge(string text, string block)
        {
            text = text ?? "";
            string eol = DetectEol(text);
            string body = Normalize(block).TrimEnd('\n').Replace("\n", eol);

            if (TryFind(text, out int start, out int end))
                return text.Substring(0, start) + body + text.Substring(end);

            if (text.Length == 0) return body + eol;
            string trimmed = text.TrimEnd('\r', '\n');
            return trimmed + eol + eol + body + eol;
        }

        /// <summary>Returns <paramref name="text"/> without the section (and the blank line before it).</summary>
        internal static string Remove(string text)
        {
            if (text == null || !TryFind(text, out int start, out int end)) return text;

            // Swallow the line break after the end marker and blank lines before the section.
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            while (start > 0 && (text[start - 1] == '\n' || text[start - 1] == '\r')) start--;

            string before = text.Substring(0, start);
            string after = text.Substring(end);
            if (before.Length == 0) return after.TrimStart('\r', '\n');
            string eol = DetectEol(text);
            return after.Length == 0 ? before + eol : before + eol + eol + after.TrimStart('\r', '\n');
        }

        /// <summary>Span of the section: from the begin marker to just after the end marker.</summary>
        private static bool TryFind(string text, out int start, out int end)
        {
            start = text.IndexOf(BeginPrefix, StringComparison.Ordinal);
            end = -1;
            if (start < 0) return false;
            int e = text.IndexOf(End, start, StringComparison.Ordinal);
            if (e < 0)
            {
                start = -1;
                return false;
            }

            end = e + End.Length;
            return true;
        }

        private static string DetectEol(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

        private static string Normalize(string s) => s.Replace("\r\n", "\n");
    }

    /// <summary>One line of commands.txt: a verb and the rest of the line.</summary>
    internal readonly struct CommandLine
    {
        public readonly string Verb;
        public readonly string Arg;
        public readonly string Text;

        public CommandLine(string verb, string arg, string text)
        {
            Verb = verb;
            Arg = arg;
            Text = text;
        }

        /// <summary>Verbs Unity understands; anything else fails with a hint to send <c>help</c>.</summary>
        internal static readonly string[] Verbs =
            { "refresh", "compile", "clear", "play", "stop", "pause", "resume", "wait", "mark", "show", "ask", "status", "menu", "help" };

        /// <summary>
        /// Splits commands.txt into commands. Blank lines and lines starting with <c>//</c> are skipped;
        /// a leading <c>/</c> or <c>-</c> and surrounding backticks are tolerated (agents add them).
        /// </summary>
        internal static List<CommandLine> Parse(string text)
        {
            var list = new List<CommandLine>();
            if (string.IsNullOrEmpty(text)) return list;

            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim().Trim('`').Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line[0] == '/' || line[0] == '-') line = line.Substring(1).TrimStart();
                if (line.Length == 0) continue;

                int space = line.IndexOfAny(new[] { ' ', '\t' });
                string verb = (space < 0 ? line : line.Substring(0, space)).ToLowerInvariant();
                string arg = space < 0 ? "" : line.Substring(space + 1).Trim();
                list.Add(new CommandLine(verb, arg, line));
            }

            return list;
        }

        /// <summary>Entry id from "812", "#812" or "id:812"; -1 when there is none.</summary>
        internal static int ParseId(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return -1;
            string s = arg.Trim();
            if (s.StartsWith("id:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(3);
            s = s.TrimStart('#');
            return int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id)
                ? id
                : -1;
        }
    }
}
#endif
