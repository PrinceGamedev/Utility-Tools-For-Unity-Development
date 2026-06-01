using System;

namespace UtilityTools
{
    /// <summary>
    /// Mark a method to show a clickable button in the Inspector.
    /// Works in Edit/Play depending on Mode.
    /// You can also choose whether the button group is rendered at the Top or Bottom of the inspector.
    ///
    /// Supports parameterless methods, methods with parameters (incl. Lists/arrays/serializable
    /// classes via an auto-generated arg editor), coroutine methods (IEnumerator return), and
    /// methods that return a value (the result is shown under the button).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class ButtonAttribute : Attribute
    {
        public enum Mode { Always, EditModeOnly, PlayModeOnly }
        public enum Position { Top, Bottom }

        public readonly string Label;
        public readonly Mode ShowMode;
        public readonly Position UiPosition;

        /// <summary>Hover tooltip shown on the button (optional).</summary>
        public readonly string Tooltip;

        /// <summary>Button tint as a hex color, e.g. "#c0392b" or "e74c3c" (optional).</summary>
        public readonly string Color;

        /// <summary>When true, a confirmation dialog is shown before the method runs.</summary>
        public readonly bool Confirm;

        /// <summary>Custom message for the confirmation dialog (optional; defaults to the label).</summary>
        public readonly string ConfirmMessage;

        /// <param name="label">Custom label (optional). Defaults to method name.</param>
        /// <param name="mode">When to show (Always/Edit/Play).</param>
        /// <param name="position">Where to show in Inspector (Top/Bottom). Default = Bottom.</param>
        /// <param name="tooltip">Hover tooltip (optional).</param>
        /// <param name="color">Hex tint for the button, e.g. "#c0392b" (optional).</param>
        /// <param name="confirm">Ask for confirmation before running (optional).</param>
        /// <param name="confirmMessage">Custom confirmation message (optional).</param>
        public ButtonAttribute(
            string label = null,
            Mode mode = Mode.Always,
            Position position = Position.Bottom,
            string tooltip = null,
            string color = null,
            bool confirm = false,
            string confirmMessage = null)
        {
            Label = label;
            ShowMode = mode;
            UiPosition = position;
            Tooltip = tooltip;
            Color = color;
            Confirm = confirm;
            ConfirmMessage = confirmMessage;
        }
    }
}
