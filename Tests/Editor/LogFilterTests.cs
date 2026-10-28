using NUnit.Framework;
using UnityEngine;
using UtilityTools.AIConsole;
using UtilityTools.Logging;

namespace UtilityTools.Tests
{
    public class LogFilterTests
    {
        [TearDown]
        public void TearDown() => LogFilter.Clear();

        private static void Keep(bool untagged, bool errors, params string[] tags) =>
            LogFilter.SetRules(new LogFilter.Rules(tags, untagged, errors));

        [Test]
        public void NoFilter_EveryLogPasses()
        {
            Assert.IsFalse(LogFilter.Active);
            Assert.IsTrue(LogFilter.PassesMessage(LogType.Log, "[Ads] Banner"));
            Assert.IsTrue(LogFilter.PassesULog(LogType.Log, ULog.Default, null));
        }

        [TestCase("[UI] Opened", true)]
        [TestCase("[ui] Opened", true)]
        [TestCase("  [Ads][UI] Banner", true)]
        [TestCase("[Ads] [UI] Banner", true)]
        [TestCase("[Ads] Banner", false)]
        [TestCase("Plain text", false)]
        [TestCase("[12:00] not a tag", false)]
        [TestCase("Opened [UI]", false)]
        [TestCase("", false)]
        public void Message_IsKeptWhenAnyLeadingTagIsPicked(string message, bool kept)
        {
            Keep(false, true, "UI");
            Assert.AreEqual(kept, LogFilter.PassesMessage(LogType.Log, message));
        }

        [Test]
        public void Untagged_FollowsItsSwitch_TaggedLogsStillNeedAPickedTag()
        {
            Keep(true, true, "UI");
            Assert.IsTrue(LogFilter.PassesMessage(LogType.Log, "Plain text"));
            Assert.IsTrue(LogFilter.PassesMessage(LogType.Warning, "[12:00] not a tag"));
            Assert.IsFalse(LogFilter.PassesMessage(LogType.Log, "[Ads] Banner"));
        }

        [Test]
        public void Errors_PassWhateverTheirTags_WhenKept()
        {
            Keep(false, true);
            Assert.IsTrue(LogFilter.PassesMessage(LogType.Error, "[Ads] Failed"));
            Assert.IsTrue(LogFilter.PassesMessage(LogType.Exception, null));
            Assert.IsTrue(LogFilter.PassesMessage(LogType.Assert, "Plain"));
            Assert.IsFalse(LogFilter.PassesMessage(LogType.Warning, "[Ads] Slow"));

            Keep(false, false, "UI");
            Assert.IsFalse(LogFilter.PassesMessage(LogType.Error, "[Ads] Failed"));
            Assert.IsTrue(LogFilter.PassesMessage(LogType.Error, "[UI] Failed"));
        }

        [Test]
        public void ULog_UsesSystemAndTags_DefaultChannelIsUntagged()
        {
            var ga = new LogChannel("GA", "#F2C14E");
            var ads = new LogChannel("Ads", "#ffffff");
            Keep(false, true, "GA");

            Assert.IsTrue(LogFilter.PassesULog(LogType.Log, ga, null));
            Assert.IsTrue(LogFilter.PassesULog(LogType.Log, ads, new[] { "ga" }));
            Assert.IsFalse(LogFilter.PassesULog(LogType.Log, ads, new[] { "UI" }));
            Assert.IsFalse(LogFilter.PassesULog(LogType.Log, ULog.Default, null));
            Assert.IsTrue(LogFilter.PassesULog(LogType.Error, ads, null));

            Keep(true, true, "GA");
            Assert.IsTrue(LogFilter.PassesULog(LogType.Log, ULog.Default, null));
            Assert.IsFalse(LogFilter.PassesULog(LogType.Log, ULog.Default, new[] { "UI" }));
        }

        [Test]
        public void MessageOf_ReadsDebugLogArgument()
        {
            Assert.AreEqual("[UI] Opened", LogFilter.MessageOf("{0}", new object[] { "[UI] Opened" }));
            Assert.AreEqual("42", LogFilter.MessageOf("{0}", new object[] { 42 }));
            Assert.AreEqual("[UI] {0} coins", LogFilter.MessageOf("[UI] {0} coins", new object[] { 5 }));
        }

        [Test]
        public void BuildRules_DescribeWhatIsKept()
        {
            var rules = new BuildLogRules();
            Assert.AreEqual("every log", rules.Describe());

            rules.filter = true;
            rules.Set("UI", true);
            rules.Set("GA", true);
            rules.Set("ui", false);
            Assert.IsTrue(rules.Has("ga"));
            Assert.IsFalse(rules.Has("UI"));
            Assert.AreEqual("GA + untagged + errors", rules.Describe());

            rules.tags.Clear();
            rules.keepUntagged = false;
            rules.keepErrors = false;
            Assert.AreEqual("no logs", rules.Describe());
        }
    }
}
