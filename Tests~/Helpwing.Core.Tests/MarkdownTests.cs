using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Helpwing.Tests
{
    /// <summary>The same cases the dashboard's and the React Native SDK's suites run.</summary>
    public class MarkdownTests
    {
        private static string Text(IEnumerable<MarkdownInline> spans)
        {
            return string.Concat(spans.Select(span =>
                span.Kind == InlineKind.Break ? "\n" : span.Children.Count > 0 ? Text(span.Children) : span.Text));
        }

        private static string Text(IEnumerable<MarkdownBlock> blocks)
        {
            return string.Concat(blocks.Select(block =>
            {
                switch (block.Kind)
                {
                    case BlockKind.List: return string.Join(" ", block.Items.Select(Text));
                    case BlockKind.Quote: return Text(block.Blocks);
                    case BlockKind.Code: return block.Text;
                    default: return Text(block.Spans);
                }
            }));
        }

        private static List<MarkdownInline> Spans(string source) => Markdown.Parse(source)[0].Spans;

        [Test]
        public void RendersNothingForAnEmptyDraft()
        {
            Assert.That(Markdown.Parse(""), Is.Empty);
            Assert.That(Markdown.Parse("   \n\n "), Is.Empty);
            Assert.That(Markdown.Parse(null), Is.Empty);
        }

        [Test]
        public void KeepsASingleNewlineAsABreak()
        {
            var blocks = Markdown.Parse("Hi Rajiv,\nThanks for writing in.");
            Assert.That(blocks[0].Kind, Is.EqualTo(BlockKind.Paragraph));
            Assert.That(blocks[0].Spans.Select(s => s.Kind), Is.EqualTo(new[] { InlineKind.Text, InlineKind.Break, InlineKind.Text }));
        }

        [Test]
        public void MarksEmphasisWithoutEatingTheWordsAround()
        {
            var spans = Spans("**no mail is being held.** What we *can* do is enable a domain.");
            Assert.That(spans[0].Kind, Is.EqualTo(InlineKind.Strong));
            Assert.That(Text(spans), Is.EqualTo("no mail is being held. What we can do is enable a domain."));
            Assert.That(spans.Count(s => s.Kind == InlineKind.Em), Is.EqualTo(1));
        }

        [Test]
        public void LeavesAnUnderscoreInsideAWordAlone()
        {
            Assert.That(Spans("the field is body_text_html here").All(s => s.Kind == InlineKind.Text), Is.True);
        }

        [Test]
        public void ReadsBothListMarkersAndTheStartNumber()
        {
            var bullets = Markdown.Parse("- the From name is wrong\n- the footer is wrong");
            Assert.That(bullets[0].Kind, Is.EqualTo(BlockKind.List));
            Assert.That(bullets[0].Ordered, Is.False);
            Assert.That(Text(bullets), Is.EqualTo("the From name is wrong the footer is wrong"));

            var numbered = Markdown.Parse("2. The agreement\n3. Written confirmation")[0];
            Assert.That(numbered.Ordered, Is.True);
            Assert.That(numbered.Start, Is.EqualTo(2));
        }

        [Test]
        public void NestsAListUnderTheItemItWasIndentedInto()
        {
            var list = Markdown.Parse("- outer\n  - inner\n- second")[0];
            Assert.That(list.Items, Has.Count.EqualTo(2));
            Assert.That(list.Items[0].Select(b => b.Kind), Is.EqualTo(new[] { BlockKind.Paragraph, BlockKind.List }));
        }

        [Test]
        public void ReadsHeadingsQuotesRulesAndFencedCode()
        {
            var blocks = Markdown.Parse("## On the review\n\n> quoted line\n\n---\n\n```json\n{\"a\": 1}\n```");
            Assert.That(blocks.Select(b => b.Kind), Is.EqualTo(new[] { BlockKind.Heading, BlockKind.Quote, BlockKind.Rule, BlockKind.Code }));
            Assert.That(blocks[0].Level, Is.EqualTo(2));
            Assert.That(blocks[3].Language, Is.EqualTo("json"));
            Assert.That(blocks[3].Text, Is.EqualTo("{\"a\": 1}"));
        }

        [Test]
        public void DoesNotReadMarkersInsideCode()
        {
            var spans = Spans("the domain is `quickbooks-enterprises.com` **and** it is blocked");
            Assert.That(spans[1].Kind, Is.EqualTo(InlineKind.Code));
            Assert.That(spans[1].Text, Is.EqualTo("quickbooks-enterprises.com"));
            Assert.That(spans.Any(s => s.Kind == InlineKind.Strong), Is.True);
        }

        [Test]
        public void LinksAGuideButNotAnAddressMidSentence()
        {
            var spans = Spans("I'd point you to the [brand use guide](https://quickbooks.intuit.com/help) instead.");
            Assert.That(spans[1].Kind, Is.EqualTo(InlineKind.Link));
            Assert.That(spans[1].Href, Is.EqualTo("https://quickbooks.intuit.com/help"));
            Assert.That(Text(spans), Is.EqualTo("I'd point you to the brand use guide instead."));

            Assert.That(Spans("One went to batkinson@occaps.com, a third party.").Any(s => s.Kind == InlineKind.Link), Is.False);
        }

        [Test]
        public void AutolinksAPastedUrlAndStopsBeforeTheSentenceDoes()
        {
            var link = Spans("See https://helpwing.app/docs (the setup page).").First(s => s.Kind == InlineKind.Link);
            Assert.That(link.Href, Is.EqualTo("https://helpwing.app/docs"));
        }

        [Test]
        public void ReadsAPipeTableWithAlignments()
        {
            var table = Markdown.Parse("| Entity | Domain |\n| --- | ---: |\n| QB Enterprise | .com |")[0];
            Assert.That(table.Kind, Is.EqualTo(BlockKind.Table));
            Assert.That(table.Head.Select(c => Text(c.Spans)), Is.EqualTo(new[] { "Entity", "Domain" }));
            Assert.That(table.Head[1].Align, Is.EqualTo(ColumnAlign.Right));
            Assert.That(table.Rows, Has.Count.EqualTo(1));
        }

        [Test]
        public void ReadsAPastedPictureAsTheFileItArrivedWith()
        {
            var span = Spans("![the zone file](cid:shot-1@northwind)")[0];
            Assert.That(span.Kind, Is.EqualTo(InlineKind.Image));
            Assert.That(span.Cid, Is.EqualTo("shot-1@northwind"));
            Assert.That(span.Alt, Is.EqualTo("the zone file"));
        }

        [Test]
        public void LeavesARemoteImageAsALink()
        {
            var span = Spans("![](https://tracker.example/open.gif)")[0];
            Assert.That(span.Kind, Is.EqualTo(InlineKind.Link));
            Assert.That(span.Href, Is.EqualTo("https://tracker.example/open.gif"));
        }

        [Test]
        public void KeepsAnEscapedMarker()
        {
            var spans = Spans("a literal \\*asterisk\\* stays");
            Assert.That(Text(spans), Is.EqualTo("a literal *asterisk* stays"));
            Assert.That(spans.Any(s => s.Kind == InlineKind.Em), Is.False);
        }

        [Test]
        public void TakesTheSchemesASupportReplyCanPointAt()
        {
            Assert.That(Markdown.SafeHref("https://helpwing.app"), Is.EqualTo("https://helpwing.app"));
            Assert.That(Markdown.SafeHref("mailto:support@helpwing.app"), Is.EqualTo("mailto:support@helpwing.app"));
            Assert.That(Markdown.SafeHref("support@helpwing.app"), Is.EqualTo("mailto:support@helpwing.app"));
            Assert.That(Markdown.SafeHref("www.helpwing.app"), Is.EqualTo("https://www.helpwing.app"));
            Assert.That(Markdown.SafeHref("tel:+15551234"), Is.EqualTo("tel:+15551234"));
        }

        [Test]
        public void RefusesAnythingABrowserWouldRun()
        {
            Assert.That(Markdown.SafeHref("javascript:alert(1)"), Is.Null);
            Assert.That(Markdown.SafeHref("JavaScript:alert(1)"), Is.Null);
            Assert.That(Markdown.SafeHref("data:text/html;base64,PHNjcmlwdD4="), Is.Null);
            Assert.That(Markdown.SafeHref(""), Is.Null);
        }

        [Test]
        public void PrintsAnUnsafeLinkAsItsWords()
        {
            var spans = Spans("[click me](javascript:alert(1))");
            Assert.That(spans.Any(s => s.Kind == InlineKind.Link), Is.False);
            Assert.That(Text(spans), Does.Contain("click me"));
        }
    }
}
