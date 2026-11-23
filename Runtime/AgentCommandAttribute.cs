using System;

namespace UtilityTools
{
    /// <summary>
    /// Marks a static method an AI agent may call with <c>run &lt;id&gt; [args]</c> through the AI Console
    /// (Editor only). The method takes no parameters, or one <c>string</c> with the rest of the line.
    /// Its return value (if any) becomes the result; throw to report a failure.
    /// <code>
    /// [AgentCommand("level.reset", "level.reset [index]")]
    /// static string ResetLevel(string args) { … return "reset level 2"; }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class AgentCommandAttribute : Attribute
    {
        public AgentCommandAttribute(string id, string usage = null)
        {
            Id = id;
            Usage = usage;
        }

        /// <summary>The name agents use, e.g. <c>shipduel.fire</c>. Unique per project.</summary>
        public string Id { get; }

        /// <summary>Shown by <c>run</c> with no arguments; defaults to the id.</summary>
        public string Usage { get; }
    }
}
