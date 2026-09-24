using UnityEngine;
using UnityEngine.UIElements;

namespace Helpwing.UI
{
    /// <summary>Inline-style shorthands. Everything is styled in code, so the package ships no USS or UXML.</summary>
    internal static class Styles
    {
        public static Color ToColor(string hex, Color fallback = default)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var color) ? color : fallback;
        }

        public static T Padding<T>(this T element, float vertical, float horizontal) where T : VisualElement
        {
            element.style.paddingTop = vertical;
            element.style.paddingBottom = vertical;
            element.style.paddingLeft = horizontal;
            element.style.paddingRight = horizontal;
            return element;
        }

        public static T Margin<T>(this T element, float vertical, float horizontal) where T : VisualElement
        {
            element.style.marginTop = vertical;
            element.style.marginBottom = vertical;
            element.style.marginLeft = horizontal;
            element.style.marginRight = horizontal;
            return element;
        }

        public static T Radius<T>(this T element, float radius) where T : VisualElement
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
            return element;
        }

        public static T Border<T>(this T element, float width, Color color) where T : VisualElement
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            return element;
        }

        public static T Text<T>(this T element, float size, Color color, FontStyle weight = FontStyle.Normal) where T : VisualElement
        {
            element.style.fontSize = size;
            element.style.color = color;
            element.style.unityFontStyleAndWeight = weight;
            element.style.whiteSpace = WhiteSpace.Normal;
            return element;
        }

        public static Label Label(string text, float size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var label = new Label(text) { enableRichText = false };
            label.Text(size, color, weight).Margin(0, 0).Padding(0, 0);
            return label;
        }

        /// <summary>Strip a control's default chrome so the inline styles are all there is.</summary>
        public static void Bare(VisualElement element)
        {
            element.style.backgroundColor = Color.clear;
            element.Border(0, Color.clear).Margin(0, 0).Padding(0, 0);
        }
    }
}
