#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UtilityTools.AIConsole
{
    internal enum EntryKind
    {
        Log,
        Warning,
        Error,
        Exception,
        Assert,
        CompileError,
        CompileWarning,
        Marker
    }

    /// <summary>One captured console line. Serializable with JsonUtility (domain-reload snapshot).</summary>
    [Serializable]
    internal sealed class ConsoleEntry
    {
        public int id;
        public long ticks;          // UTC
        public int frame = -1;      // -1 = background thread / not in a frame
        public int thread;
        public EntryKind kind;
        public string system;       // ULog channel; null for plain Debug.Log / the default channel
        public string color;        // "#rrggbb" for the system
        public string[] tags;       // extra tags: [UI][Boss] prefixes and Tagged(...); null = none
        public string message;      // clean text (no rich-text tags)
        public string stackTrace;   // raw, as Unity reported it
        public string fingerprint;  // groups repeats of the "same" log
        public bool play;
        public string scene;
        public string context;      // hierarchy / asset path of the context object
        public int contextId;
        public string[] fieldKeys;
        public string[] fieldValues;
        public string file;         // compile messages: file; others: first user frame
        public int line;
        public int column;
        public string assembly;     // compile messages: assembly name without .dll ("Assembly-CSharp")

        [NonSerialized] private List<StackFrameInfo> _frames;

        public DateTime TimeUtc => new DateTime(ticks, DateTimeKind.Utc);

        public bool IsError => kind == EntryKind.Error || kind == EntryKind.Exception ||
                               kind == EntryKind.Assert || kind == EntryKind.CompileError;

        public bool IsWarning => kind == EntryKind.Warning || kind == EntryKind.CompileWarning;

        public bool IsCompile => kind == EntryKind.CompileError || kind == EntryKind.CompileWarning;

        public int FieldCount => fieldKeys?.Length ?? 0;

        public int TagCount => tags?.Length ?? 0;

        /// <summary>System first, then extra tags — everything the tag bar can filter on.</summary>
        public IEnumerable<string> AllTags
        {
            get
            {
                if (system != null) yield return system;
                if (tags == null) yield break;
                foreach (var t in tags) yield return t;
            }
        }

        public bool HasTag(string tag)
        {
            if (string.Equals(system, tag, StringComparison.OrdinalIgnoreCase)) return true;
            if (tags == null) return false;
            foreach (var t in tags)
                if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>"[Spawn][UI][Boss]" or "" — compact form used in AI text.</summary>
        public string TagsText
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in AllTags) sb.Append('[').Append(t).Append(']');
                return sb.ToString();
            }
        }

        public string FirstLine
        {
            get
            {
                if (string.IsNullOrEmpty(message)) return "";
                int nl = message.IndexOf('\n');
                return nl < 0 ? message : message.Substring(0, nl).TrimEnd('\r');
            }
        }

        public List<StackFrameInfo> Frames => _frames ??= StackTraceParser.Parse(stackTrace);

        /// <summary>Short machine name used in filters and AI files.</summary>
        public string KindName => KindToName(kind);

        public static string KindToName(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Log: return "log";
                case EntryKind.Warning: return "warning";
                case EntryKind.Error: return "error";
                case EntryKind.Exception: return "exception";
                case EntryKind.Assert: return "assert";
                case EntryKind.CompileError: return "compile-error";
                case EntryKind.CompileWarning: return "compile-warning";
                default: return "marker";
            }
        }
    }

    /// <summary>All occurrences of one fingerprint in this session.</summary>
    internal sealed class EntryGroup
    {
        public string Fingerprint;
        public ConsoleEntry Latest;
        public int Count;
        public int FirstId;
        public long FirstTicks;
    }
}
#endif
