using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// <c>test editmode|playmode [filter]</c>: runs Unity Test Framework tests and reports the counts, the failing
    /// tests and the NUnit XML path. The filter is a regex on the full test name. Only compiled when the Test
    /// Framework package is installed.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class TestVerb : IAgentVerb, ICallbacks
    {
        internal const string TestsFolder = "tests";
        private const string StateKey = "UtilityTools.AIConsole.TestRun"; // "running" or "done|ok|summary"
        private const string ScenesKey = "UtilityTools.AIConsole.TestRunScenes"; // scenes open before the run
        private const string CleanupKey = "UtilityTools.AIConsole.TestRunCleanupStart";
        /// <summary>The temporary scene the Play Mode runner opens; it puts the user's scenes back a moment after Play Mode ends.</summary>
        private const string RunnerScenePrefix = "Assets/InitTestScene";
        private static readonly TimeSpan CleanupWait = TimeSpan.FromSeconds(15);
        private const int MaxFailuresListed = 10;
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

        private static TestRunnerApi s_Api;

        static TestVerb()
        {
            var verb = new TestVerb();
            AgentVerbs.Register(verb);
            Api.RegisterCallbacks(verb); // again after every domain reload, so a Play Mode run still reports back
        }

        private static TestRunnerApi Api => s_Api != null ? s_Api : s_Api = ScriptableObject.CreateInstance<TestRunnerApi>();

        public string Name => "test";
        public string Usage => "test editmode|playmode [filter]";
        public bool ReadOnly => false;

        public AgentVerbResult Start(AgentVerbCall call)
        {
            string arg = call.Arg.Trim();
            int space = arg.IndexOf(' ');
            string mode = (space < 0 ? arg : arg.Substring(0, space)).ToLowerInvariant();
            string filter = space < 0 ? "" : arg.Substring(space + 1).Trim();

            if (mode != "editmode" && mode != "playmode") return AgentVerbResult.Failure("usage: " + Usage);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return AgentVerbResult.Failure("stop Play Mode first");
            if (RunnerSceneOpen()) return AgentVerbResult.Failure("a previous test run is still restoring your scenes — retry in a few seconds");
            if (EditorUtility.scriptCompilationFailed) return AgentVerbResult.Failure("compile errors — read errors.md");
            if (mode == "playmode" && AnySceneDirty())
                return AgentVerbResult.Failure("unsaved scene changes would block the Play Mode run — save them first");

            string relative = AIBridge.Folder.Replace('\\', '/').TrimEnd('/') + "/" + TestsFolder + "/" +
                              Path.GetFileNameWithoutExtension(call.Request) + "_" + call.Number + ".xml";
            call.Data = relative;
            SessionState.SetString(StateKey, "running|" + relative);
            SessionState.SetString(ScenesKey, EncodeScenes(EditorSceneManager.GetSceneManagerSetup()));

            var testFilter = new Filter { testMode = mode == "editmode" ? TestMode.EditMode : TestMode.PlayMode };
            if (filter.Length > 0) testFilter.groupNames = new[] { filter };
            Api.Execute(new ExecutionSettings(testFilter));
            return AgentVerbResult.Running;
        }

        public AgentVerbResult Check(AgentVerbCall call)
        {
            string state = SessionState.GetString(StateKey, "");
            // A Play Mode run reports its result before Unity has left Play Mode and put the user's scenes back.
            if (state.StartsWith("done|", StringComparison.Ordinal) && !EditorApplication.isPlayingOrWillChangePlaymode &&
                RunnerHasCleanedUp())
            {
                SessionState.EraseString(StateKey);
                SessionState.EraseString(CleanupKey);
                RestoreScenesIfReplaced();
                string[] parts = state.Split(new[] { '|' }, 3);
                return parts[1] == "ok" ? AgentVerbResult.Success(parts[2]) : AgentVerbResult.Failure(parts[2]);
            }

            if (call.Elapsed > Timeout)
            {
                SessionState.EraseString(StateKey);
                return AgentVerbResult.Failure("timed out waiting for the test run");
            }

            return AgentVerbResult.Running;
        }

        // ---------- ICallbacks ----------

        public void RunStarted(ITestAdaptor testsToRun) { }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            string state = SessionState.GetString(StateKey, "");
            if (!state.StartsWith("running|", StringComparison.Ordinal)) return; // a run someone started in the Test Runner window

            string relative = state.Substring("running|".Length);
            string full = Path.GetFullPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            TestRunnerApi.SaveResultToFile(result, full);

            bool ok = result.FailCount == 0 && result.PassCount + result.SkipCount + result.InconclusiveCount > 0;
            SessionState.SetString(StateKey, "done|" + (ok ? "ok" : "failed") + "|" + Summarize(result, relative));
        }

        private static string Summarize(ITestResultAdaptor result, string relative)
        {
            int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            if (total == 0) return "no tests matched — check the mode and the filter";

            var failed = new List<string>();
            CollectFailures(result, failed);
            string summary = string.Format(CultureInfo.InvariantCulture, "passed={0} failed={1} skipped={2} ({3:0.0}s) results={4}",
                result.PassCount, result.FailCount, result.SkipCount + result.InconclusiveCount, result.Duration, relative);
            if (failed.Count > 0)
                summary += " failing: " + string.Join(", ", failed.GetRange(0, Math.Min(failed.Count, MaxFailuresListed))) +
                           (failed.Count > MaxFailuresListed ? ", …" : "");
            return summary;
        }

        private static void CollectFailures(ITestResultAdaptor result, List<string> into)
        {
            if (!result.HasChildren)
            {
                if (result.TestStatus == TestStatus.Failed) into.Add(result.Test.FullName);
                return;
            }

            foreach (var child in result.Children) CollectFailures(child, into);
        }

        /// <summary>True once the runner's temporary scene is gone, or after waiting <see cref="CleanupWait"/> for it.</summary>
        private static bool RunnerHasCleanedUp()
        {
            if (!RunnerSceneOpen()) return true;

            string started = SessionState.GetString(CleanupKey, "");
            if (started.Length == 0)
            {
                SessionState.SetString(CleanupKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            return DateTime.UtcNow - new DateTime(long.Parse(started, CultureInfo.InvariantCulture), DateTimeKind.Utc) > CleanupWait;
        }

        /// <summary>
        /// A test run can leave an empty untitled scene (or the runner's scene) open instead of the user's scenes.
        /// Reopens what was open before, but only in that case, so real changes are never discarded.
        /// </summary>
        private static void RestoreScenesIfReplaced()
        {
            var before = DecodeScenes(SessionState.GetString(ScenesKey, ""));
            SessionState.EraseString(ScenesKey);
            if (before.Length == 0) return;

            var only = SceneManager.sceneCount == 1 ? SceneManager.GetSceneAt(0) : default;
            bool replaced = only.IsValid() && !only.isDirty && (string.IsNullOrEmpty(only.path) || IsRunnerScene(only.path));
            if (replaced) EditorSceneManager.RestoreSceneManagerSetup(before);
        }

        private static bool RunnerSceneOpen()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (IsRunnerScene(SceneManager.GetSceneAt(i).path)) return true;
            return false;
        }

        private static bool IsRunnerScene(string path) => path != null && path.StartsWith(RunnerScenePrefix, StringComparison.Ordinal);

        /// <summary>Saved scenes only: untitled scenes and the runner's scene can't be reopened.</summary>
        private static string EncodeScenes(SceneSetup[] setup)
        {
            var parts = new List<string>();
            foreach (var s in setup)
                if (!string.IsNullOrEmpty(s.path) && !IsRunnerScene(s.path))
                    parts.Add(s.path + "|" + (s.isLoaded ? 1 : 0) + "|" + (s.isActive ? 1 : 0));
            return string.Join("\n", parts);
        }

        private static SceneSetup[] DecodeScenes(string text)
        {
            var list = new List<SceneSetup>();
            foreach (string line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.Split('|');
                if (f.Length == 3 && AssetDatabase.LoadAssetAtPath<SceneAsset>(f[0]) != null)
                    list.Add(new SceneSetup { path = f[0], isLoaded = f[1] == "1", isActive = f[2] == "1" });
            }

            if (list.Count > 0 && !list.Exists(s => s.isActive))
            {
                var first = list[0];
                first.isActive = true;
                first.isLoaded = true;
            }

            return list.ToArray();
        }

        private static bool AnySceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            return false;
        }
    }
}
