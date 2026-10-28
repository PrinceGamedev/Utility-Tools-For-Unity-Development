#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// Search syntax for the AI Console (all terms AND-ed, case-insensitive):
    /// <list type="bullet">
    /// <item><c>word</c> / <c>"exact phrase"</c> — in message, system, fields or context</item>
    /// <item><c>-word</c> — exclude</item>
    /// <item><c>type:error</c> (log|warning|error|exception|assert|compile|marker; repeat to OR)</item>
    /// <item><c>tag:UI</c> / <c>sys:Network</c> / <c>-tag:Audio</c> — system or any tag (repeat to OR)</item>
    /// <item><c>#812</c> or <c>id:812</c>, <c>fp:41a536b8e9a5</c>, <c>file:Spawner.cs</c></item>
    /// <item><c>/regex/</c> — on the message</item>
    /// </list>
    /// </summary>
    internal sealed class SearchQuery
    {
        private readonly List<string> _include = new List<string>();
        private readonly List<string> _exclude = new List<string>();
        private readonly HashSet<EntryKind> _kinds = new HashSet<EntryKind>();
        private readonly HashSet<string> _systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excludedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _files = new List<string>();
        private readonly List<Regex> _regexes = new List<Regex>();
        private int _id = -1;
        private string _fingerprint;

        public string Text { get; }

        /// <summary>True when the query narrows anything (markers are hidden while searching).</summary>
        public bool IsEmpty { get; private set; } = true;

        /// <summary>Set when a /regex/ term could not be compiled (shown to the user).</summary>
        public string Error { get; private set; }

        public SearchQuery(string text)
        {
            Text = text ?? "";
            foreach (var token in Tokenize(Text)) Add(token);
        }

        private void Add(string token)
        {
            if (token.Length == 0) return;
            IsEmpty = false;

            bool negate = token.Length > 1 && token[0] == '-';
            string body = negate ? token.Substring(1) : token;

            if (body.Length > 2 && body[0] == '/' && body[body.Length - 1] == '/')
            {
                try { _regexes.Add(new Regex(body.Substring(1, body.Length - 2), RegexOptions.IgnoreCase)); }
                catch (ArgumentException e) { Error = e.Message; }
                return;
            }

            if (body.Length > 1 && body[0] == '#' && int.TryParse(body.Substring(1), out int hashId))
            {
                _id = hashId;
                return;
            }

            int colon = body.IndexOf(':');
            if (colon > 0)
            {
                string key = body.Substring(0, colon).ToLowerInvariant();
                string value = body.Substring(colon + 1);
                switch (key)
                {
                    case "type":
                    case "t":
                        AddKinds(value);
                        return;
                    case "sys":
                    case "system":
                    case "tag":
                        (negate ? _excludedSystems : _systems).Add(value);
                        return;
                    case "id":
                        if (int.TryParse(value.TrimStart('#'), out int id)) _id = id;
                        return;
                    case "fp":
                        _fingerprint = value;
                        return;
                    case "file":
                        _files.Add(value);
                        return;
                }
            }

            (negate ? _exclude : _include).Add(body);
        }

        private void AddKinds(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "log": case "info": _kinds.Add(EntryKind.Log); break;
                case "warning": case "warn": _kinds.Add(EntryKind.Warning); _kinds.Add(EntryKind.CompileWarning); break;
                case "error": case "err":
                    _kinds.Add(EntryKind.Error); _kinds.Add(EntryKind.Exception);
                    _kinds.Add(EntryKind.Assert); _kinds.Add(EntryKind.CompileError);
                    break;
                case "exception": _kinds.Add(EntryKind.Exception); break;
                case "assert": _kinds.Add(EntryKind.Assert); break;
                case "compile": _kinds.Add(EntryKind.CompileError); _kinds.Add(EntryKind.CompileWarning); break;
                case "marker": _kinds.Add(EntryKind.Marker); break;
            }
        }

        public bool Matches(ConsoleEntry e)
        {
            if (IsEmpty) return true;
            if (_id >= 0 && e.id != _id) return false;
            if (_fingerprint != null && !string.Equals(e.fingerprint, _fingerprint, StringComparison.OrdinalIgnoreCase)) return false;
            if (_kinds.Count > 0 && !_kinds.Contains(e.kind)) return false;
            if (_kinds.Count == 0 && e.kind == EntryKind.Marker) return false; // markers only on request
            // sys: / tag: match the system or any extra tag (repeat to OR; -tag: excludes).
            if (_systems.Count > 0 && !AnyTagIn(e, _systems)) return false;
            if (_excludedSystems.Count > 0 && AnyTagIn(e, _excludedSystems)) return false;

            for (int i = 0; i < _files.Count; i++)
                if (!MentionsFile(e, _files[i])) return false;

            for (int i = 0; i < _regexes.Count; i++)
                if (e.message == null || !_regexes[i].IsMatch(e.message)) return false;

            for (int i = 0; i < _include.Count; i++)
                if (!Contains(e, _include[i])) return false;

            for (int i = 0; i < _exclude.Count; i++)
                if (Contains(e, _exclude[i])) return false;

            return true;
        }

        private static bool AnyTagIn(ConsoleEntry e, HashSet<string> set)
        {
            foreach (var t in e.AllTags)
                if (set.Contains(t)) return true;
            return false;
        }

        private static bool Contains(ConsoleEntry e, string term)
        {
            if (Has(e.message, term) || Has(e.system, term) || Has(e.context, term)) return true;
            for (int i = 0; i < e.TagCount; i++)
                if (Has(e.tags[i], term)) return true;
            for (int i = 0; i < e.FieldCount; i++)
                if (Has(e.fieldKeys[i], term) || Has(e.fieldValues[i], term)) return true;
            return false;
        }

        private static bool MentionsFile(ConsoleEntry e, string file)
        {
            if (Has(e.file, file)) return true;
            foreach (var f in e.Frames)
                if (Has(f.File, file)) return true;
            return false;
        }

        private static bool Has(string haystack, string needle) =>
            haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Splits on spaces, keeping "quoted phrases" (and -"quoted") together.</summary>
        internal static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;

                bool negate = text[i] == '-' && i + 1 < text.Length && text[i + 1] == '"';
                int start = negate ? i + 1 : i;
                if (text[start] == '"')
                {
                    int end = text.IndexOf('"', start + 1);
                    if (end < 0) end = text.Length;
                    string phrase = text.Substring(start + 1, end - start - 1);
                    if (phrase.Length > 0) tokens.Add(negate ? "-" + phrase : phrase);
                    i = end + 1;
                }
                else
                {
                    int end = i;
                    while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                    tokens.Add(text.Substring(i, end - i));
                    i = end;
                }
            }

            return tokens;
        }
    }
}
#endif
