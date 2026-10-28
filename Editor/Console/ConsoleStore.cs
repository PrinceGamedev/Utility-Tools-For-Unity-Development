#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// In-memory log for this editor session (main thread only). Survives domain reloads through a
    /// snapshot in Library/; a new editor session starts empty.
    /// </summary>
    internal static class ConsoleStore
    {
        public enum CompileState { Unknown, Compiling, Ok, Failed }

        private static readonly List<ConsoleEntry> s_Entries = new List<ConsoleEntry>();
        private static readonly List<ConsoleEntry> s_CompileIssues = new List<ConsoleEntry>();
        private static readonly Dictionary<string, EntryGroup> s_Groups = new Dictionary<string, EntryGroup>();

        private static int s_NextId = 1;

        /// <summary>Entries in arrival order (oldest first), capped at the configured maximum.</summary>
        public static IReadOnlyList<ConsoleEntry> Entries => s_Entries;

        /// <summary>Compiler errors/warnings from the most recent compilation (current state, not history).</summary>
        public static IReadOnlyList<ConsoleEntry> CompileIssues => s_CompileIssues;

        public static IReadOnlyDictionary<string, EntryGroup> Groups => s_Groups;

        public static int ErrorCount { get; private set; }
        public static int WarningCount { get; private set; }
        public static int LogCount { get; private set; }

        public static CompileState Compile { get; set; } = CompileState.Unknown;
        public static long LastCompileTicks { get; set; }

        /// <summary>Raised on the main thread after entries were added, cleared or compile issues changed.</summary>
        public static event Action Changed;

        /// <summary>Raised for each new entry (AI bridge appends it to session.jsonl).</summary>
        public static event Action<ConsoleEntry> Added;

        public static event Action Cleared;

        public static int LastId => s_NextId - 1;

        public static int CompileErrorCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < s_CompileIssues.Count; i++)
                    if (s_CompileIssues[i].kind == EntryKind.CompileError) n++;
                return n;
            }
        }

        public static void Add(ConsoleEntry e)
        {
            e.id = s_NextId++;
            s_Entries.Add(e);
            Count(e, +1);
            Group(e);

            int max = Mathf.Max(500, ConsoleProjectSettings.instance.maxEntries);
            if (s_Entries.Count > max)
            {
                int drop = Mathf.Max(1, max / 10); // trim in chunks, not one by one
                for (int i = 0; i < drop; i++) Count(s_Entries[i], -1);
                s_Entries.RemoveRange(0, drop);
            }

            Added?.Invoke(e);
        }

        public static void SetCompileIssues(List<ConsoleEntry> issues)
        {
            s_CompileIssues.Clear();
            foreach (var e in issues)
            {
                e.id = s_NextId++;
                s_CompileIssues.Add(e);
                Added?.Invoke(e);
            }
        }

        public static void ClearCompileIssues() => s_CompileIssues.Clear();

        /// <summary>Adds one compiler message that arrived outside a CompilationPipeline callback.</summary>
        public static void AddCompileIssue(ConsoleEntry e)
        {
            e.id = s_NextId++;
            s_CompileIssues.Add(e);
            Added?.Invoke(e);
        }

        // Compile warnings removed by Clear, so Unity's re-printed copies don't bring them back.
        // Forgotten when the next compilation starts — a real recompile reports them again (like Unity).
        private static readonly HashSet<string> s_DismissedCompile = new HashSet<string>();

        private static string CompileKey(string file, int line, int column, string code) =>
            file + "|" + line + "|" + column + "|" + code;

        /// <summary>True when this compiler message is already listed or was cleared since the last compile.</summary>
        public static bool HasCompileIssue(string file, int line, int column, string code)
        {
            if (s_DismissedCompile.Contains(CompileKey(file, line, column, code))) return true;
            foreach (var c in s_CompileIssues)
                if (c.line == line && c.column == column && c.file == file &&
                    StackTraceParser.CompilerCode(c.message) == code)
                    return true;
            return false;
        }

        public static void ForgetDismissedCompileIssues() => s_DismissedCompile.Clear();

        /// <summary>
        /// Clears the session like Unity's Console: logs and compile warnings go, compile errors stay
        /// (they still block Play Mode until fixed).
        /// </summary>
        public static void Clear()
        {
            s_Entries.Clear();
            s_Groups.Clear();
            ErrorCount = WarningCount = LogCount = 0;

            for (int i = s_CompileIssues.Count - 1; i >= 0; i--)
            {
                var c = s_CompileIssues[i];
                if (c.kind != EntryKind.CompileWarning) continue;
                s_DismissedCompile.Add(CompileKey(c.file, c.line, c.column, StackTraceParser.CompilerCode(c.message)));
                s_CompileIssues.RemoveAt(i);
            }

            Cleared?.Invoke();
            NotifyChanged();
        }

        public static void NotifyChanged() => Changed?.Invoke();

        public static ConsoleEntry FindById(int id)
        {
            // ids are increasing → binary search
            int lo = 0, hi = s_Entries.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int v = s_Entries[mid].id;
                if (v == id) return s_Entries[mid];
                if (v < id) lo = mid + 1;
                else hi = mid - 1;
            }

            for (int i = 0; i < s_CompileIssues.Count; i++)
                if (s_CompileIssues[i].id == id) return s_CompileIssues[i];
            return null;
        }

        public static EntryGroup GroupOf(ConsoleEntry e) =>
            e.fingerprint != null && s_Groups.TryGetValue(e.fingerprint, out var g) ? g : null;

        private static void Count(ConsoleEntry e, int delta)
        {
            if (e.IsError) ErrorCount += delta;
            else if (e.IsWarning) WarningCount += delta;
            else if (e.kind != EntryKind.Marker) LogCount += delta;
        }

        private static void Group(ConsoleEntry e)
        {
            if (e.fingerprint == null) return;
            if (!s_Groups.TryGetValue(e.fingerprint, out var g))
            {
                g = new EntryGroup { Fingerprint = e.fingerprint, FirstId = e.id, FirstTicks = e.ticks };
                s_Groups.Add(e.fingerprint, g);
            }

            g.Count++;
            g.Latest = e;
        }

        // ---------- Domain-reload snapshot ----------
        [Serializable]
        private sealed class Snapshot
        {
            public int nextId;
            public int compile;
            public long lastCompileTicks;
            public List<ConsoleEntry> entries;
            public List<ConsoleEntry> compileIssues;
        }

        private static string SnapshotPath => Path.Combine("Library", "UtilityTools", "AIConsole", "snapshot.json");

        public static void SaveSnapshot()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath));
                var snap = new Snapshot
                {
                    nextId = s_NextId,
                    compile = (int)Compile,
                    lastCompileTicks = LastCompileTicks,
                    entries = s_Entries,
                    compileIssues = s_CompileIssues
                };
                File.WriteAllText(SnapshotPath, JsonUtility.ToJson(snap));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UtilityTools] AI Console could not save its snapshot: " + e.Message);
            }
        }

        public static void LoadSnapshot()
        {
            try
            {
                if (!File.Exists(SnapshotPath)) return;
                var snap = JsonUtility.FromJson<Snapshot>(File.ReadAllText(SnapshotPath));
                if (snap == null) return;

                s_NextId = Math.Max(s_NextId, snap.nextId);
                Compile = (CompileState)snap.compile;
                LastCompileTicks = snap.lastCompileTicks;

                // Entries logged during this reload (before LoadSnapshot) keep their place at the end.
                var early = new List<ConsoleEntry>(s_Entries);
                s_Entries.Clear();
                s_Groups.Clear();
                ErrorCount = WarningCount = LogCount = 0;

                if (snap.entries != null)
                    foreach (var e in snap.entries) { Normalize(e); s_Entries.Add(e); Count(e, +1); Group(e); }
                foreach (var e in early) { e.id = s_NextId++; s_Entries.Add(e); Count(e, +1); Group(e); }

                if (snap.compileIssues != null && s_CompileIssues.Count == 0)
                    foreach (var e in snap.compileIssues) { Normalize(e); s_CompileIssues.Add(e); }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UtilityTools] AI Console could not restore its snapshot: " + e.Message);
            }
        }

        /// <summary>
        /// JsonUtility writes null strings and arrays as "" / [] — restore nulls so "no system",
        /// "no fingerprint" etc. mean the same after a domain reload as before it.
        /// </summary>
        internal static void Normalize(ConsoleEntry e)
        {
            if (e.system == "") e.system = null;
            if (e.color == "") e.color = null;
            if (e.fingerprint == "") e.fingerprint = null;
            if (e.context == "") e.context = null;
            if (e.scene == "") e.scene = null;
            if (e.file == "") e.file = null;
            if (e.stackTrace == "") e.stackTrace = null;
            if (e.fieldKeys != null && e.fieldKeys.Length == 0) e.fieldKeys = null;
            if (e.fieldValues != null && e.fieldValues.Length == 0) e.fieldValues = null;
            if (e.tags != null && e.tags.Length == 0) e.tags = null;
        }

        public static void DeleteSnapshot()
        {
            try { if (File.Exists(SnapshotPath)) File.Delete(SnapshotPath); }
            catch { /* best effort */ }
        }
    }
}
#endif
