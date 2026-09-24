using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Helpwing
{
    /// <summary>The colours the chat draws with, as <c>#rrggbb</c> strings.</summary>
    public sealed class Theme
    {
        public string Accent { get; set; } = "";
        /// <summary>Text on top of the accent: white or ink, whichever stays readable.</summary>
        public string OnAccent { get; set; } = "";
        public string Background { get; set; } = "";
        public string Surface { get; set; } = "";
        public string Border { get; set; } = "";
        public string Text { get; set; } = "";
        public string MutedText { get; set; } = "";
        public string Danger { get; set; } = "";

        /// <summary>This theme with every non-empty colour of <paramref name="overrides"/> on top.</summary>
        public Theme With(Theme overrides)
        {
            if (overrides == null) return this;
            string Pick(string mine, string theirs) => string.IsNullOrEmpty(theirs) ? mine : theirs;
            return new Theme
            {
                Accent = Pick(Accent, overrides.Accent),
                OnAccent = Pick(OnAccent, overrides.OnAccent),
                Background = Pick(Background, overrides.Background),
                Surface = Pick(Surface, overrides.Surface),
                Border = Pick(Border, overrides.Border),
                Text = Pick(Text, overrides.Text),
                MutedText = Pick(MutedText, overrides.MutedText),
                Danger = Pick(Danger, overrides.Danger),
            };
        }
    }

    /// <summary>Everything worked out from the one accent the project chose, exactly as widget.js does it.</summary>
    public static class Palette
    {
        public const string DefaultAccent = "#2563eb";

        public static Theme ForAccent(string accent, bool dark = false)
        {
            var chosen = Expand(accent) != null ? accent : DefaultAccent;
            return dark
                ? new Theme
                {
                    Accent = chosen,
                    OnAccent = ReadableOn(chosen),
                    Background = "#0b0f19",
                    Surface = "#1a2032",
                    Border = "#2a3346",
                    Text = "#f4f6fb",
                    MutedText = "#98a2b3",
                    Danger = "#f97066",
                }
                : new Theme
                {
                    Accent = chosen,
                    OnAccent = ReadableOn(chosen),
                    Background = "#ffffff",
                    Surface = "#f2f4f7",
                    Border = "#e4e7ec",
                    Text = "#101828",
                    MutedText = "#667085",
                    Danger = "#d92d20",
                };
        }

        /// <summary>White or ink, by relative luminance.</summary>
        public static string ReadableOn(string color)
        {
            var hex = Expand(color);
            if (hex == null) return "#ffffff";
            var channels = new double[3];
            for (var i = 0; i < 3; i++)
            {
                var value = int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                channels[i] = value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            var luminance = 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
            return luminance > 0.55 ? "#101828" : "#ffffff";
        }

        /// <summary>Six hex digits without the hash, or null when <paramref name="color"/> is not a colour.</summary>
        public static string Expand(string color)
        {
            var hex = (color ?? "").Replace("#", "");
            if (hex.Length == 3) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            return Regex.IsMatch(hex, "^[0-9a-fA-F]{6}$") ? hex : null;
        }

        /// <summary>Whether the dark palette applies: the app's override, then the project's setting, then the device.</summary>
        public static bool ResolveDark(bool? forced, WidgetConfig config, bool deviceIsDark)
        {
            if (forced.HasValue) return forced.Value;
            var scheme = config?.ColorScheme ?? "system";
            return scheme == "dark" || (scheme != "light" && deviceIsDark);
        }
    }
}
