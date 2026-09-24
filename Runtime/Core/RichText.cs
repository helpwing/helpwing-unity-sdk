using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Helpwing
{
    public enum SegmentKind
    {
        /// <summary>Rich text for a TextCore / TextMeshPro label.</summary>
        Text,
        /// <summary>A fenced code block; <see cref="RichSegment.Text"/> is escaped plain text.</summary>
        Code,
        /// <summary>A picture that came with the message.</summary>
        Image,
        Rule,
    }

    public sealed class RichSegment
    {
        public SegmentKind Kind { get; internal set; }
        public string Text { get; internal set; } = "";
        public string Url { get; internal set; } = "";
        public string Alt { get; internal set; } = "";
    }

    /// <summary>A message body as rich-text segments. <c>&lt;link="i"&gt;</c> tags index into <see cref="Links"/>.</summary>
    public sealed class RichDocument
    {
        public List<RichSegment> Segments { get; } = new List<RichSegment>();
        public List<string> Links { get; } = new List<string>();
    }

    public sealed class RichTextOptions
    {
        public string LinkColor { get; set; } = "#2563eb";
        public string MutedColor { get; set; } = "#667085";
        /// <summary>Highlight behind inline code, with alpha.</summary>
        public string CodeHighlight { get; set; } = "#8080802e";
        /// <summary><c>content_id</c> → URL, for pictures written into the body.</summary>
        public IDictionary<string, string> Images { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Renders parsed markdown as TextCore/TextMeshPro rich text (the same tags work in UI Toolkit and TMP).
    /// Every character anybody typed is escaped, so nothing in a message can become a tag.
    /// </summary>
    public static class RichText
    {
        private const float IndentEm = 1.2f;

        public static RichDocument Render(string markdown, RichTextOptions options = null)
        {
            return Render(Markdown.Parse(markdown), options);
        }

        public static RichDocument Render(IList<MarkdownBlock> blocks, RichTextOptions options = null)
        {
            var writer = new Writer(options ?? new RichTextOptions());
            writer.Blocks(blocks, 0);
            writer.Close();
            return writer.Document;
        }

        /// <summary>Plain text made safe for a rich-text label: every <c>&lt;</c> is wrapped in noparse.</summary>
        public static string Escape(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : text.Replace("<", "<noparse><</noparse>");
        }

        private sealed class Writer
        {
            private readonly RichTextOptions options;
            private readonly StringBuilder text = new StringBuilder();
            private bool tight;

            public Writer(RichTextOptions options)
            {
                this.options = options;
            }

            public RichDocument Document { get; } = new RichDocument();

            public void Close()
            {
                var value = text.ToString().TrimEnd('\n');
                if (value.Length > 0) Document.Segments.Add(new RichSegment { Kind = SegmentKind.Text, Text = value });
                text.Clear();
            }

            private void Separate()
            {
                if (tight)
                {
                    if (text.Length > 0 && !EndsWith("\n")) text.Append('\n');
                    return;
                }
                if (text.Length > 0 && !EndsWith("\n\n")) text.Append(EndsWith("\n") ? "\n" : "\n\n");
            }

            private bool EndsWith(string suffix)
            {
                if (text.Length < suffix.Length) return false;
                for (var i = 0; i < suffix.Length; i++)
                {
                    if (text[text.Length - suffix.Length + i] != suffix[i]) return false;
                }
                return true;
            }

            public void Blocks(IEnumerable<MarkdownBlock> blocks, int depth)
            {
                foreach (var block in blocks) Block(block, depth);
            }

            private void Block(MarkdownBlock block, int depth)
            {
                switch (block.Kind)
                {
                    case BlockKind.Paragraph:
                        Separate();
                        Spans(block.Spans);
                        break;
                    case BlockKind.Heading:
                        Separate();
                        var size = block.Level <= 1 ? 130 : block.Level == 2 ? 120 : 110;
                        text.Append("<size=").Append(size).Append("%><b>");
                        Spans(block.Spans);
                        text.Append("</b></size>");
                        break;
                    case BlockKind.Code:
                        Close();
                        Document.Segments.Add(new RichSegment { Kind = SegmentKind.Code, Text = Escape(block.Text) });
                        break;
                    case BlockKind.Rule:
                        Close();
                        Document.Segments.Add(new RichSegment { Kind = SegmentKind.Rule });
                        break;
                    case BlockKind.Quote:
                        Separate();
                        text.Append("<color=").Append(options.MutedColor).Append("><indent=").Append(Em(depth + 1)).Append(">");
                        var mark = text.Length;
                        Blocks(block.Blocks, depth + 1);
                        if (text.Length == mark) text.Append(' ');
                        text.Append("</indent></color>");
                        break;
                    case BlockKind.List:
                        Separate();
                        for (var i = 0; i < block.Items.Count; i++)
                        {
                            if (i > 0) text.Append('\n');
                            var marker = block.Ordered ? (block.Start + i).ToString(CultureInfo.InvariantCulture) + "." : "•";
                            text.Append("<indent=").Append(Em(depth)).Append('>').Append(marker)
                                .Append("<indent=").Append(Em(depth + 1)).Append('>');
                            Item(block.Items[i], depth + 1);
                            text.Append("</indent>");
                        }
                        break;
                    case BlockKind.Table:
                        Separate();
                        Row(block.Head, true);
                        foreach (var row in block.Rows)
                        {
                            text.Append('\n');
                            Row(row, false);
                        }
                        break;
                }
            }

            /// <summary>An item's blocks without blank lines between them, so the list stays tight.</summary>
            private void Item(List<MarkdownBlock> blocks, int depth)
            {
                var was = tight;
                tight = true;
                for (var i = 0; i < blocks.Count; i++)
                {
                    if (i > 0 && blocks[i].Kind == BlockKind.Paragraph) text.Append('\n');
                    if (blocks[i].Kind == BlockKind.Paragraph) Spans(blocks[i].Spans);
                    else Block(blocks[i], depth);
                }
                tight = was;
            }

            private void Row(List<MarkdownCell> cells, bool head)
            {
                var width = cells.Count == 0 ? 100 : 100 / cells.Count;
                for (var i = 0; i < cells.Count; i++)
                {
                    if (i > 0) text.Append("<pos=").Append(i * width).Append("%>");
                    if (head) text.Append("<b>");
                    Spans(cells[i].Spans);
                    if (head) text.Append("</b>");
                }
            }

            private void Spans(IEnumerable<MarkdownInline> spans)
            {
                foreach (var span in spans) Span(span);
            }

            private void Span(MarkdownInline span)
            {
                switch (span.Kind)
                {
                    case InlineKind.Text:
                        text.Append(Escape(span.Text));
                        break;
                    case InlineKind.Break:
                        text.Append('\n');
                        break;
                    case InlineKind.Code:
                        text.Append("<mark=").Append(options.CodeHighlight).Append('>').Append(Escape(span.Text)).Append("</mark>");
                        break;
                    case InlineKind.Strong:
                        Wrap("b", span.Children);
                        break;
                    case InlineKind.Em:
                        Wrap("i", span.Children);
                        break;
                    case InlineKind.Strike:
                        Wrap("s", span.Children);
                        break;
                    case InlineKind.Link:
                        var href = Markdown.SafeHref(span.Href);
                        if (href == null)
                        {
                            Spans(span.Children);
                            break;
                        }
                        Document.Links.Add(href);
                        text.Append("<link=\"").Append(Document.Links.Count - 1).Append("\"><color=").Append(options.LinkColor).Append("><u>");
                        Spans(span.Children);
                        text.Append("</u></color></link>");
                        break;
                    case InlineKind.Image:
                        if (options.Images != null && options.Images.TryGetValue(span.Cid, out var url) && !string.IsNullOrEmpty(url))
                        {
                            Close();
                            Document.Segments.Add(new RichSegment { Kind = SegmentKind.Image, Url = url, Alt = span.Alt });
                        }
                        else
                        {
                            // A cid with no file behind it reads as its alt text, as a mail client shows it.
                            text.Append(Escape(span.Alt));
                        }
                        break;
                }
            }

            private void Wrap(string tag, IEnumerable<MarkdownInline> children)
            {
                text.Append('<').Append(tag).Append('>');
                Spans(children);
                text.Append("</").Append(tag).Append('>');
            }

            private static string Em(int depth)
            {
                return (depth * IndentEm).ToString("0.##", CultureInfo.InvariantCulture) + "em";
            }
        }

        /// <summary>Content id → URL for the pictures written into a message's body.</summary>
        public static Dictionary<string, string> ImagesOf(ChatMessage message)
        {
            return message.Attachments
                .Where(a => !string.IsNullOrEmpty(a.ContentId) && !string.IsNullOrEmpty(a.Url))
                .GroupBy(a => a.ContentId)
                .ToDictionary(g => g.Key, g => g.Last().Url);
        }
    }
}
