using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Helpwing.Tests
{
    public class CopyTests
    {
        private static WidgetConfig Config(Dictionary<string, Dictionary<string, string>> translations = null)
        {
            var data = FakeServer.DefaultConfig();
            var config = WidgetConfig.FromJson(data);
            if (translations != null) config.Translations = translations;
            return config;
        }

        private static readonly WidgetConfig Translated = Config(new Dictionary<string, Dictionary<string, string>>
        {
            ["ru"] = new Dictionary<string, string> { ["greeting"] = "Здравствуйте! Чем можем помочь?" },
        });

        private static readonly WidgetConfig TypingTranslated = Config(new Dictionary<string, Dictionary<string, string>>
        {
            ["ru"] = new Dictionary<string, string> { ["typing_text"] = "{name} печатает…" },
        });

        [Test]
        public void ReturnsTheTranslationTheProjectWrote()
        {
            Assert.That(Copy.ForLocale(Translated, Copy.Greeting, "ru"), Is.EqualTo("Здравствуйте! Чем можем помочь?"));
            Assert.That(Copy.ForLocale(Translated, Copy.Greeting, "ru-RU"), Is.EqualTo("Здравствуйте! Чем можем помочь?"));
        }

        [Test]
        public void KeepsTheProjectsOwnWordsWithoutATranslation()
        {
            Assert.That(Copy.ForLocale(Translated, Copy.Greeting, "de"), Is.EqualTo("Hi! How can we help?"));
            Assert.That(Copy.ForLocale(Translated, Copy.OfflineMessage, "ru"), Is.EqualTo("We are offline right now."));
            Assert.That(Copy.ForLocale(Translated, Copy.Greeting), Is.EqualTo("Hi! How can we help?"));
            Assert.That(Copy.ForLocale(Config(), Copy.Greeting, "ru"), Is.EqualTo("Hi! How can we help?"));
        }

        [Test]
        public void IsEmptyBeforeTheConfigArrives()
        {
            Assert.That(Copy.ForLocale(null, Copy.Greeting, "ru"), Is.EqualTo(""));
        }

        [Test]
        public void ResolvesTypingTextLikeOtherProjectCopy()
        {
            Assert.That(Copy.ForLocale(TypingTranslated, Copy.TypingText, "ru"), Is.EqualTo("{name} печатает…"));
            Assert.That(Copy.ForLocale(TypingTranslated, Copy.TypingText, "de"), Is.EqualTo(""));
            Assert.That(Copy.ForLocale(Config(), Copy.TypingText), Is.EqualTo(""));
        }

        [Test]
        public void ReadsTranslationsFromTheWire()
        {
            var data = FakeServer.DefaultConfig();
            data["translations"] = new Dictionary<string, object> { ["ru"] = new Dictionary<string, object> { ["title"] = "Поддержка" } };
            var config = (WidgetConfig)WidgetConfig.FromJson((IDictionary<string, object>)Json.Parse(Json.Serialize(data)));
            Assert.That(Copy.ForLocale(config, Copy.Title, "ru"), Is.EqualTo("Поддержка"));
            Assert.That(Copy.ForLocale(config, Copy.Title, "en"), Is.EqualTo(""));
        }

        [TestCase("ru", new[] { "ru", "en" }, "ru")]
        [TestCase("ru-RU", new[] { "ru" }, "ru")]
        [TestCase("ru_RU", new[] { "ru" }, "ru")]
        [TestCase("RU", new[] { "ru" }, "ru")]
        [TestCase("pt-BR", new[] { "pt" }, "pt")]
        [TestCase("pt-BR", new[] { "pt-br", "pt" }, "pt-br")]
        [TestCase("de", new[] { "ru", "en" }, "")]
        [TestCase("", new[] { "ru" }, "")]
        [TestCase(null, new[] { "ru" }, "")]
        public void Matches(string locale, string[] available, string expected)
        {
            Assert.That(Copy.Match(locale, available), Is.EqualTo(expected));
        }
    }

    public class PaletteTests
    {
        [Test]
        public void PicksInkOrWhiteOnTheAccent()
        {
            Assert.That(Palette.ReadableOn("#2563eb"), Is.EqualTo("#ffffff"));
            Assert.That(Palette.ReadableOn("#c6ff3a"), Is.EqualTo("#101828"));
            Assert.That(Palette.ReadableOn("#fff"), Is.EqualTo("#101828"));
            Assert.That(Palette.ReadableOn("nonsense"), Is.EqualTo("#ffffff"));
        }

        [Test]
        public void FallsBackToTheDefaultAccent()
        {
            Assert.That(Palette.ForAccent("", false).Accent, Is.EqualTo(Palette.DefaultAccent));
            Assert.That(Palette.ForAccent("#abc", true).Background, Is.EqualTo("#0b0f19"));
        }

        [Test]
        public void OverridesOnlyWhatWasNamed()
        {
            var theme = Palette.ForAccent("#2563eb").With(new Theme { Accent = "#c6ff3a" });
            Assert.That(theme.Accent, Is.EqualTo("#c6ff3a"));
            Assert.That(theme.Background, Is.EqualTo("#ffffff"));
        }

        [TestCase(null, "system", true, true)]
        [TestCase(null, "system", false, false)]
        [TestCase(null, "dark", false, true)]
        [TestCase(null, "light", true, false)]
        [TestCase(false, "dark", true, false)]
        [TestCase(true, "light", false, true)]
        public void ResolvesDark(bool? forced, string scheme, bool device, bool expected)
        {
            var config = new WidgetConfig { ColorScheme = scheme };
            Assert.That(Palette.ResolveDark(forced, config, device), Is.EqualTo(expected));
        }
    }

    public class RichTextTests
    {
        private static string Only(string markdown, RichTextOptions options = null)
        {
            var document = RichText.Render(markdown, options);
            Assert.That(document.Segments, Has.Count.EqualTo(1));
            return document.Segments[0].Text;
        }

        [Test]
        public void EscapesEveryAngleBracketAnybodyTyped()
        {
            var text = Only("<b>not bold</b> and <color=red>");
            Assert.That(text, Does.Not.Contain("<b>"));
            Assert.That(text, Does.StartWith("<noparse><</noparse>b>"));
        }

        [Test]
        public void TurnsEmphasisIntoTags()
        {
            Assert.That(Only("**bold** _it_ ~~gone~~"), Is.EqualTo("<b>bold</b> <i>it</i> <s>gone</s>"));
        }

        [Test]
        public void IndexesLinksAndDropsUnsafeOnes()
        {
            var document = RichText.Render("[docs](https://helpwing.app) and [bad](javascript:alert(1))");
            Assert.That(document.Links, Is.EqualTo(new[] { "https://helpwing.app" }));
            Assert.That(document.Segments[0].Text, Does.Contain("<link=\"0\">"));
            Assert.That(document.Segments[0].Text, Does.Not.Contain("javascript"));
        }

        [Test]
        public void SplitsOutCodeBlocksRulesAndPictures()
        {
            var options = new RichTextOptions { Images = new Dictionary<string, string> { ["shot"] = "https://files.example/shot.png" } };
            var document = RichText.Render("Before\n\n```\nx < y\n```\n\n---\n\n![zone](cid:shot)\n\n![lost](cid:missing)", options);
            Assert.That(document.Segments.Select(s => s.Kind), Is.EqualTo(new[] { SegmentKind.Text, SegmentKind.Code, SegmentKind.Rule, SegmentKind.Image, SegmentKind.Text }));
            Assert.That(document.Segments[1].Text, Is.EqualTo("x <noparse><</noparse> y"));
            Assert.That(document.Segments[3].Url, Is.EqualTo("https://files.example/shot.png"));
            Assert.That(document.Segments[4].Text, Is.EqualTo("lost"));
        }

        [Test]
        public void RendersListsWithMarkers()
        {
            var text = Only("3. one\n4. two");
            Assert.That(text, Does.Contain("3."));
            Assert.That(text, Does.Contain("4."));
            Assert.That(text.Split('\n'), Has.Length.EqualTo(2));
        }
    }

    public class JsonTests
    {
        [Test]
        public void RoundTripsWhatTheSessionStores()
        {
            var value = new Dictionary<string, object>
            {
                ["text"] = "line\n\"quoted\" \\ é",
                ["n"] = 3,
                ["flag"] = true,
                ["none"] = null,
                ["list"] = new List<object> { 1.5, "two" },
            };
            var parsed = (IDictionary<string, object>)Json.Parse(Json.Serialize(value));
            Assert.That(parsed["text"], Is.EqualTo("line\n\"quoted\" \\ é"));
            Assert.That(parsed["n"], Is.EqualTo(3.0));
            Assert.That(parsed["flag"], Is.EqualTo(true));
            Assert.That(parsed["none"], Is.Null);
            Assert.That(((IList<object>)parsed["list"])[1], Is.EqualTo("two"));
        }

        [Test]
        public void DecodesUnicodeEscapes()
        {
            Assert.That(Json.Parse("\"\\u041f\\u0440\\u0438\\u0432\\u0435\\u0442\""), Is.EqualTo("Привет"));
        }

        [Test]
        public void ReturnsNullForGarbageWhenAskedNicely()
        {
            Assert.That(Json.TryParse("{nope"), Is.Null);
        }
    }
}
