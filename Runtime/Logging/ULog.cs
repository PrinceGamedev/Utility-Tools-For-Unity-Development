using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace UtilityTools.Logging
{
    /// <summary>
    /// Utility Tools logging entry point. Use <see cref="LogChannel"/>s for per-system logs, or the
    /// static methods here for quick logs on the <see cref="Default"/> channel.
    ///
    /// Build behaviour:
    /// <list type="bullet">
    /// <item>Verbose / Info / Warning / Assert are compiled out of release builds (the call and its
    /// arguments). Define <c>UTILITYTOOLS_LOGS</c> to keep them.</item>
    /// <item>Error / Exception are kept. Define <c>UTILITYTOOLS_STRIP_ERRORS</c> to make them no-ops in
    /// release builds.</item>
    /// <item>Define <c>UTILITYTOOLS_SILENCE_UNITY_LOGS</c> to also drop plain <c>Debug.Log</c> /
    /// <c>LogWarning</c> output (third-party code) in release builds; errors still come through.</item>
    /// </list>
    /// </summary>
    public static class ULog
    {
        private const string Editor = "UNITY_EDITOR";
        private const string DevBuild = "DEVELOPMENT_BUILD";
        private const string ForceLogs = "UTILITYTOOLS_LOGS";

        /// <summary>Channel used by the static ULog.Info / Warning / … methods. Logged without a [tag].</summary>
        public static readonly LogChannel Default = new LogChannel("General", "#9aa5b1");

        /// <summary>Master switch for Verbose / Info / Warning. Errors are always logged.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Global minimum level, applied on top of each channel's own MinLevel.</summary>
        public static LogLevel MinLevel { get; set; } = LogLevel.Verbose;

        /// <summary>
        /// Raised for every log that passes the filters, before it reaches Unity's log. Subscribe to add
        /// your own outputs (crash reporter, in-game overlay, file in dev builds…). Called on the logging
        /// thread; exceptions thrown by handlers are caught and reported once.
        /// </summary>
        public static event Action<LogRecord> Logged;

        /// <summary>
        /// Optional custom line format for Unity's Console / player log. Default: "[Channel] message (k=v, …)".
        /// </summary>
        public static Func<LogRecord, string> Formatter { get; set; }

        /// <summary>Editor-only: wrap the [Channel] tag in its colour in Unity's Console (set by settings).</summary>
        internal static bool RichTextTags { get; set; }

        /// <summary>The record currently being written to Unity's log on this thread (read by the editor capture).</summary>
        [ThreadStatic] private static LogRecord s_Current;

        [ThreadStatic] private static bool s_InHandler;

        internal static LogRecord Current => s_Current;

        internal static int MainThreadId { get; set; } = -1;

        private static readonly HashSet<string> s_Once = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitRuntime()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            lock (s_Once) s_Once.Clear();
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD && UTILITYTOOLS_SILENCE_UNITY_LOGS
            // Only Error / Assert / Exception pass (Exception is always allowed by Unity's logger).
            Debug.unityLogger.filterLogType = LogType.Assert;
#endif
        }

        // ---------- Static shortcuts on the Default channel ----------
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public static void Verbose(string message, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Verbose, message, null, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public static void Verbose(string message, Object context, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Verbose, message, context, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public static void Info(string message, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Info, message, null, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public static void Info(string message, Object context, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Info, message, context, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public static void Warning(string message, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Warning, message, null, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public static void Warning(string message, Object context, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Warning, message, context, fields);

        [HideInCallstack]
        public static void Error(string message, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Error, message, null, fields);

        [HideInCallstack]
        public static void Error(string message, Object context, params (string key, object value)[] fields)
            => Emit(Default, LogLevel.Error, message, context, fields);

        [HideInCallstack]
        public static void Exception(Exception exception, Object context = null)
            => Emit(Default, LogLevel.Error, exception?.Message, context, null, exception);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public static void Assert(bool condition, string message, Object context = null)
        {
            if (!condition) Emit(Default, LogLevel.Error, message, context, null, null, isAssert: true);
        }

        // ---------- Filtering ----------
        internal static bool IsOn(LogChannel channel, LogLevel level)
        {
            if (level >= LogLevel.Error) return true;
            return Enabled && channel.Enabled && level >= channel.MinLevel && level >= MinLevel;
        }

        internal static bool FirstTime(string file, int line)
        {
            lock (s_Once) return s_Once.Add(file + ":" + line);
        }

        // ---------- Core ----------
        [HideInCallstack]
        internal static void Emit(LogChannel channel, LogLevel level, string message, Object context,
            (string key, object value)[] fields, Exception exception = null, bool isAssert = false)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD && UTILITYTOOLS_STRIP_ERRORS
            if (level >= LogLevel.Error) return;
#endif
            if (!IsOn(channel, level)) return;

            int threadId = Thread.CurrentThread.ManagedThreadId;
            bool mainThread = threadId == MainThreadId;
            var record = new LogRecord(channel, level, message, context, fields, exception, isAssert,
                mainThread ? Time.frameCount : -1, threadId);

            RaiseLogged(record);

            var previous = s_Current;
            s_Current = record;
            try
            {
                if (exception != null)
                    Debug.unityLogger.LogException(exception, context);
                else
                    Debug.unityLogger.Log(ToLogType(level, isAssert), (object)FormatLine(record), context);
            }
            finally
            {
                s_Current = previous;
            }
        }

        private static void RaiseLogged(LogRecord record)
        {
            var handlers = Logged;
            if (handlers == null || s_InHandler) return; // a handler that logs must not recurse
            s_InHandler = true;
            try
            {
                handlers(record);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UtilityTools] A ULog.Logged handler threw: " + e);
            }
            finally
            {
                s_InHandler = false;
            }
        }

        private static LogType ToLogType(LogLevel level, bool isAssert)
        {
            if (isAssert) return LogType.Assert;
            switch (level)
            {
                case LogLevel.Warning: return LogType.Warning;
                case LogLevel.Error: return LogType.Error;
                default: return LogType.Log;
            }
        }

        /// <summary>The line written to Unity's log for <paramref name="record"/>.</summary>
        internal static string FormatLine(LogRecord record)
        {
            var custom = Formatter;
            if (custom != null)
            {
                try { return custom(record); }
                catch (Exception e) { return record.Message + "  (ULog.Formatter threw: " + e.Message + ")"; }
            }

            var sb = new StringBuilder(record.Message.Length + 32);
            if (record.Channel != Default)
            {
                if (RichTextTags)
                    sb.Append("<color=").Append(record.Channel.ColorHex).Append("><b>[")
                        .Append(record.Channel.Name).Append("]</b></color> ");
                else
                    sb.Append('[').Append(record.Channel.Name).Append("] ");
            }

            sb.Append(record.Message);

            string fields = record.FormatFields();
            if (fields.Length > 0) sb.Append("  (").Append(fields).Append(')');
            return sb.ToString();
        }
    }
}
