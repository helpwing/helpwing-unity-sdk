using UnityEngine;
using UnityEngine.UIElements;

namespace Helpwing.UI
{
    /// <summary>The resolved theme as Unity colours, plus the typeface.</summary>
    public sealed class SupportTheme
    {
        public SupportTheme(Theme theme, FontDefinition font)
        {
            Source = theme;
            Accent = Styles.ToColor(theme.Accent, new Color(0.145f, 0.388f, 0.922f));
            OnAccent = Styles.ToColor(theme.OnAccent, Color.white);
            Background = Styles.ToColor(theme.Background, Color.white);
            Surface = Styles.ToColor(theme.Surface, new Color(0.95f, 0.96f, 0.97f));
            Border = Styles.ToColor(theme.Border, new Color(0.89f, 0.91f, 0.93f));
            Text = Styles.ToColor(theme.Text, new Color(0.06f, 0.09f, 0.16f));
            MutedText = Styles.ToColor(theme.MutedText, new Color(0.4f, 0.44f, 0.52f));
            Danger = Styles.ToColor(theme.Danger, new Color(0.85f, 0.18f, 0.13f));
            Font = font;
        }

        public Theme Source { get; }
        public Color Accent { get; }
        public Color OnAccent { get; }
        public Color Background { get; }
        public Color Surface { get; }
        public Color Border { get; }
        public Color Text { get; }
        public Color MutedText { get; }
        public Color Danger { get; }
        public FontDefinition Font { get; }

        /// <summary>The built-in runtime font, so text renders with no font asset set up.</summary>
        public static FontDefinition DefaultFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font != null ? FontDefinition.FromFont(font) : default;
        }

        public static FontDefinition FontFrom(Font font, UnityEngine.TextCore.Text.FontAsset fontAsset)
        {
            if (fontAsset != null) return FontDefinition.FromSDFFont(fontAsset);
            if (font != null) return FontDefinition.FromFont(font);
            return DefaultFont();
        }

        public void ApplyFont(VisualElement element)
        {
            element.style.unityFontDefinition = new StyleFontDefinition(Font);
        }
    }
}
