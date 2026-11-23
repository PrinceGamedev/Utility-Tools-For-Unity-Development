using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UtilityTools.AIConsole;

namespace UtilityTools.Tests
{
    public class AgentQueueTests
    {
        private string _folder;

        [SetUp]
        public void CreateFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "UtilityToolsQueueTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_folder, AgentQueue.QueueFolder));
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private void Write(string relativePath, DateTime writtenUtc)
        {
            string path = Path.Combine(_folder, relativePath);
            File.WriteAllText(path, "status\n");
            File.SetLastWriteTimeUtc(path, writtenUtc);
        }

        [Test]
        public void AgentIdIsTheTextBeforeTheFirstUnderscore()
        {
            Assert.AreEqual("gameplay-01", AgentQueue.AgentOf("gameplay-01_0007.txt"));
            Assert.AreEqual("qa", AgentQueue.AgentOf("qa_run_2.txt"));
            Assert.AreEqual("solo", AgentQueue.AgentOf("solo.txt"));
            Assert.AreEqual("badname", AgentQueue.AgentOf("bad name!@_1.txt"), "unsafe characters are dropped");
            Assert.AreEqual(AgentQueue.LegacyAgent, AgentQueue.AgentOf("!!_x.txt"));
        }

        [Test]
        public void PendingIsOldestFirstAndIncludesTheLegacyFile()
        {
            var t = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            Write("queue/beta_1.txt", t.AddSeconds(2));
            Write("queue/alpha_1.txt", t.AddSeconds(5));
            Write(AICommands.CommandsFile, t);
            Write("queue/alpha_2.txt.tmp", t.AddSeconds(-10));

            var pending = AgentQueue.Pending(_folder);

            CollectionAssert.AreEqual(new[] { AICommands.CommandsFile, "beta_1.txt", "alpha_1.txt" }, pending.Select(r => r.Name).ToArray(),
                "oldest first; .tmp files are still being written");
            Assert.IsTrue(pending[0].Legacy);
            Assert.AreEqual(AgentQueue.LegacyAgent, pending[0].Agent);
            Assert.AreEqual("beta", pending[1].Agent);
        }

        [Test]
        public void SameTimeIsOrderedByName()
        {
            var t = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            Write("queue/b_1.txt", t);
            Write("queue/a_1.txt", t);

            CollectionAssert.AreEqual(new[] { "a_1.txt", "b_1.txt" }, AgentQueue.Pending(_folder).Select(r => r.Name).ToArray());
        }

        [Test]
        public void EachRequestGetsItsOwnResultsFile()
        {
            Assert.AreEqual(Path.Combine(_folder, AICommands.ResultsFile), AgentQueue.ResultsPath(_folder, true, AICommands.CommandsFile));
            Assert.AreEqual(Path.Combine(_folder, AgentQueue.ResultsFolder, "qa_7.txt"), AgentQueue.ResultsPath(_folder, false, "qa_7.txt"));
        }

        [Test]
        public void OldResultsArePruned()
        {
            Directory.CreateDirectory(Path.Combine(_folder, AgentQueue.ResultsFolder));
            var now = DateTime.UtcNow;
            Write("results/old.txt", now.AddHours(-30));
            Write("results/new.txt", now.AddMinutes(-5));

            AgentQueue.PruneResults(_folder, now);

            Assert.IsFalse(File.Exists(Path.Combine(_folder, "results/old.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "results/new.txt")));
        }
    }
}
