#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UtilityTools.Logging;

namespace UtilityTools.AIConsole
{
    [Serializable]
    internal sealed class ChannelSetting
    {
        public string name;
        public bool enabled = true;
        public LogLevel minLevel = LogLevel.Verbose;
        public string color; // empty = channel's own colour
    }

    internal enum ChannelOverride { Default, On, Off }

    /// <summary>A custom tag colour, e.g. UI → blue (team setting).</summary>
    [Serializable]
    internal sealed class TagSetting
    {
        public string name;
        public string color;
    }

    [Serializable]
    internal sealed class ChannelUserSetting
    {
        public string name;
        public ChannelOverride mode;
    }

    /// <summary>Team settings — ProjectSettings/UtilityToolsConsole.asset (commit it).</summary>
    [FilePath("ProjectSettings/UtilityToolsConsole.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ConsoleProjectSettings : ScriptableSingleton<ConsoleProjectSettings>
    {
        // Logging
        public LogLevel globalMinLevel = LogLevel.Verbose;
        public bool richTextTags = true;
        public List<ChannelSetting> channels = new List<ChannelSetting>();
        public List<TagSetting> tags = new List<TagSetting>();

        // Capture
        public int maxEntries = 10000;

        // AI bridge
        public bool aiFilesEnabled = true;
        public string aiFolder = "Logs/AIConsole";
        public int aiMaxFileMB = 8;
        public bool aiIncludeInfoLogs = true;
        public int aiTopGroups = 10;
        public int aiUserFrames = 3;

        // AI agents: commands.txt and the managed section in CLAUDE.md / AGENTS.md
        public bool aiCommandsEnabled = true;
        public bool aiAllowMenuCommands;
        public bool agentAutoUpdate = true;

        public static event Action Changed;

        public void SaveAndNotify()
        {
            Save(true);
            Changed?.Invoke();
        }

        public ChannelSetting Find(string channel)
        {
            for (int i = 0; i < channels.Count; i++)
                if (channels[i].name == channel) return channels[i];
            return null;
        }

        public TagSetting FindTag(string tag)
        {
            for (int i = 0; i < tags.Count; i++)
                if (string.Equals(tags[i].name, tag, StringComparison.OrdinalIgnoreCase)) return tags[i];
            return null;
        }

        /// <summary>Adds or recolours a custom tag.</summary>
        public void SetTagColor(string tag, string colorHex)
        {
            var t = FindTag(tag);
            if (t == null) tags.Add(t = new TagSetting { name = tag.Trim() });
            t.color = LogChannel.NormalizeHex(colorHex);
        }

        public ChannelSetting GetOrAdd(string channel)
        {
            var s = Find(channel);
            if (s == null)
            {
                s = new ChannelSetting { name = channel };
                channels.Add(s);
            }

            return s;
        }
    }

    internal enum RowDensity { Compact, Comfortable }

    /// <summary>Where the details pane docks. Auto = right when the window is wide, bottom otherwise.</summary>
    internal enum DetailsDock { Auto, Bottom, Right }

    /// <summary>Side strip: icons + names, icons only, or hidden (a thin edge to bring it back).</summary>
    internal enum RailMode { Expanded, Compact, Hidden }

    /// <summary>Personal settings — UserSettings/UtilityToolsConsole.asset (not committed by default).</summary>
    [FilePath("UserSettings/UtilityToolsConsole.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ConsoleUserSettings : ScriptableSingleton<ConsoleUserSettings>
    {
        public bool pauseOnError;
        public bool clearOnPlay = true;
        public bool collapse;
        public bool follow = true;
        public bool showTimeline = true;
        public bool showEngineFrames;
        public RowDensity density = RowDensity.Compact;
        public float detailHeight = 260f;
        public float detailWidth = 420f;
        public DetailsDock detailsDock = DetailsDock.Auto;
        public bool detailsOpen = true;
        public bool systemsOpen = true;
        public RailMode railMode = RailMode.Expanded;

        /// <summary>Text size step 0–5 (11 → 18 px); scales rows, details, chips and the side strip.</summary>
        public int textSize = 2;

        // Tag filter (persisted): selected tags and whether a log must have all of them or any.
        public List<string> tagFilter = new List<string>();
        public bool tagMatchAll;
        public bool tagEditorOpen;

        /// <summary>"Not now" on the AI agent setup card.</summary>
        public bool agentSetupDismissed;
        public string search = "";
        public bool showLog = true, showWarning = true, showError = true;
        public string sortMode = "time";
        public List<ChannelUserSetting> overrides = new List<ChannelUserSetting>();

        public static event Action Changed;

        public void SaveAndNotify()
        {
            Save(true);
            Changed?.Invoke();
        }

        /// <summary>Persist UI preferences without re-applying channel settings.</summary>
        public void SaveQuiet() => Save(true);

        public ChannelOverride GetOverride(string channel)
        {
            for (int i = 0; i < overrides.Count; i++)
                if (overrides[i].name == channel) return overrides[i].mode;
            return ChannelOverride.Default;
        }

        public void SetOverride(string channel, ChannelOverride mode)
        {
            overrides.RemoveAll(o => o.name == channel);
            if (mode != ChannelOverride.Default) overrides.Add(new ChannelUserSetting { name = channel, mode = mode });
        }
    }

    /// <summary>Applies project + personal settings to live <see cref="LogChannel"/>s.</summary>
    [InitializeOnLoad]
    internal static class ChannelConfig
    {
        static ChannelConfig()
        {
            LogChannel.Registered += OnRegistered;
            ConsoleProjectSettings.Changed += ApplyAll;
            ConsoleUserSettings.Changed += ApplyAll;
            EditorApplication.delayCall += ApplyAll; // settings singletons load lazily; apply once ready
        }

        private static void OnRegistered(LogChannel channel)
        {
            // Channels may be created from static initializers on any thread; settings are main-thread only.
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == ULog.MainThreadId) Apply(channel);
            else EditorApplication.delayCall += () => Apply(channel);
        }

        // Snapshot for ULog.TagColor, which can run on any thread (settings are main-thread only).
        private static volatile Dictionary<string, string> s_TagColors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void ApplyAll()
        {
            var project = ConsoleProjectSettings.instance;
            ULog.MinLevel = project.globalMinLevel;
            ULog.RichTextTags = project.richTextTags;

            var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in project.tags)
                if (!string.IsNullOrEmpty(t.name) && !string.IsNullOrEmpty(t.color)) colors[t.name] = LogChannel.NormalizeHex(t.color);
            s_TagColors = colors;
            ULog.TagColor = tag => s_TagColors.TryGetValue(tag, out var c) ? c : LogChannel.ColorFromName(tag);

            foreach (var channel in LogChannel.All) Apply(channel);
        }

        private static void Apply(LogChannel channel)
        {
            var s = ConsoleProjectSettings.instance.Find(channel.Name);
            bool enabled = s?.enabled ?? true;
            switch (ConsoleUserSettings.instance.GetOverride(channel.Name))
            {
                case ChannelOverride.On: enabled = true; break;
                case ChannelOverride.Off: enabled = false; break;
            }

            channel.Enabled = enabled;
            channel.MinLevel = s?.minLevel ?? LogLevel.Verbose;
            if (!string.IsNullOrEmpty(s?.color)) channel.ColorHex = LogChannel.NormalizeHex(s.color);
        }

        /// <summary>
        /// Colour for a tag or system: custom tag colour, else configured channel colour, else the live
        /// channel's colour, else a stable colour generated from the name.
        /// </summary>
        public static string ColorFor(string system)
        {
            if (string.IsNullOrEmpty(system)) return null;
            var tag = ConsoleProjectSettings.instance.FindTag(system);
            if (!string.IsNullOrEmpty(tag?.color)) return LogChannel.NormalizeHex(tag.color);
            var s = ConsoleProjectSettings.instance.Find(system);
            if (!string.IsNullOrEmpty(s?.color)) return LogChannel.NormalizeHex(s.color);
            foreach (var c in LogChannel.All)
                if (c.Name == system) return c.ColorHex;
            return LogChannel.ColorFromName(system);
        }
    }
}
#endif
