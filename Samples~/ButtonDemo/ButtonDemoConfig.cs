using UnityEngine;
using UtilityTools;

namespace UtilityTools.Samples
{
    /// <summary>[Button] also works on ScriptableObjects. Create via Assets → Create → Utility Tools Samples.</summary>
    [CreateAssetMenu(menuName = "Utility Tools Samples/Button Demo Config")]
    public class ButtonDemoConfig : ScriptableObject
    {
        [SerializeField] private float[] values = { 1f, 2f, 3f };

        [Button("Normalize Values")]
        private void Normalize()
        {
            float max = 0f;
            foreach (var v in values) max = Mathf.Max(max, Mathf.Abs(v));
            if (max <= 0f) return;
            for (int i = 0; i < values.Length; i++) values[i] /= max;
        }

        [Button("Sum")]
        private float Sum()
        {
            float total = 0f;
            foreach (var v in values) total += v;
            return total;
        }
    }
}
