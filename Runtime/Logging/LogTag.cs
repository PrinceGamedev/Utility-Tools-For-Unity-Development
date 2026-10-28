using System;
using System.Collections.Generic;

namespace UtilityTools.Logging
{
    /// <summary>
    /// A log tag with its colour, defined once in code and used anywhere. Keep the tags of a project in
    /// one static class:
    /// <code>
    /// public static class Tags
    /// {
    ///     public static readonly LogTag UI = new LogTag("UI", "#4FC3F7");
    ///     public static readonly LogTag GAEvents = new LogTag("GAEvents", "#F2C14E");
    ///     public static readonly LogTag ProfilePicture = new LogTag("ProfilePicture", "#B388FF");
    /// }
    ///
    /// Debug.Log($"{Tags.GAEvents}{Tags.ProfilePicture} Uploaded");   // "[GAEvents][ProfilePicture] Uploaded"
    /// Systems.Ads.Tagged(Tags.UI).Info("Banner shown");
    /// ULog.Tagged(Tags.UI, Tags.GAEvents).Warning("Tap ignored");
    /// </code>
    /// The AI Console colours the tag everywhere (rows, tags bar, Unity's Console) and lists it in the tags
    /// bar even before a log uses it. Tag names match case-insensitively, so a hand-written "[ui]" is the
    /// same tag.
    /// </summary>
    public sealed class LogTag
    {
        private static readonly List<LogTag> s_All = new List<LogTag>();
        private static readonly object s_Lock = new object();

        /// <summary>Raised when a tag is constructed (the editor refreshes colours here).</summary>
        internal static event Action<LogTag> Registered;

        /// <summary>Every tag constructed so far in this domain.</summary>
        public static IReadOnlyList<LogTag> All
        {
            get { lock (s_Lock) return s_All.ToArray(); }
        }

        /// <summary>Name without brackets, e.g. "UI".</summary>
        public string Name { get; }

        /// <summary>Hex colour ("#4FC3F7"); generated from the name when not given.</summary>
        public string ColorHex { get; }

        public LogTag(string name, string colorHex = null)
        {
            Name = Clean(name);
            if (Name.Length == 0) throw new ArgumentException("A LogTag needs a name.", nameof(name));
            ColorHex = string.IsNullOrEmpty(colorHex) ? LogChannel.ColorFromName(Name) : LogChannel.NormalizeHex(colorHex);

            lock (s_Lock) s_All.Add(this);
            Registered?.Invoke(this);
        }

        /// <summary>"[Name]" — so <c>$"{Tags.UI} Opened"</c> writes a tag Unity's Console and the AI Console understand.</summary>
        public override string ToString() => "[" + Name + "]";

        /// <summary>The tag defined with this name (case-insensitive), or null.</summary>
        public static LogTag Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (s_Lock)
            {
                // Last definition wins, so a re-created tag (domain reload, tests) takes effect.
                for (int i = s_All.Count - 1; i >= 0; i--)
                    if (string.Equals(s_All[i].Name, name, StringComparison.OrdinalIgnoreCase)) return s_All[i];
            }

            return null;
        }

        internal static string[] Names(LogTag[] tags)
        {
            if (tags == null) return null;
            var names = new List<string>(tags.Length);
            foreach (var t in tags)
                if (t != null) names.Add(t.Name);
            return names.ToArray();
        }

        private static string Clean(string name) => (name ?? "").Trim().Trim('[', ']').Trim();
    }
}
