using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Civilization
{
    internal sealed class DebugTabs : VisualElement
    {
        private readonly VisualElement buttons = new();
        private readonly ScrollView scroll = new() { verticalScrollerVisibility = ScrollerVisibility.Hidden };
        private readonly List<(Button Button, VisualElement Page)> tabs = new();

        internal int SelectedIndex { get; private set; }
        internal event System.Action SelectionChanged;

        internal DebugTabs()
        {
            AddToClassList("trial-tabs");
            buttons.AddToClassList("trial-tab-bar");
            scroll.AddToClassList("trial-tab-content");
            Add(buttons);
            Add(scroll);
        }

        internal VisualElement AddTab(string title)
        {
            int index = tabs.Count;
            var page = new VisualElement();
            page.style.display = index == SelectedIndex ? DisplayStyle.Flex : DisplayStyle.None;
            var button = new Button(() => Select(index)) { text = title };
            button.EnableInClassList("trial-tab-active", index == SelectedIndex);
            tabs.Add((button, page));
            buttons.Add(button);
            scroll.Add(page);
            return page;
        }

        private void Select(int index)
        {
            if (index == SelectedIndex)
                return;

            tabs[SelectedIndex].Page.style.display = DisplayStyle.None;
            tabs[SelectedIndex].Button.RemoveFromClassList("trial-tab-active");
            SelectedIndex = index;
            tabs[index].Page.style.display = DisplayStyle.Flex;
            tabs[index].Button.AddToClassList("trial-tab-active");
            scroll.scrollOffset = Vector2.zero;
            SelectionChanged?.Invoke();
        }
    }
}
