#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UtilityTools
{


    [CustomEditor(typeof(MonoBehaviour), true)]
    [CanEditMultipleObjects] // applies to every MonoBehaviour, so it must not block multi-selection
    public class ButtonMethodEditor : Editor
    {
        // --------- Per-type metadata cache ---------
        private struct MethodMeta
        {
            public MethodInfo Method;
            public ButtonAttribute[] Attrs;
            public ParameterInfo[] Params;
            public string NicifiedName;
            public GUIContent[] ParamLabels;
            public bool IsStatic;
            public bool ReturnsValue;     // non-void, non-coroutine return shown under the button
            public bool ReturnsCoroutine; // IEnumerator -> driven on EditorApplication.update
        }

        private static readonly Dictionary<Type, MethodMeta[]> TypeCache = new Dictionary<Type, MethodMeta[]>();

        // --------- Per-object param values ---------
        private static readonly Dictionary<int, Dictionary<string, object>> Cache =
            new Dictionary<int, Dictionary<string, object>>();

        // --------- Last return value per (object|method) ---------
        private static readonly Dictionary<string, object> ResultCache = new Dictionary<string, object>();

        // --------- ReorderableList cache ---------
        private static readonly Dictionary<string, ReorderableList> RLCache = new Dictionary<string, ReorderableList>();

        // --------- Fold state + labels + layers ---------
        private static readonly Dictionary<string, bool> FoldCache = new Dictionary<string, bool>();
        private static readonly Dictionary<string, GUIContent> LabelCache = new Dictionary<string, GUIContent>(256);
        private static string[] s_LayerNames;

        public override void OnInspectorGUI()
        {
            var mb = target as MonoBehaviour;
            if (!mb)
            {
                base.OnInspectorGUI();
                return;
            }

            var t = mb.GetType();
            if (!TypeCache.TryGetValue(t, out var metas))
            {
                metas = BuildTypeCache(t);
                TypeCache[t] = metas;
            }

            DrawButtonGroup(mb, metas, ButtonAttribute.Position.Top);
            base.OnInspectorGUI();
            DrawButtonGroup(mb, metas, ButtonAttribute.Position.Bottom);
        }

        // --------- Build per-type metadata once ---------
        private static MethodMeta[] BuildTypeCache(Type t)
        {
            var methods = t.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            int count = 0;
            for (int i = 0; i < methods.Length; i++)
                if (methods[i].GetCustomAttributes(typeof(ButtonAttribute), true).Length > 0)
                    count++;

            var metas = new MethodMeta[count];
            int idx = 0;

            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                var objAttrs = m.GetCustomAttributes(typeof(ButtonAttribute), true);
                if (objAttrs.Length == 0) continue;

                var attrs = new ButtonAttribute[objAttrs.Length];
                for (int a = 0; a < objAttrs.Length; a++) attrs[a] = (ButtonAttribute)objAttrs[a];

                var ps = m.GetParameters();
                var paramLabels = new GUIContent[ps.Length];
                for (int p = 0; p < ps.Length; p++)
                    paramLabels[p] = GetLabel(ObjectNames.NicifyVariableName(ps[p].Name));

                metas[idx].Method = m;
                metas[idx].Attrs = attrs;
                metas[idx].Params = ps;
                metas[idx].ParamLabels = paramLabels;
                metas[idx].NicifiedName = ObjectNames.NicifyVariableName(m.Name);
                metas[idx].IsStatic = m.IsStatic;
                metas[idx].ReturnsCoroutine = typeof(IEnumerator).IsAssignableFrom(m.ReturnType);
                metas[idx].ReturnsValue = m.ReturnType != typeof(void) && !metas[idx].ReturnsCoroutine;
                idx++;
            }

            Array.Sort(metas, (a, b) => string.Compare(a.NicifiedName, b.NicifiedName, StringComparison.Ordinal));
            return metas;
        }

  private void DrawButtonGroup(MonoBehaviour mb, MethodMeta[] metas, ButtonAttribute.Position position)
{
    // count first
    int groupCount = 0;
    for (int i = 0; i < metas.Length; i++)
        for (int a = 0; a < metas[i].Attrs.Length; a++)
            if (metas[i].Attrs[a].UiPosition == position && IsVisible(metas[i].Attrs[a])) groupCount++;
    if (groupCount == 0) return;

    EditorGUILayout.Space(8);
    DrawHorizontalLine(1);
    EditorGUILayout.Space(4);

    using (new EditorGUILayout.VerticalScope(new GUIStyle("HelpBox")))
    {
        GUILayout.Label(position == ButtonAttribute.Position.Top ? "Actions (Top)" : "Actions", EditorStyles.boldLabel);

        const float kLeftPad = 12f;     // push arrow inside the box
        const float kRunW    = 60f;

        for (int i = 0; i < metas.Length; i++)
        {
            var meta = metas[i];
            var ps   = meta.Params;

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
                        InvokeOnTargets(mb, meta, null);
                }
                else
                {
                    string foldKey = KeyBase(mb, meta.Method) + "|paramFold";
                    bool open = GetFold(foldKey, false);

                    // measure one row
                    Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    // pad left so the arrow sits inside the HelpBox edge
                    row.x += kLeftPad; row.width -= kLeftPad;

                    Rect runRect  = new Rect(row.xMax - kRunW, row.y, kRunW, row.height);
                    Rect foldRect = new Rect(row.x, row.y, row.width - kRunW - 4f, row.height);
                    foldRect = EditorGUI.IndentedRect(foldRect);

                    open = EditorGUI.Foldout(foldRect, open, content, true);
                    SetFold(foldKey, open);

                    using (new EditorGUI.DisabledScope(!AllParamsHaveValues(mb, meta)))
                    {
                        if (GUI.Button(runRect, "Run") && ConfirmRun(attr, labelStr))
                        {
                            var args = BuildArgs(mb, meta);
                            if (args != null) InvokeOnTargets(mb, meta, args);
                        }
                    }

                    if (open)
                    {
                        using (new EditorGUILayout.VerticalScope(new GUIStyle("box")))
                        {
                            EditorGUI.indentLevel++;
                            DrawParamsUI(mb, meta);
                            EditorGUI.indentLevel--;
                        }
                    }
                }

                GUI.backgroundColor = prevBg;

                DrawResult(mb, meta);

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

        private void DrawResult(MonoBehaviour mb, MethodMeta meta)
        {
            if (!meta.ReturnsValue) return;

            string key = KeyBase(mb, meta.Method);
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
            string cacheKey = string.IsNullOrEmpty(tooltip) ? text : text + "" + tooltip;
            if (!LabelCache.TryGetValue(cacheKey, out var gc))
            {
                gc = string.IsNullOrEmpty(tooltip) ? new GUIContent(text) : new GUIContent(text, tooltip);
                LabelCache[cacheKey] = gc;
            }

            return gc;
        }

        private static bool TryParseColor(string hex, out Color color)
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

        private static void EnsureCache(MonoBehaviour mb)
        {
            int id = mb.GetInstanceID();
            if (!Cache.ContainsKey(id))
            {
                if (Cache.Count > 64) PruneDeadCaches();
                Cache[id] = new Dictionary<string, object>();
            }
        }

        // Bound the static caches: drop entries whose owning object was destroyed/unloaded.
        private static void PruneDeadCaches()
        {
            var dead = new List<int>();
            foreach (var id in Cache.Keys)
                if (EditorUtility.InstanceIDToObject(id) == null) dead.Add(id);

            for (int i = 0; i < dead.Count; i++) Cache.Remove(dead[i]);
        }

        private static string KeyBase(MonoBehaviour mb, MethodInfo m)
            => mb.GetInstanceID() + "|" + m.DeclaringType.FullName + "|" + m.Name;

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
        private void DrawParamsUI(MonoBehaviour mb, MethodMeta meta)
        {
            EnsureCache(mb);
            var ps = meta.Params;
            string baseKey = KeyBase(mb, meta.Method);

            for (int i = 0; i < ps.Length; i++)
            {
                string pKey = baseKey + "|" + ps[i].Name;
                if (!Cache[mb.GetInstanceID()].TryGetValue(pKey, out var current) || current == null)
                    current = ps[i].HasDefaultValue ? ps[i].DefaultValue : GetDefaultForType(ps[i].ParameterType);

                object newVal = DrawAny(pKey, ps[i].ParameterType, meta.ParamLabels[i], current, 0);
                Cache[mb.GetInstanceID()][pKey] = newVal;
            }
        }

        private bool AllParamsHaveValues(MonoBehaviour mb, MethodMeta meta)
        {
            EnsureCache(mb);
            var ps = meta.Params;
            string baseKey = KeyBase(mb, meta.Method);

            for (int i = 0; i < ps.Length; i++)
            {
                string pKey = baseKey + "|" + ps[i].Name;
                bool exists = Cache[mb.GetInstanceID()].TryGetValue(pKey, out var val);
                if (!exists && !ps[i].HasDefaultValue) return false;
                if (val == null && ps[i].ParameterType.IsValueType && !ps[i].HasDefaultValue) return false;
            }

            return true;
        }

        private object[] BuildArgs(MonoBehaviour mb, MethodMeta meta)
        {
            EnsureCache(mb);
            var ps = meta.Params;
            var args = new object[ps.Length];
            string baseKey = KeyBase(mb, meta.Method);

            for (int i = 0; i < ps.Length; i++)
            {
                string key = baseKey + "|" + ps[i].Name;
                Cache[mb.GetInstanceID()].TryGetValue(key, out var val);

                if (val == null && ps[i].HasDefaultValue) val = ps[i].DefaultValue;
                if (val == null && ps[i].ParameterType.IsValueType) val = Activator.CreateInstance(ps[i].ParameterType);

                if (val != null && !ps[i].ParameterType.IsInstanceOfType(val))
                {
                    try
                    {
                        val = Convert.ChangeType(val, ps[i].ParameterType);
                    }
                    catch
                    {
                        Debug.LogError($"Parameter '{ps[i].Name}' expects {ps[i].ParameterType.Name}");
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
        private void InvokeOnTargets(MonoBehaviour mb, MethodMeta meta, object[] args)
        {
            if (meta.IsStatic || targets.Length <= 1)
            {
                InvokeButton(mb, meta, args);
                return;
            }

            foreach (var t in targets)
                if (t is MonoBehaviour other && meta.Method.DeclaringType.IsInstanceOfType(other))
                    InvokeButton(other, meta, args);
        }

        private void InvokeButton(MonoBehaviour mb, MethodMeta meta, object[] args)
        {
            var method = meta.Method;
            object targetObj = meta.IsStatic ? null : mb;
            string resultKey = KeyBase(mb, method);

            try
            {
                if (!meta.IsStatic)
                    Undo.RegisterCompleteObjectUndo(mb, $"Invoke {method.Name}");

                object result = method.Invoke(targetObj, args);

                if (meta.ReturnsCoroutine && result is IEnumerator routine)
                {
                    EditorCoroutineDriver.Run(routine);
                }
                else if (meta.ReturnsValue)
                {
                    ResultCache[resultKey] = result;
                }

                if (!meta.IsStatic) EditorUtility.SetDirty(mb);
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
                if (s_LayerNames == null)
                {
                    s_LayerNames = new string[32];
                    for (int i = 0; i < 32; i++) s_LayerNames[i] = LayerMask.LayerToName(i);
                }

                mask = EditorGUILayout.MaskField(label, mask, s_LayerNames);
                return (LayerMask)mask;
            }

            // object refs
            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return EditorGUILayout.ObjectField(label, current as UnityEngine.Object, t, true);

            // arrays / lists -> INLINE rows like Unity
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
                    using (new EditorGUILayout.VerticalScope(new GUIStyle("box")))
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

        // --------- NEW: Unity-like INLINE list rows ---------
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
            const float sizeFieldW  = 60f;
            const float sizeLabelW  = 34f; // "Size"
            const float padLeft     = 6f;
            const float spacing     = 4f;

            // pad inside header so it aligns nicely with content
            rect.x += padLeft; rect.width -= padLeft;

            var rightRect = new Rect(rect.xMax - (sizeLabelW + spacing + sizeFieldW), rect.y, sizeLabelW + spacing + sizeFieldW, rect.height);
            var titleRect = new Rect(rect.x, rect.y, rightRect.x - rect.x - spacing, rect.height);

            EditorGUI.LabelField(titleRect, $"{label}  ({elemType.Name})");

            // draw Size on the right
            var sizeLabelRect = new Rect(rightRect.x, rightRect.y, sizeLabelW, rightRect.height);
            var sizeFieldRect = new Rect(rightRect.x + sizeLabelW + spacing, rightRect.y, sizeFieldW, rightRect.height);

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

        rl.onAddCallback    = l => l.list.Add(GetDefaultForType(elemType));
        rl.onRemoveCallback = l => { if (l.index >= 0 && l.index < l.list.Count) l.list.RemoveAt(l.index); };

        rl.drawElementCallback = (rect, index, active, focused) =>
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            // "Element n" label (left)
            var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            EditorGUI.PrefixLabel(labelRect, new GUIContent($"Element {index}"));

            // field (right)
            var fieldRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height);

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
        private bool TryDrawInlineField(Rect r, Type t, object current, out object newVal)
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
#if UNITY_2019_1_OR_NEWER
            if (t == typeof(Gradient))
            {
                var g = current as Gradient ?? new Gradient();

#if UNITY_2022_2_OR_NEWER
                // (Rect, GUIContent, Gradient, bool hdr)
                newVal = EditorGUI.GradientField(r, GUIContent.none, g, false);
#elif UNITY_2021_1_OR_NEWER
            // (Rect, string, Gradient) or (Rect, string, Gradient, bool hdr) depending on minor version
            // Passing an empty label keeps it inline like Unity's array rows.
            newVal = EditorGUI.GradientField(r, string.Empty, g);
#else
            // (Rect, Gradient)
            newVal = EditorGUI.GradientField(r, g);
#endif

                return true;
            }
#endif
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
                if (s_LayerNames == null)
                {
                    s_LayerNames = new string[32];
                    for (int i = 0; i < 32; i++) s_LayerNames[i] = LayerMask.LayerToName(i);
                }

                int mask = current is LayerMask lm ? lm.value : 0;
                newVal = (LayerMask)EditorGUI.MaskField(r, mask, s_LayerNames);
                return true;
            }

            // object refs (the one in your screenshot)
            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                newVal = EditorGUI.ObjectField(r, current as UnityEngine.Object, t, true);
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

        private static string SummarizeValue(object obj, Type t)
        {
            if (obj == null) return "(null)";
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return obj.ToString();
            if (t == typeof(Vector2)) return ((Vector2)obj).ToString("F2");
            if (t == typeof(Vector3)) return ((Vector3)obj).ToString("F2");
            if (t == typeof(Vector4)) return ((Vector4)obj).ToString("F2");
            if (t == typeof(Color)) return ColorUtility.ToHtmlStringRGB((Color)obj);
            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
            {
                var o = obj as UnityEngine.Object;
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
            typeof(UnityEngine.Object).IsAssignableFrom(t) ? null :
            (t.IsClass && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null
                ? Activator.CreateInstance(t)
                : null);

        private static bool IsSerializableByUnity(Type t)
        {
            if (t.IsPrimitive || t.IsEnum) return true;
            if (t == typeof(string)) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return true;
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
    /// outside Play Mode. Yields are advanced once per editor update tick; yield instructions
    /// such as WaitForSeconds are not time-accurate here (each yield = one tick).
    /// </summary>
    internal static class EditorCoroutineDriver
    {
        private static readonly List<IEnumerator> s_Routines = new List<IEnumerator>();
        private static bool s_Hooked;

        public static void Run(IEnumerator routine)
        {
            if (routine == null) return;
            s_Routines.Add(routine);
            if (s_Hooked) return;
            s_Hooked = true;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            for (int i = s_Routines.Count - 1; i >= 0; i--)
            {
                bool more;
                try { more = s_Routines[i].MoveNext(); }
                catch (Exception e) { Debug.LogException(e); more = false; }
                if (!more) s_Routines.RemoveAt(i);
            }

            if (s_Routines.Count == 0)
            {
                EditorApplication.update -= Pump;
                s_Hooked = false;
            }
        }
    }
}
#endif
