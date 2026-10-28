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
        private static readonly Regex LeadingTag = new Regex(@"^\[(?<tag>[A-Za-z][\w .-]{0,31})\]\s*", RegexOptions.Compiled);

        private static readonly Regex Numbers = new Regex(@"0x[0-9a-fA-F]+|[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}|\d+(\.\d+)?", RegexOptions.Compiled);

        private static readonly string[] NonUserPrefixes =
        {
            "UnityEngine.", "UnityEditor.", "UnityEditorInternal.", "Unity.", "System.", "Mono.",
            "UtilityTools.Logging.", "UtilityTools.AIConsole."
        };

        private static string s_ProjectRoot;

        private static string ProjectRoot =>
            s_ProjectRoot ??= Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";

        public static List<StackFrameInfo> Parse(string stackTrace)
        {
            var frames = new List<StackFrameInfo>();
            if (string.IsNullOrEmpty(stackTrace)) return frames;

            foreach (var raw in stackTrace.Split('\n'))
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
            var m = string.IsNullOrEmpty(message) ? Match.Empty : LeadingTag.Match(message);
            if (m.Success)
            {
                tag = m.Groups["tag"].Value;
                rest = message.Substring(m.Length);
                return true;
            }

            tag = null;
            rest = message;
            return false;
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
