using UnityEngine;
using UnityEngine.UIElements;

namespace Helpwing.UI
{
    /// <summary>
    /// The drop-in chat: a launcher in the corner and a panel over the game. Builds its own UIDocument and
    /// PanelSettings, so there is nothing to set up but the <see cref="HelpwingClient"/>.
    /// Full screen on a phone-sized panel, a floating card on anything wider.
    /// </summary>
    [AddComponentMenu("Helpwing/Support UI")]
    [DisallowMultipleComponent]
    public sealed class SupportUI : MonoBehaviour
    {
        [Tooltip("Left empty, HelpwingClient.Instance.")]
        [SerializeField] private HelpwingClient client;
        [Tooltip("Left empty, one is made at runtime.")]
        [SerializeField] private PanelSettings panelSettings = null;
        [SerializeField] private int sortingOrder = 1000;
        [Tooltip("The typeface. Left empty, Unity's built-in runtime font.")]
        [SerializeField] private Font font = null;
        [SerializeField] private UnityEngine.TextCore.Text.FontAsset fontAsset = null;
        [SerializeField] private SupportLabels labels = new SupportLabels();
        [SerializeField] private bool showLauncher = true;
        [SerializeField] private LauncherCorner launcherCorner = LauncherCorner.Project;
        [SerializeField, Min(0f)] private float launcherOffset = 24f;
        [Tooltip("The panel's title. Left empty, the project's own heading, then its name.")]
        [SerializeField] private string title = "";
        [Tooltip("Panels narrower than this are drawn full screen.")]
        [SerializeField] private float fullScreenBelowWidth = 600f;

        private UIDocument document;
        private VisualElement root;
        private VisualElement sheet;
        private SupportChatView chat;
        private SupportLauncherView launcher;
        private Label titleLabel;
        private Button closeButton;
        private VisualElement header;

        public SupportLabels Labels => labels;

        public HelpwingClient Client => client;

        private void OnEnable()
        {
            if (client == null) client = HelpwingClient.Instance != null ? HelpwingClient.Instance : FindAnyClient();
            if (client == null)
            {
                Debug.LogWarning("[Helpwing] SupportUI found no HelpwingClient in the scene.", this);
                enabled = false;
                return;
            }

            if (document == null) document = GetComponent<UIDocument>();
            if (document == null)
            {
                // Configured while inactive, so the document enables with its panel already set.
                var host = new GameObject("Helpwing UI");
                host.SetActive(false);
                host.transform.SetParent(transform, false);
                document = host.AddComponent<UIDocument>();
                document.panelSettings = panelSettings != null ? panelSettings : RuntimePanel();
                document.sortingOrder = sortingOrder;
                host.SetActive(true);
            }
            else if (document.panelSettings == null)
            {
                document.panelSettings = panelSettings != null ? panelSettings : RuntimePanel();
            }
            Build(document.rootVisualElement);

            client.OpenChanged += OnOpenChanged;
            client.StateChanged += OnState;
            client.AppearanceChanged += Restyle;
        }

        private void OnDisable()
        {
            if (client == null) return;
            client.OpenChanged -= OnOpenChanged;
            client.StateChanged -= OnState;
            client.AppearanceChanged -= Restyle;
            root?.RemoveFromHierarchy();
        }

        /// <summary>Open the panel. The same as <c>HelpwingClient.Open()</c>.</summary>
        public void Open() => client?.Open();

        public void Close() => client?.Close();

        private static HelpwingClient FindAnyClient()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<HelpwingClient>();
#else
            return FindObjectOfType<HelpwingClient>();
#endif
        }

        private PanelSettings RuntimePanel()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Helpwing Panel";
            // Density-independent sizes, so 56 is a thumb-sized button on any phone.
            settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            settings.referenceDpi = 160;
            settings.fallbackDpi = 160;
            settings.clearColor = false;
            // Everything is styled inline; an empty theme only keeps Unity from warning that there is none.
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            return settings;
        }

        private void Build(VisualElement host)
        {
            var fontDefinition = SupportTheme.FontFrom(font, fontAsset);
            root = new VisualElement { name = "helpwing" };
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;
            root.style.unityFontDefinition = new StyleFontDefinition(fontDefinition);
            host.Add(root);

            launcher = new SupportLauncherView(client, labels.launcher, launcherCorner, launcherOffset);
            launcher.SetFont(fontDefinition);
            if (showLauncher) root.Add(launcher);

            header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.Padding(12, 16);
            header.style.borderBottomWidth = 1;
            titleLabel = Styles.Label("", 17, Color.black, FontStyle.Bold);
            titleLabel.style.flexShrink = 1;
            titleLabel.style.paddingRight = 12;
            titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            titleLabel.style.overflow = Overflow.Hidden;
            titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            header.Add(titleLabel);
            closeButton = new Button(() => client.Close()) { text = labels.close };
            Styles.Bare(closeButton);
            header.Add(closeButton);

            sheet = new VisualElement { name = "helpwing-sheet" };
            sheet.style.position = Position.Absolute;
            sheet.style.overflow = Overflow.Hidden;
            chat = new SupportChatView(client, labels, header);
            chat.SetFont(font, fontAsset);
            sheet.Add(chat);
            root.RegisterCallback<GeometryChangedEvent>(_ => Layout());

            Restyle();
            if (client.IsOpen) root.Add(sheet);
        }

        private void OnOpenChanged(bool open)
        {
            if (sheet == null) return;
            if (open && sheet.parent == null)
            {
                root.Add(sheet);
                Layout();
            }
            else if (!open)
            {
                sheet.RemoveFromHierarchy();
            }
        }

        private void OnState(ChatState _) => Restyle();

        private void Restyle()
        {
            if (header == null) return;
            var theme = new SupportTheme(client.Theme, default);
            var copy = client.Copy;
            titleLabel.text = !string.IsNullOrEmpty(title) ? title : copy.Title.Length > 0 ? copy.Title : client.State.Config?.ProjectName ?? "";
            titleLabel.style.color = theme.Text;
            header.style.borderBottomColor = theme.Border;
            closeButton.text = labels.close;
            closeButton.Text(16, theme.Accent);
            sheet.style.backgroundColor = theme.Background;
            sheet.Border(Floating() ? 1 : 0, theme.Border);
        }

        private bool Floating()
        {
            var width = root?.layout.width ?? 0;
            return !float.IsNaN(width) && width >= fullScreenBelowWidth;
        }

        /// <summary>Full screen inside the safe area on a phone; a card in the launcher's corner otherwise.</summary>
        private void Layout()
        {
            if (sheet == null || root == null || float.IsNaN(root.layout.width) || root.layout.width <= 0) return;
            if (Floating())
            {
                var left = launcherCorner == LauncherCorner.BottomLeft || (launcherCorner == LauncherCorner.Project && client.State.Config?.LauncherPosition == "bottom_left");
                sheet.style.width = 380;
                sheet.style.height = Mathf.Min(640, root.layout.height - launcherOffset * 2);
                sheet.style.top = StyleKeyword.Auto;
                sheet.style.bottom = launcherOffset;
                sheet.style.left = left ? launcherOffset : StyleKeyword.Auto;
                sheet.style.right = left ? StyleKeyword.Auto : launcherOffset;
                sheet.Radius(16).Padding(0, 0);
            }
            else
            {
                // Screen.safeArea is in pixels; the panel has its own units.
                var scale = root.layout.width / Mathf.Max(1, Screen.width);
                var safe = Screen.safeArea;
                sheet.style.width = StyleKeyword.Auto;
                sheet.style.height = StyleKeyword.Auto;
                sheet.style.left = 0;
                sheet.style.right = 0;
                sheet.style.top = 0;
                sheet.style.bottom = 0;
                sheet.Radius(0);
                sheet.style.paddingLeft = safe.xMin * scale;
                sheet.style.paddingRight = (Screen.width - safe.xMax) * scale;
                sheet.style.paddingTop = (Screen.height - safe.yMax) * scale;
                sheet.style.paddingBottom = safe.yMin * scale;
            }
            Restyle();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            // Back on Android, Escape everywhere else.
            if (client != null && client.IsOpen && Input.GetKeyDown(KeyCode.Escape)) client.Close();
#endif
        }
    }
}
