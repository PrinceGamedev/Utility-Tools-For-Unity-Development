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

        // Layout: body = list host | splitter | details pane (column or row depending on dock).
        private VisualElement _body, _listHost, _splitter, _detailsPane, _searchRow, _systemsChips;
        private Toolbar _toolbar;
        private VisualElement _searchSlot; // toolbar spacer; search sits right after it
        private Label _detailsArrow, _systemsArrow, _timelineArrow;
        private Button _dockButton;
        private VisualElement _timelineBars;
        private bool _sideDock, _narrow, _tiny;

        private const float NarrowWidth = 640f, TinyWidth = 460f, SideDockMinWidth = 900f;

        private static Texture2D s_ErrorIcon, s_WarnIcon, s_InfoIcon, s_WindowIcon;
        private static FontDefinition s_MonoFont;
        private static bool s_MonoFontLoaded, s_HasMonoFont;
        private static readonly Dictionary<string, Color> s_Colors = new Dictionary<string, Color>();

        private static ConsoleUserSettings User => ConsoleUserSettings.instance;

        private float RowHeight =>
            RowHeights[Mathf.Clamp(User.textSize, 0, RowHeights.Length - 1)] + (User.density == RowDensity.Comfortable ? 8f : 0f);

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
            _systemFilter.Clear();
            foreach (var t in User.tagFilter) _systemFilter.Add(t);
            titleContent = new GUIContent("AI Console", s_WindowIcon);

            root.Add(BuildToolbar());

            // Main row: side bar | content column (search row, banner, systems, timeline, body).
            var main = new VisualElement();
            main.AddToClassList("aic-main");
            root.Add(main);
            main.Add(BuildRail());

            var content = new VisualElement();
            content.AddToClassList("aic-content");
            main.Add(content);

            // Very narrow windows move the search field here (see ApplyResponsive).
            _searchRow = new VisualElement();
            _searchRow.AddToClassList("aic-search-row");
            content.Add(_searchRow);

            content.Add(BuildBanner());
            content.Add(BuildSystems());
            content.Add(BuildTimeline());

            _body = new VisualElement();
            _body.AddToClassList("aic-body");
            content.Add(_body);

            _listHost = new VisualElement();
            _listHost.AddToClassList("aic-list-host");
            _listHost.Add(BuildList());
            _listHost.Add(_emptyList);
            _body.Add(_listHost);

            _splitter = new VisualElement { tooltip = "Drag to resize · double-click to hide details" };
            _splitter.AddToClassList("aic-splitter");
            _splitter.AddManipulator(new SplitterDragger(this));
            _splitter.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2) SetDetailsOpen(!User.detailsOpen);
            });
            _body.Add(_splitter);
            _body.Add(BuildDetailsPane());

            root.Add(BuildStatusBar());
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsive());

            ConsoleStore.Changed += MarkDirty;
            ConsoleProjectSettings.Changed += MarkDirty;
            root.schedule.Execute(Tick).Every(100);

            ApplyTextSize();
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
            var bar = _toolbar = new Toolbar();
            bar.AddToClassList("aic-toolbar");

            var clear = new ToolbarButton(() => ConsoleCapture.ClearAll("cleared by user"))
                {
                    text = "Clear",
                    tooltip = "Clear logs and compile warnings (compile errors stay until fixed, like Unity).\n" +
                              "AI files keep the history."
                };
            clear.AddToClassList("aic-toolbar__fixed");
            bar.Add(clear);

            var collapse = MakeToggle("Collapse", User.collapse, "Group repeats of the same log (shows ×count)", SetCollapse);
            collapse.AddToClassList("aic-toolbar__fixed");
            bar.Add(collapse);
            _collapseToggle = collapse;

            // Everything else (follow, pause, sort, panels, display, AI) lives in the side bar.
            var spacer = _searchSlot = new VisualElement();
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

        private ToolbarToggle _collapseToggle;

        private void SetCollapse(bool v)
        {
            User.collapse = v;
            _collapseToggle?.SetValueWithoutNotify(v);
            SaveUser();
            MarkDirty();
        }

        private void SetFollow(bool v)
        {
            User.follow = v;
            SaveUser();
            if (v) ScrollToNewest();
        }

        private void SetErrorPause(bool v)
        {
            User.pauseOnError = v;
            SaveUser();
        }

        // ---------- Side bar ----------
        // Slim icon strip (like Rider / VS Code): one click per option, no dropdowns. Toggles show an
        // accent state; Sort and Dock cycle (Shift+click = back); tooltips name the current state.
        // The chevron hides the strip; state is remembered per user.
        private VisualElement _rail;
        private Label _railToggle;

        private static readonly string[] SortModes = { "time", "newest", "severity", "count", "system", "message" };

        /// <summary>A side-strip button whose icon, label, tooltip and lit state are recomputed on every refresh.</summary>
        private sealed class RailItem
        {
            public VisualElement Root;
            public Image Icon;
            public Label Fallback, Name, Badge;
            public Func<bool> IsOn;
            public Func<string> IconName, Label, Tip, BadgeText;
        }

        private readonly List<RailItem> _railItemsList = new List<RailItem>();
        private VisualElement _railGroups;
        private Image _railToggleIcon;

        private VisualElement BuildRail()
        {
            _rail = new VisualElement();
            _rail.AddToClassList("aic-rail");

            // Header chevron: icons + names <-> icons only. When hidden, the whole thin strip restores it.
            _railToggle = new Label();
            _railToggle.AddToClassList("aic-rail__toggle");
            _railToggleIcon = new Image();
            _railToggleIcon.AddToClassList("aic-rail__toggle-icon");
            _railToggle.Add(_railToggleIcon);
            _railToggle.AddManipulator(new Clickable(() =>
                SetRailMode(User.railMode == RailMode.Expanded ? RailMode.Compact : RailMode.Expanded)));
            _rail.Add(_railToggle);

            _railGroups = new VisualElement();
            _railGroups.AddToClassList("aic-rail__groups");
            _rail.Add(_railGroups);

            // View
            RailToggle("Profiler.LastFrame|Animation.LastKey|scrolldown|forward", "Follow",
                () => "Follow — keep the newest log in view", () => User.follow, () => SetFollow(!User.follow));
            RailToggle("PauseButton", "Error Pause", () => "Error Pause — pause Play Mode on the first error",
                () => User.pauseOnError, () => SetErrorPause(!User.pauseOnError));
            RailToggle("TreeEditor.Trash", "Clear on Play", () => "Clear on Play — clear the console when entering Play Mode",
                () => User.clearOnPlay, () =>
                {
                    User.clearOnPlay = !User.clearOnPlay;
                    SaveUser();
                });
            RailSeparator();

            // Order (cycles; Shift+click goes back)
            RailCycle(() => "AlphabeticalSorting", () => "Sort: " + SortLabel(User.sortMode),
                () => $"Sort: {SortLabel(User.sortMode)}\nClick for next · Shift+click for previous",
                () => SortLabel(User.sortMode).Substring(0, 1),
                back => SetSort(Cycle(SortModes, User.sortMode, back)));
            RailSeparator();

            // Panels
            RailToggle("Profiler.UIDetails|align_horizontally_right|UnityEditor.InspectorWindow", "Details",
                () => "Details pane", () => User.detailsOpen, () => SetDetailsOpen(!User.detailsOpen));
            RailToggle("FilterByType", "Tags", () => "Tags bar — filter by one or more tags, edit tag colours",
                () => User.systemsOpen, () =>
                {
                    User.systemsOpen = !User.systemsOpen;
                    SaveUser();
                    _systemsSignature = "";
                    MarkDirty();
                });
            RailToggle("UnityEditor.ProfilerWindow", "Timeline", () => "Timeline", () => User.showTimeline, () =>
            {
                User.showTimeline = !User.showTimeline;
                SaveUser();
                MarkDirty();
            });
            RailCycle(() => User.detailsDock == DetailsDock.Bottom ? "align_vertically_bottom"
                    : User.detailsDock == DetailsDock.Right ? "align_horizontally_right" : "GridLayoutGroup Icon",
                () => "Dock: " + User.detailsDock,
                () => $"Dock details: {User.detailsDock}" +
                      (User.detailsDock == DetailsDock.Auto ? " (right on wide windows, bottom otherwise)" : "") +
                      "\nClick for next · Shift+click for previous",
                () => null,
                back =>
                {
                    var docks = new[] { DetailsDock.Auto, DetailsDock.Bottom, DetailsDock.Right };
                    int i = Array.IndexOf(docks, User.detailsDock);
                    SetDock(docks[(i + (back ? docks.Length - 1 : 1)) % docks.Length]);
                });
            RailSeparator();

            // Display
            RailAction("Toolbar Minus|ol_minus", () => "Smaller Text",
                () => "Smaller text (Ctrl/Cmd + minus)", () => SetTextSize(User.textSize - 1));
            RailAction("Toolbar Plus|ol_plus", () => "Larger Text  " + TextSizePx + "px",
                () => $"Larger text (Ctrl/Cmd + =) · now {TextSizePx}px · Ctrl/Cmd + 0 resets",
                () => SetTextSize(User.textSize + 1));
            RailToggle("VerticalLayoutGroup Icon", "Roomy Rows", () => "Roomy rows", () => User.density == RowDensity.Comfortable,
                () => SetDensity(User.density == RowDensity.Compact ? RowDensity.Comfortable : RowDensity.Compact));
            RailToggle("UnityEditor.DebugInspectorWindow", "Engine Frames",
                () => "Engine frames — show Unity / System frames in stack traces", () => User.showEngineFrames, () =>
                {
                    User.showEngineFrames = !User.showEngineFrames;
                    SaveUser();
                    RefreshDetails(true);
                });
            RailSeparator();

            // AI
            RailAction("SaveAs|TextAsset Icon|UnityEditor.ConsoleWindow", () => "Copy Issues",
                () => "Copy issues for AI — the errors.md digest, ready to paste",
                () => EditorGUIUtility.systemCopyBuffer = IssuesDigest());
            RailAction("FolderOpened Icon", () => "AI Folder", () => "Open the AI files folder (Logs/AIConsole)", AIBridge.Reveal);

            // Bottom-pinned
            var spacer = new VisualElement();
            spacer.AddToClassList("aic-rail__spacer");
            _railGroups.Add(spacer);
            RailAction("Settings", () => "Settings", () => "Settings — Project Settings → Utility Tools → AI Console",
                () => SettingsService.OpenProjectSettings(SettingsPath));
            RailAction("_Help", () => "About " + UtilityToolsInfo.PackageVersion,
                () => $"About — Utility Tools {UtilityToolsInfo.PackageVersion}, feature versions",
                () => SettingsService.OpenProjectSettings("Project/Utility Tools"));
            RailAction("tab_prev|scrollleft", () => "Hide Strip", () => "Hide the side strip (click the thin edge to bring it back)",
                () => SetRailMode(RailMode.Hidden));

            // Hidden mode: the strip is a thin edge; clicking anywhere on it brings it back.
            _rail.RegisterCallback<ClickEvent>(e =>
            {
                if (User.railMode != RailMode.Hidden) return;
                SetRailMode(RailMode.Expanded);
                e.StopPropagation();
            });

            UpdateRail();
            return _rail;
        }

        private void SetRailMode(RailMode mode)
        {
            User.railMode = mode;
            SaveUser();
            UpdateRail();
        }

        private static string Cycle(string[] values, string current, bool back)
        {
            int i = Array.IndexOf(values, current);
            if (i < 0) i = 0;
            return values[(i + (back ? values.Length - 1 : 1)) % values.Length];
        }

        private void RailSeparator()
        {
            var s = new VisualElement();
            s.AddToClassList("aic-rail__sep");
            _railGroups.Add(s);
        }

        private void RailToggle(string icon, string label, Func<string> tip, Func<bool> isOn, Action onClick) =>
            AddRailItem(() => icon, () => label, tip, isOn, null, _ => onClick());

        private void RailCycle(Func<string> icon, Func<string> label, Func<string> tip, Func<string> badge, Action<bool> onClick) =>
            AddRailItem(icon, label, tip, null, badge, onClick);

        private void RailAction(string icon, Func<string> label, Func<string> tip, Action onClick) =>
            AddRailItem(() => icon, label, tip, null, null, _ => onClick());

        private void AddRailItem(Func<string> icon, Func<string> label, Func<string> tip, Func<bool> isOn,
            Func<string> badge, Action<bool> onClick)
        {
            var item = new RailItem
            {
                Root = new VisualElement(), IconName = icon, Label = label, Tip = tip, IsOn = isOn, BadgeText = badge
            };
            item.Root.AddToClassList("aic-rail__btn");

            // Accent marker on the strip's outer edge, shown only when the option is on.
            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.AddToClassList("aic-rail__marker");
            item.Root.Add(marker);

            var iconBox = new VisualElement();
            iconBox.AddToClassList("aic-rail__iconbox");
            item.Root.Add(iconBox);

            item.Icon = new Image();
            item.Icon.AddToClassList("aic-rail__icon");
            iconBox.Add(item.Icon);

            string text = label();
            item.Fallback = new Label(text.Length > 2 ? text.Substring(0, 2) : text);
            item.Fallback.AddToClassList("aic-rail__fallback");
            iconBox.Add(item.Fallback);

            item.Badge = new Label();
            item.Badge.AddToClassList("aic-rail__badge");
            iconBox.Add(item.Badge);

            // Shown in Expanded mode only (see USS).
            item.Name = new Label(text);
            item.Name.AddToClassList("aic-rail__name");
            item.Root.Add(item.Name);

            item.Root.RegisterCallback<ClickEvent>(e =>
            {
                onClick(e.shiftKey);
                UpdateRail();
                e.StopPropagation();
            });

            _railItemsList.Add(item);
            _railGroups.Add(item.Root);
        }

        private static readonly Dictionary<string, Texture2D> s_IconCache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Editor icon for the current skin ("d_" variant in dark). <paramref name="names"/> may list
        /// fallbacks separated by '|' — not every built-in icon is reachable through FindTexture.
        /// Returns null when none load (the button then shows its text fallback).
        /// </summary>
        private static Texture2D EditorIcon(string names)
        {
            if (string.IsNullOrEmpty(names)) return null;
            if (s_IconCache.TryGetValue(names, out var cached)) return cached;

            Texture2D tex = null;
            foreach (var name in names.Split('|'))
            {
                string dark = "d_" + name;
                tex = EditorGUIUtility.isProSkin
                    ? EditorGUIUtility.FindTexture(dark) ?? EditorGUIUtility.FindTexture(name)
                    : EditorGUIUtility.FindTexture(name) ?? EditorGUIUtility.FindTexture(dark);
                if (tex != null) break;
            }

            s_IconCache[names] = tex;
            return tex;
        }

        private void UpdateRail()
        {
            if (_rail == null) return;
            var mode = User.railMode;
            _rail.EnableInClassList("aic-rail--expanded", mode == RailMode.Expanded);
            _rail.EnableInClassList("aic-rail--compact", mode == RailMode.Compact);
            _rail.EnableInClassList("aic-rail--hidden", mode == RailMode.Hidden);

            bool expanded = mode == RailMode.Expanded;
            _railToggleIcon.image = EditorIcon(expanded ? "tab_prev" : "tab_next");
            _railToggle.text = _railToggleIcon.image == null ? (expanded ? "‹" : "›") : "";
            _railToggle.tooltip = mode == RailMode.Hidden ? "Show the side strip"
                : expanded ? "Icons only" : "Show names";
            _rail.tooltip = mode == RailMode.Hidden ? "Show the side strip" : null;

            foreach (var item in _railItemsList)
            {
                var tex = EditorIcon(item.IconName());
                item.Icon.image = tex;
                item.Icon.EnableInClassList("aic-hidden", tex == null);
                item.Fallback.EnableInClassList("aic-hidden", tex != null);
                item.Name.text = item.Label();
                item.Root.tooltip = item.Tip();
                item.Root.EnableInClassList("aic-rail__btn--on", item.IsOn != null && item.IsOn());

                string badge = item.BadgeText?.Invoke();
                item.Badge.text = badge ?? "";
                item.Badge.EnableInClassList("aic-hidden", string.IsNullOrEmpty(badge) || expanded);
            }
        }

        // ---------- Text size ----------
        // Six steps; each maps to a root class (aic--fs-N) whose USS variables size text, rows, chips and the strip.
        private static readonly int[] TextSizes = { 11, 12, 13, 14, 16, 18 };
        private static readonly float[] RowHeights = { 22, 24, 26, 28, 32, 36 };
        private const int DefaultTextSize = 2;

        private int TextSizePx => TextSizes[Mathf.Clamp(User.textSize, 0, TextSizes.Length - 1)];

        private void SetTextSize(int step)
        {
            User.textSize = Mathf.Clamp(step, 0, TextSizes.Length - 1);
            SaveUser();
            ApplyTextSize();
        }

        private void ApplyTextSize()
        {
            var root = rootVisualElement;
            for (int i = 0; i < TextSizes.Length; i++) root.EnableInClassList("aic--fs-" + i, i == User.textSize);
            if (_list != null)
            {
                _list.fixedItemHeight = RowHeight;
                _list.RefreshItems();
            }

            RefreshDetails(true);
            UpdateRail();
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

        // ---------- Tags bar ----------
        // Every log can carry several tags (its ULog system + [UI][Boss] prefixes + Tagged(...)).
        // Chips filter by one or more tags (Any / All), colours are edited in place, choices are remembered.
        private VisualElement _tagsRow, _tagEditor, _tagEditorRows;
        private Label _tagsTitle, _matchAny, _matchAll, _tagsClear, _tagsEdit;
        private string _tagEditorSignature = "";

        private VisualElement BuildSystems()
        {
            _systems = new VisualElement();
            _systems.AddToClassList("aic-tags");

            _tagsRow = new VisualElement();
            _tagsRow.AddToClassList("aic-systems");
            _systems.Add(_tagsRow);

            _systemsArrow = MakeArrow("Show or hide the tags bar", () =>
            {
                User.systemsOpen = !User.systemsOpen;
                SaveUser();
                _systemsSignature = "";
                MarkDirty();
            });
            _tagsRow.Add(_systemsArrow);

            _tagsTitle = new Label("TAGS");
            _tagsTitle.AddToClassList("aic-tags__title");
            _tagsRow.Add(_tagsTitle);

            _systemsChips = new VisualElement();
            _systemsChips.AddToClassList("aic-systems__chips");
            _tagsRow.Add(_systemsChips);

            var controls = new VisualElement();
            controls.AddToClassList("aic-tags__controls");
            _tagsRow.Add(controls);

            // Any / All segmented switch: how several selected tags combine.
            var seg = new VisualElement { tooltip = "With several tags selected: show logs that have ANY of them, or ALL of them" };
            seg.AddToClassList("aic-seg");
            _matchAny = SegButton(seg, "Any", () => SetMatchAll(false));
            _matchAll = SegButton(seg, "All", () => SetMatchAll(true));
            controls.Add(seg);

            _tagsClear = new Label("Clear") { tooltip = "Show all tags again" };
            _tagsClear.AddToClassList("aic-link");
            _tagsClear.AddManipulator(new Clickable(() =>
            {
                _systemFilter.Clear();
                SaveTagFilter();
            }));
            controls.Add(_tagsClear);

            _tagsEdit = new Label("Edit tags") { tooltip = "Add tags and pick their colours (shared with the team)" };
            _tagsEdit.AddToClassList("aic-link");
            _tagsEdit.AddManipulator(new Clickable(() =>
            {
                User.tagEditorOpen = !User.tagEditorOpen;
                SaveUser();
                _tagEditorSignature = "";
                _systemsSignature = "";
                MarkDirty();
            }));
            controls.Add(_tagsEdit);

            _tagEditor = new VisualElement();
            _tagEditor.AddToClassList("aic-tag-editor");
            _systems.Add(_tagEditor);
            return _systems;
        }

        private static Label SegButton(VisualElement parent, string text, Action onClick)
        {
            var b = new Label(text);
            b.AddToClassList("aic-seg__btn");
            b.AddManipulator(new Clickable(onClick));
            parent.Add(b);
            return b;
        }

        private void SetMatchAll(bool all)
        {
            User.tagMatchAll = all;
            SaveUser();
            _systemsSignature = "";
            MarkDirty();
        }

        private void SaveTagFilter()
        {
            User.tagFilter = new List<string>(_systemFilter);
            SaveUser();
            _systemsSignature = "";
            MarkDirty();
        }

        /// <summary>Clickable ▾/▸ label used as the header of collapsible panels.</summary>
        private static Label MakeArrow(string tooltip, Action onClick)
        {
            var arrow = new Label("▾") { tooltip = tooltip };
            arrow.AddToClassList("aic-arrow");
            arrow.AddManipulator(new Clickable(onClick));
            return arrow;
        }

        /// <summary>Tags with logs, custom tags (even unused), and switched-off systems.</summary>
        private List<string> TagNames()
        {
            var names = new List<string>(_systemCounts.Keys);
            var seen = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (var t in ConsoleProjectSettings.instance.tags)
                if (!string.IsNullOrEmpty(t.name) && seen.Add(t.name)) names.Add(t.name);
            foreach (var ch in LogChannel.All)
                if (ch != ULog.Default && !ch.Enabled && seen.Add(ch.Name)) names.Add(ch.Name);
            foreach (var t in _systemFilter)
                if (seen.Add(t)) names.Add(t); // keep selected chips visible even with no logs yet
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private void UpdateSystems()
        {
            var names = TagNames();

            // Rebuild chips only when names, counts, colours or selection changed.
            var sig = new System.Text.StringBuilder();
            foreach (var n in names)
            {
                _systemCounts.TryGetValue(n, out int c);
                _systemErrors.TryGetValue(n, out int e);
                sig.Append(n).Append(c).Append('/').Append(e).Append(_systemFilter.Contains(n) ? '*' : '-')
                    .Append(IsChannelOff(n) ? 'x' : 'o').Append(ChannelConfig.ColorFor(n)).Append('|');
            }

            sig.Append(User.systemsOpen).Append(User.tagMatchAll).Append(User.tagEditorOpen);
            string signature = sig.ToString();
            // Always shown, so tags can be added (Edit tags) before any log uses them.
            _systems.style.display = DisplayStyle.Flex;
            UpdateTagEditor(names);
            if (signature == _systemsSignature) return;
            _systemsSignature = signature;

            bool open = User.systemsOpen;
            _systems.EnableInClassList("aic-panel--collapsed", !open);
            _systemsArrow.text = open ? "▾" : $"▸  Tags ({names.Count}" +
                                         (_systemFilter.Count > 0 ? $", {_systemFilter.Count} selected)" : ")");
            _tagsTitle.EnableInClassList("aic-hidden", !open);

            bool several = _systemFilter.Count > 1;
            _matchAny.parent.EnableInClassList("aic-hidden", !open || !several);
            _matchAny.EnableInClassList("aic-seg__btn--on", !User.tagMatchAll);
            _matchAll.EnableInClassList("aic-seg__btn--on", User.tagMatchAll);
            _tagsClear.EnableInClassList("aic-hidden", !open || _systemFilter.Count == 0);
            _tagsEdit.EnableInClassList("aic-hidden", !open);
            _tagsEdit.EnableInClassList("aic-link--on", User.tagEditorOpen);

            _systemsChips.Clear();
            if (!open) return;
            if (names.Count == 0)
            {
                var hint = new Label("No tags yet — write logs as [UI] text or Spawn.Tagged(\"UI\"), or add one with Edit tags.");
                hint.AddToClassList("aic-tags__hint");
                _systemsChips.Add(hint);
                return;
            }

            foreach (var n in names) _systemsChips.Add(MakeChip(n));
        }

        private VisualElement MakeChip(string tag)
        {
            _systemCounts.TryGetValue(tag, out int count);
            _systemErrors.TryGetValue(tag, out int errors);
            bool off = IsChannelOff(tag);
            bool selected = _systemFilter.Contains(tag);

            var chip = new VisualElement
            {
                tooltip = off ? tag + " is switched off (only errors are logged)"
                    : selected ? "Click to remove " + tag + " from the filter"
                    : "Click to filter by " + tag + " (click more tags to combine)"
            };
            chip.AddToClassList("aic-chip");
            chip.EnableInClassList("aic-chip--selected", selected);
            chip.EnableInClassList("aic-chip--off", off);

            var color = ParseColor(ChannelConfig.ColorFor(tag));
            if (selected)
            {
                chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor =
                    chip.style.borderRightColor = color;
                chip.style.backgroundColor = new Color(color.r, color.g, color.b, 0.22f);
            }

            var dot = new VisualElement();
            dot.AddToClassList("aic-chip__dot");
            dot.style.backgroundColor = color;
            chip.Add(dot);

            var label = new Label(tag);
            label.AddToClassList("aic-chip__label");
            chip.Add(label);

            var countLabel = new Label(errors > 0 ? $"{count} · {errors}⛔" : count.ToString(CultureInfo.InvariantCulture));
            countLabel.AddToClassList("aic-chip__count");
            countLabel.EnableInClassList("aic-chip__count--error", errors > 0);
            chip.Add(countLabel);

            chip.AddManipulator(new Clickable(() =>
            {
                if (!_systemFilter.Remove(tag)) _systemFilter.Add(tag);
                SaveTagFilter();
            }));
            chip.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Show only " + tag, _ =>
                {
                    _systemFilter.Clear();
                    _systemFilter.Add(tag);
                    SaveTagFilter();
                });
                evt.menu.AppendAction("Show all tags", _ =>
                {
                    _systemFilter.Clear();
                    SaveTagFilter();
                });
                if (IsChannel(tag))
                {
                    evt.menu.AppendSeparator();
                    var mode = User.GetOverride(tag);
                    evt.menu.AppendAction("Mute for me", _ => SetOverride(tag, ChannelOverride.Off), _ => Check(mode == ChannelOverride.Off));
                    evt.menu.AppendAction("Force on for me", _ => SetOverride(tag, ChannelOverride.On), _ => Check(mode == ChannelOverride.On));
                    evt.menu.AppendAction("Use project setting", _ => SetOverride(tag, ChannelOverride.Default), _ => Check(mode == ChannelOverride.Default));
                }

                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Edit tag colours…", _ =>
                {
                    User.tagEditorOpen = true;
                    SaveUser();
                    _tagEditorSignature = "";
                    _systemsSignature = "";
                    MarkDirty();
                });
            }));
            return chip;
        }

        // ---------- Tag editor (inline) ----------
        private void UpdateTagEditor(List<string> names)
        {
            bool open = User.tagEditorOpen && User.systemsOpen;
            _tagEditor.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;

            // Rebuilt only when the set of tags changes — never while a colour picker is in use.
            string signature = string.Join("|", names);
            if (signature == _tagEditorSignature) return;
            _tagEditorSignature = signature;
            _tagEditor.Clear();

            var add = new VisualElement();
            add.AddToClassList("aic-tag-editor__add");
            var newName = new TextField { tooltip = "Tag name, e.g. UI — logs written as [UI] text, or Tagged(\"UI\")" };
            newName.AddToClassList("aic-tag-editor__name-field");
            var newColor = new ColorField { value = new Color(0.35f, 0.63f, 1f), showAlpha = false, showEyeDropper = false };
            newColor.AddToClassList("aic-tag-editor__color");
            var addButton = new Button(() =>
            {
                string n = newName.value?.Trim().Trim('[', ']');
                if (string.IsNullOrEmpty(n)) return;
                var project = ConsoleProjectSettings.instance;
                project.SetTagColor(n, "#" + ColorUtility.ToHtmlStringRGB(newColor.value));
                project.SaveAndNotify();
                newName.value = "";
                _tagEditorSignature = "";
                MarkDirty();
            }) { text = "Add tag" };
            var hint = new Label("New tag");
            hint.AddToClassList("aic-tag-editor__hint");
            add.Add(hint);
            add.Add(newName);
            add.Add(newColor);
            add.Add(addButton);
            _tagEditor.Add(add);

            _tagEditorRows = new VisualElement();
            _tagEditorRows.AddToClassList("aic-tag-editor__rows");
            _tagEditor.Add(_tagEditorRows);

            foreach (var n in names)
            {
                string tag = n;
                var row = new VisualElement();
                row.AddToClassList("aic-tag-editor__row");

                ColorUtility.TryParseHtmlString(ChannelConfig.ColorFor(tag), out var c);
                var color = new ColorField { value = c, showAlpha = false, showEyeDropper = false, tooltip = "Colour of " + tag };
                color.AddToClassList("aic-tag-editor__color");
                color.RegisterValueChangedCallback(e =>
                {
                    var project = ConsoleProjectSettings.instance;
                    project.SetTagColor(tag, "#" + ColorUtility.ToHtmlStringRGB(e.newValue));
                    project.SaveAndNotify();
                    _systemsSignature = "";
                    MarkDirty();
                });
                row.Add(color);

                var name = new Label(tag);
                name.AddToClassList("aic-tag-editor__label");
                row.Add(name);

                _systemCounts.TryGetValue(tag, out int count);
                var cnt = new Label(count + (count == 1 ? " log" : " logs"));
                cnt.AddToClassList("aic-tag-editor__count");
                row.Add(cnt);

                if (ConsoleProjectSettings.instance.FindTag(tag) != null)
                {
                    var reset = new Label("Remove") { tooltip = "Remove the custom colour (and the tag, if no logs use it)" };
                    reset.AddToClassList("aic-link");
                    reset.AddManipulator(new Clickable(() =>
                    {
                        var project = ConsoleProjectSettings.instance;
                        project.tags.RemoveAll(t => string.Equals(t.name, tag, StringComparison.OrdinalIgnoreCase));
                        project.SaveAndNotify();
                        _tagEditorSignature = "";
                        _systemsSignature = "";
                        MarkDirty();
                    }));
                    row.Add(reset);
                }

                _tagEditorRows.Add(row);
            }
        }

        private static bool IsChannel(string name)
        {
            foreach (var ch in LogChannel.All)
                if (ch != ULog.Default && ch.Name == name) return true;
            return false;
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

            _timelineArrow = MakeArrow("Show or hide the timeline", () =>
            {
                User.showTimeline = !User.showTimeline;
                SaveUser();
                MarkDirty();
            });
            _timeline.Add(_timelineArrow);

            _timelineBars = new VisualElement();
            _timelineBars.AddToClassList("aic-timeline__bars");
            _timeline.Add(_timelineBars);

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
                _timelineBars.Add(col);
            }

            return _timeline;
        }

        private long _timelineFrom, _timelineStep;

        private void UpdateTimeline()
        {
            var entries = ConsoleStore.Entries;
            int logs = 0;
            foreach (var e in entries)
                if (e.kind != EntryKind.Marker && ++logs > 1) break;

            // Only worth showing once there are real logs to plot (markers alone draw nothing).
            bool open = User.showTimeline;
            _timeline.style.display = logs > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _timeline.EnableInClassList("aic-panel--collapsed", !open);
            _timelineArrow.text = open ? "▾" : "▸  Timeline";
            if (!open || logs <= 1) return;

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
            public Label Time, System, Message, Fields, Badge, Id, MoreTags;
            public Label[] TagChips; // System + 2 more tag chips; the rest is "+n"
            public ConsoleEntry Entry;
        }

        private const int MaxRowTags = 3;

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
            // Overlay shown when nothing matches. It lives in the list host: ListView doesn't accept children.
            _emptyList = new Label("No logs match.\nClear the search or enable more types / systems.");
            _emptyList.AddToClassList("aic-empty");
            _emptyList.AddToClassList("aic-empty--overlay");
            _emptyList.pickingMode = PickingMode.Ignore;
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
            v.TagChips = new Label[MaxRowTags];
            v.TagChips[0] = v.System;
            for (int i = 1; i < MaxRowTags; i++) v.TagChips[i] = Add<Label>(v.Root, "aic-row__system");
            v.MoreTags = Add<Label>(v.Root, "aic-row__more");
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

            v.RuleLeft.EnableInClassList("aic-hidden", !marker);
            v.RuleRight.EnableInClassList("aic-hidden", !marker);
            v.Icon.EnableInClassList("aic-hidden", marker);
            v.Icon.image = e.IsError ? s_ErrorIcon : e.IsWarning ? s_WarnIcon : s_InfoIcon;

            string firstTag = null;
            foreach (var t in e.AllTags) { firstTag = t; break; }
            Color stripColor = firstTag != null ? ParseColor(TagColor(e, firstTag))
                : e.IsError ? ParseColor("#ff5f5a") : e.IsWarning ? ParseColor("#f2c14e") : Color.clear;
            v.Strip.style.backgroundColor = marker ? Color.clear : stripColor;

            v.Time.text = new DateTime(e.ticks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

            // Tag chips: up to MaxRowTags, then "+n".
            int shown = 0, total = 0;
            if (!marker)
            {
                foreach (var t in e.AllTags)
                {
                    total++;
                    if (shown >= MaxRowTags) continue;
                    var chip = v.TagChips[shown++];
                    var c = ParseColor(TagColor(e, t));
                    chip.text = t;
                    chip.style.color = c;
                    chip.style.backgroundColor = new Color(c.r, c.g, c.b, 0.16f);
                }
            }

            for (int i = 0; i < MaxRowTags; i++) v.TagChips[i].EnableInClassList("aic-hidden", i >= shown);
            v.MoreTags.text = "+" + (total - shown).ToString(CultureInfo.InvariantCulture);
            v.MoreTags.EnableInClassList("aic-hidden", total <= shown);
            v.System.RemoveFromClassList("aic-row__file");

            if (e.IsCompile && e.file != null)
            {
                // "GA_Menu.cs:33" chip + the message without its long path prefix (full path is in details).
                v.System.text = Path.GetFileName(e.file) + ":" + e.line.ToString(CultureInfo.InvariantCulture);
                v.System.style.color = StyleKeyword.Null;
                v.System.style.backgroundColor = StyleKeyword.Null;
                v.System.AddToClassList("aic-row__file");
                v.System.RemoveFromClassList("aic-hidden");
                v.System.tooltip = $"{e.file}:{e.line}:{e.column}";
                v.Message.text = AIFormat.CompilerText(e);
            }
            else
            {
                v.System.tooltip = null;
                v.Message.text = e.FirstLine;
            }

            if (e.FieldCount > 0 && !marker)
            {
                var parts = new string[e.FieldCount];
                for (int i = 0; i < parts.Length; i++) parts[i] = e.fieldKeys[i] + "=" + e.fieldValues[i];
                v.Fields.text = string.Join("  ", parts);
            }

            // Visibility via classes (not inline styles) so the responsive USS rules can still hide them.
            v.Fields.EnableInClassList("aic-hidden", e.FieldCount == 0 || marker);

            int count = User.collapse ? ConsoleStore.GroupOf(e)?.Count ?? 1 : 1;
            v.Badge.text = "×" + count.ToString(CultureInfo.InvariantCulture);
            v.Badge.EnableInClassList("aic-hidden", count <= 1);

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

            // Count every tag of every entry (system + extra tags) for the tag bar.
            foreach (var e in ConsoleStore.Entries)
            {
                if (e.system == null && e.TagCount == 0) continue;
                foreach (var tag in e.AllTags)
                {
                    _systemCounts.TryGetValue(tag, out int c);
                    _systemCounts[tag] = c + 1;
                    if (!e.IsError) continue;
                    _systemErrors.TryGetValue(tag, out int er);
                    _systemErrors[tag] = er + 1;
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
            UpdateRail();
            RefreshDetails(false);
        }

        private bool Visible(ConsoleEntry e, ConsoleUserSettings u)
        {
            if (e.kind == EntryKind.Marker) return _query.Matches(e);
            if (e.IsError) { if (!u.showError) return false; }
            else if (e.IsWarning) { if (!u.showWarning) return false; }
            else if (!u.showLog) return false;

            if (_systemFilter.Count > 0 && !MatchesTagFilter(e, u.tagMatchAll)) return false;
            return _query.Matches(e);
        }

        /// <summary>Colour for one of an entry's tags (the channel's own colour for its system).</summary>
        private static string TagColor(ConsoleEntry e, string tag) =>
            tag == e.system && e.color != null && ConsoleProjectSettings.instance.FindTag(tag) == null
                ? e.color
                : ChannelConfig.ColorFor(tag);

        /// <summary>Any: the log has at least one selected tag. All: it has every selected tag.</summary>
        private bool MatchesTagFilter(ConsoleEntry e, bool all)
        {
            foreach (var tag in _systemFilter)
            {
                bool has = e.HasTag(tag);
                if (all && !has) return false;
                if (!all && has) return true;
            }

            return all;
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
                User.tagFilter.Clear();
                SaveUser();
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

        // ---------- Layout: details pane, docking, responsive ----------
        private VisualElement BuildDetailsPane()
        {
            _detailsPane = new VisualElement();
            _detailsPane.AddToClassList("aic-details-pane");

            var header = new VisualElement();
            header.AddToClassList("aic-details-header");

            _detailsArrow = MakeArrow("Show or hide details (or double-click the divider)",
                () => SetDetailsOpen(!User.detailsOpen));
            header.Add(_detailsArrow);

            var title = new Label("DETAILS");
            title.AddToClassList("aic-pane-title");
            title.AddManipulator(new Clickable(() => SetDetailsOpen(!User.detailsOpen)));
            header.Add(title);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            header.Add(spacer);

            _dockButton = new Button(CycleDock) { tooltip = "Dock details: Auto → Bottom → Right" };
            _dockButton.AddToClassList("aic-dock-button");
            header.Add(_dockButton);

            _detailsPane.Add(header);

            _details = new ScrollView(ScrollViewMode.Vertical);
            _details.AddToClassList("aic-details");
            _detailsPane.Add(_details);
            return _detailsPane;
        }

        private void SetDetailsOpen(bool open)
        {
            User.detailsOpen = open;
            SaveUser();
            ApplyResponsive();
        }

        private void SetDock(DetailsDock dock)
        {
            User.detailsDock = dock;
            SaveUser();
            ApplyResponsive();
        }

        private void CycleDock()
        {
            User.detailsDock = User.detailsDock == DetailsDock.Auto ? DetailsDock.Bottom
                : User.detailsDock == DetailsDock.Bottom ? DetailsDock.Right : DetailsDock.Auto;
            SaveUser();
            ApplyResponsive();
        }

        /// <summary>Re-applies breakpoints, dock side and pane sizes. Runs on every window resize.</summary>
        private void ApplyResponsive()
        {
            if (_body == null) return;
            var root = rootVisualElement;
            float w = root.resolvedStyle.width, h = root.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0) return;

            _narrow = w < NarrowWidth;
            _tiny = w < TinyWidth;
            root.EnableInClassList("aic--narrow", _narrow);
            root.EnableInClassList("aic--tiny", _tiny);

            // Search gets its own row when the toolbar is too tight.
            if (_tiny && _search.parent != _searchRow) _searchRow.Add(_search);
            else if (!_tiny && _search.parent == _searchRow) _toolbar.Insert(_toolbar.IndexOf(_searchSlot) + 1, _search);
            _searchRow.style.display = _tiny ? DisplayStyle.Flex : DisplayStyle.None;

            bool side = User.detailsDock == DetailsDock.Right ||
                        (User.detailsDock == DetailsDock.Auto && w >= SideDockMinWidth && w > h * 1.2f);
            _sideDock = side;
            _body.EnableInClassList("aic-body--side", side);
            _splitter.EnableInClassList("aic-splitter--h", side);
            _splitter.EnableInClassList("aic-splitter--v", !side);

            bool open = User.detailsOpen;
            _detailsPane.EnableInClassList("aic-details-pane--collapsed", !open);
            _detailsArrow.text = open ? "▾" : side ? "◂" : "▴";
            _splitter.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (side)
            {
                _detailsPane.style.height = StyleKeyword.Auto;
                _detailsPane.style.width = open ? Mathf.Clamp(User.detailWidth, 220f, Mathf.Max(220f, w - 240f)) : 30f;
            }
            else
            {
                _detailsPane.style.width = StyleKeyword.Auto;
                float headerOnly = 22f;
                _detailsPane.style.height = open ? Mathf.Clamp(User.detailHeight, 80f, Mathf.Max(80f, h - 160f)) : headerOnly;
            }

            _dockButton.text = User.detailsDock == DetailsDock.Auto ? (side ? "Auto ▸" : "Auto ▾")
                : User.detailsDock == DetailsDock.Right ? "Right" : "Bottom";
            UpdateRail();
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

            foreach (var tag in e.AllTags)
            {
                string t = tag;
                var chip = new Label(t) { tooltip = "Click to filter by " + t };
                chip.AddToClassList("aic-row__system");
                chip.AddToClassList("aic-d-tag");
                var c = ParseColor(TagColor(e, t));
                chip.style.color = c;
                chip.style.backgroundColor = new Color(c.r, c.g, c.b, 0.16f);
                chip.AddManipulator(new Clickable(() =>
                {
                    _systemFilter.Add(t);
                    SaveTagFilter();
                }));
                header.Add(chip);
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
            foreach (var tag in e.AllTags)
            {
                string t = tag;
                menu.AppendAction("Filter by Tag/" + t, _ =>
                {
                    _systemFilter.Add(t);
                    SaveTagFilter();
                });
                menu.AppendAction("Show Only Tag/" + t, _ =>
                {
                    _systemFilter.Clear();
                    _systemFilter.Add(t);
                    SaveTagFilter();
                });
            }

            if (e.system != null && IsChannel(e.system))
                menu.AppendAction("Mute " + e.system + " for Me", _ => SetOverride(e.system, ChannelOverride.Off));
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
            // Text size: Ctrl/Cmd + = / + (larger), - (smaller), 0 (reset).
            if (evt.actionKey && (evt.keyCode == KeyCode.Equals || evt.keyCode == KeyCode.KeypadPlus || evt.keyCode == KeyCode.Plus))
            {
                SetTextSize(User.textSize + 1);
                evt.StopPropagation();
                return;
            }

            if (evt.actionKey && (evt.keyCode == KeyCode.Minus || evt.keyCode == KeyCode.KeypadMinus))
            {
                SetTextSize(User.textSize - 1);
                evt.StopPropagation();
                return;
            }

            if (evt.actionKey && (evt.keyCode == KeyCode.Alpha0 || evt.keyCode == KeyCode.Keypad0))
            {
                SetTextSize(DefaultTextSize);
                evt.StopPropagation();
                return;
            }

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
                _startY = _window._sideDock ? e.position.x : e.position.y;
                var pane = _window._detailsPane.resolvedStyle;
                _startHeight = _window._sideDock ? pane.width : pane.height;
                _active = true;
                target.CapturePointer(e.pointerId);
                e.StopPropagation();
            }

            private void OnMove(PointerMoveEvent e)
            {
                if (!_active || !target.HasPointerCapture(e.pointerId)) return;
                var root = _window.rootVisualElement.resolvedStyle;
                if (_window._sideDock)
                {
                    // Details on the right: dragging left makes them wider.
                    float max = Mathf.Max(220f, root.width - 240f);
                    _window._detailsPane.style.width = Mathf.Clamp(_startHeight - (e.position.x - _startY), 220f, max);
                }
                else
                {
                    float max = Mathf.Max(80f, root.height - 160f);
                    _window._detailsPane.style.height = Mathf.Clamp(_startHeight - (e.position.y - _startY), 80f, max);
                }

                e.StopPropagation();
            }

            private void OnUp(PointerUpEvent e)
            {
                if (!_active || !CanStopManipulation(e)) return;
                _active = false;
                target.ReleasePointer(e.pointerId);
                if (_window._sideDock) User.detailWidth = _window._detailsPane.resolvedStyle.width;
                else User.detailHeight = _window._detailsPane.resolvedStyle.height;
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
