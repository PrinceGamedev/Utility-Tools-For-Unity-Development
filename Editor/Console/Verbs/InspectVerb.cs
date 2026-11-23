#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// <c>inspect &lt;hierarchy path&gt; [component]</c>: a GameObject's components and their serialized fields as text,
    /// e.g. <c>inspect PlayerShip/Hull</c> or <c>inspect Main Camera ClickShooter</c>.
    /// </summary>
    internal sealed class InspectVerb : IAgentVerb
    {
        private const int MaxFieldsPerComponent = 60;
        private const int MaxChildrenListed = 30;
        private const int MaxArrayItems = 5;

        public string Name => "inspect";
        public string Usage => "inspect <hierarchy path> [component]";
        public bool ReadOnly => true;

        public AgentVerbResult Start(AgentVerbCall call)
        {
            string arg = call.Arg.Trim();
            if (arg.Length == 0) return AgentVerbResult.Failure("usage: " + Usage);

            var roots = LoadedRoots();
            if (!TryResolve(roots, arg, out var target, out string component))
                return AgentVerbResult.Failure($"no GameObject at `{arg}` — roots: " + string.Join(", ", roots.Select(r => r.name).Distinct().Take(20)));

            return AgentVerbResult.Success(Describe(target, component));
        }

        public AgentVerbResult Check(AgentVerbCall call) => AgentVerbResult.Failure("interrupted");

        /// <summary>
        /// Finds the object for <paramref name="arg"/>: the longest prefix that is a hierarchy path wins, and the word
        /// after it (if any) names a component. Names may contain spaces ("Main Camera").
        /// </summary>
        internal static bool TryResolve(IReadOnlyList<GameObject> roots, string arg, out GameObject target, out string component)
        {
            target = FindByPath(roots, arg);
            component = null;
            if (target != null) return true;

            int space = arg.LastIndexOf(' ');
            if (space <= 0) return false;
            target = FindByPath(roots, arg.Substring(0, space).TrimEnd());
            component = arg.Substring(space + 1);
            return target != null;
        }

        internal static GameObject FindByPath(IReadOnlyList<GameObject> roots, string path)
        {
            string[] parts = path.Trim().Trim('/').Split('/');
            foreach (var root in roots)
            {
                if (root == null || root.name != parts[0]) continue;
                var current = root.transform;
                for (int i = 1; i < parts.Length && current != null; i++) current = Child(current, parts[i]);
                if (current != null) return current.gameObject;
            }

            return null;
        }

        private static Transform Child(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        private static List<GameObject> LoadedRoots()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
            }

            return roots;
        }

        private static string Describe(GameObject go, string componentFilter)
        {
            var sb = new StringBuilder();
            sb.Append("GameObject ").Append(PathOf(go.transform))
              .Append(" (").Append(go.activeInHierarchy ? "active" : "inactive")
              .Append(", layer ").Append(LayerMask.LayerToName(go.layer))
              .Append(", tag ").Append(go.tag).Append(", scene ").Append(go.scene.name).Append(')');

            var t = go.transform;
            sb.Append("\nworld position ").Append(Vec(t.position)).Append(" rotation ").Append(Vec(t.eulerAngles)).Append(" scale ").Append(Vec(t.lossyScale));
            sb.Append("\nchildren ").Append(t.childCount);
            if (t.childCount > 0)
                sb.Append(": ").Append(string.Join(", ", Enumerable.Range(0, Math.Min(t.childCount, MaxChildrenListed)).Select(i => t.GetChild(i).name)))
                  .Append(t.childCount > MaxChildrenListed ? ", …" : "");

            int shown = 0;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) { sb.Append("\n- (missing script)"); continue; }
                if (componentFilter != null && !string.Equals(c.GetType().Name, componentFilter, StringComparison.OrdinalIgnoreCase)) continue;
                shown++;
                sb.Append("\n- ").Append(c.GetType().Name);
                if (c is Behaviour b) sb.Append(b.enabled ? "" : " (disabled)");
                sb.Append(": ").Append(Fields(c));
            }

            if (componentFilter != null && shown == 0)
                sb.Append("\nno component `").Append(componentFilter).Append("` — has: ")
                  .Append(string.Join(", ", go.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().Name)));
            return sb.ToString();
        }

        private static string Fields(Component c)
        {
            var parts = new List<string>();
            using (var so = new SerializedObject(c))
            {
                var p = so.GetIterator();
                bool enter = true;
                while (p.NextVisible(enter) && parts.Count < MaxFieldsPerComponent)
                {
                    enter = false;
                    if (p.name == "m_Script") continue;
                    parts.Add(p.name + "=" + Value(p));
                }
            }

            return parts.Count == 0 ? "(no serialized fields)" : string.Join("; ", parts);
        }

        private static string Value(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask: return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return Num(p.floatValue);
                case SerializedPropertyType.String: return "\"" + p.stringValue + "\"";
                case SerializedPropertyType.Color: return "rgba" + Vec(new Vector4(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a));
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? "null" : p.objectReferenceValue.name + " (" + p.objectReferenceValue.GetType().Name + ")";
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length ? p.enumDisplayNames[p.enumValueIndex] : p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Vector2: return Vec(p.vector2Value);
                case SerializedPropertyType.Vector3: return Vec(p.vector3Value);
                case SerializedPropertyType.Vector4: return Vec(p.vector4Value);
                case SerializedPropertyType.Quaternion: return "euler" + Vec(p.quaternionValue.eulerAngles);
                case SerializedPropertyType.Rect: return Vec(new Vector4(p.rectValue.x, p.rectValue.y, p.rectValue.width, p.rectValue.height));
                case SerializedPropertyType.Bounds: return "center" + Vec(p.boundsValue.center) + " size" + Vec(p.boundsValue.size);
                default:
                    return p.isArray ? ArrayValue(p) : "{…}";
            }
        }

        private static string ArrayValue(SerializedProperty p)
        {
            var items = new List<string>();
            for (int i = 0; i < Math.Min(p.arraySize, MaxArrayItems); i++)
            {
                var item = p.GetArrayElementAtIndex(i);
                items.Add(item.propertyType == SerializedPropertyType.Generic ? "{…}" : Value(item));
            }

            return "[" + p.arraySize + "] " + string.Join(", ", items) + (p.arraySize > MaxArrayItems ? ", …" : "");
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        private static string Num(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Vec(Vector2 v) => "(" + Num(v.x) + ", " + Num(v.y) + ")";
        private static string Vec(Vector3 v) => "(" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + ")";
        private static string Vec(Vector4 v) => "(" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + ", " + Num(v.w) + ")";
    }
}
#endif
