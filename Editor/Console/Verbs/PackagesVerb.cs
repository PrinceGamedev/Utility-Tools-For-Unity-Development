#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.PackageManager;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// <c>packages resolve</c>: re-reads Packages/manifest.json and waits for any compile it causes.
    /// Unity otherwise resolves packages only when its window gains focus.
    /// </summary>
    internal sealed class PackagesVerb : IAgentVerb
    {
        /// <summary>How long to wait for a resolve to start compiling before reporting "nothing to compile".</summary>
        private static readonly TimeSpan CompileGrace = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

        public string Name => "packages";
        public string Usage => "packages resolve";
        public bool ReadOnly => false;

        public AgentVerbResult Start(AgentVerbCall call)
        {
            if (call.Arg.Trim() != "resolve") return AgentVerbResult.Failure("usage: " + Usage);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return AgentVerbResult.Failure("stop Play Mode first");

            call.Data = ConsoleStore.LastCompileTicks.ToString(CultureInfo.InvariantCulture);
            Client.Resolve();
            return AgentVerbResult.Running;
        }

        public AgentVerbResult Check(AgentVerbCall call)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (!call.Data.EndsWith("+", StringComparison.Ordinal)) call.Data += "+"; // a compile was seen
                return AgentVerbResult.Running;
            }

            long baseline = long.Parse(call.Data.TrimEnd('+'), CultureInfo.InvariantCulture);
            string took = string.Format(CultureInfo.InvariantCulture, " ({0:0.0}s)", call.Elapsed.TotalSeconds);

            if (ConsoleStore.LastCompileTicks > baseline)
            {
                int errors = ConsoleStore.CompileErrorCount;
                return errors == 0
                    ? AgentVerbResult.Success("resolved; compiled" + took)
                    : AgentVerbResult.Failure($"resolved; compile failed: {errors} error(s) — read errors.md{took}");
            }

            bool compileSeen = call.Data.EndsWith("+", StringComparison.Ordinal);
            if (!compileSeen && call.Elapsed > CompileGrace) return AgentVerbResult.Success("resolved; nothing to compile" + took);
            if (call.Elapsed > Timeout) return AgentVerbResult.Failure("timed out waiting for the package resolve");
            return AgentVerbResult.Running;
        }
    }
}
#endif
