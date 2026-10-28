using NUnit.Framework;
using UtilityTools.AIConsole;
using UtilityTools.Logging;

namespace UtilityTools.Tests
{
    public class LogTagTests
    {
        [Test]
        public void ToString_IsTheBracketedName_SoInterpolationWritesATag()
        {
            var tag = new LogTag("TestUiTag", "#4FC3F7");
            Assert.AreEqual("[TestUiTag]", tag.ToString());
            Assert.AreEqual("[TestUiTag] Opened", $"{tag} Opened");

            Assert.IsTrue(StackTraceParser.TrySplitTag($"{tag} Opened", out var name, out var rest));
            Assert.AreEqual("TestUiTag", name);
            Assert.AreEqual("Opened", rest);
        }

        [Test]
        public void Constructor_CleansNameAndNormalizesColour()
        {
            var tag = new LogTag(" [TestClean] ", "a1b2c3");
            Assert.AreEqual("TestClean", tag.Name);
            Assert.AreEqual("#a1b2c3", tag.ColorHex);
            StringAssert.StartsWith("#", new LogTag("TestNoColour").ColorHex);
        }

        [Test]
        public void Find_IsCaseInsensitive_AndLastDefinitionWins()
        {
            new LogTag("TestFindTag", "#111111");
            var second = new LogTag("TestFindTag", "#222222");
            Assert.AreEqual(second.ColorHex, LogTag.Find("testfindtag").ColorHex);
            Assert.IsNull(LogTag.Find("TestNoSuchTag"));
            Assert.IsNull(LogTag.Find(null));
        }

        [Test]
        public void Names_SkipsNulls()
        {
            var a = new LogTag("TestNamesA");
            var names = LogTag.Names(new[] { a, null });
            Assert.AreEqual(1, names.Length);
            Assert.AreEqual("TestNamesA", names[0]);
            Assert.IsNull(LogTag.Names(null));
        }
    }

    public class ShortMethodTests
    {
        [TestCase("GameAnalyticsSDK.Wrapper.GA_Wrapper:setEnabledInfoLog (bool)", "GA_Wrapper.setEnabledInfoLog")]
        [TestCase("Spawner:Spawn (int)", "Spawner.Spawn")]
        [TestCase("Foo.Bar.Baz (System.Int32 x)", "Bar.Baz")]
        [TestCase("Game.Enemy..ctor ()", "Enemy.ctor")]
        [TestCase("Game.Enemy:.ctor ()", "Enemy.ctor")]
        [TestCase("Main", "Main")]
        [TestCase("", "")]
        public void ShortMethod(string full, string expected) => Assert.AreEqual(expected, StackTraceParser.ShortMethod(full));
    }
}
