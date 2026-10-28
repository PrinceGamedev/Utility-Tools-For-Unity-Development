#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace UtilityTools.AIConsole
{
    internal struct StackFrameInfo
    {
        public string Method;    // "Spawner:Spawn (int)" or "Rethrow as InvalidOperationException: …"
        public string File;      // project-relative when inside the project; null when unknown
        public int Line;
        public bool IsUser;      // your code (not Unity / System / packages / the logger)
        public bool IsSeparator; // "Rethrow as …" line between exceptions in a chain
    }

    /// <summary>Parses Unity and .NET stack traces; message cleanup and fingerprints.</summary>
    internal static class StackTraceParser
    {
        // "Spawner:Spawn (int) (at Assets/Scripts/Spawner.cs:42)"
        private static readonly Regex UnityAt = new Regex(@"\(at (?<file>[^()]*?):(?<line>\d+)\)\s*$", RegexOptions.Compiled);

        // "  at Spawner.Spawn (System.Int32 n) [0x00001] in D:\proj\Assets\Spawner.cs:42"
        private static readonly Regex DotNetAt = new Regex(
            @"^\s*at (?<method>.+?)(?:\s*\[0x[0-9a-fA-F]+\])?(?: in (?<file>.+?):(?:line )?(?<line>\d+))?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex RichText = new Regex(
            @"</?(?:b|i|u|s|size|color|a|link|material|quad|mark|sup|sub|style|font|align|br|noparse|nobr|alpha|cspace|indent|line-height|margin|mspace|pos|rotate|smallcaps|space|voffset|width|lowercase|uppercase|allcaps)\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "[Network] message" — tag must start with a letter so "[12:00]" isn't taken as a tag.
        // \G (not ^) so tags can be read one after another from a start offset: "[UI][Net] text".
        private static readonly Regex LeadingTag = new Regex(@"\G\[(?<tag>[A-Za-z][\w .-]{0,31})\]\s*", RegexOptions.Compiled);

        private static readonly Regex Numbers = new Regex(@"0x[0-9a-fA-F]+|[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}|\d+(\.\d+)?", RegexOptions.Compiled);

        private static readonly string[] NonUserPrefixes =
        {
            "UnityEngine.", "UnityEditor.", "UnityEditorInternal.", "Unity.", "System.", "Mono.",
            "UtilityTools.Logging.", "UtilityTools.AIConsole."
        };

        private static readonly char[] LineBreak = { '\n' };

        // "Assets\Foo.cs(12,5): warning CS0618: text" — how Unity prints compiler messages to the Console.
        private static readonly Regex CompilerLine = new Regex(
            @"^(?<file>[^\r\n]+?)\((?<line>\d+),(?<col>\d+)\): (?<sev>warning|error) (?<code>[A-Za-z]+\d+): ",
            RegexOptions.Compiled);

        private static readonly Regex CompilerCodeRx = new Regex(@"\b(?:warning|error) (?<code>[A-Za-z]+\d+):", RegexOptions.Compiled);

        /// <summary>Parses a Console line that is a compiler message (Unity echoes these after a reload).</summary>
        internal static bool TryParseCompilerLine(string message, out string file, out int line, out int column,
            out bool isError, out string code)
        {
            var m = string.IsNullOrEmpty(message) ? Match.Empty : CompilerLine.Match(message);
            file = m.Success ? NormalizePath(m.Groups["file"].Value) : null;
            line = m.Success ? int.Parse(m.Groups["line"].Value) : 0;
            column = m.Success ? int.Parse(m.Groups["col"].Value) : 0;
            isError = m.Success && m.Groups["sev"].Value == "error";
            code = m.Success ? m.Groups["code"].Value : null;
            return m.Success;
        }

        /// <summary>"CS0618" from a compiler message, or null.</summary>
        internal static string CompilerCode(string message)
        {
            var m = string.IsNullOrEmpty(message) ? Match.Empty : CompilerCodeRx.Match(message);
            return m.Success ? m.Groups["code"].Value : null;
        }

        private static string s_ProjectRoot;

        private static string ProjectRoot =>
            s_ProjectRoot ??= Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";

        public static List<StackFrameInfo> Parse(string stackTrace)
        {
            var frames = new List<StackFrameInfo>();
            if (string.IsNullOrEmpty(stackTrace)) return frames;

            foreach (var raw in stackTrace.Split(LineBreak))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;

                if (line.StartsWith("Rethrow as ", StringComparison.Ordinal))
                {
                    frames.Add(new StackFrameInfo { Method = line.Trim(), IsSeparator = true });
                    continue;
                }

                var frame = new StackFrameInfo();
                var unity = UnityAt.Match(line);
                if (unity.Success)
                {
                    frame.Method = line.Substring(0, unity.Index).Trim();
                    frame.File = NormalizePath(unity.Groups["file"].Value);
                    frame.Line = int.Parse(unity.Groups["line"].Value);
                }
                else
                {
                    var net = DotNetAt.Match(line);
                    if (net.Success)
                    {
                        frame.Method = net.Groups["method"].Value.Trim();
                        if (net.Groups["file"].Success)
                        {
                            frame.File = NormalizePath(net.Groups["file"].Value);
                            frame.Line = int.Parse(net.Groups["line"].Value);
                        }
                    }
                    else
                    {
                        frame.Method = line.Trim();
                    }
                }

                frame.IsUser = IsUserFrame(frame);
                frames.Add(frame);
            }

            return frames;
        }

        /// <summary>
        /// "Namespace.Type:Method (args)" (Unity) or "Namespace.Type.Method (args)" (.NET) → "Type.Method",
        /// for readable stack frames; the full name stays in the tooltip.
        /// </summary>
        internal static string ShortMethod(string method)
        {
            if (string.IsNullOrEmpty(method)) return method;
            string m = method;
            int paren = m.IndexOf(" (", StringComparison.Ordinal);
            if (paren < 0) paren = m.IndexOf('(');
            if (paren > 0) m = m.Substring(0, paren);
            m = m.Trim();

            string type, name;
            int colon = m.LastIndexOf(':');
            int ctor = m.LastIndexOf("..", StringComparison.Ordinal); // .NET constructors: Type..ctor
            if (colon > 0)
            {
                type = m.Substring(0, colon);
                name = m.Substring(colon + 1);
            }
            else if (ctor > 0)
            {
                type = m.Substring(0, ctor);
                name = m.Substring(ctor + 1);
            }
            else
            {
                int dot = m.LastIndexOf('.');
                if (dot <= 0) return m;
                type = m.Substring(0, dot);
                name = m.Substring(dot + 1);
            }

            int typeDot = type.LastIndexOf('.');
            if (typeDot >= 0) type = type.Substring(typeDot + 1);
            return name.StartsWith(".", StringComparison.Ordinal) ? type + name : type + "." + name;
        }

        /// <summary>Project-relative "Assets/…" / "Packages/…" path, or the path as given (null if unknown).</summary>
        internal static string NormalizePath(string file)
        {
            if (string.IsNullOrEmpty(file) || file.StartsWith("<", StringComparison.Ordinal)) return null;
            file = file.Replace('\\', '/');
            if (file.StartsWith(ProjectRoot, StringComparison.OrdinalIgnoreCase))
                file = file.Substring(ProjectRoot.Length);
            if (file.StartsWith("./", StringComparison.Ordinal)) file = file.Substring(2);
            return file;
        }

        internal static bool IsUserFrame(StackFrameInfo f)
        {
            if (f.IsSeparator || string.IsNullOrEmpty(f.File) || string.IsNullOrEmpty(f.Method)) return false;

            for (int i = 0; i < NonUserPrefixes.Length; i++)
                if (f.Method.StartsWith(NonUserPrefixes[i], StringComparison.Ordinal)) return false;

            if (f.File.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            if (f.File.StartsWith("Packages/", StringComparison.Ordinal))
                return !f.File.StartsWith("Packages/com.unity.", StringComparison.Ordinal);
            if (f.File.StartsWith("Library/PackageCache/", StringComparison.Ordinal))
                return !f.File.StartsWith("Library/PackageCache/com.unity.", StringComparison.Ordinal);
            return false; // absolute paths outside the project = Unity / build machine sources
        }

        /// <summary>First frame in your own code, if any.</summary>
        public static bool TryGetFirstUserFrame(List<StackFrameInfo> frames, out StackFrameInfo frame)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].IsUser)
                {
                    frame = frames[i];
                    return true;
                }
            }

            frame = default;
            return false;
        }

        public static string StripRichText(string text) =>
            string.IsNullOrEmpty(text) || text.IndexOf('<') < 0 ? text : RichText.Replace(text, "");

        /// <summary>Splits a leading "[Tag] " off a plain Debug.Log message.</summary>
        public static bool TrySplitTag(string message, out string tag, out string rest)
        {
            bool found = TrySplitTags(message, out var tags, out _);
            tag = found ? tags[0] : null;
            if (found)
            {
                var m = LeadingTag.Match(message);
                rest = message.Substring(m.Length);
            }
            else
            {
                rest = message;
            }

            return found;
        }

        /// <summary>Splits every leading tag off a message: "[UI][Net] text" or "[UI] [Net] text" → UI, Net.</summary>
        public static bool TrySplitTags(string message, out List<string> tags, out string rest)
        {
            tags = null;
            rest = message;
            if (string.IsNullOrEmpty(message)) return false;

            int pos = 0;
            while (pos < message.Length)
            {
                var m = LeadingTag.Match(message, pos);
                if (!m.Success || m.Index != pos) break;
                (tags ??= new List<string>()).Add(m.Groups["tag"].Value);
                pos += m.Length;
            }

            if (tags == null) return false;
            rest = message.Substring(pos);
            return true;
        }

        /// <summary>
        /// Stable id for "the same log": kind + system + first message line with numbers/ids masked +
        /// file and method of the first user frame (line numbers excluded so edits don't split groups).
        /// </summary>
        public static string Fingerprint(EntryKind kind, string system, string message, List<StackFrameInfo> frames)
        {
            string first = message ?? "";
            int nl = first.IndexOf('\n');
            if (nl >= 0) first = first.Substring(0, nl);
            if (first.Length > 300) first = first.Substring(0, 300);
            first = Numbers.Replace(first, "#");

            var sb = new StringBuilder();
            sb.Append((int)kind).Append('|').Append(system).Append('|').Append(first);
            if (frames != null && TryGetFirstUserFrame(frames, out var f))
                sb.Append('|').Append(f.File).Append('|').Append(f.Method);

            return Hash(sb.ToString());
        }

        internal static string Hash(string s)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 1099511628211UL;
                }

                return h.ToString("x16").Substring(0, 12);
            }
        }
    }
}
#endif
