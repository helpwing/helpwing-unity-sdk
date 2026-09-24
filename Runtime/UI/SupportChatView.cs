using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Helpwing.UI
{
    /// <summary>
    /// The conversation as a VisualElement. Put it in your own UI Toolkit screen, or let SupportUI present it.
    /// While it is attached to a panel the visitor counts as reading, which stops replies also being emailed.
    /// </summary>
    public sealed class SupportChatView : VisualElement
    {
        private const string BrandUrl = "https://helpwing.app";

        private readonly HelpwingClient client;
        private readonly SupportLabels labels;
        private readonly Dictionary<ChatMessage, VisualElement> bubbles = new Dictionary<ChatMessage, VisualElement>();
        private FontDefinition font;
        private SupportTheme theme;
        private ScrollView transcript;
        private TextField input;
        private TextField email;
        private Label inputPlaceholder;
        private Label emailPlaceholder;
        private Button sendButton;
        private Label typingLabel;
        private Label availability;
        private Label offlineBanner;
        private Label branding;
        private VisualElement body;
        private VisualElement status;
        private int shownCount;

        public SupportChatView(HelpwingClient client, SupportLabels labels = null, VisualElement header = null)
        {
            this.client = client;
            this.labels = labels ?? new SupportLabels();
            Header = header;
            font = SupportTheme.DefaultFont();
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
        }

        /// <summary>Drawn above the transcript; SupportUI puts its title and close button here.</summary>
        public VisualElement Header { get; }

        /// <summary>Set the typeface. A null font and asset go back to the built-in one.</summary>
        public void SetFont(Font legacyFont, UnityEngine.TextCore.Text.FontAsset fontAsset)
        {
            font = SupportTheme.FontFrom(legacyFont, fontAsset);
            Rebuild();
        }

        private void Attach()
        {
            client.StateChanged += OnState;
            client.AppearanceChanged += Rebuild;
            client.Chat?.SetPresent(true);
            Rebuild();
        }

        private void Detach()
        {
            client.StateChanged -= OnState;
            client.AppearanceChanged -= Rebuild;
            // SupportUI keeps presence while its panel is open; a bare view gives it up on removal.
            if (!client.IsOpen) client.Chat?.SetPresent(false);
        }

        private void OnState(ChatState state)
        {
            if (theme == null || Key(theme.Source) != Key(client.Theme)) Rebuild();
            else Refresh(state);
        }

        private static string Key(Theme value)
        {
            return string.Join(",", value.Accent, value.OnAccent, value.Background, value.Surface, value.Border, value.Text, value.MutedText, value.Danger);
        }

        /// <summary>Build every element again, for a new theme or font.</summary>
        public void Rebuild()
        {
            var draft = input?.value ?? "";
            var address = email?.value ?? "";
            theme = new SupportTheme(client.Theme, font);
            bubbles.Clear();
            shownCount = 0;
            Clear();
            theme.ApplyFont(this);
            style.backgroundColor = theme.Background;
            if (Header != null) Add(Header);

            status = new VisualElement();
            status.style.flexGrow = 1;
            status.style.alignItems = Align.Center;
            status.style.justifyContent = Justify.Center;
            status.Padding(24, 24);
            Add(status);

            body = new VisualElement();
            body.style.flexGrow = 1;
            Add(body);

            availability = Styles.Label("", 12, theme.MutedText).Padding(8, 16);
            availability.style.borderBottomWidth = 1;
            availability.style.borderBottomColor = theme.Border;
            body.Add(availability);

            offlineBanner = Styles.Label(labels.offline, 12, theme.MutedText).Padding(8, 16);
            offlineBanner.style.backgroundColor = theme.Surface;
            body.Add(offlineBanner);

            transcript = new ScrollView(ScrollViewMode.Vertical);
            transcript.style.flexGrow = 1;
            transcript.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            transcript.contentContainer.Padding(12, 0);
            body.Add(transcript);

            typingLabel = Styles.Label("", 12, theme.MutedText);
            typingLabel.style.paddingLeft = 20;
            typingLabel.style.paddingBottom = 6;
            body.Add(typingLabel);

            body.Add(Composer());

            branding = Styles.Label(labels.branding, 11, theme.MutedText);
            branding.style.unityTextAlign = TextAnchor.MiddleCenter;
            branding.style.paddingBottom = 10;
            branding.AddManipulator(new Clickable(() => Application.OpenURL(BrandUrl)));
            body.Add(branding);

            input.SetValueWithoutNotify(draft);
            email.SetValueWithoutNotify(address);
            Refresh(client.State);
        }

        private VisualElement Composer()
        {
            var wrapper = new VisualElement().Padding(8, 12);
            wrapper.style.paddingBottom = 12;
            wrapper.style.borderTopWidth = 1;
            wrapper.style.borderTopColor = theme.Border;

            email = Field(false, labels.emailPlaceholder, out emailPlaceholder);
            email.keyboardType = TouchScreenKeyboardType.EmailAddress;
            email.style.height = 40;
            email.style.marginBottom = 8;
            wrapper.Add(email);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;

            input = Field(true, labels.placeholder, out inputPlaceholder);
            input.style.flexGrow = 1;
            input.style.flexShrink = 1;
            input.style.minHeight = 40;
            input.style.maxHeight = 120;
            input.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            row.Add(input);

            sendButton = new Button(Submit) { text = labels.send };
            Styles.Bare(sendButton);
            sendButton.Text(15, theme.OnAccent, FontStyle.Bold).Radius(20).Padding(0, 18);
            sendButton.style.height = 40;
            sendButton.style.marginLeft = 8;
            sendButton.style.backgroundColor = theme.Accent;
            row.Add(sendButton);
            wrapper.Add(row);

            input.RegisterValueChangedCallback(_ => UpdateComposer());
            email.RegisterValueChangedCallback(_ => UpdateComposer());
            return wrapper;
        }

        private TextField Field(bool multiline, string placeholderText, out Label placeholder)
        {
            var field = new TextField { multiline = multiline };
            Styles.Bare(field);
            var box = field.Q(className: TextField.inputUssClassName) ?? field;
            Styles.Bare(box);
            box.style.backgroundColor = theme.Surface;
            box.Radius(20).Padding(10, 16);
            box.style.color = theme.Text;
            box.style.fontSize = 15;
            box.style.whiteSpace = WhiteSpace.Normal;

            // Drawn by hand: the built-in placeholder needs 2023.1.
            placeholder = Styles.Label(placeholderText, 15, theme.MutedText);
            placeholder.pickingMode = PickingMode.Ignore;
            placeholder.style.position = Position.Absolute;
            placeholder.style.left = 16;
            placeholder.style.top = 10;
            box.Add(placeholder);
            return field;
        }

        private void OnKey(KeyDownEvent evt)
        {
            if ((evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) || evt.shiftKey) return;
            if (!Ready()) return;
            Submit();
            // The field still gets the key and adds a newline; clear it once it has.
            input.schedule.Execute(() => input.value = "");
        }

        private bool AskForEmail()
        {
            // Asked only until there is a conversation to reply to.
            var state = client.State;
            return state.Config != null && state.Config.RequireEmail && state.Conversation == null;
        }

        private bool Ready()
        {
            return input.value.Trim().Length > 0 && (!AskForEmail() || email.value.Trim().Length > 0);
        }

        private void Submit()
        {
            if (!Ready()) return;
            var text = input.value;
            var address = AskForEmail() ? email.value.Trim() : null;
            input.value = "";
            _ = client.Send(text, address);
        }

        private void UpdateComposer()
        {
            inputPlaceholder.style.display = string.IsNullOrEmpty(input.value) ? DisplayStyle.Flex : DisplayStyle.None;
            emailPlaceholder.style.display = string.IsNullOrEmpty(email.value) ? DisplayStyle.Flex : DisplayStyle.None;
            sendButton.SetEnabled(Ready());
            sendButton.style.opacity = Ready() ? 1f : 0.4f;
        }

        /// <summary>Bring what is drawn up to date with the state, reusing bubbles that have not changed.</summary>
        private void Refresh(ChatState state)
        {
            if (theme == null) return;
            var config = state.Config;

            if (state.Status == ChatStatus.Idle || state.Status == ChatStatus.Loading || state.Status == ChatStatus.Unconfigured)
            {
                body.style.display = DisplayStyle.None;
                status.style.display = DisplayStyle.Flex;
                status.Clear();
                var text = Styles.Label(state.Status == ChatStatus.Unconfigured ? labels.unavailable : labels.loading, 14, theme.MutedText);
                text.style.unityTextAlign = TextAnchor.MiddleCenter;
                status.Add(text);
                return;
            }
            status.style.display = DisplayStyle.None;
            body.style.display = DisplayStyle.Flex;

            availability.style.display = config != null && config.ShowAgentAvailability ? DisplayStyle.Flex : DisplayStyle.None;
            availability.text = config != null && config.IsOnline ? labels.online : labels.away;
            offlineBanner.style.display = state.Offline ? DisplayStyle.Flex : DisplayStyle.None;
            typingLabel.style.display = state.Typing != null ? DisplayStyle.Flex : DisplayStyle.None;
            typingLabel.text = state.Typing != null ? labels.Typing(state.Typing.Name) : "";
            branding.style.display = config != null && config.ShowBranding ? DisplayStyle.Flex : DisplayStyle.None;
            email.style.display = AskForEmail() ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateComposer();

            Transcript(state);
        }

        private void Transcript(ChatState state)
        {
            var content = transcript.contentContainer;
            content.Clear();
            if (state.Messages.Count == 0)
            {
                var copy = client.Copy;
                var offline = state.Config != null && !state.Config.IsOnline && copy.OfflineMessage.Length > 0;
                var greeting = Styles.Label(offline ? copy.OfflineMessage : copy.Greeting, 15, theme.MutedText).Padding(32, 32);
                greeting.style.unityTextAlign = TextAnchor.MiddleCenter;
                content.Add(greeting);
                shownCount = 0;
                return;
            }

            var keep = new HashSet<ChatMessage>(state.Messages);
            foreach (var stale in new List<ChatMessage>(bubbles.Keys))
            {
                if (!keep.Contains(stale)) bubbles.Remove(stale);
            }
            foreach (var message in state.Messages)
            {
                if (!bubbles.TryGetValue(message, out var bubble))
                {
                    bubble = new MessageBubble(message, theme, labels, id => _ = client.Retry(id));
                    bubbles[message] = bubble;
                }
                content.Add(bubble);
            }

            // Only downwards, and only when something arrived.
            if (state.Messages.Count > shownCount)
            {
                transcript.schedule.Execute(() => transcript.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(50);
            }
            shownCount = state.Messages.Count;
        }
    }
}
