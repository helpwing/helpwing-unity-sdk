using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Helpwing
{
    // The same markdown subset every Helpwing surface renders (dashboard, widget.js, email, the other SDKs):
    // CommonMark minus reference links, HTML, setext headings and indented code, plus strikethrough and pipe tables.
    // A single newline is a line break. Nothing here produces markup; renderers walk the tree.

    public enum InlineKind
    {
        Text,
        Break,
        Code,
        Strong,
        Em,
        Strike,
        Link,
        /// <summary>A picture that came with the message, named by <c>cid</c>. Never a URL.</summary>
        Image,
    }

    public sealed class MarkdownInline
    {
        public InlineKind Kind { get; internal set; }
        public string Text { get; internal set; } = "";
        public string Href { get; internal set; } = "";
        public string Cid { get; internal set; } = "";
        public string Alt { get; internal set; } = "";
        public List<MarkdownInline> Children { get; internal set; } = new List<MarkdownInline>();
    }

    public enum BlockKind
    {
        Paragraph,
        Heading,
        Code,
        Quote,
        List,
        Table,
        Rule,
    }

    public enum ColumnAlign
    {
        Left,
        Center,
        Right,
    }

    public sealed class MarkdownCell
    {
        public List<MarkdownInline> Spans { get; internal set; } = new List<MarkdownInline>();
        public ColumnAlign Align { get; internal set; }
    }

    public sealed class MarkdownBlock
    {
        public BlockKind Kind { get; internal set; }
        /// <summary>Paragraph and heading content.</summary>
        public List<MarkdownInline> Spans { get; internal set; } = new List<MarkdownInline>();
        public int Level { get; internal set; }
        public string Language { get; internal set; } = "";
        /// <summary>A code block's text.</summary>
        public string Text { get; internal set; } = "";
        /// <summary>A quote's content.</summary>
        public List<MarkdownBlock> Blocks { get; internal set; } = new List<MarkdownBlock>();
        public bool Ordered { get; internal set; }
        public int Start { get; internal set; } = 1;
        public List<List<MarkdownBlock>> Items { get; internal set; } = new List<List<MarkdownBlock>>();
        public List<MarkdownCell> Head { get; internal set; } = new List<MarkdownCell>();
        public List<List<MarkdownCell>> Rows { get; internal set; } = new List<List<MarkdownCell>>();
    }

    public static class Markdown
    {
        private static readonly Regex Fence = new Regex(@"^ {0,3}(```|~~~)[ \t]*([^\s`]*)");
        private static readonly Regex HeadingLine = new Regex(@"^ {0,3}(#{1,6})[ \t]+(.*?)[ \t]*#*[ \t]*\z");
        private static readonly Regex RuleLine = new Regex(@"^ {0,3}([-*_])[ \t]*(?:\1[ \t]*){2,}\z");
        private static readonly Regex QuoteLine = new Regex(@"^ {0,3}>[ \t]?");
        private static readonly Regex ItemLine = new Regex(@"^( *)([-*+]|[0-9]{1,9}[.)])[ \t]+(.*)\z");
        private static readonly Regex Divider = new Regex(@"^ {0,3}\|?[ \t]*:?-{1,}:?[ \t]*(\|[ \t]*:?-{1,}:?[ \t]*)*\|?[ \t]*\z");

        /// <summary>Schemes a link may carry. Anything else, <c>javascript:</c> above all, is not a link.</summary>
        private static readonly Regex SafeHrefPattern = new Regex(@"^(?:https?://|mailto:|tel:)[^\s]+\z", RegexOptions.IgnoreCase);
        private static readonly Regex BareUrl = new Regex(@"^(?:https?://|www\.)[^\s<>\[\]()]*[^\s<>\[\]().,;:!?'""]", RegexOptions.IgnoreCase);
        private static readonly Regex Email = new Regex(@"^[^\s@<>]+@[^\s@<>]+\.[a-z]{2,}\z", RegexOptions.IgnoreCase);
        private static readonly Regex Www = new Regex(@"^www\.[^\s]+\z", RegexOptions.IgnoreCase);
        private static readonly Regex Escapable = new Regex(@"[\\`*_{}\[\]()#+\-.!|~>]");
        private static readonly Regex LinkPattern = new Regex(@"^(!?)\[([^\]\[]*)\]\([ \t]*<?([^\s)]*)>?(?:[ \t]+""[^""]*"")?[ \t]*\)");
        private static readonly Regex CidPattern = new Regex(@"^cid:(\S+)\z", RegexOptions.IgnoreCase);
        private static readonly Regex CodeSpan = new Regex(@"^(`+)([^`][\s\S]*?)\1(?!`)");
        private static readonly Regex AutoLink = new Regex(@"^<((?:https?://|mailto:)[^\s>]+)>", RegexOptions.IgnoreCase);
        private static readonly Regex WordChar = new Regex("[A-Za-z0-9_]");
        private static readonly Regex UrlNeighbour = new Regex("[A-Za-z0-9_@/.]");

        private static readonly (Regex Pattern, InlineKind Kind, bool Wordish)[] Runs =
        {
            (new Regex(@"^\*\*(\S|\S[\s\S]*?\S)\*\*"), InlineKind.Strong, false),
            (new Regex(@"^__(\S|\S[\s\S]*?\S)__"), InlineKind.Strong, true),
            (new Regex(@"^~~(\S|\S[\s\S]*?\S)~~"), InlineKind.Strike, false),
            (new Regex(@"^\*(\S|\S[\s\S]*?\S)\*"), InlineKind.Em, false),
            (new Regex(@"^_(\S|\S[\s\S]*?\S)_"), InlineKind.Em, true),
        };

        /// <summary>Markdown source as blocks. Empty source is no blocks.</summary>
        public static List<MarkdownBlock> Parse(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return new List<MarkdownBlock>();
            var normalised = source.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
            return ParseBlocks(normalised.Split('\n').ToList());
        }

        /// <summary>A destination worth linking, or null. Bare addresses and <c>www.</c> hosts are promoted.</summary>
        public static string SafeHref(string raw)
        {
            var url = (raw ?? "").Trim();
            if (url.Length == 0) return null;
            if (SafeHrefPattern.IsMatch(url)) return url;
            if (Email.IsMatch(url)) return "mailto:" + url;
            if (Www.IsMatch(url)) return "https://" + url;
            return null;
        }

        // -- inline -----------------------------------------------------------------------

        private static MarkdownInline TextSpan(string text) => new MarkdownInline { Kind = InlineKind.Text, Text = text };

        /// <summary>A bare URL keeps its trailing bracket only when the text opened one.</summary>
        private static string TrimUrl(string url)
        {
            var end = url.Length;
            while (end > 0 && url[end - 1] == ')')
            {
                var slice = url.Substring(0, end);
                var opens = slice.Count(c => c == '(');
                var closes = slice.Count(c => c == ')');
                if (opens >= closes) break;
                end--;
            }
            return url.Substring(0, end);
        }

        /// <summary>The spans of one paragraph, heading or cell. Not linkable inside a link's own label.</summary>
        private static List<MarkdownInline> Inline(string source, bool linkable = true)
        {
            var spans = new List<MarkdownInline>();
            var plain = new StringBuilder();
            var index = 0;

            void Flush()
            {
                if (plain.Length > 0) spans.Add(TextSpan(plain.ToString()));
                plain.Clear();
            }

            while (index < source.Length)
            {
                var c = source[index];
                var rest = source.Substring(index);
                var before = index > 0 ? source[index - 1].ToString() : "";

                if (c == '\\' && index + 1 < source.Length && Escapable.IsMatch(source[index + 1].ToString()))
                {
                    plain.Append(source[index + 1]);
                    index += 2;
                    continue;
                }

                if (c == '\n')
                {
                    Flush();
                    spans.Add(new MarkdownInline { Kind = InlineKind.Break });
                    index++;
                    continue;
                }

                // Code first: everything inside it is literal.
                if (c == '`')
                {
                    var code = CodeSpan.Match(rest);
                    if (code.Success)
                    {
                        Flush();
                        spans.Add(new MarkdownInline { Kind = InlineKind.Code, Text = code.Groups[2].Value.Replace('\n', ' ').Trim() });
                        index += code.Length;
                        continue;
                    }
                }

                if (c == '[' || c == '!')
                {
                    var link = LinkPattern.Match(rest);
                    var isImage = link.Success && link.Groups[1].Value == "!";
                    if (link.Success && (c == '[' || isImage))
                    {
                        var label = link.Groups[2].Value;
                        var target = link.Groups[3].Value;
                        var cid = isImage ? CidPattern.Match(target) : Match.Empty;
                        var href = SafeHref(target);
                        Flush();
                        // A picture that came with the message is shown; a remote one is only a link to it.
                        if (cid.Success) spans.Add(new MarkdownInline { Kind = InlineKind.Image, Cid = cid.Groups[1].Value, Alt = label });
                        else if (href == null) spans.AddRange(Inline(label, linkable));
                        else if (isImage) spans.Add(new MarkdownInline { Kind = InlineKind.Link, Href = href, Children = { TextSpan(label.Length > 0 ? label : href) } });
                        else spans.Add(new MarkdownInline { Kind = InlineKind.Link, Href = href, Children = Inline(label, false) });
                        index += link.Length;
                        continue;
                    }
                }

                if (c == '<')
                {
                    var auto = AutoLink.Match(rest);
                    if (auto.Success)
                    {
                        var href = SafeHref(auto.Groups[1].Value);
                        Flush();
                        if (href != null) spans.Add(new MarkdownInline { Kind = InlineKind.Link, Href = href, Children = { TextSpan(auto.Groups[1].Value) } });
                        else plain.Append(auto.Value);
                        index += auto.Length;
                        continue;
                    }
                }

                if (c == '*' || c == '_' || c == '~')
                {
                    Match found = null;
                    InlineKind kind = InlineKind.Text;
                    foreach (var run in Runs)
                    {
                        var match = run.Pattern.Match(rest);
                        if (!match.Success) continue;
                        // `snake_case_name` is one word: the underscore forms only stand at a word boundary.
                        if (run.Wordish)
                        {
                            var after = match.Length < rest.Length ? rest[match.Length].ToString() : "";
                            if (WordChar.IsMatch(before) || WordChar.IsMatch(after)) continue;
                        }
                        found = match;
                        kind = run.Kind;
                        break;
                    }
                    if (found != null)
                    {
                        Flush();
                        spans.Add(new MarkdownInline { Kind = kind, Children = Inline(found.Groups[1].Value, linkable) });
                        index += found.Length;
                        continue;
                    }
                }

                if (linkable && (c == 'h' || c == 'w' || c == 'H' || c == 'W') && !UrlNeighbour.IsMatch(before))
                {
                    var bare = BareUrl.Match(rest);
                    if (bare.Success)
                    {
                        var text = TrimUrl(bare.Value);
                        var href = SafeHref(text);
                        if (href != null)
                        {
                            Flush();
                            spans.Add(new MarkdownInline { Kind = InlineKind.Link, Href = href, Children = { TextSpan(text) } });
                            index += text.Length;
                            continue;
                        }
                    }
                }

                plain.Append(c);
                index++;
            }

            Flush();
            return spans;
        }

        // -- blocks -----------------------------------------------------------------------

        private static int IndentOf(string line) => line.Length - line.TrimStart().Length;

        /// <summary>Whether a line starts something of its own, and so cannot continue a paragraph.</summary>
        private static bool OpensBlock(string line)
        {
            return Fence.IsMatch(line) || HeadingLine.IsMatch(line) || RuleLine.IsMatch(line) || QuoteLine.IsMatch(line) || ItemLine.IsMatch(line);
        }

        private static List<string> Cells(string row)
        {
            var trimmed = row.Trim();
            if (trimmed.StartsWith("|")) trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|")) trimmed = trimmed.Substring(0, trimmed.Length - 1);
            var cells = new List<string>();
            var current = new StringBuilder();
            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
                {
                    current.Append('|');
                    i++;
                    continue;
                }
                if (c == '|')
                {
                    cells.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            cells.Add(current.ToString().Trim());
            return cells;
        }

        private static List<ColumnAlign> Alignments(string divider)
        {
            return Cells(divider).Select(cell =>
            {
                var left = cell.StartsWith(":");
                var right = cell.EndsWith(":");
                if (left && right) return ColumnAlign.Center;
                return right ? ColumnAlign.Right : ColumnAlign.Left;
            }).ToList();
        }

        private static bool IsOrderedMarker(string marker) => marker.Length > 0 && char.IsDigit(marker[0]);

        /// <summary>One list, from its first marker to the first line no longer part of it.</summary>
        private static MarkdownBlock List(List<string> lines, int from, out int next)
        {
            var first = ItemLine.Match(lines[from]);
            var ordered = IsOrderedMarker(first.Groups[2].Value);
            var indent = first.Groups[1].Value.Length;
            var items = new List<List<MarkdownBlock>>();
            var index = from;

            while (index < lines.Count)
            {
                var marker = ItemLine.Match(lines[index]);
                // A deeper marker belongs to the item above; a shallower one, or a switch of kind, is another list.
                if (!marker.Success || marker.Groups[1].Value.Length != indent || ordered != IsOrderedMarker(marker.Groups[2].Value)) break;

                var content = marker.Groups[1].Value.Length + marker.Groups[2].Value.Length + 1;
                var item = new List<string> { marker.Groups[3].Value };
                index++;

                while (index < lines.Count)
                {
                    var line = lines[index];
                    if (line.Trim().Length == 0)
                    {
                        var after = index + 1 < lines.Count ? lines[index + 1] : null;
                        if (after == null || after.Trim().Length == 0 || (IndentOf(after) < content && !ItemLine.IsMatch(after))) break;
                        item.Add("");
                        index++;
                        continue;
                    }
                    if (IndentOf(line) >= content)
                    {
                        item.Add(line.Substring(content));
                        index++;
                        continue;
                    }
                    if (OpensBlock(line)) break;
                    item.Add(line.Trim()); // A wrapped line still belongs to the item's paragraph.
                    index++;
                }

                items.Add(ParseBlocks(item));
            }

            var start = 1;
            if (ordered)
            {
                var digits = new string(first.Groups[2].Value.TakeWhile(char.IsDigit).ToArray());
                if (!int.TryParse(digits, out start)) start = 1;
            }
            next = index;
            return new MarkdownBlock { Kind = BlockKind.List, Ordered = ordered, Start = start, Items = items };
        }

        private static List<MarkdownBlock> ParseBlocks(List<string> lines)
        {
            var blocks = new List<MarkdownBlock>();
            var index = 0;

            while (index < lines.Count)
            {
                var line = lines[index];

                if (line.Trim().Length == 0)
                {
                    index++;
                    continue;
                }

                var fence = Fence.Match(line);
                if (fence.Success)
                {
                    var closes = new Regex("^ {0,3}" + Regex.Escape(fence.Groups[1].Value) + @"[ \t]*\z");
                    var body = new List<string>();
                    index++;
                    while (index < lines.Count && !closes.IsMatch(lines[index])) body.Add(lines[index++]);
                    index++; // The closing fence, or the end of the source when it was never written.
                    blocks.Add(new MarkdownBlock { Kind = BlockKind.Code, Language = fence.Groups[2].Value, Text = string.Join("\n", body) });
                    continue;
                }

                var heading = HeadingLine.Match(line);
                if (heading.Success)
                {
                    blocks.Add(new MarkdownBlock { Kind = BlockKind.Heading, Level = heading.Groups[1].Value.Length, Spans = Inline(heading.Groups[2].Value) });
                    index++;
                    continue;
                }

                if (RuleLine.IsMatch(line))
                {
                    blocks.Add(new MarkdownBlock { Kind = BlockKind.Rule });
                    index++;
                    continue;
                }

                if (QuoteLine.IsMatch(line))
                {
                    var quoted = new List<string>();
                    while (index < lines.Count)
                    {
                        var next = lines[index];
                        if (QuoteLine.IsMatch(next)) quoted.Add(QuoteLine.Replace(next, "", 1));
                        else if (next.Trim().Length > 0 && !OpensBlock(next)) quoted.Add(next); // Wrapped, still quoted.
                        else break;
                        index++;
                    }
                    blocks.Add(new MarkdownBlock { Kind = BlockKind.Quote, Blocks = ParseBlocks(quoted) });
                    continue;
                }

                if (ItemLine.IsMatch(line))
                {
                    blocks.Add(List(lines, index, out var after));
                    index = after;
                    continue;
                }

                var divider = index + 1 < lines.Count ? lines[index + 1] : null;
                if (line.Contains("|") && !string.IsNullOrEmpty(divider) && Divider.IsMatch(divider) && divider.Contains("-"))
                {
                    var align = Alignments(divider);
                    List<MarkdownCell> Row(string text) => Cells(text)
                        .Select((cell, column) => new MarkdownCell { Spans = Inline(cell), Align = column < align.Count ? align[column] : ColumnAlign.Left })
                        .ToList();
                    var head = Row(line);
                    var rows = new List<List<MarkdownCell>>();
                    index += 2;
                    while (index < lines.Count && lines[index].Trim().Length > 0 && lines[index].Contains("|"))
                    {
                        rows.Add(Row(lines[index]));
                        index++;
                    }
                    blocks.Add(new MarkdownBlock { Kind = BlockKind.Table, Head = head, Rows = rows });
                    continue;
                }

                var paragraph = new List<string> { line };
                index++;
                while (index < lines.Count && lines[index].Trim().Length > 0 && !OpensBlock(lines[index])) paragraph.Add(lines[index++]);
                blocks.Add(new MarkdownBlock { Kind = BlockKind.Paragraph, Spans = Inline(string.Join("\n", paragraph).Trim()) });
            }

            return blocks;
        }
    }
}
