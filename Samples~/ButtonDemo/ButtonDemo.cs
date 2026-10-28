using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UtilityTools;

namespace UtilityTools.Samples
{
    /// <summary>
    /// Add this to a GameObject to see every [Button] option in the Inspector.
    /// </summary>
    public class ButtonDemo : MonoBehaviour
    {
        public enum Shape { Cube, Sphere, Capsule }

        [SerializeField] private int score;

        [Button]
        private void LogHello() => Debug.Log($"Hello from {name}", this);

        [Button("Reset Score", ButtonAttribute.Mode.EditModeOnly, tooltip: "Sets score back to 0 (undoable)")]
        private void ResetScore()
        {
            Debug.Log($"Score was {score}", this);
            score = 0;
        }

        [Button("Rebuild", position: ButtonAttribute.Position.Top, color: "#2e86de")]
        private void Rebuild() => Debug.Log("Rebuilding…", this);

        [Button("Delete Children", color: "#c0392b", confirm: true,
            confirmMessage: "Destroy every child of this object?")]
        private void DeleteChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }

        // Parameters get an editor; unopened params run with their defaults.
        [Button("Spawn")]
        private void Spawn(int count = 3, Shape shape = Shape.Sphere, List<Vector3> offsets = null)
        {
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive((PrimitiveType)System.Enum.Parse(typeof(PrimitiveType), shape.ToString()));
                go.transform.SetParent(transform, false);
                if (offsets != null && i < offsets.Count) go.transform.localPosition = offsets[i];
            }
        }

        // Coroutines: StartCoroutine in Play Mode, editor pump in Edit Mode.
        [Button("Count Down")]
        private IEnumerator CountDown()
        {
            for (int i = 3; i > 0; i--)
            {
                Debug.Log(i, this);
                yield return new WaitForSecondsRealtime(1f);
            }

            Debug.Log("Go!", this);
        }

        // Return values are shown under the button.
        [Button("Child Count")]
        private int ChildCount() => transform.childCount;
    }
}
