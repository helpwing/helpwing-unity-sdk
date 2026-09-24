using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Helpwing.UI
{
    /// <summary>One message in the transcript.</summary>
    public sealed class MessageBubble : VisualElement
    {
        public MessageBubble(ChatMessage message, SupportTheme theme, SupportLabels labels, Action<string> onRetry)
        {
            var mine = message.Author == MessageAuthor.Customer;
            this.Margin(4, 0).Padding(0, 16);

            if (message.Author == MessageAuthor.System)
            {
                style.alignItems = Align.Center;
                var note = Styles.Label(message.Text, 12, theme.MutedText);
                note.style.unityTextAlign = TextAnchor.MiddleCenter;
                note.Padding(4, 8);
                Add(note);
                return;
            }

            style.alignItems = mine ? Align.FlexEnd : Align.FlexStart;
            var foreground = mine ? theme.OnAccent : theme.Text;
            // On the accent there is no second colour to be quiet in, so quotes keep the bubble's own.
            var muted = mine ? theme.OnAccent : theme.MutedText;

            var bubble = new VisualElement();
            bubble.style.maxWidth = Length.Percent(85);
            bubble.Padding(10, 14).Radius(16);
            bubble.style.backgroundColor = mine ? theme.Accent : theme.Surface;
            if (mine) bubble.style.borderBottomRightRadius = 4;
            else bubble.style.borderBottomLeftRadius = 4;
            if (message.Delivery == Delivery.Pending) bubble.style.opacity = 0.6f;

            if (!mine && !string.IsNullOrEmpty(message.AuthorName))
            {
                var author = Styles.Label(message.AuthorName, 12, theme.MutedText, FontStyle.Bold);
                author.style.marginBottom = 2;
                bubble.Add(author);
            }

            if (message.Markdown)
            {
                bubble.Add(new MarkdownView(message.Text, theme, foreground, muted, mine ? theme.OnAccent : theme.Accent, RichText.ImagesOf(message)));
            }
            else
            {
                bubble.Add(Styles.Label(message.Text, 15, foreground));
            }

            // A picture pasted into an email body is drawn above, where it was written.
            foreach (var attachment in message.Attachments.Where(a => !a.IsInline))
            {
                var file = Styles.Label(attachment.Filename, 13, muted);
                file.style.marginTop = 6;
                if (!string.IsNullOrEmpty(attachment.Url))
                {
                    var url = attachment.Url;
                    file.AddManipulator(new Clickable(() => MarkdownView.OpenLink(url)));
                }
                bubble.Add(file);
            }
            Add(bubble);

            if (message.Delivery == Delivery.Pending)
            {
                Add(Status(labels.sending, theme.MutedText));
            }
            else if (message.Delivery == Delivery.Failed)
            {
                var failed = Status(labels.failed + " " + labels.retry, theme.Danger);
                var id = message.ClientMessageId;
                failed.AddManipulator(new Clickable(() => { if (id != null) onRetry?.Invoke(id); }));
                Add(failed);
            }
        }

        private static Label Status(string text, Color color)
        {
            var label = Styles.Label(text, 11, color);
            label.style.marginTop = 3;
            label.style.marginLeft = 4;
            label.style.marginRight = 4;
            return label;
        }
    }
}
