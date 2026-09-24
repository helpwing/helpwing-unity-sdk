using UnityEngine;
using UnityEngine.UIElements;

namespace Helpwing.UI
{
    public enum LauncherCorner
    {
        /// <summary>The corner the project chose in the dashboard.</summary>
        Project,
        BottomRight,
        BottomLeft,
    }

    /// <summary>The round button in the corner, with the unread badge. Hidden when there is nothing to launch.</summary>
    public sealed class SupportLauncherView : VisualElement
    {
        private readonly HelpwingClient client;
        private readonly Button button;
        private readonly Label badge;

        public SupportLauncherView(HelpwingClient client, string label = "Chat", LauncherCorner corner = LauncherCorner.Project, float offset = 24f)
        {
            this.client = client;
            Corner = corner;
            Offset = offset;
            style.position = Position.Absolute;
            pickingMode = PickingMode.Ignore;

            button = new Button(client.Open) { text = label };
            Styles.Bare(button);
            button.style.minWidth = 56;
            button.style.height = 56;
            button.Radius(28).Padding(0, 20);
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(button);

            badge = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
            badge.style.position = Position.Absolute;
            badge.style.top = -4;
            badge.style.right = -4;
            badge.style.minWidth = 22;
            badge.style.height = 22;
            badge.Radius(11).Padding(0, 6).Margin(0, 0);
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(badge);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                client.StateChanged += OnState;
                client.OpenChanged += OnOpen;
                client.AppearanceChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                client.StateChanged -= OnState;
                client.OpenChanged -= OnOpen;
                client.AppearanceChanged -= Refresh;
            });
        }

        public LauncherCorner Corner { get; set; }
        public float Offset { get; set; }

        public void SetFont(FontDefinition font)
        {
            style.unityFontDefinition = new StyleFontDefinition(font);
        }

        private void OnState(ChatState _) => Refresh();

        private void OnOpen(bool _) => Refresh();

        public void Refresh()
        {
            var state = client.State;
            var config = state.Config;
            // Honour the project turning the chat off for this kind of device.
            var shownHere = config == null || (Application.isMobilePlatform ? config.ShowOnMobile : config.ShowOnDesktop);
            style.display = client.IsReady && !client.IsOpen && shownHere ? DisplayStyle.Flex : DisplayStyle.None;

            var theme = new SupportTheme(client.Theme, default);
            button.style.backgroundColor = theme.Accent;
            button.Text(15, theme.OnAccent, FontStyle.Bold);
            badge.Text(12, Color.white, FontStyle.Bold);
            badge.style.backgroundColor = theme.Danger;
            badge.Border(2, theme.Background);

            var unread = state.UnreadCount;
            badge.style.display = unread > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            badge.text = unread > 9 ? "9+" : unread.ToString();

            var left = Corner == LauncherCorner.BottomLeft || (Corner == LauncherCorner.Project && config?.LauncherPosition == "bottom_left");
            style.bottom = Offset;
            style.left = left ? Offset : StyleKeyword.Auto;
            style.right = left ? StyleKeyword.Auto : Offset;
        }
    }
}
