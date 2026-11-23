#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UtilityTools.AIConsole
{
    /// <summary>One command file waiting to run: a queue request or the shared legacy <c>commands.txt</c>.</summary>
    internal readonly struct AgentRequest
    {
        public readonly string Path;
        public readonly string Name;
        public readonly string Agent;
        public readonly bool Legacy;
        public readonly DateTime WrittenUtc;

        public AgentRequest(string path, string name, string agent, bool legacy, DateTime writtenUtc)
        {
            Path = path;
            Name = name;
            Agent = agent;
            Legacy = legacy;
            WrittenUtc = writtenUtc;
        }
    }

    /// <summary>
    /// The multi-agent request queue: each agent drops its own file in <c>queue/</c> and gets its own answer in
    /// <c>results/</c>, so agents never overwrite each other's batch or read each other's result.
    /// File layout and ordering only; running the commands stays in <see cref="AICommands"/>.
    /// </summary>
    internal static class AgentQueue
    {
        internal const string QueueFolder = "queue";
        internal const string ResultsFolder = "results";
        internal const string HistoryFile = "history.jsonl";
        internal const string LegacyAgent = "anonymous";

        private static readonly TimeSpan ResultsRetention = TimeSpan.FromHours(24);

        /// <summary>Agent id from a request name <c>&lt;agent&gt;_&lt;anything&gt;.txt</c>; the whole name when there is no <c>_</c>.</summary>
        internal static string AgentOf(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName ?? "");
            int underscore = stem.IndexOf('_');
            string id = underscore > 0 ? stem.Substring(0, underscore) : stem;

            var sb = new StringBuilder(id.Length);
            foreach (char ch in id)
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '.') sb.Append(ch);
            return sb.Length > 0 ? sb.ToString() : LegacyAgent;
        }

        /// <summary>Waiting requests, oldest first: every <c>queue/*.txt</c> plus the legacy <c>commands.txt</c>.</summary>
        internal static List<AgentRequest> Pending(string folder)
        {
            var list = new List<AgentRequest>();

            string legacy = Path.Combine(folder, AICommands.CommandsFile);
            if (File.Exists(legacy))
                list.Add(new AgentRequest(legacy, AICommands.CommandsFile, LegacyAgent, true, File.GetLastWriteTimeUtc(legacy)));

            string queue = Path.Combine(folder, QueueFolder);
            if (Directory.Exists(queue))
            {
                foreach (string path in Directory.GetFiles(queue))
                {
                    string name = Path.GetFileName(path);
                    if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue; // .tmp = still being written
                    list.Add(new AgentRequest(path, name, AgentOf(name), false, File.GetLastWriteTimeUtc(path)));
                }
            }

            list.Sort(Compare);
            return list;
        }

        internal static int Compare(AgentRequest a, AgentRequest b)
        {
            int byTime = a.WrittenUtc.CompareTo(b.WrittenUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Name, b.Name);
        }

        /// <summary>Where the answer to a request goes: <c>results/&lt;same name&gt;</c>, or <c>results.txt</c> for the legacy file.</summary>
        internal static string ResultsPath(string folder, bool legacy, string requestName) =>
            legacy
                ? Path.Combine(folder, AICommands.ResultsFile)
                : Path.Combine(folder, ResultsFolder, requestName);

        internal static void AppendHistory(string folder, string jsonLine)
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, HistoryFile), jsonLine + "\n", new UTF8Encoding(false));
        }

        /// <summary>Deletes answers older than a day so <c>results/</c> doesn't grow forever.</summary>
        internal static void PruneResults(string folder, DateTime nowUtc)
        {
            string results = Path.Combine(folder, ResultsFolder);
            if (!Directory.Exists(results)) return;

            foreach (string path in Directory.GetFiles(results))
                if (nowUtc - File.GetLastWriteTimeUtc(path) > ResultsRetention) File.Delete(path);
        }
    }
}
#endif
