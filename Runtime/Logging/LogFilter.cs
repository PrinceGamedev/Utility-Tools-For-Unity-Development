using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UtilityTools.Logging
{
    /// <summary>
    /// Keeps only the logs whose tags you picked; everything else is dropped before Unity writes it (so no
    /// output and no stack trace). Pick the tags per build type in Project Settings → Utility Tools →
    /// AI Console → Logs in builds; the build stores them and the player applies them on start. The Editor
    /// always shows every log.
    ///
    /// A log's tags are its ULog system, its Tagged(...) tags, and the leading "[Tag]" prefixes of a plain
    /// <c>Debug.Log</c> ("[UI][GA] Opened" → UI, GA). A log is kept when any of its tags is picked. Logs
    /// without a tag (most third-party code) and errors / exceptions each have their own switch.
    /// </summary>
    public static class LogFilter
    {
        internal sealed class Rules
        {
            public readonly HashSet<string> Tags;
            public readonly bool KeepUntagged;
            public readonly bool KeepErrors;

            public Rules(IEnumerable<string> tags, bool keepUntagged, bool keepErrors)
            {
                Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (tags != null)
                    foreach (var t in tags)
                        if (!string.IsNullOrEmpty(t)) Tags.Add(t.Trim());
                KeepUntagged = keepUntagged;
                KeepErrors = keepErrors;
            }
        }

        private static volatile Rules s_Rules; // null = every log passes

        /// <summary>True when logs are being filtered by tag.</summary>
        public static bool Active => s_Rules != null;

        /// <summary>The picked tags (empty when no filter is active).</summary>
        public static IReadOnlyCollection<string> Tags => (IReadOnlyCollection<string>)s_Rules?.Tags ?? Array.Empty<string>();

        /// <summary>
        /// Filters logs from now on, e.g. from an in-game debug menu: only logs with one of
        /// <paramref name="tags"/> pass, plus untagged logs and errors when their switches are on.
        /// </summary>
        public static void Configure(IEnumerable<string> tags, bool keepUntagged = false, bool keepErrors = true)
        {
            SetRules(new Rules(tags, keepUntagged, keepErrors));
            Install();
        }

        /// <summary>Stops filtering: every log passes again.</summary>
        public static void Clear() => s_Rules = null;

        internal static void SetRules(Rules rules) => s_Rules = rules;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void LoadFromBuild()
        {
#if !UNITY_EDITOR
            var data = Resources.Load<LogFilterData>(LogFilterData.ResourceName);
            if (data == null) return; // this build keeps every log
            Configure(data.tags, data.keepUntagged, data.keepErrors);
            Resources.UnloadAsset(data);
#endif
        }

        private static void Install()
        {
            var logger = Debug.unityLogger;
            if (!(logger.logHandler is Handler)) logger.logHandler = new Handler(logger.logHandler);
        }

        private static bool IsError(LogType type) => type == LogType.Error || type == LogType.Assert || type == LogType.Exception;

        /// <summary>Decision for a ULog call: its system (unless the default channel) and its tags.</summary>
        internal static bool PassesULog(LogType type, LogChannel channel, string[] tags)
        {
            var rules = s_Rules;
            if (rules == null || (rules.KeepErrors && IsError(type))) return true;

            bool tagged = false;
            if (channel != null && channel != ULog.Default)
            {
                tagged = true;
                if (rules.Tags.Contains(channel.Name)) return true;
            }

            if (tags != null)
                foreach (var t in tags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    tagged = true;
                    if (rules.Tags.Contains(t)) return true;
                }

            return !tagged && rules.KeepUntagged;
        }

        /// <summary>Decision for a plain Debug.Log line, from its leading "[Tag]" prefixes.</summary>
        internal static bool PassesMessage(LogType type, string message)
        {
            var rules = s_Rules;
            if (rules == null || (rules.KeepErrors && IsError(type))) return true;

            bool tagged = false;
            int pos = 0;
            if (message != null)
            {
                while (pos < message.Length && message[pos] == ' ') pos++;
                while (TryReadTag(message, ref pos, out var tag))
                {
                    tagged = true;
                    if (rules.Tags.Contains(tag)) return true;
                }
            }

            return !tagged && rules.KeepUntagged;
        }

        /// <summary>
        /// Reads "[Tag]" at <paramref name="pos"/> and the spaces after it. Same rule as the AI Console: the
        /// name starts with a letter, so "[12:00]" is not a tag.
        /// </summary>
        private static bool TryReadTag(string s, ref int pos, out string tag)
        {
            tag = null;
            if (pos + 2 >= s.Length || s[pos] != '[' || !char.IsLetter(s[pos + 1])) return false;

            int end = pos + 2;
            while (end < s.Length && end - pos <= 32)
            {
                char c = s[end];
                if (c == ']') break;
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == ' ' || c == '.' || c == '-')) return false;
                end++;
            }

            if (end >= s.Length || s[end] != ']') return false;
            tag = s.Substring(pos + 1, end - pos - 1);
            pos = end + 1;
            while (pos < s.Length && s[pos] == ' ') pos++;
            return true;
        }

        /// <summary>Text of a Debug.Log call as it reaches the log handler.</summary>
        internal static string MessageOf(string format, object[] args)
        {
            if (format == "{0}" && args != null && args.Length == 1) return args[0] as string ?? args[0]?.ToString();
            return format;
        }

        private sealed class Handler : ILogHandler
        {
            private readonly ILogHandler _inner;

            public Handler(ILogHandler inner) => _inner = inner;

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                // ULog decided already (it knows the system and tags without parsing text).
                if (ULog.Current == null && !PassesMessage(logType, MessageOf(format, args))) return;
                _inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, Object context)
            {
                if (ULog.Current == null && !PassesMessage(LogType.Exception, null)) return;
                _inner.LogException(exception, context);
            }
        }
    }
}
