using System.Collections.Generic;
using System.Linq;

namespace Helpwing
{
    /// <summary>The project's own words (title, greeting, offline message) in the app's language.</summary>
    public static class Copy
    {
        public const string Title = "title";
        public const string Greeting = "greeting";
        public const string OfflineMessage = "offline_message";
        public const string TypingText = "typing_text";

        /// <summary>The project's translation for <paramref name="locale"/>, else what it wrote without one. Never a stock line.</summary>
        public static string ForLocale(WidgetConfig config, string field, string locale = null)
        {
            if (config == null) return "";
            var translations = config.Translations ?? new Dictionary<string, Dictionary<string, string>>();
            var matched = Match(locale, translations.Keys);
            if (matched.Length > 0 && translations.TryGetValue(matched, out var fields) && fields != null
                && fields.TryGetValue(field, out var translated) && !string.IsNullOrEmpty(translated))
            {
                return translated;
            }
            return config.Field(field) ?? "";
        }

        /// <summary>The available tag <paramref name="locale"/> asks for, falling back from <c>pt-BR</c> to <c>pt</c>.</summary>
        public static string Match(string locale, IEnumerable<string> available)
        {
            var tag = (locale ?? "").Trim().Replace('_', '-').ToLowerInvariant();
            if (tag.Length == 0) return "";
            var tags = available as ICollection<string> ?? available.ToList();
            if (tags.Contains(tag)) return tag;
            var primary = tag.Split('-')[0];
            return tags.Contains(primary) ? primary : "";
        }
    }
}
