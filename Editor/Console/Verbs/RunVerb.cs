#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// <c>run &lt;id&gt; [args]</c> calls a project method marked <see cref="AgentCommandAttribute"/>; <c>run</c> lists them.
    /// Only marked methods can be called, so agents never run arbitrary code.
    /// </summary>
    internal sealed class RunVerb : IAgentVerb
    {
        public string Name => "run";
        public string Usage => "run [<id> [args]]";
        public bool ReadOnly => false;

        public AgentVerbResult Start(AgentVerbCall call)
        {
            var commands = Discover(TypeCache.GetMethodsWithAttribute<AgentCommandAttribute>());
            string arg = call.Arg.Trim();
            if (arg.Length == 0) return AgentVerbResult.Success(List(commands));

            int space = arg.IndexOfAny(new[] { ' ', '\t' });
            string id = space < 0 ? arg : arg.Substring(0, space);
            string rest = space < 0 ? "" : arg.Substring(space + 1).Trim();

            var matches = commands.Where(c => c.Id == id).ToList();
            if (matches.Count == 0) return AgentVerbResult.Failure($"no command `{id}` — send `run` to list them");
            if (matches.Count > 1) return AgentVerbResult.Failure($"`{id}` is defined twice: " + string.Join(", ", matches.Select(m => m.Where)));
            if (matches[0].Problem != null) return AgentVerbResult.Failure(matches[0].Where + ": " + matches[0].Problem);

            object value = Invoke(matches[0].Method, rest);
            return AgentVerbResult.Success(value == null ? "ok" : value.ToString());
        }

        public AgentVerbResult Check(AgentVerbCall call) => AgentVerbResult.Failure("interrupted");

        internal sealed class Entry
        {
            public string Id;
            public string Usage;
            public MethodInfo Method;
            public string Problem;
            public string Where => Method.DeclaringType?.FullName + "." + Method.Name;
        }

        /// <summary>The marked methods with their ids, and why one can't be called (<c>Problem</c>), if so.</summary>
        internal static List<Entry> Discover(IEnumerable<MethodInfo> methods)
        {
            var list = new List<Entry>();
            foreach (var method in methods)
            {
                var attribute = method.GetCustomAttribute<AgentCommandAttribute>();
                if (attribute == null) continue;
                list.Add(new Entry
                {
                    Id = attribute.Id ?? "",
                    Usage = string.IsNullOrEmpty(attribute.Usage) ? attribute.Id : attribute.Usage,
                    Method = method,
                    Problem = ProblemWith(method, attribute.Id)
                });
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        private static string ProblemWith(MethodInfo method, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(new[] { ' ', '\t' }) >= 0) return "the id must be one word";
            if (!method.IsStatic) return "the method must be static";
            if (method.ContainsGenericParameters) return "the method can't be generic";
            var parameters = method.GetParameters();
            if (parameters.Length > 1 || parameters.Length == 1 && parameters[0].ParameterType != typeof(string))
                return "the method must take no parameters or one string";
            return null;
        }

        private static object Invoke(MethodInfo method, string rest)
        {
            var args = method.GetParameters().Length == 1 ? new object[] { rest } : Array.Empty<object>();
            return method.Invoke(null, args);
        }

        private static string List(List<Entry> commands)
        {
            if (commands.Count == 0) return "no commands — mark a static method with [UtilityTools.AgentCommand(\"id\")]";
            return "commands:\n" + string.Join("\n", commands.Select(c => "- " + c.Usage + (c.Problem != null ? "  (can't run: " + c.Problem + ")" : "")));
        }
    }
}
#endif
