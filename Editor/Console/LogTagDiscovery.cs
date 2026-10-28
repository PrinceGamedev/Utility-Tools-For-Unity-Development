#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UtilityTools.Logging;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// Finds the <see cref="LogTag"/>s a project defines, so the AI Console shows them (with their colours)
    /// before any log uses them. Only classes whose static fields are all LogTags are initialised — the
    /// recommended <c>public static class Tags { … }</c> — so no other game code runs in edit mode.
    /// Tags declared anywhere else still appear once the code runs.
    /// </summary>
    [InitializeOnLoad]
    internal static class LogTagDiscovery
    {
        private const string RuntimeAssembly = "UtilityTools.Runtime";

        /// <summary>"Tags.UI"-style declaration of each discovered tag, by name (for the settings page).</summary>
        internal static readonly Dictionary<string, string> Declarations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static LogTagDiscovery()
        {
            EditorApplication.delayCall += Run;
        }

        internal static void Run()
        {
            Declarations.Clear();
            int before = LogTag.All.Count;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!References(asm, RuntimeAssembly)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                foreach (var type in types)
                    if (type != null && IsTagHolder(type, out var fields)) Initialise(type, fields);
            }

            if (LogTag.All.Count != before)
            {
                ChannelConfig.ApplyAll();
                ConsoleStore.NotifyChanged();
            }
        }

        private static bool References(Assembly asm, string name)
        {
            if (asm.IsDynamic) return false;
            try
            {
                foreach (var r in asm.GetReferencedAssemblies())
                    if (r.Name == name) return true;
            }
            catch (Exception)
            {
                // Some dynamic or broken assemblies can't list references.
            }

            return false;
        }

        /// <summary>True when the type declares LogTag fields and no other static state.</summary>
        internal static bool IsTagHolder(Type type, out List<FieldInfo> tagFields)
        {
            tagFields = null;
            if (type.ContainsGenericParameters) return false;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            FieldInfo[] fields;
            try { fields = type.GetFields(flags); }
            catch (Exception) { return false; }

            foreach (var f in fields)
            {
                if (f.IsLiteral) continue; // const
                if (f.FieldType != typeof(LogTag)) return false;
                (tagFields ??= new List<FieldInfo>()).Add(f);
            }

            return tagFields != null;
        }

        private static void Initialise(Type type, List<FieldInfo> fields)
        {
            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                foreach (var f in fields)
                    if (f.GetValue(null) is LogTag tag)
                        Declarations[tag.Name] = type.Name + "." + f.Name;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UtilityTools] Could not read the LogTags in {type.FullName}: {e.InnerException?.Message ?? e.Message}");
            }
        }
    }
}
#endif
