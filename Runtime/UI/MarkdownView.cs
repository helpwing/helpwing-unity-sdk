using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Helpwing.UI
{
    /// <summary>A message body drawn from markdown: rich-text labels, code blocks, rules and attached pictures.</summary>
    public sealed class MarkdownView : VisualElement
    {
        private static readonly Dictionary<string, Texture2D> Pictures = new Dictionary<string, Texture2D>();

        /// <param name="color">The body colour; <paramref name="muted"/> is quotes and code chrome.</param>
        public MarkdownView(string source, SupportTheme theme, Color color, Color muted, Color link, IDictionary<string, string> images)
        {
            var document = RichText.Render(source, new RichTextOptions
            {
                LinkColor = "#" + ColorUtility.ToHtmlStringRGB(link),
                MutedColor = "#" + ColorUtility.ToHtmlStringRGB(muted),
                CodeHighlight = "#" + ColorUtility.ToHtmlStringRGB(muted) + "33",
                Images = images,
            });

            foreach (var segment in document.Segments)
            {
                switch (segment.Kind)
                {
                    case SegmentKind.Text:
                        Add(TextLabel(segment.Text, color, document.Links));
                        break;
                    case SegmentKind.Code:
                        var code = new Label(segment.Text) { enableRichText = true };
                        code.Text(13, color).Padding(8, 10).Radius(6).Margin(4, 0);
                        code.style.backgroundColor = new Color(muted.r, muted.g, muted.b, 0.15f);
                        Add(code);
                        break;
                    case SegmentKind.Rule:
                        var rule = new VisualElement();
                        rule.style.height = 1;
                        rule.style.backgroundColor = new Color(muted.r, muted.g, muted.b, 0.5f);
                        rule.Margin(6, 0);
                        Add(rule);
                        break;
                    case SegmentKind.Image:
                        Add(Picture(segment.Url, segment.Alt));
                        break;
                }
            }

#if !UNITY_2023_2_OR_NEWER
            // No link-tag events before 2023.2, so each link is offered as its own tappable line.
            foreach (var href in document.Links)
            {
                var chip = Styles.Label(href, 12, link);
                chip.style.unityTextAlign = TextAnchor.MiddleLeft;
                chip.style.marginTop = 4;
                var target = href;
                chip.AddManipulator(new Clickable(() => OpenLink(target)));
                Add(chip);
            }
#endif
        }

        /// <summary>Opens only http, https, mailto and tel. Anything else is not a link.</summary>
        public static void OpenLink(string href)
        {
            var safe = Markdown.SafeHref(href);
            if (safe != null) Application.OpenURL(safe);
        }

        private static Label TextLabel(string rich, Color color, List<string> links)
        {
            var label = new Label(rich) { enableRichText = true };
            label.Text(15, color).Margin(1, 0).Padding(0, 0);
#if UNITY_2023_2_OR_NEWER
            label.RegisterCallback<UnityEngine.UIElements.Experimental.PointerUpLinkTagEvent>(evt =>
            {
                if (int.TryParse(evt.linkID, out var index) && index >= 0 && index < links.Count) OpenLink(links[index]);
            });
#endif
            return label;
        }

        /// <summary>A picture that came with the message, sized to the bubble once it has loaded.</summary>
        private static VisualElement Picture(string url, string alt)
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, tooltip = alt };
            image.style.width = Length.Percent(100);
            image.style.height = 120;
            image.Radius(6).Margin(4, 0);
            image.style.overflow = Overflow.Hidden;

            void Show(Texture2D texture)
            {
                image.image = texture;
                Fit(image, texture);
            }

            image.RegisterCallback<GeometryChangedEvent>(_ => Fit(image, image.image as Texture2D));
            if (Pictures.TryGetValue(url, out var cached) && cached != null)
            {
                Show(cached);
                return image;
            }

            var request = UnityWebRequestTexture.GetTexture(url);
            request.SendWebRequest().completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(request);
                    Pictures[url] = texture;
                    Show(texture);
                }
                else
                {
                    image.style.height = StyleKeyword.Auto;
                    image.Add(Styles.Label(alt, 13, Color.gray));
                }
                request.Dispose();
            };
            return image;
        }

        private static void Fit(VisualElement image, Texture2D texture)
        {
            if (texture == null || texture.width == 0 || float.IsNaN(image.layout.width) || image.layout.width <= 0) return;
            var height = Mathf.Min(image.layout.width * texture.height / texture.width, 360f);
            if (Mathf.Abs(image.resolvedStyle.height - height) > 0.5f) image.style.height = height;
        }
    }
}
