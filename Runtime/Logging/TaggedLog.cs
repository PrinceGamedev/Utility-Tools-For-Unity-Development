using System;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UtilityTools.Logging
{
    /// <summary>
    /// A channel plus extra tags for one log call: <c>Systems.Spawn.Tagged("UI", "Boss").Info("Opened")</c>.
    /// Like the channel's own methods, Verbose / Info / Warning calls are compiled out of release builds —
    /// including the <c>Tagged(...)</c> call itself, because C# omits the receiver of a conditional call.
    /// </summary>
    public readonly struct TaggedLog
    {
        private const string Editor = "UNITY_EDITOR";
        private const string DevBuild = "DEVELOPMENT_BUILD";
        private const string ForceLogs = "UTILITYTOOLS_LOGS";

        private readonly LogChannel _channel;
        private readonly string[] _tags;

        internal TaggedLog(LogChannel channel, string[] tags)
        {
            _channel = channel;
            _tags = tags;
        }

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Verbose(string message, params (string key, object value)[] fields)
            => ULog.Emit(_channel, LogLevel.Verbose, message, null, fields, tags: _tags);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Info(string message, params (string key, object value)[] fields)
            => ULog.Emit(_channel, LogLevel.Info, message, null, fields, tags: _tags);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Info(string message, Object context, params (string key, object value)[] fields)
            => ULog.Emit(_channel, LogLevel.Info, message, context, fields, tags: _tags);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Warning(string message, params (string key, object value)[] fields)
            => ULog.Emit(_channel, LogLevel.Warning, message, null, fields, tags: _tags);

        [HideInCallstack, Conditional(Editor), Conditional(DevBuild), Conditional(ForceLogs)]
        public void Warning(string message, Object context, params (string key, object value)[] fields)
            => ULog.Emit(_channel, LogLevel.Warning, message, context, fields, tags: _tags);

        [HideInCallstack]
        public void Error(string message, params (string key, object value)[] fields)
            => ULog.Emit(_channel, LogLevel.Error, message, null, fields, tags: _tags);

        [HideInCallstack]
        public void Error(string message, Object context, params (string key, object value)[] fields)
            => ULog.Emit(_channel, LogLevel.Error, message, context, fields, tags: _tags);

        [HideInCallstack]
        public void Exception(Exception exception, Object context = null)
            => ULog.Emit(_channel, LogLevel.Error, exception?.Message, context, null, exception, tags: _tags);
    }
}
