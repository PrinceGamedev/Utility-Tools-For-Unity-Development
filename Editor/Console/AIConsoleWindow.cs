#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using UtilityTools.Logging;
using Object = UnityEngine.Object;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// The AI Console window: a full replacement for Unity's Console with search, systems, colours,
    /// sorting, timeline, exception chains and source preview. Shares #ids with the AI bridge files.
    /// </summary>
    public sealed class AIConsoleWindow : EditorWindow, IHasCustomMenu
    {
        private const string StyleSheetGuid = "12b2590b7261538641fa0f87e5fda5b3";
        private const string SettingsPath = "Project/Utility Tools/AI Console";
        private const int TimelineBuckets = 60;

        private const string SearchHelp =
            "Search (all terms must match):\n" +
            "  word  \"exact phrase\"  -exclude\n" +
            "  type:error|warning|log|exception|assert|compile|marker\n" +
            "  sys:Network  -sys:Audio   #812  id:812   fp:<fingerprint>\n" +
            "  file:Spawner.cs   /regex/\n" +
            "Ctrl/Cmd+F focuses search · Esc clears it.";

        [MenuItem("Tools/Utility Tools/AI Console", false, 0)]
        [MenuItem("Window/Utility Tools/AI Console")]
        public static void Open()
        {
            var window = GetWindow<AIConsoleWindow>();
            window.minSize = new Vector2(420, 240);
            window.Show();
        }

        [Shortcut("Utility Tools/AI Console", KeyCode.C, ShortcutModifiers.Action | ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
        private static void OpenViaShortcut() => Open();

        // ---------- State ----------
        private readonly List<ConsoleEntry> _rows = new List<ConsoleEntry>();
        private readonly HashSet<string> _systemFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _systemCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _systemErrors = new Dictionary<string, int>(StringComparer.Ordinal);
        private SearchQuery _query = new SearchQuery("");
        private bool _dirty = true;
        private int _selectedId = -1;
        private int _detailsId = -1;
        private int _detailsCount = -1;
        private string _systemsSignature = "";

        // ---------- UI ----------
        private ToolbarSearchField _search;
        private ToolbarToggle _logToggle, _warnToggle, _errorToggle;
        private ToolbarMenu _sortMenu;
        private VisualElement _banner;
        private Label _bannerTitle, _bannerText;
        private VisualElement _systems;
        private VisualElement _timeline;
        private readonly VisualElement[] _timelineCols = new VisualElement[TimelineBuckets];
        private readonly long[] _timelineStart = new long[TimelineBuckets];
        private ListView _list;
        private ScrollView _details;
        private Label _statusLeft, _statusAi;
        private Label _emptyList;

        private static Texture2D s_ErrorIcon, s_WarnIcon, s_InfoIcon, s_WindowIcon;
        private static FontDefinition s_MonoFont;
        private static bool s_MonoFontLoaded, s_HasMonoFont;
        private static readonly Dictionary<string, Color> s_Colors = new Dictionary<string, Color>();

        private static ConsoleUserSettings User => ConsoleUserSettings.instance;

        private float RowHeight => User.density == RowDensity.Comfortable ? 30f : 22f;

        // ---------- Lifecycle ----------
        private void CreateGUI()
        {
            LoadAssets();
            var root = rootVisualElement;
            root.AddToClassList("aic-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "aic--dark" : "aic--light");

            var sheet = LoadStyleSheet();
            if (sheet != null) root.styleSheets.Add(sheet);

            _query = new SearchQuery(User.search);

            root.Add(BuildToolbar());
            root.Add(BuildBanner());

            _systems = new VisualElement();
            _systems.AddToClassList("aic-systems");
            root.Add(_systems);

            root.Add(BuildTimeline());
            root.Add(BuildList());

            var splitter = new VisualElement();
            splitter.AddToClassList("aic-splitter");
            splitter.AddManipulator(new SplitterDragger(this));
            root.Add(splitter);

            _details = new ScrollView(ScrollViewMode.Vertical);
            _details.AddToClassList("aic-details");
            _details.style.height = Mathf.Max(80f, User.detailHeight);
            root.Add(_details);

            root.Add(BuildStatusBar());
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            ConsoleStore.Changed += MarkDirty;
            ConsoleProjectSettings.Changed += MarkDirty;
            root.schedule.Execute(Tick).Every(100);

            ShowDetails(null);
            Rebuild();
        }

        private void OnDisable()
        {
            ConsoleStore.Changed -= MarkDirty;
            ConsoleProjectSettings.Changed -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        private void Tick()
        {
            if (_dirty) Rebuild();
        }

        private static void LoadAssets()
        {
            if (s_ErrorIcon == null) s_ErrorIcon = EditorGUIUtility.FindTexture("console.erroricon.sml");
            if (s_WarnIcon == null) s_WarnIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
            if (s_InfoIcon == null) s_InfoIcon = EditorGUIUtility.FindTexture("console.infoicon.sml");
            if (s_WindowIcon == null) s_WindowIcon = EditorGUIUtility.FindTexture("UnityEditor.ConsoleWindow");

            if (!s_MonoFontLoaded)
            {
                s_MonoFontLoaded = true;
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Consolas", "Menlo", "SF Mono", "DejaVu Sans Mono", "Liberation Mono", "Courier New" }, 12);
                    if (font != null)
                    {
                        font.hideFlags = HideFlags.HideAndDontSave;
                        s_MonoFont = FontDefinition.FromFont(font);
                        s_HasMonoFont = true;
                    }
                }
                catch
                {
                    s_HasMonoFont = false; // default editor font is fine
                }
            }
        }

        private static StyleSheet LoadStyleSheet()
        {
            string path = AssetDatabase.GUIDToAssetPath(StyleSheetGuid);
            var sheet = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null) return sheet;

            // Fallback for copies whose .meta GUIDs were regenerated.
            foreach (var guid in AssetDatabase.FindAssets("AIConsole t:StyleSheet"))
            {
                path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Console/AIConsole.uss", StringComparison.Ordinal))
                    return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }

            return null;
        }

        // ---------- Toolbar ----------
        private VisualElement BuildToolbar()
        {
            var bar = new Toolbar();
            bar.AddToClassList("aic-toolbar");

            bar.Add(new ToolbarButton(() => ConsoleCapture.ClearAll("cleared by user"))
                { text = "Clear", tooltip = "Clear the console. AI files keep the history." });

            bar.Add(MakeToggle("Collapse", User.collapse, "Group repeats of the same log (shows ×count)", v =>
            {
                User.collapse = v;
                SaveUser();
                MarkDirty();
            }));

            bar.Add(MakeToggle("Follow", User.follow, "Keep the newest log in view", v =>
            {
                User.follow = v;
                SaveUser();
                if (v) ScrollToNewest();
            }));

            bar.Add(MakeToggle("Error Pause", User.pauseOnError, "Pause Play Mode when an error is logged", v =>
            {
                User.pauseOnError = v;
                SaveUser();
            }));

            _sortMenu = new ToolbarMenu { tooltip = "Sort order" };
            foreach (var mode in new[] { "time", "newest", "severity", "count", "system", "message" })
            {
                string m = mode;
                _sortMenu.menu.AppendAction(SortLabel(m), _ => SetSort(m),
                    _ => User.sortMode == m ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }

            _sortMenu.text = "Sort: " + SortLabel(User.sortMode);
            bar.Add(_sortMenu);

            var spacer = new VisualElement();
            spacer.AddToClassList("aic-toolbar__spacer");
            bar.Add(spacer);

            _search = new ToolbarSearchField { tooltip = SearchHelp };
            _search.AddToClassList("aic-search");
            _search.SetValueWithoutNotify(User.search);
            _search.RegisterValueChangedCallback(e =>
            {
                User.search = e.newValue;
                _query = new SearchQuery(e.newValue);
                MarkDirty();
                SaveUser();
            });
            bar.Add(_search);

            _logToggle = MakeTypeToggle(s_InfoIcon, User.showLog, "Show logs", v => User.showLog = v);
            _warnToggle = MakeTypeToggle(s_WarnIcon, User.showWarning, "Show warnings", v => User.showWarning = v);
            _errorToggle = MakeTypeToggle(s_ErrorIcon, User.showError, "Show errors, exceptions and compile errors", v => User.showError = v);
            bar.Add(_logToggle);
            bar.Add(_warnToggle);
            bar.Add(_errorToggle);

            var more = new ToolbarMenu { text = "•••", tooltip = "More" };
            FillMoreMenu(more.menu);
            bar.Add(more);
            return bar;
        }

        private static ToolbarToggle MakeToggle(string text, bool value, string tooltip, Action<bool> onChange)
        {
            var t = new ToolbarToggle { text = text, tooltip = tooltip };
            t.SetValueWithoutNotify(value);
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        private ToolbarToggle MakeTypeToggle(Texture2D icon, bool value, string tooltip, Action<bool> set)
        {
            var t = new ToolbarToggle { tooltip = tooltip, text = "0" };
            t.AddToClassList("aic-type-toggle");
            if (icon != null)
            {
                var img = new Image { image = icon };
                img.AddToClassList("aic-type-toggle__icon");
                t.Insert(0, img);
            }

            t.SetValueWithoutNotify(value);
            t.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                SaveUser();
                MarkDirty();
            });
            return t;
        }

        private void FillMoreMenu(DropdownMenu menu)
        {
            menu.AppendAction("Clear on Play", _ => { User.clearOnPlay = !User.clearOnPlay; SaveUser(); },
                _ => Check(User.clearOnPlay));
            menu.AppendAction("Show Timeline", _ => { User.showTimeline = !User.showTimeline; SaveUser(); MarkDirty(); },
                _ => Check(User.showTimeline));
            menu.AppendAction("Show Engine Frames", _ => { User.showEngineFrames = !User.showEngineFrames; SaveUser(); RefreshDetails(true); },
                _ => Check(User.showEngineFrames));
            menu.AppendAction("Comfortable Rows", _ => SetDensity(User.density == RowDensity.Compact ? RowDensity.Comfortable : RowDensity.Compact),
                _ => Check(User.density == RowDensity.Comfortable));
            menu.AppendSeparator();
            menu.AppendAction("Copy Issues for AI", _ => EditorGUIUtility.systemCopyBuffer = IssuesDigest());
            menu.AppendAction("Open AI Files Folder", _ => AIBridge.Reveal());
            menu.AppendSeparator();
            menu.AppendAction("Settings…", _ => SettingsService.OpenProjectSettings(SettingsPath));
            menu.AppendAction($"About Utility Tools {UtilityToolsInfo.PackageVersion}…",
                _ => SettingsService.OpenProjectSettings("Project/Utility Tools"));
        }

        private static DropdownMenuAction.Status Check(bool on) =>
            on ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal;

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Open AI Files Folder"), false, AIBridge.Reveal);
            menu.AddItem(new GUIContent("Copy Issues for AI"), false, () => EditorGUIUtility.systemCopyBuffer = IssuesDigest());
            menu.AddItem(new GUIContent("AI Console Settings…"), false, () => SettingsService.OpenProjectSettings(SettingsPath));
        }

        private static string IssuesDigest()
        {
            var s = ConsoleProjectSettings.instance;
            return AIFormat.IssuesDigest(Mathf.Max(1, s.aiTopGroups), Mathf.Clamp(s.aiUserFrames, 1, 10));
        }

        private static string SortLabel(string mode)
        {
            switch (mode)
            {
                case "newest": return "Newest first";
                case "severity": return "Severity";
                case "count": return "Count";
                case "system": return "System";
                case "message": return "Message";
                default: return "Time";
            }
        }

        private void SetSort(string mode)
        {
            User.sortMode = mode;
            _sortMenu.text = "Sort: " + SortLabel(mode);
            SaveUser();
            MarkDirty();
        }

        private void SetDensity(RowDensity density)
        {
            User.density = density;
            SaveUser();
            _list.fixedItemHeight = RowHeight;
            _list.RefreshItems();
        }

        private static void SaveUser() => User.SaveQuiet();

        // ---------- Banner ----------
        private VisualElement BuildBanner()
        {
            _banner = new VisualElement();
            _banner.AddToClassList("aic-banner");
            _bannerTitle = new Label();
            _bannerTitle.AddToClassList("aic-banner__title");
            _bannerText = new Label();
            _bannerText.AddToClassList("aic-banner__text");
            _banner.Add(_bannerTitle);
            _banner.Add(_bannerText);
            _banner.AddManipulator(new Clickable(() =>
            {
                foreach (var c in ConsoleStore.CompileIssues)
                {
                    if (c.kind != EntryKind.CompileError) continue;
                    SelectEntry(c);
                    return;
                }
            }));
            _banner.style.display = DisplayStyle.None;
            return _banner;
        }

        private void UpdateBanner()
        {
            int compileErrors = ConsoleStore.CompileErrorCount;
            bool compiling = ConsoleStore.Compile == ConsoleStore.CompileState.Compiling;
            _banner.EnableInClassList("aic-banner--busy", compiling);
            _banner.EnableInClassList("aic-banner--error", !compiling && compileErrors > 0);

            if (compiling)
            {
                _bannerTitle.text = "Compiling scripts…";
                _bannerText.text = "";
            }
            else if (compileErrors > 0)
            {
                _bannerTitle.text = compileErrors == 1 ? "1 compile error" : compileErrors + " compile errors";
                foreach (var c in ConsoleStore.CompileIssues)
                {
                    if (c.kind != EntryKind.CompileError) continue;
                    _bannerText.text = c.FirstLine;
                    break;
                }
            }

            _banner.style.display = compiling || compileErrors > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---------- Systems ----------
        private void UpdateSystems()
        {
            var names = new List<string>(_systemCounts.Keys);
            foreach (var ch in LogChannel.All)
                if (ch != ULog.Default && !ch.Enabled && !names.Contains(ch.Name)) names.Add(ch.Name);
            names.Sort(StringComparer.OrdinalIgnoreCase);

            // Rebuild chips only when names, counts or selection changed.
            var sig = new System.Text.StringBuilder();
            foreach (var n in names)
            {
                _systemCounts.TryGetValue(n, out int c);
                _systemErrors.TryGetValue(n, out int e);
                sig.Append(n).Append(c).Append('/').Append(e).Append(_systemFilter.Contains(n) ? '*' : '-')
                    .Append(IsChannelOff(n) ? 'x' : 'o').Append('|');
            }

            string signature = sig.ToString();
            _systems.style.display = names.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (signature == _systemsSignature) return;
            _systemsSignature = signature;

            _systems.Clear();
            foreach (var n in names) _systems.Add(MakeChip(n));
        }

        private VisualElement MakeChip(string system)
        {
            _systemCounts.TryGetValue(system, out int count);
            _systemErrors.TryGetValue(system, out int errors);
            bool off = IsChannelOff(system);

            var chip = new VisualElement { tooltip = off ? system + " is switched off (only errors are logged)" : "Click to filter by " + system };
            chip.AddToClassList("aic-chip");
            chip.EnableInClassList("aic-chip--selected", _systemFilter.Contains(system));
            chip.EnableInClassList("aic-chip--off", off);

            var dot = new VisualElement();
            dot.AddToClassList("aic-chip__dot");
            dot.style.backgroundColor = ParseColor(ChannelConfig.ColorFor(system));
            chip.Add(dot);

            var label = new Label(system);
            label.AddToClassList("aic-chip__label");
            chip.Add(label);

            var countLabel = new Label(errors > 0 ? $"{count} · {errors}⛔" : count.ToString(CultureInfo.InvariantCulture));
            countLabel.AddToClassList("aic-chip__count");
            countLabel.EnableInClassList("aic-chip__count--error", errors > 0);
            chip.Add(countLabel);

            chip.AddManipulator(new Clickable(() =>
            {
                if (!_systemFilter.Remove(system)) _systemFilter.Add(system);
                MarkDirty();
            }));
            chip.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Show only " + system, _ =>
                {
                    _systemFilter.Clear();
                    _systemFilter.Add(system);
                    MarkDirty();
                });
                evt.menu.AppendAction("Show all systems", _ => { _systemFilter.Clear(); MarkDirty(); });
                evt.menu.AppendSeparator();
                var mode = User.GetOverride(system);
                evt.menu.AppendAction("Mute for me", _ => SetOverride(system, ChannelOverride.Off), _ => Check(mode == ChannelOverride.Off));
                evt.menu.AppendAction("Force on for me", _ => SetOverride(system, ChannelOverride.On), _ => Check(mode == ChannelOverride.On));
                evt.menu.AppendAction("Use project setting", _ => SetOverride(system, ChannelOverride.Default), _ => Check(mode == ChannelOverride.Default));
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("System settings…", _ => SettingsService.OpenProjectSettings(SettingsPath));
            }));
            return chip;
        }

        private void SetOverride(string system, ChannelOverride mode)
        {
            User.SetOverride(system, mode);
            User.SaveAndNotify();
            _systemsSignature = "";
            MarkDirty();
        }

        private static bool IsChannelOff(string system)
        {
            foreach (var ch in LogChannel.All)
                if (ch.Name == system) return !ch.Enabled;
            return false;
        }

        // ---------- Timeline ----------
        private VisualElement BuildTimeline()
        {
            _timeline = new VisualElement();
            _timeline.AddToClassList("aic-timeline");
            for (int i = 0; i < TimelineBuckets; i++)
            {
                int bucket = i;
                var col = new VisualElement();
                col.AddToClassList("aic-timeline__col");
                foreach (var kind in new[] { "error", "warning", "log" })
                {
                    var bar = new VisualElement();
                    bar.AddToClassList("aic-timeline__bar");
                    bar.AddToClassList("aic-timeline__bar--" + kind);
                    col.Add(bar);
                }

                col.AddManipulator(new Clickable(() => JumpToBucket(bucket)));
                _timelineCols[i] = col;
                _timeline.Add(col);
            }

            return _timeline;
        }

        private long _timelineFrom, _timelineStep;

        private void UpdateTimeline()
        {
            var entries = ConsoleStore.Entries;
            bool show = User.showTimeline && entries.Count > 1;
            _timeline.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            long from = entries[0].ticks, to = entries[entries.Count - 1].ticks;
            long span = Math.Max(to - from, TimeSpan.FromSeconds(10).Ticks);
            long step = span / TimelineBuckets + 1;
            _timelineFrom = from;
            _timelineStep = step;

            var err = new int[TimelineBuckets];
            var warn = new int[TimelineBuckets];
            var log = new int[TimelineBuckets];
            foreach (var e in entries)
            {
                if (e.kind == EntryKind.Marker) continue;
                int b = (int)Math.Min(TimelineBuckets - 1, (e.ticks - from) / step);
                if (e.IsError) err[b]++;
                else if (e.IsWarning) warn[b]++;
                else log[b]++;
            }

            int max = 1;
            for (int i = 0; i < TimelineBuckets; i++) max = Math.Max(max, err[i] + warn[i] + log[i]);

            for (int i = 0; i < TimelineBuckets; i++)
            {
                var col = _timelineCols[i];
                SetBar(col[0], err[i], max);
                SetBar(col[1], warn[i], max);
                SetBar(col[2], log[i], max);
                _timelineStart[i] = from + step * i;
                var start = new DateTime(_timelineStart[i], DateTimeKind.Utc).ToLocalTime();
                col.tooltip = $"{start:HH:mm:ss}  ·  {err[i]} errors, {warn[i]} warnings, {log[i]} logs";
            }
        }

        private static void SetBar(VisualElement bar, int count, int max)
        {
            // sqrt keeps a single error visible next to thousands of logs
            float h = count == 0 ? 0f : Mathf.Max(2f, 20f * Mathf.Sqrt(count / (float)max));
            bar.style.height = h;
        }

        private void JumpToBucket(int bucket)
        {
            long from = _timelineFrom + _timelineStep * bucket, to = from + _timelineStep;
            ConsoleEntry best = null;
            foreach (var e in _rows)
            {
                if (e.ticks < from || e.ticks >= to) continue;
                if (best == null || (e.IsError && !best.IsError) || (e.IsError == best.IsError && e.id < best.id)) best = e;
            }

            if (best != null) SelectEntry(best);
        }

        // ---------- List ----------
        private sealed class RowView
        {
            public VisualElement Root, Strip, RuleLeft, RuleRight;
            public Image Icon;
            public Label Time, System, Message, Fields, Badge, Id;
            public ConsoleEntry Entry;
        }

        private VisualElement BuildList()
        {
            _list = new ListView(_rows, RowHeight, MakeRow, BindRow)
            {
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None
            };
            _list.AddToClassList("aic-list");
#if UNITY_2022_2_OR_NEWER
            _list.selectionChanged += OnSelectionChanged;
            _list.itemsChosen += OnItemsChosen;
#else
            _list.onSelectionChange += OnSelectionChanged;
            _list.onItemsChosen += OnItemsChosen;
#endif
            _emptyList = new Label("No logs match.\nClear the search or enable more types / systems.");
            _emptyList.AddToClassList("aic-empty");
            _emptyList.pickingMode = PickingMode.Ignore;
            _emptyList.style.position = Position.Absolute;
            _emptyList.style.left = _emptyList.style.right = _emptyList.style.top = _emptyList.style.bottom = 0;
            _list.Add(_emptyList);
            return _list;
        }

        private VisualElement MakeRow()
        {
            var v = new RowView { Root = new VisualElement() };
            v.Root.AddToClassList("aic-row");
            v.Root.userData = v;

            v.Strip = Add<VisualElement>(v.Root, "aic-row__strip");
            v.Icon = Add<Image>(v.Root, "aic-row__icon");
            v.Time = Add<Label>(v.Root, "aic-row__time");
            v.System = Add<Label>(v.Root, "aic-row__system");
            v.RuleLeft = Add<VisualElement>(v.Root, "aic-row__rule");
            v.Message = Add<Label>(v.Root, "aic-row__msg");
            v.RuleRight = Add<VisualElement>(v.Root, "aic-row__rule");
            v.Fields = Add<Label>(v.Root, "aic-row__fields");
            v.Badge = Add<Label>(v.Root, "aic-row__badge");
            v.Id = Add<Label>(v.Root, "aic-row__id");
            v.Message.enableRichText = false;
            v.Fields.enableRichText = false;

            v.Root.AddManipulator(new ContextualMenuManipulator(evt => FillRowMenu(evt.menu, v.Entry)));
            return v.Root;
        }

        private static T Add<T>(VisualElement parent, string cls) where T : VisualElement, new()
        {
            var el = new T();
            el.AddToClassList(cls);
            parent.Add(el);
            return el;
        }

        private void BindRow(VisualElement element, int index)
        {
            var v = (RowView)element.userData;
            var e = _rows[index];
            v.Entry = e;

            bool marker = e.kind == EntryKind.Marker;
            v.Root.EnableInClassList("aic-row--error", e.IsError);
            v.Root.EnableInClassList("aic-row--warning", e.IsWarning);
            v.Root.EnableInClassList("aic-row--marker", marker);
            v.Root.EnableInClassList("aic-row--pinned", e.IsCompile);

            v.RuleLeft.style.display = v.RuleRight.style.display = marker ? DisplayStyle.Flex : DisplayStyle.None;
            v.Icon.style.display = marker ? DisplayStyle.None : DisplayStyle.Flex;
            v.Icon.image = e.IsError ? s_ErrorIcon : e.IsWarning ? s_WarnIcon : s_InfoIcon;

            Color stripColor = e.system != null ? ParseColor(e.color ?? ChannelConfig.ColorFor(e.system))
                : e.IsError ? ParseColor("#ff5f5a") : e.IsWarning ? ParseColor("#f2c14e") : Color.clear;
            v.Strip.style.backgroundColor = marker ? Color.clear : stripColor;

            v.Time.text = new DateTime(e.ticks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

            if (e.system != null && !marker)
            {
                var c = ParseColor(e.color ?? ChannelConfig.ColorFor(e.system));
                v.System.text = e.system;
                v.System.style.color = c;
                v.System.style.backgroundColor = new Color(c.r, c.g, c.b, 0.16f);
                v.System.style.display = DisplayStyle.Flex;
            }
            else
            {
                v.System.style.display = DisplayStyle.None;
            }

            v.Message.text = e.FirstLine;

            if (e.FieldCount > 0 && !marker)
            {
                var parts = new string[e.FieldCount];
                for (int i = 0; i < parts.Length; i++) parts[i] = e.fieldKeys[i] + "=" + e.fieldValues[i];
                v.Fields.text = string.Join("  ", parts);
                v.Fields.style.display = DisplayStyle.Flex;
            }
            else
            {
                v.Fields.style.display = DisplayStyle.None;
            }

            int count = User.collapse ? ConsoleStore.GroupOf(e)?.Count ?? 1 : 1;
            v.Badge.text = "×" + count.ToString(CultureInfo.InvariantCulture);
            v.Badge.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;

            v.Id.text = marker ? "" : "#" + e.id.ToString(CultureInfo.InvariantCulture);
        }

        // ---------- Rebuild ----------
        private void Rebuild()
        {
            _dirty = false;
            if (_list == null) return;

            var u = User;
            _rows.Clear();
            _systemCounts.Clear();
            _systemErrors.Clear();

            foreach (var e in ConsoleStore.Entries)
            {
                if (e.system == null) continue;
                _systemCounts.TryGetValue(e.system, out int c);
                _systemCounts[e.system] = c + 1;
                if (e.IsError)
                {
                    _systemErrors.TryGetValue(e.system, out int er);
                    _systemErrors[e.system] = er + 1;
                }
            }

            // Compile issues are current state: pinned on top.
            foreach (var c in ConsoleStore.CompileIssues)
                if (Visible(c, u)) _rows.Add(c);
            int pinned = _rows.Count;

            if (u.collapse)
            {
                // One row per fingerprint, represented by its latest occurrence.
                foreach (var g in ConsoleStore.Groups.Values)
                    if (Visible(g.Latest, u)) _rows.Add(g.Latest);
            }
            else
            {
                foreach (var e in ConsoleStore.Entries)
                {
                    if (e.kind == EntryKind.Marker && (_systemFilter.Count > 0 || !_query.IsEmpty)) continue;
                    if (Visible(e, u)) _rows.Add(e);
                }
            }

            SortRows(pinned, u.sortMode);

            _list.fixedItemHeight = RowHeight;
            _list.RefreshItems();
            _emptyList.style.display = _rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            RestoreSelection();
            if (u.follow) ScrollToNewest();

            _logToggle.text = Count(ConsoleStore.LogCount);
            _warnToggle.text = Count(ConsoleStore.WarningCount + CompileCount(EntryKind.CompileWarning));
            _errorToggle.text = Count(ConsoleStore.ErrorCount + ConsoleStore.CompileErrorCount);

            UpdateBanner();
            UpdateSystems();
            UpdateTimeline();
            UpdateStatus();
            RefreshDetails(false);
        }

        private bool Visible(ConsoleEntry e, ConsoleUserSettings u)
        {
            if (e.kind == EntryKind.Marker) return _query.Matches(e);
            if (e.IsError) { if (!u.showError) return false; }
            else if (e.IsWarning) { if (!u.showWarning) return false; }
            else if (!u.showLog) return false;

            if (_systemFilter.Count > 0 && (e.system == null || !_systemFilter.Contains(e.system))) return false;
            return _query.Matches(e);
        }

        private void SortRows(int start, string mode)
        {
            int count = _rows.Count - start;
            if (count < 2) return;

            Comparison<ConsoleEntry> cmp;
            switch (mode)
            {
                case "newest": cmp = (a, b) => b.id.CompareTo(a.id); break;
                case "severity":
                    cmp = (a, b) =>
                    {
                        int s = Severity(b).CompareTo(Severity(a));
                        return s != 0 ? s : b.id.CompareTo(a.id);
                    };
                    break;
                case "count":
                    cmp = (a, b) =>
                    {
                        int s = (ConsoleStore.GroupOf(b)?.Count ?? 1).CompareTo(ConsoleStore.GroupOf(a)?.Count ?? 1);
                        return s != 0 ? s : b.id.CompareTo(a.id);
                    };
                    break;
                case "system":
                    cmp = (a, b) =>
                    {
                        int s = string.Compare(a.system ?? "￿", b.system ?? "￿", StringComparison.OrdinalIgnoreCase);
                        return s != 0 ? s : a.id.CompareTo(b.id);
                    };
                    break;
                case "message":
                    cmp = (a, b) =>
                    {
                        int s = string.Compare(a.message, b.message, StringComparison.OrdinalIgnoreCase);
                        return s != 0 ? s : a.id.CompareTo(b.id);
                    };
                    break;
                default: cmp = (a, b) => a.id.CompareTo(b.id); break;
            }

            _rows.Sort(start, count, Comparer<ConsoleEntry>.Create(cmp));
        }

        private static int Severity(ConsoleEntry e) => e.IsError ? 3 : e.IsWarning ? 2 : e.kind == EntryKind.Marker ? 0 : 1;

        private static int CompileCount(EntryKind kind)
        {
            int n = 0;
            foreach (var c in ConsoleStore.CompileIssues)
                if (c.kind == kind) n++;
            return n;
        }

        private static string Count(int n) => n > 999 ? "999+" : n.ToString(CultureInfo.InvariantCulture);

        private void ScrollToNewest()
        {
            if (_rows.Count == 0) return;
            // Don't yank the view away from a row the user is reading.
            int sel = _list.selectedIndex;
            bool newestAtTop = User.sortMode == "newest";
            if (sel >= 0 && sel != (newestAtTop ? 0 : _rows.Count - 1)) return;
            _list.ScrollToItem(newestAtTop ? 0 : _rows.Count - 1);
        }

        private void RestoreSelection()
        {
            if (_selectedId < 0) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].id != _selectedId) continue;
                _list.SetSelectionWithoutNotify(new[] { i });
                return;
            }

            _list.ClearSelection();
        }

        private void SelectEntry(ConsoleEntry e)
        {
            int index = _rows.IndexOf(e);
            if (index < 0)
            {
                // Filtered out: clear filters that hide it, then retry.
                _systemFilter.Clear();
                _search.value = "";
                Rebuild();
                index = _rows.IndexOf(e);
            }

            _selectedId = e.id;
            if (index >= 0)
            {
                _list.SetSelection(index);
                _list.ScrollToItem(index);
            }

            ShowDetails(e);
        }

        private void OnSelectionChanged(IEnumerable<object> items)
        {
            ConsoleEntry e = null;
            foreach (var o in items) { e = o as ConsoleEntry; break; }
            _selectedId = e?.id ?? -1;
            ShowDetails(e);
        }

        private void OnItemsChosen(IEnumerable<object> items)
        {
            foreach (var o in items)
            {
                if (o is ConsoleEntry e) OpenBestSource(e);
                return;
            }
        }

        // ---------- Status bar ----------
        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("aic-statusbar");
            _statusLeft = new Label();
            _statusLeft.AddToClassList("aic-statusbar__left");
            bar.Add(_statusLeft);

            _statusAi = new Label
            {
                tooltip = $"AI bridge files. Click for options.\nAI Console {UtilityToolsInfo.Features.AIConsole} · " +
                          $"Utility Tools {UtilityToolsInfo.PackageVersion}"
            };
            _statusAi.AddToClassList("aic-statusbar__ai");
            _statusAi.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Open AI Files Folder", _ => AIBridge.Reveal());
                evt.menu.AppendAction("Copy status line", _ => EditorGUIUtility.systemCopyBuffer = AIFormat.Status(true));
                evt.menu.AppendAction("Copy Issues for AI", _ => EditorGUIUtility.systemCopyBuffer = IssuesDigest());
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Write AI files", _ =>
                {
                    var s = ConsoleProjectSettings.instance;
                    s.aiFilesEnabled = !s.aiFilesEnabled;
                    s.SaveAndNotify();
                }, _ => Check(AIBridge.Enabled));
                evt.menu.AppendAction("Settings…", _ => SettingsService.OpenProjectSettings(SettingsPath));
            }) { activators = { new ManipulatorActivationFilter { button = MouseButton.LeftMouse } } });
            bar.Add(_statusAi);
            return bar;
        }

        private void UpdateStatus()
        {
            int total = ConsoleStore.Entries.Count;
            _statusLeft.text = string.Format(CultureInfo.InvariantCulture, "{0:N0} shown · {1:N0} in session · last #{2}",
                _rows.Count, total, ConsoleStore.LastId);

            bool on = AIBridge.Enabled;
            _statusAi.EnableInClassList("aic-statusbar__ai--on", on);
            _statusAi.text = on
                ? $"● AI files  {AIBridge.Folder}" + (AIBridge.LastWriteUtc == default ? "" : $"  · {AIBridge.LastWriteUtc.ToLocalTime():HH:mm:ss}")
                : "○ AI files off";

            int errors = ConsoleStore.ErrorCount + ConsoleStore.CompileErrorCount;
            titleContent = new GUIContent(errors > 0 ? $"AI Console ({errors})" : "AI Console", s_WindowIcon,
                errors > 0 ? errors + " errors" : "AI Console");
        }

        // ---------- Details ----------
        private void RefreshDetails(bool force)
        {
            if (_selectedId < 0) return;
            var e = ConsoleStore.FindById(_selectedId);
            if (e == null) return;
            int count = ConsoleStore.GroupOf(e)?.Count ?? 1;
            if (force || e.id != _detailsId || count != _detailsCount) ShowDetails(e);
        }

        private void ShowDetails(ConsoleEntry e)
        {
            if (_details == null) return;
            _details.Clear();
            _detailsId = e?.id ?? -1;

            if (e == null)
            {
                var empty = new Label("Select a log to see its message, fields, context, stack and source.\n" +
                                      "Double-click a row to open its code · right-click for more.");
                empty.AddToClassList("aic-empty");
                empty.style.marginTop = 20;
                _details.Add(empty);
                return;
            }

            var group = ConsoleStore.GroupOf(e);
            _detailsCount = group?.Count ?? 1;

            // Header
            var header = new VisualElement();
            header.AddToClassList("aic-d-header");
            var kind = new Label(e.KindName.ToUpperInvariant());
            kind.AddToClassList("aic-d-kind");
            kind.AddToClassList(e.IsError ? "aic-d-kind--error" : e.IsWarning ? "aic-d-kind--warning" :
                e.kind == EntryKind.Marker ? "aic-d-kind--marker" : "aic-d-kind--log");
            header.Add(kind);

            if (e.system != null)
            {
                var sys = new Label(e.system);
                sys.AddToClassList("aic-row__system");
                var c = ParseColor(e.color ?? ChannelConfig.ColorFor(e.system));
                sys.style.color = c;
                sys.style.backgroundColor = new Color(c.r, c.g, c.b, 0.16f);
                header.Add(sys);
            }

            Meta(header, "#" + e.id);
            Meta(header, new DateTime(e.ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            if (e.frame >= 0) Meta(header, "frame " + e.frame);
            if (!e.IsCompile && e.kind != EntryKind.Marker)
                Meta(header, (e.play ? "Play" : "Edit") + (string.IsNullOrEmpty(e.scene) ? "" : " · " + e.scene));
            if (e.thread != 0 && ULog.MainThreadId > 0 && e.thread != ULog.MainThreadId) Meta(header, "thread " + e.thread);
            if (group != null && group.Count > 1) Meta(header, $"×{group.Count} (first #{group.FirstId})");
            _details.Add(header);

            // Message (selectable)
            var message = new TextField { multiline = true, isReadOnly = true, value = e.message ?? "" };
            message.AddToClassList("aic-d-message");
            _details.Add(message);

            if (e.IsCompile && e.file != null)
            {
                Section("LOCATION");
                var loc = Kv("File", $"{e.file}:{e.line}:{e.column}");
                loc.AddManipulator(new Clickable(() => OpenSource(e.file, e.line)));
                _details.Add(loc);
            }

            if (e.FieldCount > 0)
            {
                Section("FIELDS");
                for (int i = 0; i < e.FieldCount; i++) _details.Add(Kv(e.fieldKeys[i], e.fieldValues[i]));
            }

            if (!string.IsNullOrEmpty(e.context))
            {
                Section("CONTEXT");
                var ctx = Kv("Object", e.context);
                if (e.contextId != 0)
                {
                    ctx.tooltip = "Click to select it";
                    ctx.AddManipulator(new Clickable(() => PingContext(e)));
                }

                _details.Add(ctx);
            }

            var frames = e.Frames;
            if (frames.Count > 0) AddFrames(frames);

            if (TryGetSourceLocation(e, out string srcFile, out int srcLine)) AddSourcePreview(srcFile, srcLine);

            // Actions
            var actions = new VisualElement();
            actions.AddToClassList("aic-d-actions");
            if (TryGetSourceLocation(e, out _, out _)) actions.Add(new Button(() => OpenBestSource(e)) { text = "Open Source" });
            if (e.contextId != 0) actions.Add(new Button(() => PingContext(e)) { text = "Select Object" });
            actions.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = PlainText(e)) { text = "Copy" });
            actions.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = AIFormat.EntryMarkdown(e)) { text = "Copy for AI" });
            actions.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = AIFormat.JsonLine(e)) { text = "Copy JSON" });
            if (e.fingerprint != null) actions.Add(new Button(() => _search.value = "fp:" + e.fingerprint) { text = "Show Similar" });
            _details.Add(actions);
        }

        private static void Meta(VisualElement parent, string text)
        {
            var l = new Label(text);
            l.AddToClassList("aic-d-meta");
            parent.Add(l);
        }

        private void Section(string title)
        {
            var l = new Label(title);
            l.AddToClassList("aic-d-section");
            _details.Add(l);
        }

        private static VisualElement Kv(string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("aic-d-kv");
            var k = new Label(key);
            k.AddToClassList("aic-d-kv__key");
            var v = new Label(value) { enableRichText = false };
            v.AddToClassList("aic-d-kv__value");
            row.Add(k);
            row.Add(v);
            return row;
        }

        private void AddFrames(List<StackFrameInfo> frames)
        {
            bool chain = false;
            foreach (var f in frames) chain |= f.IsSeparator;
            Section(chain ? "EXCEPTION CHAIN" : "STACK TRACE");

            bool showEngine = User.showEngineFrames;
            int hidden = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                if (!f.IsUser && !f.IsSeparator && !showEngine)
                {
                    hidden++;
                    bool nextVisible = i + 1 >= frames.Count || frames[i + 1].IsUser || frames[i + 1].IsSeparator;
                    if (nextVisible) { AddEngineToggle(hidden); hidden = 0; }
                    continue;
                }

                _details.Add(FrameRow(f));
            }
        }

        private void AddEngineToggle(int count)
        {
            var t = new Label($"▸ {count} engine frame{(count == 1 ? "" : "s")}");
            t.AddToClassList("aic-frame-toggle");
            t.AddManipulator(new Clickable(() =>
            {
                User.showEngineFrames = true;
                SaveUser();
                RefreshDetails(true);
            }));
            _details.Add(t);
        }

        private VisualElement FrameRow(StackFrameInfo f)
        {
            var row = new VisualElement();
            row.AddToClassList("aic-frame");
            row.AddToClassList(f.IsSeparator ? "aic-frame--separator" : f.IsUser ? "aic-frame--user" : "aic-frame--engine");

            var method = new Label(f.Method) { enableRichText = false };
            method.AddToClassList("aic-frame__method");
            row.Add(method);

            if (!string.IsNullOrEmpty(f.File))
            {
                var file = new Label($"{f.File}:{f.Line}") { tooltip = "Open in code editor" };
                file.AddToClassList("aic-frame__file");
                row.Add(file);
                string path = f.File;
                int line = f.Line;
                row.AddManipulator(new Clickable(() => OpenSource(path, line)));
            }

            return row;
        }

        private static bool TryGetSourceLocation(ConsoleEntry e, out string file, out int line)
        {
            if (e.IsCompile && !string.IsNullOrEmpty(e.file))
            {
                file = e.file;
                line = e.line;
                return true;
            }

            if (StackTraceParser.TryGetFirstUserFrame(e.Frames, out var f))
            {
                file = f.File;
                line = f.Line;
                return true;
            }

            file = null;
            line = 0;
            return false;
        }

        private void AddSourcePreview(string file, int line)
        {
            var lines = SourceCache.Get(file);
            if (lines == null || line <= 0 || line > lines.Length) return;

            Section("SOURCE");
            var box = new VisualElement();
            box.AddToClassList("aic-source");
            box.AddManipulator(new Clickable(() => OpenSource(file, line)));
            box.tooltip = "Click to open";

            var title = new Label($"{file}:{line}");
            title.AddToClassList("aic-source__title");
            box.Add(title);

            int from = Math.Max(1, line - 3), to = Math.Min(lines.Length, line + 3);
            for (int n = from; n <= to; n++)
            {
                var row = new VisualElement();
                row.AddToClassList("aic-source__line");
                row.EnableInClassList("aic-source__line--hit", n == line);

                var no = new Label(n.ToString(CultureInfo.InvariantCulture));
                no.AddToClassList("aic-source__no");
                var code = new Label(lines[n - 1].Replace("\t", "    ")) { enableRichText = false };
                code.AddToClassList("aic-source__code");
                if (s_HasMonoFont)
                {
                    no.style.unityFontDefinition = s_MonoFont;
                    code.style.unityFontDefinition = s_MonoFont;
                }

                row.Add(no);
                row.Add(code);
                box.Add(row);
            }

            _details.Add(box);
        }

        // ---------- Row menu & actions ----------
        private void FillRowMenu(DropdownMenu menu, ConsoleEntry e)
        {
            if (e == null) return;
            if (TryGetSourceLocation(e, out _, out _)) menu.AppendAction("Open Source", _ => OpenBestSource(e));
            if (e.contextId != 0) menu.AppendAction("Select Context Object", _ => PingContext(e));
            menu.AppendSeparator();
            menu.AppendAction("Copy", _ => EditorGUIUtility.systemCopyBuffer = PlainText(e));
            menu.AppendAction("Copy for AI", _ => EditorGUIUtility.systemCopyBuffer = AIFormat.EntryMarkdown(e));
            menu.AppendAction("Copy JSON", _ => EditorGUIUtility.systemCopyBuffer = AIFormat.JsonLine(e));
            menu.AppendAction("Copy #id", _ => EditorGUIUtility.systemCopyBuffer = "#" + e.id);
            menu.AppendSeparator();
            if (e.fingerprint != null) menu.AppendAction("Show Similar", _ => _search.value = "fp:" + e.fingerprint);
            if (e.system != null)
            {
                menu.AppendAction("Show Only " + e.system, _ =>
                {
                    _systemFilter.Clear();
                    _systemFilter.Add(e.system);
                    MarkDirty();
                });
                menu.AppendAction("Mute " + e.system + " for Me", _ => SetOverride(e.system, ChannelOverride.Off));
            }
        }

        private static string PlainText(ConsoleEntry e)
        {
            string text = e.message ?? "";
            if (e.IsCompile) return text;
            return string.IsNullOrEmpty(e.stackTrace) ? text : text + "\n" + e.stackTrace.TrimEnd();
        }

        private static void PingContext(ConsoleEntry e)
        {
            var obj = EditorUtility.InstanceIDToObject(e.contextId);
            if (obj == null)
            {
                Debug.LogWarning($"[UtilityTools] The context object of #{e.id} no longer exists.");
                return;
            }

            EditorGUIUtility.PingObject(obj);
            Selection.activeObject = obj;
        }

        private static void OpenBestSource(ConsoleEntry e)
        {
            if (TryGetSourceLocation(e, out string file, out int line)) OpenSource(file, line);
        }

        internal static void OpenSource(string file, int line)
        {
            if (string.IsNullOrEmpty(file)) return;
            var asset = AssetDatabase.LoadAssetAtPath<Object>(file);
            if (asset != null && AssetDatabase.OpenAsset(asset, Math.Max(1, line))) return;

            string full = SourceCache.ResolvePath(file);
            if (full != null) InternalEditorUtility.OpenFileAtLineExternal(full, Math.Max(1, line));
        }

        // ---------- Keyboard ----------
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.actionKey && evt.keyCode == KeyCode.F)
            {
                _search.Focus();
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Escape && !string.IsNullOrEmpty(_search.value))
            {
                _search.value = "";
                evt.StopPropagation();
                return;
            }

            if (evt.actionKey && evt.keyCode == KeyCode.C && _list.selectedItem is ConsoleEntry e &&
                _list.panel?.focusController?.focusedElement is VisualElement focused &&
                (focused == _list || _list.Contains(focused)))
            {
                EditorGUIUtility.systemCopyBuffer = evt.shiftKey ? AIFormat.EntryMarkdown(e) : PlainText(e);
                evt.StopPropagation();
            }
        }

        // ---------- Helpers ----------
        private static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.clear;
            if (!s_Colors.TryGetValue(hex, out var c))
            {
                if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.gray;
                s_Colors[hex] = c;
            }

            return c;
        }

        /// <summary>Drag handle between the list and the details pane.</summary>
        private sealed class SplitterDragger : PointerManipulator
        {
            private readonly AIConsoleWindow _window;
            private float _startY, _startHeight;
            private bool _active;

            public SplitterDragger(AIConsoleWindow window)
            {
                _window = window;
                activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
            }

            private void OnDown(PointerDownEvent e)
            {
                if (!CanStartManipulation(e)) return;
                _startY = e.position.y;
                _startHeight = _window._details.resolvedStyle.height;
                _active = true;
                target.CapturePointer(e.pointerId);
                e.StopPropagation();
            }

            private void OnMove(PointerMoveEvent e)
            {
                if (!_active || !target.HasPointerCapture(e.pointerId)) return;
                float max = Mathf.Max(80f, _window.rootVisualElement.resolvedStyle.height - 140f);
                float h = Mathf.Clamp(_startHeight - (e.position.y - _startY), 60f, max);
                _window._details.style.height = h;
                e.StopPropagation();
            }

            private void OnUp(PointerUpEvent e)
            {
                if (!_active || !CanStopManipulation(e)) return;
                _active = false;
                target.ReleasePointer(e.pointerId);
                User.detailHeight = _window._details.resolvedStyle.height;
                SaveUser();
                e.StopPropagation();
            }
        }
    }

    /// <summary>Reads source files for the preview (cached by modification time).</summary>
    internal static class SourceCache
    {
        private static readonly Dictionary<string, (DateTime stamp, string[] lines)> s_Cache =
            new Dictionary<string, (DateTime, string[])>();

        public static string[] Get(string file)
        {
            string full = ResolvePath(file);
            if (full == null) return null;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(full);
                if (s_Cache.TryGetValue(full, out var hit) && hit.stamp == stamp) return hit.lines;
                if (new FileInfo(full).Length > 2 * 1024 * 1024) return null;
                var lines = File.ReadAllLines(full);
                s_Cache[full] = (stamp, lines);
                return lines;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Physical path for "Assets/…", "Packages/…" (incl. PackageCache) or absolute paths.</summary>
        public static string ResolvePath(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (File.Exists(file)) return Path.GetFullPath(file);

            if (file.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(file);
                if (info != null)
                {
                    string rest = file.Substring(info.assetPath.Length).TrimStart('/');
                    string full = Path.Combine(info.resolvedPath, rest);
                    if (File.Exists(full)) return full;
                }
            }

            return null;
        }
    }
}
#endif
