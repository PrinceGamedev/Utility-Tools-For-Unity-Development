#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UtilityTools.AIConsole
{
    /// <summary>One running command as a verb sees it. <see cref="Data"/> survives domain reloads.</summary>
    internal sealed class AgentVerbCall
    {
        public AgentVerbCall(string arg, string agent, string request, int number, DateTime startedUtc, string data)
        {
            Arg = arg ?? "";
            Agent = agent;
            Request = request;
            Number = number;
            StartedUtc = startedUtc;
            Data = data ?? "";
        }

        public string Arg { get; }
        public string Agent { get; }
        /// <summary>The request file name, used to name output files.</summary>
        public string Request { get; }
        /// <summary>The command's position in its batch.</summary>
        public int Number { get; }
        public DateTime StartedUtc { get; }
        public TimeSpan Elapsed => DateTime.UtcNow - StartedUtc;
        public string Data { get; set; }
    }

    internal readonly struct AgentVerbResult
    {
        public readonly bool Done;
        public readonly bool Ok;
        public readonly string Text;

        private AgentVerbResult(bool done, bool ok, string text)
        {
            Done = done;
            Ok = ok;
            Text = text;
        }

        public static AgentVerbResult Running => new AgentVerbResult(false, false, null);
        public static AgentVerbResult Success(string text) => new AgentVerbResult(true, true, text);
        public static AgentVerbResult Failure(string text) => new AgentVerbResult(true, false, text);
    }

    /// <summary>
    /// A command agents can send besides the built-in ones in <see cref="AICommands"/>.
    /// <see cref="Start"/> runs once; while it returns <see cref="AgentVerbResult.Running"/>,
    /// <see cref="Check"/> is called every Editor tick (also after a domain reload).
    /// </summary>
    internal interface IAgentVerb
    {
        string Name { get; }
        string Usage { get; }
        /// <summary>True when it only reads state, so it may run while another agent holds the lease.</summary>
        bool ReadOnly { get; }
        AgentVerbResult Start(AgentVerbCall call);
        AgentVerbResult Check(AgentVerbCall call);
    }

    internal static class AgentVerbs
    {
        private static readonly Dictionary<string, IAgentVerb> s_Verbs = new Dictionary<string, IAgentVerb>(StringComparer.Ordinal);

        static AgentVerbs()
        {
            Register(new SceneVerb());
            Register(new PackagesVerb());
            Register(new RunVerb());
            Register(new ScreenshotVerb());
            Register(new InspectVerb());
        }

        /// <summary>Adds or replaces a verb. Optional assemblies (the test runner bridge) call this on load.</summary>
        internal static void Register(IAgentVerb verb) => s_Verbs[verb.Name] = verb;

        internal static bool TryGet(string name, out IAgentVerb verb) => s_Verbs.TryGetValue(name ?? "", out verb);

        internal static IEnumerable<IAgentVerb> All => s_Verbs.Values;
    }
}
#endif
