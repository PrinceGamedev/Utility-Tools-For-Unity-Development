using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UtilityTools.Logging
{
    /// <summary>
    /// A named log source for one game system ("Spawn", "Network", …) with its own colour and on/off
    /// switch. Declare channels once as static fields and log through them:
    /// <code>
    /// public static class Systems
    /// {
    ///     public static readonly LogChannel Spawn = new LogChannel("Spawn", "#2ecc71");
    /// }
    /// Systems.Spawn.Info("Spawned enemies", ("count", 3), ("wave", 2));
    /// </code>
    /// Verbose / Info / Warning / Assert / *Once calls are removed by the compiler — including building
    /// their arguments — unless UNITY_EDITOR, DEVELOPMENT_BUILD or UTILITYTOOLS_LOGS is defined.
    /// Error and Exception are always compiled in.
    /// </summary>
    public sealed class LogChannel
    {
        // [Conditional] symbols are OR'ed: the call survives if any one is defined at the call site.
        private const string Editor = "UNITY_EDITOR";
        private const string DevBuild = "DEVELOPMENT_BUILD";
        private const string ForceLogs = "UTILITYTOOLS_LOGS";

        private static readonly List<LogChannel> s_All = new List<LogChannel>();
        private static readonly object s_Lock = new object();

        /// <summary>Raised when a channel is constructed (the editor applies project settings here).</summary>
        internal static event Action<LogChannel> Registered;

        /// <summary>Every channel constructed so far in this domain.</summary>
        public static IReadOnlyList<LogChannel> All
        {
            get { lock (s_Lock) return s_All.ToArray(); }
        }

        public string Name { get; }

        /// <summary>Hex colour ("#2ecc71"); generated from the name when not given.</summary>
        public string ColorHex { get; internal set; }

        /// <summary>Runtime switch. Errors and exceptions are logged even when disabled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Logs below this level are dropped (errors are never dropped).</summary>
        public LogLevel MinLevel { get; set; } = LogLevel.Verbose;

        public LogChannel(string name, string colorHex = null)
        {
            Name = string.IsNullOrEmpty(name) ? "General" : name;
            ColorHex = string.IsNullOrEmpty(colorHex) ? ColorFromName(Name) : NormalizeHex(colorHex);

            lock (s_Lock) s_All.Add(this);
            Registered?.Invoke(this);
        }

        /// <summary>True when a log at <paramref name="level"/> would be written. Use it to guard expensive messages.</summary>
        public bool IsOn(LogLevel level) => ULog.IsOn(this, level);

        /// <summary>Shorthand for <c>IsOn(LogLevel.Info)</c>.</summary>
        public bool IsInfoOn => IsOn(LogLevel.Info);

        public override string ToString() => Name;

        // ---------- Verbose ----------
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Verbose(string message, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Verbose, message, null, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Verbose(string message, Object context, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Verbose, message, context, fields);

        /// <summary>The message is only built when the channel is on.</summary>
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Verbose(Func<string> message)
        {
            if (IsOn(LogLevel.Verbose)) ULog.Emit(this, LogLevel.Verbose, message?.Invoke(), null, null);
        }

        // ---------- Info ----------
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Info(string message, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Info, message, null, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Info(string message, Object context, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Info, message, context, fields);

        /// <summary>The message is only built when the channel is on.</summary>
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Info(Func<string> message)
        {
            if (IsOn(LogLevel.Info)) ULog.Emit(this, LogLevel.Info, message?.Invoke(), null, null);
        }

        /// <summary>Logs only the first time this call site is reached (per domain).</summary>
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void InfoOnce(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (ULog.FirstTime(file, line)) ULog.Emit(this, LogLevel.Info, message, null, null);
        }

        // ---------- Warning ----------
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Warning(string message, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Warning, message, null, fields);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Warning(string message, Object context, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Warning, message, context, fields);

        /// <summary>The message is only built when the channel is on.</summary>
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Warning(Func<string> message)
        {
            if (IsOn(LogLevel.Warning)) ULog.Emit(this, LogLevel.Warning, message?.Invoke(), null, null);
        }

        /// <summary>Logs only the first time this call site is reached (per domain).</summary>
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void WarningOnce(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (ULog.FirstTime(file, line)) ULog.Emit(this, LogLevel.Warning, message, null, null);
        }

        // ---------- Error / Exception (always compiled) ----------
        [HideInCallstack]
        public void Error(string message, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Error, message, null, fields);

        [HideInCallstack]
        public void Error(string message, Object context, params (string key, object value)[] fields)
            => ULog.Emit(this, LogLevel.Error, message, context, fields);

        [HideInCallstack]
        public void Exception(Exception exception, Object context = null)
            => ULog.Emit(this, LogLevel.Error, exception?.Message, context, null, exception);

        // ---------- Assert (debug only) ----------
        /// <summary>Logs an assert-type error when <paramref name="condition"/> is false. Removed from release builds.</summary>
        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Assert(bool condition, string message, Object context = null)
        {
            if (!condition) ULog.Emit(this, LogLevel.Error, message, context, null, null, isAssert: true);
        }

        // ---------- Colours ----------
        internal static string NormalizeHex(string hex)
        {
            hex = hex.Trim();
            return hex[0] == '#' ? hex : "#" + hex;
        }

        /// <summary>Stable, readable colour per name (same name → same colour on every machine).</summary>
        internal static string ColorFromName(string name)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in name) h = (h ^ c) * 16777619;
                float hue = (h % 360) / 360f;
                return "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(hue, 0.55f, 0.95f));
            }
        }
    }
}
