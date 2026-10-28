#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UtilityTools
{
    // Define UTILITYTOOLS_DISABLE_BUTTON_EDITOR to unregister both editors (e.g. when another package such
    // as Odin or NaughtyAttributes already provides an editor for all objects).

    /// <summary>Draws <see cref="ButtonAttribute"/> methods on every MonoBehaviour inspector.</summary>
#if !UTILITYTOOLS_DISABLE_BUTTON_EDITOR
    [CustomEditor(typeof(MonoBehaviour), true)]
    [CanEditMultipleObjects] // applies to every MonoBehaviour, so it must not block multi-selection
#endif
    public class ButtonMethodEditor : ButtonEditorBase { }

    /// <summary>Draws <see cref="ButtonAttribute"/> methods on every ScriptableObject inspector.</summary>
#if !UTILITYTOOLS_DISABLE_BUTTON_EDITOR
    [CustomEditor(typeof(ScriptableObject), true)]
    [CanEditMultipleObjects]
#endif
    public class ButtonScriptableObjectEditor : ButtonEditorBase { }

    /// <summary>
    /// Shared [Button] inspector logic. The default inspector is built with UI Toolkit, so UI Toolkit-only
    /// property drawers keep working.
    /// </summary>
    public abstract class ButtonEditorBase : Editor
    {
        // --------- Per-type metadata cache ---------
        internal struct MethodMeta
        {
            public MethodInfo Method;
            public ButtonAttribute[] Attrs;
            public ParameterInfo[] Params;
            public string NicifiedName;
            public GUIContent[] ParamLabels;
            public bool IsStatic;
            public bool ReturnsValue;     // non-void, non-coroutine return shown under the button
            public bool ReturnsCoroutine; // IEnumerator -> StartCoroutine in Play Mode, editor pump otherwise
            public int Level;             // 0 = most-derived type; higher = further up the hierarchy
        }

        private static readonly Dictionary<Type, MethodMeta[]> TypeCache = new Dictionary<Type, MethodMeta[]>();

        // Hierarchy walk stops at these: their methods never carry [Button].
        private static readonly HashSet<Type> StopTypes = new HashSet<Type>
        {
            typeof(object), typeof(Object), typeof(Component), typeof(Behaviour),
            typeof(MonoBehaviour), typeof(ScriptableObject)
        };

        // All per-object caches below are keyed by the owner's instance ID ("<id>|..." for string keys)
        // so PruneDeadCaches can drop entries whose owner was destroyed or unloaded.

        // --------- Per-object param values ---------
        private static readonly Dictionary<int, Dictionary<string, object>> ParamCache =
            new Dictionary<int, Dictionary<string, object>>();

        // --------- Last return value per (object|method) ---------
        private static readonly Dictionary<string, object> ResultCache = new Dictionary<string, object>();

        // --------- ReorderableList cache ---------
        private static readonly Dictionary<string, ReorderableList> RLCache = new Dictionary<string, ReorderableList>();

        // --------- Fold state + labels + layers ---------
        private static readonly Dictionary<string, bool> FoldCache = new Dictionary<string, bool>();
        private static readonly Dictionary<string, GUIContent> LabelCache = new Dictionary<string, GUIContent>(256);
        private static string[] s_LayerNames;

        private const int PruneThreshold = 256;

        private void OnEnable()
        {
            if (ParamCache.Count + ResultCache.Count + RLCache.Count + FoldCache.Count > PruneThreshold)
                PruneDeadCaches();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            bool hasButtons = target && GetMetas(target.GetType()).Length > 0;

            if (hasButtons) root.Add(new IMGUIContainer(() => DrawButtonGroup(ButtonAttribute.Position.Top)));
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            if (hasButtons) root.Add(new IMGUIContainer(() => DrawButtonGroup(ButtonAttribute.Position.Bottom)));

            return root;
        }

        // IMGUI path, used when this editor is drawn from an IMGUI context (e.g. Editor.OnInspectorGUI calls).
        public override void OnInspectorGUI()
        {
            DrawButtonGroup(ButtonAttribute.Position.Top);
            base.OnInspectorGUI();
            DrawButtonGroup(ButtonAttribute.Position.Bottom);
        }

        // --------- Build per-type metadata once ---------
        internal static MethodMeta[] GetMetas(Type t)
        {
            if (!TypeCache.TryGetValue(t, out var metas))
            {
                metas = BuildTypeCache(t);
                TypeCache[t] = metas;
            }

            return metas;
        }

        // Walks the hierarchy with DeclaredOnly so private base-class methods are found too.
        // Order: base-class buttons first, then declaration order within each class.
        internal static MethodMeta[] BuildTypeCache(Type t)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            var metas = new List<MethodMeta>();
            var seen = new HashSet<(Module, int)>(); // base definitions already taken (most-derived override wins)
            int level = 0;

            for (var type = t; type != null && !StopTypes.Contains(type); type = type.BaseType, level++)
            {
                foreach (var m in type.GetMethods(flags))
                {
                    var def = m.GetBaseDefinition();
                    if (!seen.Add((def.Module, def.MetadataToken))) continue;

                    // inherit:true also picks up [Button] from an overridden base declaration
                    var objAttrs = m.GetCustomAttributes(typeof(ButtonAttribute), true);
                    if (objAttrs.Length == 0) continue;

                    var attrs = new ButtonAttribute[objAttrs.Length];
                    for (int a = 0; a < objAttrs.Length; a++) attrs[a] = (ButtonAttribute)objAttrs[a];

                    var ps = m.GetParameters();
                    var paramLabels = new GUIContent[ps.Length];
                    for (int p = 0; p < ps.Length; p++)
                        paramLabels[p] = GetLabel(ObjectNames.NicifyVariableName(ps[p].Name));

                    bool isCoroutine = typeof(IEnumerator).IsAssignableFrom(m.ReturnType);
                    metas.Add(new MethodMeta
                    {
                        Method = m,
                        Attrs = attrs,
                        Params = ps,
                        ParamLabels = paramLabels,
                        NicifiedName = ObjectNames.NicifyVariableName(m.Name),
                        IsStatic = m.IsStatic,
                        ReturnsCoroutine = isCoroutine,
                        ReturnsValue = m.ReturnType != typeof(void) && !isCoroutine,
                        Level = level
                    });
                }
            }

            metas.Sort((a, b) => a.Level != b.Level
                ? b.Level.CompareTo(a.Level)
                : a.Method.MetadataToken.CompareTo(b.Method.MetadataToken));
            return metas.ToArray();
        }

        private void DrawButtonGroup(ButtonAttribute.Position position)
        {
            var owner = target;
            if (!owner) return;
            var metas = GetMetas(owner.GetType());

            int groupCount = 0;
            for (int i = 0; i < metas.Length; i++)
                for (int a = 0; a < metas[i].Attrs.Length; a++)
                    if (metas[i].Attrs[a].UiPosition == position && IsVisible(metas[i].Attrs[a])) groupCount++;
            if (groupCount == 0) return;

            EditorGUILayout.Space(8);
            DrawHorizontalLine(1);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(position == ButtonAttribute.Position.Top ? "Actions (Top)" : "Actions",
                    EditorStyles.boldLabel);

                const float kLeftPad = 12f; // push arrow inside the box
                const float kRunW = 60f;

                for (int i = 0; i < metas.Length; i++)
                {
                    var meta = metas[i];
                    var ps = meta.Params;

                    for (int a = 0; a < meta.Attrs.Length; a++)
                    {
                        var attr = meta.Attrs[a];
                        if (attr.UiPosition != position) continue;
                        if (!IsVisible(attr)) continue;

                        string labelStr = string.IsNullOrEmpty(attr.Label) ? meta.NicifiedName : attr.Label;
                        var content = GetLabel(labelStr, attr.Tooltip);

                        Color prevBg = GUI.backgroundColor;
                        if (TryParseColor(attr.Color, out var tint)) GUI.backgroundColor = tint;

                        if (ps.Length == 0)
                        {
                            if (GUILayout.Button(content, GUILayout.Height(22)) && ConfirmRun(attr, labelStr))
                                InvokeOnTargets(owner, meta, null);
                        }
                        else
                        {
                            string foldKey = KeyBase(owner, meta.Method) + "|paramFold";
                            bool open = GetFold(foldKey, false);

                            // measure one row
                            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                            // pad left so the arrow sits inside the HelpBox edge
                            row.x += kLeftPad;
                            row.width -= kLeftPad;

                            Rect runRect = new Rect(row.xMax - kRunW, row.y, kRunW, row.height);
                            Rect foldRect = new Rect(row.x, row.y, row.width - kRunW - 4f, row.height);
                            foldRect = EditorGUI.IndentedRect(foldRect);

                            open = EditorGUI.Foldout(foldRect, open, content, true);
                            SetFold(foldKey, open);

                            // Params that were never shown run with their defaults.
                            if (GUI.Button(runRect, "Run") && ConfirmRun(attr, labelStr))
                            {
                                var args = BuildArgs(owner, meta);
                                if (args != null) InvokeOnTargets(owner, meta, args);
                            }

                            if (open)
                            {
                                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                                {
                                    EditorGUI.indentLevel++;
                                    DrawParamsUI(owner, meta);
                                    EditorGUI.indentLevel--;
                                }
                            }
                        }

                        GUI.backgroundColor = prevBg;

                        DrawResult(owner, meta);

                        EditorGUILayout.Space(3);
                    }
                }
            }

            EditorGUILayout.Space(4);
            DrawHorizontalLine(1);
        }

        // --------- Confirmation + result rendering ---------
        private static bool ConfirmRun(ButtonAttribute attr, string label)
        {
            if (!attr.Confirm) return true;
            string msg = string.IsNullOrEmpty(attr.ConfirmMessage)
                ? $"Run \"{label}\"?"
                : attr.ConfirmMessage;
            return EditorUtility.DisplayDialog("Confirm Action", msg, "Run", "Cancel");
        }

        private static void DrawResult(Object owner, MethodMeta meta)
        {
            if (!meta.ReturnsValue) return;

            string key = KeyBase(owner, meta.Method);
            if (!ResultCache.TryGetValue(key, out var result)) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(
                    "→ " + SummarizeValue(result, result?.GetType() ?? meta.Method.ReturnType),
                    EditorStyles.miniLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                    ResultCache.Remove(key);
            }
        }

        // --------- Helpers ---------
        private static GUIContent GetLabel(string text) => GetLabel(text, null);

        private static GUIContent GetLabel(string text, string tooltip)
        {
            string cacheKey = string.IsNullOrEmpty(tooltip) ? text : text + "\u0001" + tooltip;
            if (!LabelCache.TryGetValue(cacheKey, out var gc))
            {
                gc = string.IsNullOrEmpty(tooltip) ? new GUIContent(text) : new GUIContent(text, tooltip);
                LabelCache[cacheKey] = gc;
            }

            return gc;
        }

        internal static bool TryParseColor(string hex, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(hex)) return false;
            if (hex[0] != '#') hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out color);
        }

        private static bool IsVisible(ButtonAttribute btn) =>
            btn.ShowMode == ButtonAttribute.Mode.Always ||
            (btn.ShowMode == ButtonAttribute.Mode.EditModeOnly && !Application.isPlaying) ||
            (btn.ShowMode == ButtonAttribute.Mode.PlayModeOnly && Application.isPlaying);

        private static Dictionary<string, object> ParamsFor(Object owner)
        {
            int id = owner.GetInstanceID();
            if (!ParamCache.TryGetValue(id, out var values))
            {
                values = new Dictionary<string, object>();
                ParamCache[id] = values;
            }

            return values;
        }

        // Bound the static caches: drop entries whose owning object was destroyed/unloaded.
        internal static void PruneDeadCaches()
        {
            var alive = new Dictionary<int, bool>();

            var deadIds = new List<int>();
            foreach (var id in ParamCache.Keys)
                if (!IsAlive(id, alive)) deadIds.Add(id);
            for (int i = 0; i < deadIds.Count; i++) ParamCache.Remove(deadIds[i]);

            PruneByOwner(ResultCache, alive);
            PruneByOwner(RLCache, alive);
            PruneByOwner(FoldCache, alive);
        }

        private static void PruneByOwner<T>(Dictionary<string, T> cache, Dictionary<int, bool> alive)
        {
            var dead = new List<string>();
            foreach (var key in cache.Keys)
                if (TryGetOwnerId(key, out int id) && !IsAlive(id, alive)) dead.Add(key);
            for (int i = 0; i < dead.Count; i++) cache.Remove(dead[i]);
        }

        internal static bool TryGetOwnerId(string key, out int id)
        {
            id = 0;
            int bar = key.IndexOf('|');
            return bar > 0 && int.TryParse(key.Substring(0, bar), out id);
        }

        private static bool IsAlive(int id, Dictionary<int, bool> memo)
        {
            if (!memo.TryGetValue(id, out bool alive))
                memo[id] = alive = EditorUtility.InstanceIDToObject(id) != null;
            return alive;
        }

        // Metadata token keeps overloads apart.
        private static string KeyBase(Object owner, MethodInfo m)
            => owner.GetInstanceID() + "|" + m.DeclaringType.FullName + "|" + m.Name + "|" + m.MetadataToken;

        private static bool GetFold(string key, bool defaultOpen)
        {
            if (!FoldCache.TryGetValue(key, out bool open))
            {
                open = defaultOpen;
                FoldCache[key] = open;
            }

            return open;
        }

        private static void SetFold(string key, bool value) => FoldCache[key] = value;

        // --------- Params UI / Build args ---------
        private void DrawParamsUI(Object owner, MethodMeta meta)
        {
            var values = ParamsFor(owner);
            var ps = meta.Params;
            string baseKey = KeyBase(owner, meta.Method);

            for (int i = 0; i < ps.Length; i++)
            {
                string pKey = baseKey + "|" + ps[i].Name;
                if (!values.TryGetValue(pKey, out var current) || current == null)
                    current = GetParamDefault(ps[i]);

                values[pKey] = DrawAny(pKey, ps[i].ParameterType, meta.ParamLabels[i], current, 0);
            }
        }

        private static object GetParamDefault(ParameterInfo p)
        {
            if (!p.HasDefaultValue) return GetDefaultForType(p.ParameterType);
            object val = p.DefaultValue;
            if (val != null && p.ParameterType.IsEnum && !p.ParameterType.IsInstanceOfType(val))
                val = Enum.ToObject(p.ParameterType, val);
            return val;
        }

        private static object[] BuildArgs(Object owner, MethodMeta meta)
        {
            var values = ParamsFor(owner);
            var ps = meta.Params;
            var args = new object[ps.Length];
            string baseKey = KeyBase(owner, meta.Method);

            for (int i = 0; i < ps.Length; i++)
            {
                var type = ps[i].ParameterType;
                values.TryGetValue(baseKey + "|" + ps[i].Name, out var val);
                if (val == null) val = GetParamDefault(ps[i]);

                if (val != null && !type.IsInstanceOfType(val))
                {
                    try
                    {
                        val = type.IsEnum ? Enum.ToObject(type, val) : Convert.ChangeType(val, type);
                    }
                    catch
                    {
                        Debug.LogError($"Parameter '{ps[i].Name}' expects {type.Name}");
                        return null;
                    }
                }

                args[i] = val;
            }

            return args;
        }

        // --------- Invocation (return values + coroutines + static) ---------
        // With a multi-selection, instance methods run on every selected object (using the
        // primary target's param values); static methods run once.
        private void InvokeOnTargets(Object owner, MethodMeta meta, object[] args)
        {
            if (meta.IsStatic || targets.Length <= 1)
            {
                InvokeButton(owner, meta, args);
                return;
            }

            foreach (var t in targets)
                if (t && meta.Method.DeclaringType.IsInstanceOfType(t))
                    InvokeButton(t, meta, args);
        }

        private static void InvokeButton(Object owner, MethodMeta meta, object[] args)
        {
            var method = meta.Method;
            object targetObj = meta.IsStatic ? null : owner;

            try
            {
                if (!meta.IsStatic)
                    Undo.RegisterCompleteObjectUndo(owner, $"Invoke {method.Name}");

                object result = method.Invoke(targetObj, args);

                if (meta.ReturnsCoroutine && result is IEnumerator routine)
                {
                    // In Play Mode use the real scheduler so WaitForSeconds etc. behave normally.
                    if (Application.isPlaying && owner is MonoBehaviour mb && mb.isActiveAndEnabled)
                        mb.StartCoroutine(routine);
                    else
                        EditorCoroutineDriver.Run(routine);
                }
                else if (meta.ReturnsValue)
                {
                    ResultCache[KeyBase(owner, method)] = result;
                }

                if (!meta.IsStatic) EditorUtility.SetDirty(owner);
            }
            catch (TargetInvocationException tie)
            {
                Debug.LogException(tie.InnerException ?? tie);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // --------- Big drawer (parameters) ---------
        // scope = unique key for this value (object|method|param[|field...]); keys fold/list UI state.
        private object DrawAny(string scope, Type t, GUIContent label, object current, int depth)
        {
            if (depth > 6)
            {
                EditorGUILayout.HelpBox(label.text + ": nesting depth limit reached.", MessageType.Info);
                return current;
            }

            // primitives / common
            if (t == typeof(int)) return EditorGUILayout.IntField(label, current is int i ? i : 0);
            if (t == typeof(long)) return EditorGUILayout.LongField(label, current is long l ? l : 0L);
            if (t == typeof(float)) return EditorGUILayout.FloatField(label, current is float f ? f : 0f);
            if (t == typeof(double)) return EditorGUILayout.DoubleField(label, current is double d ? d : 0d);
            if (t == typeof(bool)) return EditorGUILayout.Toggle(label, current is bool b && b);
            if (t == typeof(string)) return EditorGUILayout.TextField(label, current as string ?? "");

            if (t.IsEnum)
            {
                var e = current is Enum ev ? ev : (Enum)Enum.GetValues(t).GetValue(0);
                return EditorGUILayout.EnumPopup(label, e);
            }

            // unity math types
            if (t == typeof(Vector2)) return EditorGUILayout.Vector2Field(label, current is Vector2 v2 ? v2 : default);
            if (t == typeof(Vector2Int))
                return EditorGUILayout.Vector2IntField(label.text, current is Vector2Int v2i ? v2i : default);
            if (t == typeof(Vector3)) return EditorGUILayout.Vector3Field(label, current is Vector3 v3 ? v3 : default);
            if (t == typeof(Vector3Int))
                return EditorGUILayout.Vector3IntField(label.text, current is Vector3Int v3i ? v3i : default);
            if (t == typeof(Vector4))
                return EditorGUILayout.Vector4Field(label.text, current is Vector4 v4 ? v4 : default);

            if (t == typeof(Quaternion))
            {
                Quaternion q = current is Quaternion qv ? qv : Quaternion.identity;
                Vector3 eul = EditorGUILayout.Vector3Field(GetLabel(label.text + " (Euler)"), q.eulerAngles);
                return Quaternion.Euler(eul);
            }

            if (t == typeof(Color)) return EditorGUILayout.ColorField(label, current is Color c ? c : Color.white);
            if (t == typeof(Rect)) return EditorGUILayout.RectField(label, current is Rect r ? r : default);
            if (t == typeof(Bounds)) return EditorGUILayout.BoundsField(label, current is Bounds b ? b : default);
            if (t == typeof(BoundsInt))
            {
                var bi = current is BoundsInt bii ? bii : new BoundsInt(0, 0, 0, 1, 1, 1);
                var pos = EditorGUILayout.Vector3IntField(GetLabel(label.text + " Position"), bi.position);
                var size = EditorGUILayout.Vector3IntField(GetLabel(label.text + " Size"), bi.size);
                return new BoundsInt(pos, size);
            }

            // curves / gradients / layermask
            if (t == typeof(AnimationCurve))
                return EditorGUILayout.CurveField(label,
                    current as AnimationCurve ?? AnimationCurve.Linear(0, 0, 1, 1));

            if (t == typeof(Gradient))
            {
                var g = current as Gradient ?? new Gradient();
                return EditorGUILayout.GradientField(label, g);
            }

            if (t == typeof(LayerMask))
            {
                int mask = current is LayerMask lm ? lm.value : 0;
                mask = EditorGUILayout.MaskField(label, mask, LayerNames);
                return (LayerMask)mask;
            }

            // object refs
            if (typeof(Object).IsAssignableFrom(t))
                return EditorGUILayout.ObjectField(label, current as Object, t, true);

            // arrays / lists -> inline rows like Unity
            if (t.IsArray)
            {
                Type elemType = t.GetElementType();
                Array arr = current as Array ?? Array.CreateInstance(elemType, 0);

                IList listBoxed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType));
                for (int i = 0; i < arr.Length; i++)
                    listBoxed.Add(arr.GetValue(i) ?? GetDefaultForType(elemType));

                DrawReorderableListInline(scope, label.text, elemType, listBoxed);

                var resized = Array.CreateInstance(elemType, listBoxed.Count);
                for (int i = 0; i < listBoxed.Count; i++) resized.SetValue(listBoxed[i], i);
                return resized;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elemType = t.GetGenericArguments()[0];
                var list = current as IList ?? (IList)Activator.CreateInstance(t);
                DrawReorderableListInline(scope, label.text, elemType, list);
                return list;
            }

            // not serializable by Unity -> info
            if (IsDictionaryType(t))
            {
                EditorGUILayout.HelpBox(label.text + ": Dictionary types aren’t supported by Unity serialization.",
                    MessageType.Info);
                return current;
            }

            if (t.IsInterface || t.IsAbstract)
            {
                EditorGUILayout.HelpBox(label.text + ": " + t.Name + " is interface/abstract. Provide a concrete type.",
                    MessageType.Info);
                return current;
            }

            if (IsSerializableByUnity(t))
            {
                if (current == null) current = Activator.CreateInstance(t);
                string fk = scope + "|CLASS|" + t.FullName;
                bool open = GetFold(fk, false);
                open = EditorGUILayout.Foldout(open, $"{label.text} ({t.Name})", true);
                SetFold(fk, open);
                if (open)
                {
                    using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                    {
                        EditorGUI.indentLevel++;
                        foreach (var f in GetUnitySerializableFields(t))
                        {
                            object fieldVal = f.GetValue(current);
                            object newVal = DrawAny(scope + "|" + f.Name, f.FieldType,
                                GetLabel(ObjectNames.NicifyVariableName(f.Name)), fieldVal, depth + 1);
                            if (!Equals(newVal, fieldVal)) f.SetValue(current, newVal);
                        }

                        EditorGUI.indentLevel--;
                    }
                }

                return current;
            }

            EditorGUILayout.HelpBox(label.text + ": Type " + t.Name + " not supported here.", MessageType.Warning);
            return current;
        }

        // --------- Unity-like inline list rows ---------
        private void DrawReorderableListInline(string scope, string label, Type elemType, IList list)
        {
            string key = scope + "|RL_INLINE|" + elemType.FullName;

            if (!RLCache.TryGetValue(key, out var rl))
            {
                rl = new ReorderableList(list, elemType, true, true, true, true);

                // NOTE: callbacks must read rl.list, not `list` — the cached ReorderableList outlives this
                // call, and array params hand in a fresh IList every frame.
                rl.drawHeaderCallback = rect =>
                {
                    // left: list title   right: "Size [int]"
                    const float sizeFieldW = 60f;
                    const float sizeLabelW = 34f; // "Size"
                    const float padLeft = 6f;
                    const float spacing = 4f;

                    // pad inside header so it aligns nicely with content
                    rect.x += padLeft;
                    rect.width -= padLeft;

                    var rightRect = new Rect(rect.xMax - (sizeLabelW + spacing + sizeFieldW), rect.y,
                        sizeLabelW + spacing + sizeFieldW, rect.height);
                    var titleRect = new Rect(rect.x, rect.y, rightRect.x - rect.x - spacing, rect.height);

                    EditorGUI.LabelField(titleRect, $"{label}  ({elemType.Name})");

                    // draw Size on the right
                    var sizeLabelRect = new Rect(rightRect.x, rightRect.y, sizeLabelW, rightRect.height);
                    var sizeFieldRect = new Rect(rightRect.x + sizeLabelW + spacing, rightRect.y, sizeFieldW,
                        rightRect.height);

                    EditorGUI.LabelField(sizeLabelRect, "Size", EditorStyles.miniLabel);

                    var cur = rl.list;
                    EditorGUI.BeginChangeCheck();
                    int newSize = Mathf.Max(0, EditorGUI.IntField(sizeFieldRect, cur.Count));
                    if (EditorGUI.EndChangeCheck())
                    {
                        while (cur.Count < newSize) cur.Add(GetDefaultForType(elemType));
                        while (cur.Count > newSize) cur.RemoveAt(cur.Count - 1);
                        rl.index = Mathf.Clamp(rl.index, 0, cur.Count - 1);
                    }
                };

                rl.onAddCallback = l => l.list.Add(GetDefaultForType(elemType));
                rl.onRemoveCallback = l =>
                {
                    if (l.index >= 0 && l.index < l.list.Count) l.list.RemoveAt(l.index);
                };

                rl.drawElementCallback = (rect, index, active, focused) =>
                {
                    rect.y += 2f;
                    rect.height = EditorGUIUtility.singleLineHeight;

                    // "Element n" label (left)
                    var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
                    EditorGUI.PrefixLabel(labelRect, new GUIContent($"Element {index}"));

                    // field (right)
                    var fieldRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y,
                        rect.width - EditorGUIUtility.labelWidth, rect.height);

                    object val = rl.list[index];
                    if (TryDrawInlineField(fieldRect, elemType, val, out var newVal))
                        rl.list[index] = newVal;
                    else
                        EditorGUI.LabelField(fieldRect, SummarizeValue(val, elemType));
                };

                rl.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 6f;

                RLCache[key] = rl;
            }
            else
            {
                rl.list = list;
            }

            rl.DoLayoutList();
        }

        // Draw a single-line field in a provided rect (like Unity's array rows)
        private static bool TryDrawInlineField(Rect r, Type t, object current, out object newVal)
        {
            newVal = current;

            // primitives / enums / string
            if (t == typeof(bool))
            {
                newVal = EditorGUI.Toggle(r, current is bool b && b);
                return true;
            }

            if (t == typeof(int))
            {
                newVal = EditorGUI.IntField(r, current is int i ? i : 0);
                return true;
            }

            if (t == typeof(long))
            {
                newVal = EditorGUI.LongField(r, current is long l ? l : 0L);
                return true;
            }

            if (t == typeof(float))
            {
                newVal = EditorGUI.FloatField(r, current is float f ? f : 0f);
                return true;
            }

            if (t == typeof(double))
            {
                newVal = EditorGUI.DoubleField(r, current is double d ? d : 0d);
                return true;
            }

            if (t == typeof(string))
            {
                newVal = EditorGUI.TextField(r, current as string ?? "");
                return true;
            }

            if (t.IsEnum)
            {
                newVal = EditorGUI.EnumPopup(r, current is Enum e ? e : (Enum)Enum.GetValues(t).GetValue(0));
                return true;
            }

            // vectors (manual split so it's truly single-line)
            if (t == typeof(Vector2))
            {
                var v = current is Vector2 v2 ? v2 : default;
                Split4(r, out var a, out var b, out _, out _);
                newVal = new Vector2(EditorGUI.FloatField(a, v.x), EditorGUI.FloatField(b, v.y));
                return true;
            }

            if (t == typeof(Vector2Int))
            {
                var v = current is Vector2Int v2i ? v2i : default;
                Split4(r, out var a, out var b, out _, out _);
                newVal = new Vector2Int(EditorGUI.IntField(a, v.x), EditorGUI.IntField(b, v.y));
                return true;
            }

            if (t == typeof(Vector3))
            {
                var v = current is Vector3 v3 ? v3 : default;
                Split4(r, out var a, out var b, out var c, out _);
                newVal = new Vector3(EditorGUI.FloatField(a, v.x), EditorGUI.FloatField(b, v.y),
                    EditorGUI.FloatField(c, v.z));
                return true;
            }

            if (t == typeof(Vector3Int))
            {
                var v = current is Vector3Int v3i ? v3i : default;
                Split4(r, out var a, out var b, out var c, out _);
                newVal = new Vector3Int(EditorGUI.IntField(a, v.x), EditorGUI.IntField(b, v.y),
                    EditorGUI.IntField(c, v.z));
                return true;
            }

            if (t == typeof(Vector4))
            {
                var v = current is Vector4 v4 ? v4 : default;
                Split4(r, out var a, out var b, out var c, out var d);
                newVal = new Vector4(EditorGUI.FloatField(a, v.x), EditorGUI.FloatField(b, v.y),
                    EditorGUI.FloatField(c, v.z), EditorGUI.FloatField(d, v.w));
                return true;
            }

            if (t == typeof(Quaternion))
            {
                var q = current is Quaternion qq ? qq : Quaternion.identity;
                Split4(r, out var a, out var b, out var c, out var d);
                newVal = new Quaternion(EditorGUI.FloatField(a, q.x), EditorGUI.FloatField(b, q.y),
                    EditorGUI.FloatField(c, q.z), EditorGUI.FloatField(d, q.w));
                return true;
            }

            // color / curve / gradient / rect-ish (keep to one line)
            if (t == typeof(Color))
            {
                newVal = EditorGUI.ColorField(r, current is Color col ? col : Color.white);
                return true;
            }

            if (t == typeof(AnimationCurve))
            {
                newVal = EditorGUI.CurveField(r, current as AnimationCurve ?? AnimationCurve.Linear(0, 0, 1, 1));
                return true;
            }

            if (t == typeof(Gradient))
            {
                var g = current as Gradient ?? new Gradient();
#if UNITY_2022_2_OR_NEWER
                newVal = EditorGUI.GradientField(r, GUIContent.none, g, false);
#else
                // Empty label keeps it inline like Unity's array rows.
                newVal = EditorGUI.GradientField(r, string.Empty, g);
#endif
                return true;
            }

            if (t == typeof(Rect))
            {
                var rr = current is Rect R ? R : default;
                Split4(r, out var a, out var b, out var c, out var d);
                newVal = new Rect(EditorGUI.FloatField(a, rr.x), EditorGUI.FloatField(b, rr.y),
                    EditorGUI.FloatField(c, rr.width), EditorGUI.FloatField(d, rr.height));
                return true;
            }

            if (t == typeof(Bounds))
            {
                var b = current is Bounds B ? B : default;
                Split4(r, out var a, out var bb, out var c, out var d);
                var centerX = EditorGUI.FloatField(a, b.center.x);
                var centerY = EditorGUI.FloatField(bb, b.center.y);
                var sizeX = EditorGUI.FloatField(c, b.size.x);
                var sizeY = EditorGUI.FloatField(d, b.size.y);
                newVal = new Bounds(new Vector3(centerX, centerY, b.center.z), new Vector3(sizeX, sizeY, b.size.z));
                return true;
            }

            if (t == typeof(LayerMask))
            {
                int mask = current is LayerMask lm ? lm.value : 0;
                newVal = (LayerMask)EditorGUI.MaskField(r, mask, LayerNames);
                return true;
            }

            // object refs
            if (typeof(Object).IsAssignableFrom(t))
            {
                newVal = EditorGUI.ObjectField(r, current as Object, t, true);
                return true;
            }

            // complex / unsupported -> let the summary show (non-editable)
            return false;
        }

        // split the rect into 4 equal columns with small gaps (for Vector4 etc.)
        private static void Split4(Rect r, out Rect a, out Rect b, out Rect c, out Rect d)
        {
            const float gap = 2f;
            float w = (r.width - gap * 3f) / 4f;
            a = new Rect(r.x, r.y, w, r.height);
            b = new Rect(a.xMax + gap, r.y, w, r.height);
            c = new Rect(b.xMax + gap, r.y, w, r.height);
            d = new Rect(c.xMax + gap, r.y, w, r.height);
        }

        private static string[] LayerNames
        {
            get
            {
                if (s_LayerNames == null)
                {
                    s_LayerNames = new string[32];
                    for (int i = 0; i < 32; i++) s_LayerNames[i] = LayerMask.LayerToName(i);
                }

                return s_LayerNames;
            }
        }

        private static string SummarizeValue(object obj, Type t)
        {
            if (obj == null) return "(null)";
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return obj.ToString();
            if (t == typeof(Vector2)) return ((Vector2)obj).ToString("F2");
            if (t == typeof(Vector3)) return ((Vector3)obj).ToString("F2");
            if (t == typeof(Vector4)) return ((Vector4)obj).ToString("F2");
            if (t == typeof(Color)) return ColorUtility.ToHtmlStringRGB((Color)obj);
            if (typeof(Object).IsAssignableFrom(t))
            {
                var o = obj as Object;
                return o ? o.name : "(null)";
            }

            return obj.ToString();
        }

        // --------- Utilities ---------
        private static bool IsDictionaryType(Type t) =>
            t.IsGenericType &&
            (t.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
             ImplementsGeneric(t, typeof(IDictionary<,>)));

        private static bool ImplementsGeneric(Type t, Type openGeneric)
        {
            var ifaces = t.GetInterfaces();
            for (int i = 0; i < ifaces.Length; i++)
                if (ifaces[i].IsGenericType && ifaces[i].GetGenericTypeDefinition() == openGeneric)
                    return true;
            return false;
        }

        private static object GetDefaultForType(Type t) =>
            t.IsValueType ? Activator.CreateInstance(t) :
            typeof(Object).IsAssignableFrom(t) ? null :
            (t.IsClass && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null
                ? Activator.CreateInstance(t)
                : null);

        private static bool IsSerializableByUnity(Type t)
        {
            if (t.IsPrimitive || t.IsEnum) return true;
            if (t == typeof(string)) return true;
            if (typeof(Object).IsAssignableFrom(t)) return true;
            if (t.IsArray) return IsSerializableByUnity(t.GetElementType());
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return IsSerializableByUnity(t.GetGenericArguments()[0]);
            if (t.IsValueType || t.IsClass)
                return t.GetCustomAttribute<SerializableAttribute>() != null;
            return false;
        }

        private static IEnumerable<FieldInfo> GetUnitySerializableFields(Type t)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = t.GetFields(flags);
            for (int i = 0; i < f.Length; i++)
            {
                if (f[i].IsNotSerialized) continue;
                if (f[i].IsPublic || f[i].GetCustomAttribute<SerializeField>() != null) yield return f[i];
            }
        }

        private static void DrawHorizontalLine(float thickness = 1f)
        {
            var rect = EditorGUILayout.GetControlRect(false, thickness);
            rect.height = thickness;
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.2f));
        }
    }

    /// <summary>
    /// Minimal editor-time coroutine pump so [Button] methods returning IEnumerator can run
    /// outside Play Mode. Advanced once per editor update tick. Nested IEnumerators and
    /// CustomYieldInstructions (e.g. WaitForSecondsRealtime, WaitUntil) are followed; other yield
    /// instructions such as WaitForSeconds are not time-accurate here (each yield = one tick).
    /// In Play Mode, [Button] coroutines on an active MonoBehaviour use StartCoroutine instead.
    /// </summary>
    internal static class EditorCoroutineDriver
    {
        private static readonly List<Stack<IEnumerator>> s_Routines = new List<Stack<IEnumerator>>();
        private static bool s_Hooked;

        public static void Run(IEnumerator routine)
        {
            if (routine == null) return;
            s_Routines.Add(Start(routine));
            if (s_Hooked) return;
            s_Hooked = true;
            EditorApplication.update += Pump;
        }

        internal static Stack<IEnumerator> Start(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            return stack;
        }

        /// <summary>Advances one tick. Returns false once the routine (and everything it yielded) is done.</summary>
        internal static bool Step(Stack<IEnumerator> stack)
        {
            if (stack.Count == 0) return false;

            var top = stack.Peek();
            bool more;
            try { more = top.MoveNext(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                stack.Clear();
                return false;
            }

            if (more)
            {
                if (top.Current is IEnumerator nested) stack.Push(nested);
                return true;
            }

            stack.Pop();
            return stack.Count > 0;
        }

        private static void Pump()
        {
            for (int i = s_Routines.Count - 1; i >= 0; i--)
                if (!Step(s_Routines[i])) s_Routines.RemoveAt(i);

            if (s_Routines.Count == 0)
            {
                EditorApplication.update -= Pump;
                s_Hooked = false;
            }
        }
    }
}
#endif
