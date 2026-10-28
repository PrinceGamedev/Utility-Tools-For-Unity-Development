using System;
using System.Threading;
using UnityEngine;
using UtilityTools;
using UtilityTools.Logging;

namespace UtilityTools.Samples
{
    /// <summary>
    /// Declare one channel per game system. Colours are optional (a stable colour is generated from the name).
    /// Toggle systems in Project Settings → Utility Tools → AI Console, or right-click a chip in the AI Console.
    /// </summary>
    public static class DemoSystems
    {
        public static readonly LogChannel Spawn = new LogChannel("Spawn", "#2ecc71");
        public static readonly LogChannel Network = new LogChannel("Network", "#3498db");
        public static readonly LogChannel Save = new LogChannel("Save");
    }

    /// <summary>
    /// Define tags and their colours once; use them anywhere. The AI Console lists them in its tags bar
    /// (even before they are used) and colours them in every log.
    /// </summary>
    public static class DemoTags
    {
        public static readonly LogTag UI = new LogTag("UI", "#4FC3F7");
        public static readonly LogTag Boss = new LogTag("Boss", "#FF7043");
        public static readonly LogTag Tutorial = new LogTag("Tutorial", "#AED581");
        public static readonly LogTag Audio = new LogTag("Audio", "#BA68C8");
    }

    /// <summary>
    /// Add to a GameObject, open Tools → Utility Tools → AI Console, then press the buttons.
    /// Every log also lands in Logs/AIConsole/ for AI agents.
    /// </summary>
    public class LoggingDemo : MonoBehaviour
    {
        [SerializeField] private int wave = 2;

        [Button("Log Every Level")]
        private void LogEveryLevel()
        {
            DemoSystems.Spawn.Verbose("Verbose detail — removed from release builds");
            DemoSystems.Spawn.Info("Spawned enemies", this, ("count", 3), ("wave", wave));
            DemoSystems.Network.Warning("Ping high", ("ms", 240), ("region", "eu-west"));
            DemoSystems.Save.Error("Save slot corrupted", ("slot", 1));
            ULog.Info("Untagged message on the default channel");
        }

        [Button("Tagged Logs")]
        private void TaggedLogs()
        {
            // A log can carry several tags: its system plus any extra ones. Filter them in the AI Console's
            // tags bar (click several, Any / All). Colours come from DemoTags.
            DemoSystems.Spawn.Tagged(DemoTags.UI, DemoTags.Boss).Info("Boss health bar shown", ("hp", 500));
            DemoSystems.Network.Tagged(DemoTags.UI).Warning("Lobby list slow", ("ms", 900));
            ULog.Tagged(DemoTags.Tutorial).Info("Step 3 reached");
            Debug.Log($"{DemoTags.UI}{DemoTags.Audio} Button click sound played"); // plain Debug.Log: "[UI][Audio] …"
        }

        [Button("Nested Exception")]
        private void NestedException()
        {
            try
            {
                try { LoadSave(); }
                catch (Exception inner) { throw new InvalidOperationException("Could not start wave " + wave, inner); }
            }
            catch (Exception e)
            {
                DemoSystems.Spawn.Exception(e, this);
            }
        }

        private static void LoadSave() => throw new NullReferenceException("Save data was null");

        [Button("Repeat Error ×25")]
        private void RepeatError()
        {
            // Shows up once with ×25 when Collapse is on; errors.md lists it as one group.
            for (int i = 0; i < 25; i++) DemoSystems.Network.Error("Request timed out", ("attempt", i + 1));
        }

        [Button("Log From Background Thread")]
        private void BackgroundThread()
        {
            new Thread(() => DemoSystems.Network.Info("Hello from a worker thread",
                ("thread", Thread.CurrentThread.ManagedThreadId))).Start();
        }

        [Button("Expensive Message (lazy)")]
        private void Lazy()
        {
            // The lambda only runs when Spawn is on — free when the system is muted.
            DemoSystems.Spawn.Info(() => "Expensive dump: " + JsonUtility.ToJson(this));
        }

        [Button("Assert + Once")]
        private void AssertAndOnce()
        {
            DemoSystems.Spawn.Assert(wave > 0, "Wave must be positive");
            DemoSystems.Save.InfoOnce("Shown only the first time this line runs");
        }

        [Button("Plain Debug.Log With [Tag]")]
        private void PlainTagged()
        {
            // Plain Unity logs are captured too; leading [Tags] are read from the text (case-insensitive).
            Debug.Log("[Audio] Music started");
            Debug.LogWarning("Plain warning without a tag");
        }
    }
}
