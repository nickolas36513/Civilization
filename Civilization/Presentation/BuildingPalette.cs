using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Civilization
{
    internal sealed class BuildingPalette : VisualElement
    {
        private readonly BuildingPlacement placement;
        private readonly Dictionary<BuildingDefinition, Button> buttons = new();
        private readonly Label status;
        private BuildingDefinition previousSelection;
        private int previousRotation = -1;
        private string previousStatus;

        internal BuildingPalette(IEnumerable<BuildingDefinition> buildings, BuildingPlacement placement)
        {
            this.placement = placement;
            AddToClassList("building-palette");

            var header = new VisualElement();
            header.AddToClassList("building-palette-header");
            Add(header);
            var heading = new Label("Build");
            heading.AddToClassList("trial-section");
            header.Add(heading);
            var controls = new Label("E  Place   Q  Remove / cancel   R  Rotate");
            controls.AddToClassList("trial-subtle");
            header.Add(controls);
            status = new Label();
            status.AddToClassList("building-status");
            header.Add(status);

            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.contentContainer.AddToClassList("building-icon-row");
            Add(scroll);

            foreach (BuildingDefinition building in buildings)
            {
                if (building?.Prefab == null)
                    continue;

                Vector2Int size = building.Footprint;
                var button = new Button(() => placement.Select(building))
                {
                    tooltip = $"{building.Label} · {size.x} × {size.y} cells"
                };
                button.AddToClassList("building-icon-button");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("building-icon");
                if (building.Icon != null)
                    icon.style.backgroundImage = new StyleBackground(building.Icon);
                else
                    icon.generateVisualContent += DrawCubeIcon;
                button.Add(icon);
                var label = new Label(building.Label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("building-icon-label");
                button.Add(label);
                scroll.Add(button);
                buttons.Add(building, button);
            }

            Refresh();
        }

        internal void Refresh()
        {
            BuildingDefinition selection = placement.Selected;
            if (selection != previousSelection)
            {
                if (previousSelection != null && buttons.TryGetValue(previousSelection, out Button previous))
                    previous.RemoveFromClassList("building-icon-selected");
                if (selection != null && buttons.TryGetValue(selection, out Button current))
                    current.AddToClassList("building-icon-selected");
            }

            if (selection != previousSelection || placement.RotationDegrees != previousRotation || placement.Status != previousStatus)
            {
                status.text = selection == null ? placement.Status
                    : $"{selection.Label} · {placement.RotationDegrees}° · {placement.Status}";
            }

            previousSelection = selection;
            previousRotation = placement.RotationDegrees;
            previousStatus = placement.Status;
        }

        private static void DrawCubeIcon(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            DrawFace(painter, new Color(0.67f, 0.89f, 0.85f), new(14, 1), new(27, 8), new(14, 15), new(1, 8));
            DrawFace(painter, new Color(0.38f, 0.67f, 0.64f), new(1, 8), new(14, 15), new(14, 28), new(1, 21));
            DrawFace(painter, new Color(0.23f, 0.48f, 0.50f), new(14, 15), new(27, 8), new(27, 21), new(14, 28));
        }

        private static void DrawFace(Painter2D painter, Color color, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.LineTo(c);
            painter.LineTo(d);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
