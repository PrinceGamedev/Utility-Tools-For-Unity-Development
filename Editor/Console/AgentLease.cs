#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UtilityTools.AIConsole
{
    /// <summary>Who holds the Editor lease, for what, until when. Stored as <c>lease.json</c> in the AI folder.</summary>
    [Serializable]
    internal sealed class LeaseInfo
    {
        public string agent;
        public string task;
        public string taken;   // ISO UTC
        public string expires; // ISO UTC
    }

    /// <summary>
    /// The Editor lease: one agent at a time can reserve the Editor for multi-step work (play → test → stop).
    /// While a lease is active, other agents' batches that contain anything beyond read-only commands wait in
    /// the queue. With no lease, every batch runs as before. The lease expires on its own, so a crashed
    /// agent can't block the team.
    /// </summary>
    internal static class AgentLease
    {
        internal const string FileName = "lease.json";
        internal const int MaxMinutes = 15;

        internal const string Usage = "usage: lease take <minutes> <task> | renew <minutes> | release | break | status";

        /// <summary>Built-in commands that never need the lease: they read state or only affect the sender's own batch.</summary>
        private static readonly HashSet<string> FreeVerbs =
            new HashSet<string>(StringComparer.Ordinal) { "status", "help", "show", "mark", "wait", "lease" };

        internal static bool NeedsLease(string verb)
        {
            if (FreeVerbs.Contains(verb ?? "")) return false;
            return !(AgentVerbs.TryGet(verb, out var extension) && extension.ReadOnly);
        }

        internal static bool IsActive(LeaseInfo lease, DateTime nowUtc) =>
            lease != null && !string.IsNullOrEmpty(lease.agent) && TryParse(lease.expires, out var until) && until > nowUtc;

        /// <summary>True when <paramref name="agent"/> may run this batch now.</summary>
        internal static bool Allows(LeaseInfo lease, string agent, IReadOnlyList<CommandLine> commands, DateTime nowUtc)
        {
            if (!IsActive(lease, nowUtc) || lease.agent == agent) return true;
            foreach (var c in commands)
                if (NeedsLease(c.Verb)) return false;
            return true;
        }

        /// <summary>
        /// Applies one <c>lease …</c> command for <paramref name="agent"/>. Pure: returns the new lease
        /// (<c>null</c> = free) and the result text; the caller stores it.
        /// </summary>
        internal static string Apply(LeaseInfo current, string agent, string arg, DateTime nowUtc, out LeaseInfo next, out bool ok)
        {
            bool active = IsActive(current, nowUtc);
            bool mine = active && current.agent == agent;
            next = active ? current : null;

            string[] words = (arg ?? "").Trim().Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string action = words.Length > 0 ? words[0].ToLowerInvariant() : "status";

            switch (action)
            {
                case "status":
                    ok = true;
                    return Describe(next);

                case "take":
                    if (!TryMinutes(words, out int takeMinutes, out string takeNote)) { ok = false; return Usage; }
                    if (active && !mine) { ok = false; return "held by " + Describe(current); }
                    string task = words.Length > 2 ? words[2] : mine ? current.task : "";
                    next = new LeaseInfo
                    {
                        agent = agent,
                        task = task,
                        taken = mine ? current.taken : AIFormat.Iso(nowUtc.Ticks),
                        expires = AIFormat.Iso(nowUtc.AddMinutes(takeMinutes).Ticks)
                    };
                    ok = true;
                    return (mine ? "renewed" : "taken") + " until " + next.expires + takeNote;

                case "renew":
                    if (!mine)
                    {
                        ok = false;
                        return active ? "you don't hold the lease — held by " + Describe(current) : "the lease is free — use `lease take`";
                    }
                    if (!TryMinutes(words, out int renewMinutes, out string renewNote)) { ok = false; return Usage; }
                    next = new LeaseInfo
                    {
                        agent = current.agent,
                        task = current.task,
                        taken = current.taken,
                        expires = AIFormat.Iso(nowUtc.AddMinutes(renewMinutes).Ticks)
                    };
                    ok = true;
                    return "renewed until " + next.expires + renewNote;

                case "release":
                    if (mine) { next = null; ok = true; return "released"; }
                    ok = !active;
                    return active ? "held by " + Describe(current) + " — only the holder can release; `lease break` if it is stuck" : "already free";

                case "break":
                    ok = true;
                    if (!active) return "already free";
                    next = null;
                    return "broken (was " + Describe(current) + ")";

                default:
                    ok = false;
                    return Usage;
            }
        }

        /// <summary><c>free</c>, or <c>&lt;agent&gt; until &lt;time&gt; task="…"</c>.</summary>
        internal static string Describe(LeaseInfo lease)
        {
            if (lease == null) return "free";
            string task = string.IsNullOrEmpty(lease.task) ? "" : " task=\"" + lease.task.Replace("\"", "'") + "\"";
            return lease.agent + " until " + lease.expires + task;
        }

        // ---------- File ----------

        internal static LeaseInfo Load(string folder)
        {
            string path = Path.Combine(folder, FileName);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<LeaseInfo>(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        internal static void Store(string folder, LeaseInfo lease)
        {
            string path = Path.Combine(folder, FileName);
            if (lease == null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            AIBridge.WriteAtomic(path, JsonUtility.ToJson(lease, true));
        }

        // ---------- Helpers ----------

        private static bool TryMinutes(string[] words, out int minutes, out string note)
        {
            note = "";
            minutes = 0;
            if (words.Length < 2 || !int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes < 1)
                return false;

            if (minutes > MaxMinutes)
            {
                minutes = MaxMinutes;
                note = " (capped at " + MaxMinutes + " min)";
            }

            return true;
        }

        private static bool TryParse(string iso, out DateTime utc) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
    }
}
#endif
