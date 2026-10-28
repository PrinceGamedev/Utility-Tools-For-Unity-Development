using NUnit.Framework;
using UtilityTools.AIConsole;

namespace UtilityTools.Tests
{
    public class AgentBlockTests
    {
        private static readonly string Block = AgentBlock.Build("Logs/AIConsole");

        [Test]
        public void Build_HasMarkersAndFolder()
        {
            StringAssert.StartsWith(AgentBlock.BeginPrefix, Block);
            StringAssert.EndsWith(AgentBlock.End + "\n", Block);
            StringAssert.Contains("`Logs/AIConsole/commands.txt`", Block);
            StringAssert.DoesNotContain("\r", Block);
        }

        [Test]
        public void Build_UsesConfiguredFolder()
        {
            StringAssert.Contains("`Temp/Agent/commands.txt`", AgentBlock.Build("Temp\\Agent\\"));
            StringAssert.Contains("f=Temp/Agent;", AgentBlock.Build("Temp/Agent"));
        }

        [Test]
        public void Merge_AppendsAfterExistingText_WithOneBlankLine()
        {
            string merged = AgentBlock.Merge("# Project\n\nRules.\n\n\n", Block);
            StringAssert.StartsWith("# Project\n\nRules.\n\n" + AgentBlock.BeginPrefix, merged);
            Assert.AreEqual(AgentFileState.Installed, AgentBlock.StateOf(merged, Block));
        }

        [Test]
        public void Merge_EmptyOrMissingFile_IsJustTheSection()
        {
            Assert.AreEqual(Block, AgentBlock.Merge("", Block));
            Assert.AreEqual(Block, AgentBlock.Merge(null, Block));
        }

        [Test]
        public void Merge_ReplacesOnlyTheSection_AndKeepsCrlf()
        {
            string old = AgentBlock.BeginPrefix + " v0 -->\r\nold text\r\n" + AgentBlock.End;
            string file = "# Top\r\n\r\n" + old + "\r\n\r\n## After\r\nkeep me\r\n";
            Assert.AreEqual(AgentFileState.Outdated, AgentBlock.StateOf(file, Block));

            string merged = AgentBlock.Merge(file, Block);
            StringAssert.StartsWith("# Top\r\n\r\n" + AgentBlock.BeginPrefix, merged);
            StringAssert.EndsWith(AgentBlock.End + "\r\n\r\n## After\r\nkeep me\r\n", merged);
            StringAssert.DoesNotContain("old text", merged);
            Assert.IsFalse(merged.Replace("\r\n", "").Contains("\n"), "mixed line endings");
            Assert.AreEqual(AgentFileState.Installed, AgentBlock.StateOf(merged, Block));
            Assert.AreEqual(merged, AgentBlock.Merge(merged, Block), "merging twice changes nothing");
        }

        [Test]
        public void StateOf_NoFileAndNotInstalled()
        {
            Assert.AreEqual(AgentFileState.NoFile, AgentBlock.StateOf(null, Block));
            Assert.AreEqual(AgentFileState.NotInstalled, AgentBlock.StateOf("# Notes\n", Block));
            Assert.AreEqual(AgentFileState.NotInstalled, AgentBlock.StateOf(AgentBlock.BeginPrefix + " but no end", Block),
                "a begin marker without an end is not treated as a section");
        }

        [Test]
        public void Remove_RestoresTheOriginalFile()
        {
            const string original = "# Project\n\nRules.\n";
            Assert.AreEqual(original, AgentBlock.Remove(AgentBlock.Merge(original, Block)));

            const string middle = "# Top\r\n\r\n## After\r\nkeep\r\n";
            string withSection = "# Top\r\n\r\n" + Block.Replace("\n", "\r\n") + "\r\n## After\r\nkeep\r\n";
            Assert.AreEqual(middle, AgentBlock.Remove(withSection));

            Assert.AreEqual("", AgentBlock.Remove(Block));
            Assert.AreEqual("no section\n", AgentBlock.Remove("no section\n"));
        }
    }

    public class CommandLineTests
    {
        [Test]
        public void Parse_VerbsArgsAndNoise()
        {
            var c = CommandLine.Parse("refresh\r\n\r\n// comment\n  `play`  \n/wait 5\n- mark Run 2: spawn\nASK Start level 3, then press Done\n");
            Assert.AreEqual(5, c.Count);
            Assert.AreEqual("refresh", c[0].Verb);
            Assert.AreEqual("play", c[1].Verb);
            Assert.AreEqual("wait", c[2].Verb);
            Assert.AreEqual("5", c[2].Arg);
            Assert.AreEqual("mark", c[3].Verb);
            Assert.AreEqual("Run 2: spawn", c[3].Arg);
            Assert.AreEqual("ask", c[4].Verb);
            Assert.AreEqual("Start level 3, then press Done", c[4].Arg);
        }

        [Test]
        public void Parse_EmptyInput()
        {
            Assert.AreEqual(0, CommandLine.Parse(null).Count);
            Assert.AreEqual(0, CommandLine.Parse("\n \n// only a comment\n").Count);
        }

        [TestCase("812", 812)]
        [TestCase("#812", 812)]
        [TestCase("id:7", 7)]
        [TestCase(" #12 ", 12)]
        [TestCase("abc", -1)]
        [TestCase("-3", -1)]
        [TestCase("", -1)]
        public void ParseId(string arg, int expected) => Assert.AreEqual(expected, CommandLine.ParseId(arg));
    }
}
