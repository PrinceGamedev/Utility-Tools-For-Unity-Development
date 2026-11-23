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

        /// <summary>Deletes answers and outputs older than a day so <c>results/</c> and <c>out/</c> don't grow forever.</summary>
        internal static void PruneResults(string folder, DateTime nowUtc)
        {
            foreach (string sub in new[] { ResultsFolder, OutFolder })
            {
                string dir = Path.Combine(folder, sub);
                if (!Directory.Exists(dir)) continue;

                foreach (string path in Directory.GetFiles(dir))
                    if (nowUtc - File.GetLastWriteTimeUtc(path) > ResultsRetention) File.Delete(path);
            }
        }

        // ---------- Long command output ----------

        internal const string OutFolder = "out";
        private const int MaxInlineChars = 300;
        private const int SummaryChars = 160;

        /// <summary>True when a result is too long, or multi-line, to sit on its line in the answer.</summary>
        internal static bool NeedsOutputFile(string result) =>
            result != null && (result.Length > MaxInlineChars || result.IndexOf('\n') >= 0);

        /// <summary>Writes a command's full output to <c>out/&lt;request&gt;_&lt;n&gt;.txt</c>; returns the path under the AI folder.</summary>
        internal static string WriteOutput(string folder, string requestName, int commandNumber, string text)
        {
            string stem = Path.GetFileNameWithoutExtension(requestName ?? "commands");
            string relative = OutFolder + "/" + stem + "_" + commandNumber + ".txt";
            AIBridge.WriteAtomic(Path.Combine(folder, OutFolder, stem + "_" + commandNumber + ".txt"), text);
            return relative;
        }

        /// <summary>The first line of an output, clipped, for the one-line result.</summary>
        internal static string Summary(string text)
        {
            string first = (text ?? "").Replace("\r", "");
            int newline = first.IndexOf('\n');
            if (newline >= 0) first = first.Substring(0, newline);
            return first.Length > SummaryChars ? first.Substring(0, SummaryChars) + "…" : first;
        }
    }
}
#endif
