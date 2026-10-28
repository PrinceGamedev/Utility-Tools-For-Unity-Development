using System.Collections.Generic;
using NUnit.Framework;
using UtilityTools.AIConsole;

namespace UtilityTools.Tests
{
    public class CompileIssueTests
    {
        private static ConsoleEntry Warning(string file, string assembly) => new ConsoleEntry
        {
            kind = EntryKind.CompileWarning,
            file = file,
            line = 1,
            message = file + "(1,1): warning CS0162: Unreachable code detected",
            assembly = assembly
        };

        [Test]
        public void Keep_OnlyMessagesOfAssembliesThatWereNotRebuilt()
        {
            var game = Warning("Assets/Game.cs", "Assembly-CSharp");
            var tool = Warning("Packages/x/Tool.cs", "UtilityTools.Editor");
            var kept = ConsoleStore.KeepCompileIssues(new[] { game, tool }, new HashSet<string> { "UtilityTools.Editor" }, null, null);

            Assert.AreEqual(1, kept.Count);
            Assert.AreEqual("Assets/Game.cs", kept[0].file);
        }

        [Test]
        public void Keep_UsesTheLookupForMessagesWithoutAnAssembly_AndDropsUnknownOnes()
        {
            var known = Warning("Assets/Game.cs", null);
            var unknown = Warning("Assets/Gone.cs", null);
            var kept = ConsoleStore.KeepCompileIssues(new[] { known, unknown }, new HashSet<string>(),
                e => e.file == "Assets/Game.cs" ? "Assembly-CSharp" : null, null);

            Assert.AreEqual(1, kept.Count);
            Assert.AreEqual("Assembly-CSharp", kept[0].assembly, "the looked-up assembly is remembered");
        }

        [Test]
        public void Keep_DropsMessagesWhoseFileIsGone()
        {
            var deleted = Warning("Assets/Deleted.cs", "Assembly-CSharp");
            var kept = ConsoleStore.KeepCompileIssues(new[] { deleted }, new HashSet<string>(), null, f => false);
            Assert.AreEqual(0, kept.Count);
        }

        [TestCase("Library/ScriptAssemblies/Assembly-CSharp.dll", "Assembly-CSharp")]
        [TestCase("UtilityTools.Editor.dll", "UtilityTools.Editor")]
        [TestCase("Game", "Game")]
        [TestCase("", null)]
        public void AssemblyKey(string input, string expected) => Assert.AreEqual(expected, StackTraceParser.AssemblyKey(input));
    }
}
