using System;
using NUnit.Framework;
using UtilityTools.AIConsole;
using UtilityTools.Logging;

namespace UtilityTools.Tests
{
    public class StackTraceParserTests
    {
        private const string UnityTrace =
            "UnityEngine.Debug:Log (object)\n" +
            "Spawner:Spawn (int) (at Assets/Scripts/Spawner.cs:42)\n" +
            "UtilityTools.Logging.LogChannel:Info (string) (at Packages/com.gameyogi.utilitytools/Runtime/Logging/LogChannel.cs:80)\n" +
            "Wave:Start () (at Assets/Scripts/Wave.cs:10)\n" +
            "UnityEngine.Events.InvokableCall:Invoke () (at /home/bokken/build/output/unity/unity/Runtime/Export/UnityEvent.cs:178)\n";

        [Test]
        public void Parse_UnityFormat_SplitsMethodFileLine()
        {
            var f = StackTraceParser.Parse(UnityTrace);
            Assert.AreEqual(5, f.Count);
            Assert.AreEqual("Spawner:Spawn (int)", f[1].Method);
            Assert.AreEqual("Assets/Scripts/Spawner.cs", f[1].File);
            Assert.AreEqual(42, f[1].Line);
        }

        [Test]
        public void Parse_MarksOnlyProjectCodeAsUser()
        {
            var f = StackTraceParser.Parse(UnityTrace);
            Assert.IsFalse(f[0].IsUser, "UnityEngine frame");
            Assert.IsTrue(f[1].IsUser, "Assets/ frame");
            Assert.IsFalse(f[2].IsUser, "the logger itself");
            Assert.IsTrue(f[3].IsUser);
            Assert.IsFalse(f[4].IsUser, "absolute build-machine path");
        }

        [Test]
        public void Parse_DotNetFormat_AndRethrowSeparator()
        {
            var f = StackTraceParser.Parse(
                "  at Foo.Bar (System.Int32 x) [0x00012] in D:/Other/Bar.cs:17 \n" +
                "  at Foo.Baz () [0x00000] in <filename unknown>:0 \n" +
                "Rethrow as InvalidOperationException: wrapped\n");
            Assert.AreEqual(3, f.Count);
            Assert.AreEqual("Foo.Bar (System.Int32 x)", f[0].Method);
            Assert.AreEqual(17, f[0].Line);
            Assert.IsNull(f[1].File);
            Assert.IsTrue(f[2].IsSeparator);
        }

        [Test]
        public void Parse_EmptyOrNull_ReturnsNoFrames()
        {
            Assert.AreEqual(0, StackTraceParser.Parse(null).Count);
            Assert.AreEqual(0, StackTraceParser.Parse("").Count);
        }

        [TestCase("[Network] Ping high", true, "Network", "Ping high")]
        [TestCase("[Save System] ok", true, "Save System", "ok")]
        [TestCase("[12:00] time", false, null, "[12:00] time")]
        [TestCase("no tag", false, null, "no tag")]
        public void TrySplitTag(string message, bool expected, string tag, string rest)
        {
            Assert.AreEqual(expected, StackTraceParser.TrySplitTag(message, out var t, out var r));
            Assert.AreEqual(tag, t);
            Assert.AreEqual(rest, r);
        }

        [Test]
        public void StripRichText_RemovesTagsButKeepsText()
        {
            Assert.AreEqual("[Spawn] hi <3", StackTraceParser.StripRichText("<color=#ff0000><b>[Spawn]</b></color> hi <3"));
            Assert.AreEqual("a < b", StackTraceParser.StripRichText("a < b"));
        }

        [Test]
        public void Fingerprint_IgnoresNumbersButNotKindOrSystem()
        {
            var f = StackTraceParser.Parse(UnityTrace);
            string a = StackTraceParser.Fingerprint(EntryKind.Error, "Spawn", "Enemy 12 missing at 0x1F", f);
            string b = StackTraceParser.Fingerprint(EntryKind.Error, "Spawn", "Enemy 873 missing at 0xAB", f);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, StackTraceParser.Fingerprint(EntryKind.Warning, "Spawn", "Enemy 12 missing at 0x1F", f));
            Assert.AreNotEqual(a, StackTraceParser.Fingerprint(EntryKind.Error, "Audio", "Enemy 12 missing at 0x1F", f));
            Assert.AreEqual(12, a.Length);
        }

        [Test]
        public void TryParseCompilerLine_ReadsUnityCompilerEcho()
        {
            Assert.IsTrue(StackTraceParser.TryParseCompilerLine(
                "Assets\\GameAnalytics\\Editor\\GA_Menu.cs(33,17): warning CS0618: 'X' is obsolete",
                out var file, out int line, out int col, out bool isError, out var code));
            Assert.AreEqual("Assets/GameAnalytics/Editor/GA_Menu.cs", file);
            Assert.AreEqual(33, line);
            Assert.AreEqual(17, col);
            Assert.IsFalse(isError);
            Assert.AreEqual("CS0618", code);

            Assert.IsTrue(StackTraceParser.TryParseCompilerLine("Assets/A.cs(1,2): error CS1002: ; expected",
                out _, out _, out _, out isError, out _));
            Assert.IsTrue(isError);
            Assert.IsFalse(StackTraceParser.TryParseCompilerLine("Plain warning (1,2) text", out _, out _, out _, out _, out _));
        }

        [Test]
        public void CompilerCode_FindsCode()
        {
            Assert.AreEqual("CS0618", StackTraceParser.CompilerCode("Assets/A.cs(1,2): warning CS0618: x"));
            Assert.IsNull(StackTraceParser.CompilerCode("no code here"));
        }

        [Test]
        public void Fingerprint_IgnoresLineNumberChanges()
        {
            var before = StackTraceParser.Parse("Spawner:Spawn (int) (at Assets/Scripts/Spawner.cs:42)\n");
            var after = StackTraceParser.Parse("Spawner:Spawn (int) (at Assets/Scripts/Spawner.cs:57)\n");
            Assert.AreEqual(
                StackTraceParser.Fingerprint(EntryKind.Exception, null, "NullReferenceException", before),
                StackTraceParser.Fingerprint(EntryKind.Exception, null, "NullReferenceException", after));
        }
    }

    public class AIFormatTests
    {
        private static ConsoleEntry SampleEntry() => new ConsoleEntry
        {
            id = 812,
            ticks = new DateTime(2026, 10, 28, 12, 4, 11, 300, DateTimeKind.Utc).Ticks,
            kind = EntryKind.Exception,
            system = "Spawn",
            message = "NullReferenceException: \"quoted\"\nsecond line",
            stackTrace = "Spawner:Spawn (int) (at Assets/Scripts/Spawner.cs:42)\nUnityEngine.Debug:Log (object)\n",
            play = true,
            scene = "Level1",
            frame = 1534,
            context = "Enemies/Goblin(3)",
            fieldKeys = new[] { "wave" },
            fieldValues = new[] { "2" },
            fingerprint = "41a536b8e9a5"
        };

        [Test]
        public void JsonLine_IsOneLineWithEscapes()
        {
            string json = AIFormat.JsonLine(SampleEntry());
            StringAssert.StartsWith("{\"id\":812,\"t\":\"2026-10-28T12:04:11.300Z\",\"k\":\"exception\",\"sys\":\"Spawn\"", json);
            StringAssert.Contains("\\\"quoted\\\"\\nsecond line", json);
            Assert.AreEqual(-1, json.IndexOf('\n'));
            StringAssert.EndsWith("}", json);
        }

        [Test]
        public void JsonLine_KeepsOnlyProjectFramesAndCountsTheRest()
        {
            string json = AIFormat.JsonLine(SampleEntry());
            StringAssert.Contains("\"at\":[\"Assets/Scripts/Spawner.cs:42 Spawner:Spawn (int)\"]", json);
            StringAssert.Contains("\"engine_frames\":1", json);
            StringAssert.Contains("\"f\":{\"wave\":\"2\"}", json);
            StringAssert.Contains("\"ctx\":\"Enemies/Goblin(3)\"", json);
        }

        [Test]
        public void JsonLine_CompileEntryHasLocation()
        {
            var e = new ConsoleEntry
            {
                id = 5, kind = EntryKind.CompileError, ticks = DateTime.UtcNow.Ticks,
                message = "Assets/Foo.cs(12,5): error CS1002: ; expected", file = "Assets/Foo.cs", line = 12, column = 5
            };
            string json = AIFormat.JsonLine(e);
            StringAssert.Contains("\"msg\":\"error CS1002: ; expected\"", json);
            StringAssert.Contains("\"file\":\"Assets/Foo.cs\",\"line\":12,\"col\":5", json);
        }

        [Test]
        public void Normalize_RestoresNullsAfterJsonRoundTrip()
        {
            // JsonUtility writes null strings/arrays as ""/[] in the domain-reload snapshot.
            var e = new ConsoleEntry { system = "", fingerprint = "", context = "", fieldKeys = new string[0], fieldValues = new string[0] };
            ConsoleStore.Normalize(e);
            Assert.IsNull(e.system);
            Assert.IsNull(e.fingerprint);
            Assert.IsNull(e.context);
            Assert.AreEqual(0, e.FieldCount);
            StringAssert.DoesNotContain("\"sys\"", AIFormat.JsonLine(e));
        }

        [Test]
        public void Clear_RemovesCompileWarningsButKeepsErrors_UntilNextCompile()
        {
            ConsoleStore.ClearCompileIssues();
            ConsoleStore.ForgetDismissedCompileIssues();
            try
            {
                ConsoleStore.SetCompileIssues(new System.Collections.Generic.List<ConsoleEntry>
                {
                    new ConsoleEntry { kind = EntryKind.CompileWarning, file = "Assets/A.cs", line = 1, column = 2,
                        message = "Assets/A.cs(1,2): warning CS0618: x" },
                    new ConsoleEntry { kind = EntryKind.CompileError, file = "Assets/B.cs", line = 3, column = 4,
                        message = "Assets/B.cs(3,4): error CS1002: ; expected" }
                });

                ConsoleStore.Clear();

                Assert.AreEqual(1, ConsoleStore.CompileIssues.Count, "only the error remains");
                Assert.AreEqual(EntryKind.CompileError, ConsoleStore.CompileIssues[0].kind);
                Assert.IsTrue(ConsoleStore.HasCompileIssue("Assets/A.cs", 1, 2, "CS0618"),
                    "a cleared warning is still recognised, so Unity's re-printed copy is dropped");

                ConsoleStore.ForgetDismissedCompileIssues(); // what a new compilation does
                Assert.IsFalse(ConsoleStore.HasCompileIssue("Assets/A.cs", 1, 2, "CS0618"));
            }
            finally
            {
                ConsoleStore.ClearCompileIssues();
                ConsoleStore.ForgetDismissedCompileIssues();
            }
        }

        [Test]
        public void Clip_AddsEllipsisOnlyWhenTooLong()
        {
            Assert.AreEqual("abc", AIFormat.Clip("abc", 5));
            Assert.AreEqual("ab…", AIFormat.Clip("abcdef", 2));
            Assert.AreEqual("", AIFormat.Clip(null, 2));
        }
    }

    public class SearchQueryTests
    {
        private static ConsoleEntry Entry(EntryKind kind, string message, string system = null, int id = 1) =>
            new ConsoleEntry
            {
                id = id, kind = kind, message = message, system = system,
                stackTrace = "Spawner:Spawn (int) (at Assets/Scripts/Spawner.cs:42)\n",
                fieldKeys = new[] { "wave" }, fieldValues = new[] { "2" }
            };

        [Test]
        public void Tokenize_KeepsQuotedPhrases()
        {
            CollectionAssert.AreEqual(new[] { "a", "two words", "-neg ated", "sys:X" },
                SearchQuery.Tokenize("a \"two words\" -\"neg ated\" sys:X"));
        }

        [Test]
        public void EmptyQuery_MatchesEverythingIncludingMarkers()
        {
            var q = new SearchQuery("  ");
            Assert.IsTrue(q.IsEmpty);
            Assert.IsTrue(q.Matches(Entry(EntryKind.Marker, "▶ Entered Play Mode")));
        }

        [Test]
        public void Words_AreAndedAndCaseInsensitive_AcrossMessageSystemAndFields()
        {
            var e = Entry(EntryKind.Error, "Enemy prefab missing", "Spawn");
            Assert.IsTrue(new SearchQuery("ENEMY missing").Matches(e));
            Assert.IsTrue(new SearchQuery("spawn wave").Matches(e));
            Assert.IsFalse(new SearchQuery("enemy audio").Matches(e));
        }

        [Test]
        public void Exclude_Type_System_Id_File_Regex()
        {
            var e = Entry(EntryKind.Exception, "NullReferenceException: boom", "Spawn", 812);
            Assert.IsFalse(new SearchQuery("-boom").Matches(e));
            Assert.IsTrue(new SearchQuery("type:error").Matches(e), "error includes exceptions");
            Assert.IsFalse(new SearchQuery("type:warning").Matches(e));
            Assert.IsTrue(new SearchQuery("sys:spawn").Matches(e));
            Assert.IsFalse(new SearchQuery("-sys:Spawn").Matches(e));
            Assert.IsTrue(new SearchQuery("#812").Matches(e));
            Assert.IsFalse(new SearchQuery("id:813").Matches(e));
            Assert.IsTrue(new SearchQuery("file:Spawner.cs").Matches(e));
            Assert.IsTrue(new SearchQuery("/null\\w+exception/").Matches(e));
        }

        [Test]
        public void NonEmptyQuery_HidesMarkersUnlessRequested()
        {
            var marker = Entry(EntryKind.Marker, "▶ Entered Play Mode");
            Assert.IsFalse(new SearchQuery("play").Matches(marker));
            Assert.IsTrue(new SearchQuery("type:marker play").Matches(marker));
        }

        [Test]
        public void BadRegex_ReportsErrorInsteadOfThrowing()
        {
            var q = new SearchQuery("/([/");
            Assert.IsNotNull(q.Error);
        }
    }

    public class ULogTests
    {
        private LogLevel _globalMin;
        private bool _globalEnabled;

        [SetUp]
        public void SetUp()
        {
            _globalMin = ULog.MinLevel;
            _globalEnabled = ULog.Enabled;
            ULog.MinLevel = LogLevel.Verbose;
            ULog.Enabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            ULog.MinLevel = _globalMin;
            ULog.Enabled = _globalEnabled;
        }

        [Test]
        public void IsOn_RespectsChannelSwitchAndLevels()
        {
            var ch = new LogChannel("Tests.IsOn");
            Assert.IsTrue(ch.IsOn(LogLevel.Verbose));

            ch.MinLevel = LogLevel.Warning;
            Assert.IsFalse(ch.IsOn(LogLevel.Info));
            Assert.IsTrue(ch.IsOn(LogLevel.Warning));

            ch.Enabled = false;
            Assert.IsFalse(ch.IsOn(LogLevel.Warning));
            Assert.IsTrue(ch.IsOn(LogLevel.Error), "errors are never dropped");
        }

        [Test]
        public void IsOn_RespectsGlobalSwitches()
        {
            var ch = new LogChannel("Tests.Global");
            ULog.MinLevel = LogLevel.Warning;
            Assert.IsFalse(ch.IsOn(LogLevel.Info));
            ULog.MinLevel = LogLevel.Verbose;
            ULog.Enabled = false;
            Assert.IsFalse(ch.IsOn(LogLevel.Warning));
            Assert.IsTrue(ch.IsOn(LogLevel.Error));
        }

        [Test]
        public void Channel_ColourIsStableHexFromName()
        {
            var a = new LogChannel("Tests.Colour");
            var b = new LogChannel("Tests.Colour");
            Assert.AreEqual(a.ColorHex, b.ColorHex);
            StringAssert.IsMatch("^#[0-9A-F]{6}$", a.ColorHex);
            Assert.AreEqual("#2ECC71", new LogChannel("Tests.Given", "2ECC71").ColorHex);
        }

        [Test]
        public void FormatLine_TagsChannelAndAppendsFields()
        {
            bool rich = ULog.RichTextTags;
            ULog.RichTextTags = false;
            try
            {
                var ch = new LogChannel("Spawn");
                var rec = new LogRecord(ch, LogLevel.Info, "Spawned", null,
                    new (string, object)[] { ("count", 3), ("speed", 1.5f) }, null, false, -1, 1);
                Assert.AreEqual("[Spawn] Spawned  (count=3, speed=1.5)", ULog.FormatLine(rec));

                var plain = new LogRecord(ULog.Default, LogLevel.Info, "Hello", null, null, null, false, -1, 1);
                Assert.AreEqual("Hello", ULog.FormatLine(plain), "the default channel has no tag");
            }
            finally
            {
                ULog.RichTextTags = rich;
            }
        }

        [Test]
        public void FormatValue_IsCultureInvariant()
        {
            Assert.AreEqual("1.5", LogRecord.FormatValue(1.5f));
            Assert.AreEqual("null", LogRecord.FormatValue(null));
            Assert.AreEqual("text", LogRecord.FormatValue("text"));
        }

        [Test]
        public void Formatter_OverridesLineAndSurvivesExceptions()
        {
            var previous = ULog.Formatter;
            try
            {
                var rec = new LogRecord(new LogChannel("Tests.Fmt"), LogLevel.Info, "m", null, null, null, false, -1, 1);
                ULog.Formatter = r => "custom:" + r.Message;
                Assert.AreEqual("custom:m", ULog.FormatLine(rec));

                ULog.Formatter = _ => throw new InvalidOperationException("nope");
                StringAssert.Contains("nope", ULog.FormatLine(rec));
            }
            finally
            {
                ULog.Formatter = previous;
            }
        }
    }
}
