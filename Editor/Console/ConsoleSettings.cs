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

        // Capture
        public int maxEntries = 10000;

        // AI bridge
        public bool aiFilesEnabled = true;
        public string aiFolder = "Logs/AIConsole";
        public int aiMaxFileMB = 8;
        public bool aiIncludeInfoLogs = true;
        public int aiTopGroups = 10;
        public int aiUserFrames = 3;

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

        public static void ApplyAll()
        {
            var project = ConsoleProjectSettings.instance;
            ULog.MinLevel = project.globalMinLevel;
            ULog.RichTextTags = project.richTextTags;
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

        /// <summary>Colour for a system name: configured channel colour, else the stable generated one.</summary>
        public static string ColorFor(string system)
        {
            if (string.IsNullOrEmpty(system)) return null;
            var s = ConsoleProjectSettings.instance.Find(system);
            if (!string.IsNullOrEmpty(s?.color)) return LogChannel.NormalizeHex(s.color);
            foreach (var c in LogChannel.All)
                if (c.Name == system) return c.ColorHex;
            return LogChannel.ColorFromName(system);
        }
    }
}
#endif
