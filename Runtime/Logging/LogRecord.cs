using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Object = UnityEngine.Object;

namespace UtilityTools.Logging
{
    /// <summary>
    /// One structured log, as produced by <see cref="LogChannel"/> / <see cref="ULog"/>. Delivered to
    /// <see cref="ULog.Logged"/> subscribers (custom outputs) before it is written to Unity's log.
    /// </summary>
    public sealed class LogRecord
    {
        public LogChannel Channel { get; }
        public LogLevel Level { get; }
        public string Message { get; }
        public Object Context { get; }
        public IReadOnlyList<KeyValuePair<string, object>> Fields { get; }

        /// <summary>Extra tags besides the channel (from <see cref="LogChannel.Tagged"/>). Never null.</summary>
        public IReadOnlyList<string> Tags { get; }
        public Exception Exception { get; }
        public bool IsAssert { get; }
        public DateTime TimeUtc { get; }

        /// <summary>Time.frameCount, or -1 when logged from a background thread.</summary>
        public int Frame { get; }

        public int ThreadId { get; }

        private static readonly KeyValuePair<string, object>[] NoFields = new KeyValuePair<string, object>[0];
        private static readonly string[] NoTags = new string[0];

        internal LogRecord(LogChannel channel, LogLevel level, string message, Object context,
            (string key, object value)[] fields, Exception exception, bool isAssert, int frame, int threadId,
            string[] tags = null)
        {
            Channel = channel;
            Tags = tags != null && tags.Length > 0 ? tags : NoTags;
            Level = level;
            Message = message ?? "";
            Context = context;
            Exception = exception;
            IsAssert = isAssert;
            TimeUtc = DateTime.UtcNow;
            Frame = frame;
            ThreadId = threadId;

            if (fields == null || fields.Length == 0)
            {
                Fields = NoFields;
            }
            else
            {
                var list = new KeyValuePair<string, object>[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    list[i] = new KeyValuePair<string, object>(fields[i].key ?? "", fields[i].value);
                Fields = list;
            }
        }

        /// <summary>Invariant-culture string for a field value (what the Console and AI files show).</summary>
        public static string FormatValue(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case float f: return f.ToString("G", CultureInfo.InvariantCulture);
                case double d: return d.ToString("G", CultureInfo.InvariantCulture);
                case IFormattable fmt: return fmt.ToString(null, CultureInfo.InvariantCulture);
                case Object o: return o ? o.name : "null";
                default: return value.ToString();
            }
        }

        /// <summary>"key=value, key=value", or empty when there are no fields.</summary>
        public string FormatFields()
        {
            if (Fields.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < Fields.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Fields[i].Key).Append('=').Append(FormatValue(Fields[i].Value));
            }

            return sb.ToString();
        }
    }
}
