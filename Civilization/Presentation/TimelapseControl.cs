using UnityEngine;
using UnityEngine.UIElements;

namespace Civilization
{
    internal sealed class TimelapseControl : VisualElement
    {
        private static readonly int[] Speeds = { 1, 2, 4, 8, 16, 32, 64, 128 };

        private readonly Label currentSpeed = new();
        private readonly Button slower;
        private readonly Button faster;
        private int speedIndex;

        internal int Speed => Speeds[speedIndex];

        internal TimelapseControl()
        {
            AddToClassList("trial-timelapse");
            currentSpeed.AddToClassList("trial-timelapse-speed");

            slower = new Button(() => ChangeSpeed(-1)) { text = "−", tooltip = "Decrease timelapse speed" };
            faster = new Button(() => ChangeSpeed(1)) { text = "+", tooltip = "Increase timelapse speed" };
            Add(slower);
            Add(currentSpeed);
            Add(faster);

            ChangeSpeed(0);
        }

        private void ChangeSpeed(int direction)
        {
            speedIndex = Mathf.Clamp(speedIndex + direction, 0, Speeds.Length - 1);
            currentSpeed.text = $"Timelapse {Speed}×";
            slower.SetEnabled(speedIndex > 0);
            faster.SetEnabled(speedIndex < Speeds.Length - 1);
        }
    }
}
