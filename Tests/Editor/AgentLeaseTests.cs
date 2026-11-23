using System;
using NUnit.Framework;
using UtilityTools.AIConsole;

namespace UtilityTools.Tests
{
    public class AgentLeaseTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

        private static LeaseInfo HeldBy(string agent, int minutesLeft) => new LeaseInfo
        {
            agent = agent,
            task = "work",
            taken = AIFormat.Iso(Now.AddMinutes(-1).Ticks),
            expires = AIFormat.Iso(Now.AddMinutes(minutesLeft).Ticks)
        };

        [Test]
        public void TakeWhenFreeReservesTheEditor()
        {
            string result = AgentLease.Apply(null, "alpha", "take 10 balance pass", Now, out var next, out bool ok);

            Assert.IsTrue(ok, result);
            Assert.AreEqual("alpha", next.agent);
            Assert.AreEqual("balance pass", next.task);
            Assert.IsTrue(AgentLease.IsActive(next, Now.AddMinutes(9)));
            Assert.IsFalse(AgentLease.IsActive(next, Now.AddMinutes(11)));
        }

        [Test]
        public void TakeIsCappedAtTheMaximum()
        {
            string result = AgentLease.Apply(null, "alpha", "take 60 long", Now, out var next, out bool ok);

            Assert.IsTrue(ok);
            StringAssert.Contains("capped", result);
            Assert.IsFalse(AgentLease.IsActive(next, Now.AddMinutes(AgentLease.MaxMinutes + 1)));
        }

        [Test]
        public void TakeFailsWhileAnotherAgentHoldsIt()
        {
            var held = HeldBy("alpha", 5);
            string result = AgentLease.Apply(held, "beta", "take 5 mine", Now, out var next, out bool ok);

            Assert.IsFalse(ok);
            StringAssert.StartsWith("held by alpha", result);
            Assert.AreSame(held, next);
        }

        [Test]
        public void TakeByTheHolderExtendsAndKeepsTheStartTime()
        {
            var held = HeldBy("alpha", 2);
            AgentLease.Apply(held, "alpha", "take 10", Now, out var next, out bool ok);

            Assert.IsTrue(ok);
            Assert.AreEqual(held.taken, next.taken);
            Assert.AreEqual("work", next.task, "no new task keeps the old one");
            Assert.IsTrue(AgentLease.IsActive(next, Now.AddMinutes(9)));
        }

        [Test]
        public void AnExpiredLeaseIsFree()
        {
            var expired = HeldBy("alpha", -1);
            Assert.IsFalse(AgentLease.IsActive(expired, Now));

            AgentLease.Apply(expired, "beta", "take 5 now mine", Now, out var next, out bool ok);
            Assert.IsTrue(ok);
            Assert.AreEqual("beta", next.agent);
        }

        [Test]
        public void OnlyTheHolderCanRenewOrRelease()
        {
            var held = HeldBy("alpha", 5);

            AgentLease.Apply(held, "beta", "renew 5", Now, out _, out bool renewOk);
            AgentLease.Apply(held, "beta", "release", Now, out var afterBeta, out bool releaseOk);
            AgentLease.Apply(held, "alpha", "release", Now, out var afterAlpha, out bool holderOk);

            Assert.IsFalse(renewOk);
            Assert.IsFalse(releaseOk);
            Assert.AreSame(held, afterBeta);
            Assert.IsTrue(holderOk);
            Assert.IsNull(afterAlpha);
        }

        [Test]
        public void BreakFreesAnyoneElsesLease()
        {
            string result = AgentLease.Apply(HeldBy("alpha", 5), "lead", "break", Now, out var next, out bool ok);

            Assert.IsTrue(ok);
            Assert.IsNull(next);
            StringAssert.Contains("was alpha", result);
        }

        [Test]
        public void StatusAndUnknownActions()
        {
            Assert.AreEqual("free", AgentLease.Apply(null, "a", "status", Now, out _, out bool freeOk));
            Assert.IsTrue(freeOk);
            StringAssert.StartsWith("alpha until", AgentLease.Apply(HeldBy("alpha", 5), "a", "", Now, out _, out _));

            string bad = AgentLease.Apply(null, "a", "grab 5", Now, out _, out bool badOk);
            Assert.IsFalse(badOk);
            Assert.AreEqual(AgentLease.Usage, bad);
            AgentLease.Apply(null, "a", "take soon", Now, out var noLease, out bool wordOk);
            Assert.IsFalse(wordOk, "minutes must be a number");
            Assert.IsNull(noLease);
        }

        [Test]
        public void ReadOnlyBatchesRunDuringSomeoneElsesLease()
        {
            var held = HeldBy("alpha", 5);
            var readOnly = CommandLine.Parse("status\nmark check\nwait 2\nlease status\nshow 12\n");
            var changing = CommandLine.Parse("status\nrefresh\n");

            Assert.IsTrue(AgentLease.Allows(held, "beta", readOnly, Now));
            Assert.IsFalse(AgentLease.Allows(held, "beta", changing, Now));
            Assert.IsTrue(AgentLease.Allows(held, "alpha", changing, Now), "the holder runs anything");
            Assert.IsTrue(AgentLease.Allows(null, "beta", changing, Now), "no lease: everyone runs");
            Assert.IsTrue(AgentLease.Allows(HeldBy("alpha", -1), "beta", changing, Now), "expired lease: everyone runs");
        }
    }
}
