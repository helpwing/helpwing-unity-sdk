using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Helpwing
{
    /// <summary>Whether to draw light or dark, over what the project and the device say.</summary>
    public enum SchemeOverride
    {
        /// <summary>The project's Colour scheme setting; <c>Auto</c> follows the device.</summary>
        Project,
        Light,
        Dark,
    }

    /// <summary>Colours to draw with instead of the project's. Blank fields keep the project's.</summary>
    [Serializable]
    public sealed class ThemeColors
    {
        public string accent = "";
        public string onAccent = "";
        public string background = "";
        public string surface = "";
        public string border = "";
        public string text = "";
        public string mutedText = "";
        public string danger = "";

        public Theme ToTheme()
        {
            return new Theme
            {
                Accent = accent,
                OnAccent = onAccent,
                Background = background,
                Surface = surface,
                Border = border,
                Text = text,
                MutedText = mutedText,
                Danger = danger,
            };
        }
    }

    /// <summary>The project's own words, resolved against the client's locale.</summary>
    public readonly struct ProjectCopy
    {
        public ProjectCopy(string title, string greeting, string offlineMessage)
        {
            Title = title;
            Greeting = greeting;
            OfflineMessage = offlineMessage;
        }

        /// <summary>Blank when the project wrote no heading.</summary>
        public string Title { get; }
        public string Greeting { get; }
        public string OfflineMessage { get; }
    }

    /// <summary>
    /// One Helpwing conversation for the whole game. Owns the <see cref="HelpwingChat"/>, polls on the main
    /// thread, stops while the app is paused, and resolves theme and copy for whatever draws the chat.
    /// </summary>
    [AddComponentMenu("Helpwing/Helpwing Client")]
    [DisallowMultipleComponent]
    public sealed class HelpwingClient : MonoBehaviour
    {
        [Tooltip("The host that serves /widget.js.")]
        [SerializeField] private string apiUrl = "https://api.helpwing.app";
        [Tooltip("The project's public key, pk_... Safe to ship in a build.")]
        [SerializeField] private string projectKey = "";
        [Tooltip("Seconds between polls while the chat is on screen.")]
        [SerializeField, Min(1f)] private float pollIntervalSeconds = 5f;
        [Tooltip("Seconds between polls while it is not, for the unread badge. Zero stops them.")]
        [SerializeField, Min(0f)] private float backgroundPollIntervalSeconds = 30f;
        [Tooltip("Picks the project's translated copy. Blank uses the device language.")]
        [SerializeField] private string locale = "";
        [SerializeField] private SchemeOverride colorScheme = SchemeOverride.Project;
        [SerializeField] private ThemeColors themeColors = new ThemeColors();
        [SerializeField] private bool dontDestroyOnLoad = true;

        private FrameScheduler scheduler;
        private string identityJson = "";
        private Identity identity;
        private bool deviceDark;
        private bool began;

        /// <summary>The first client that woke up. Usually the only one.</summary>
        public static HelpwingClient Instance { get; private set; }

        /// <summary>The conversation itself. Null until the client has begun.</summary>
        public HelpwingChat Chat { get; private set; }

        public ChatState State => Chat?.State ?? new ChatState();

        /// <summary>Every change to <see cref="State"/>.</summary>
        public event Action<ChatState> StateChanged;

        /// <summary>Theme or dark mode changed.</summary>
        public event Action AppearanceChanged;

        /// <summary>Whether the built-in chat panel is open.</summary>
        public bool IsOpen { get; private set; }

        public event Action<bool> OpenChanged;

        public string Locale => string.IsNullOrEmpty(locale) ? DeviceInfo.Locale() : locale;

        public SchemeOverride ColorScheme
        {
            get => colorScheme;
            set
            {
                colorScheme = value;
                AppearanceChanged?.Invoke();
            }
        }

        /// <summary>Colours drawn instead of the project's. Only the non-blank ones apply.</summary>
        public ThemeColors ThemeColors
        {
            get => themeColors;
            set
            {
                themeColors = value ?? new ThemeColors();
                AppearanceChanged?.Invoke();
            }
        }

        public bool IsDark
        {
            get
            {
                bool? forced = colorScheme == SchemeOverride.Project ? (bool?)null : colorScheme == SchemeOverride.Dark;
                return Palette.ResolveDark(forced, State.Config, deviceDark);
            }
        }

        /// <summary>The palette worked out from the project's accent, with <see cref="ThemeColors"/> on top.</summary>
        public Theme Theme => Palette.ForAccent(State.Config?.AccentColor, IsDark).With(themeColors?.ToTheme());

        public ProjectCopy Copy => new ProjectCopy(
            Helpwing.Copy.ForLocale(State.Config, Helpwing.Copy.Title, Locale),
            Helpwing.Copy.ForLocale(State.Config, Helpwing.Copy.Greeting, Locale),
            Helpwing.Copy.ForLocale(State.Config, Helpwing.Copy.OfflineMessage, Locale));

        /// <summary>Ready means the project has a chat and it is on.</summary>
        public bool IsReady => State.Status == ChatStatus.Ready;

        /// <summary>Make a client from code instead of the inspector. Begins immediately.</summary>
        public static HelpwingClient Create(string apiUrl, string projectKey, string locale = "", bool dontDestroyOnLoad = true)
        {
            var host = new GameObject("Helpwing");
            host.SetActive(false);
            var client = host.AddComponent<HelpwingClient>();
            client.apiUrl = apiUrl;
            client.projectKey = projectKey;
            client.locale = locale ?? "";
            client.dontDestroyOnLoad = dontDestroyOnLoad;
            host.SetActive(true);
            client.Begin();
            return client;
        }

        // -- what the game calls ----------------------------------------------------------

        /// <summary>Say who the player is, or null on sign-out. Compared by value, so calling it every frame is harmless.</summary>
        public Task Identify(Identity identity)
        {
            var json = identity == null ? "" : Json.Serialize(identity.ToJson());
            var changed = json != identityJson;
            identityJson = json;
            this.identity = identity;
            // Before Begin the identity is only remembered, and applied once the config has loaded.
            return changed && Chat != null ? Chat.IdentifyAsync(identity) : Task.CompletedTask;
        }

        public Task Send(string text, string email = null) => Chat?.SendAsync(text, email) ?? Task.CompletedTask;

        public Task Retry(string clientMessageId) => Chat?.RetryAsync(clientMessageId) ?? Task.CompletedTask;

        public Task Refresh() => Chat?.RefreshAsync() ?? Task.CompletedTask;

        /// <summary>Forget the player and the conversation on this device. For sign-out on a shared device.</summary>
        public Task ResetConversation()
        {
            identityJson = "";
            identity = null;
            return Chat?.ResetAsync() ?? Task.CompletedTask;
        }

        /// <summary>Show the built-in panel. The visitor is present while it is open.</summary>
        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Chat?.SetPresent(true);
            OpenChanged?.Invoke(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Chat?.SetPresent(false);
            OpenChanged?.Invoke(false);
        }

        // -- lifecycle --------------------------------------------------------------------

        private void Awake()
        {
            if (Instance == null) Instance = this;
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
            HelpwingChat.ListenerError = Debug.LogException;
            deviceDark = DeviceInfo.PrefersDark();
        }

        private void Start()
        {
            Begin();
        }

        /// <summary>Build the chat and load the config. Called from Start; safe to call earlier.</summary>
        public void Begin()
        {
            if (began) return;
            if (string.IsNullOrEmpty(projectKey))
            {
                Debug.LogWarning("[Helpwing] No project key set on HelpwingClient, so there is no chat.", this);
                return;
            }
            began = true;
            scheduler = new FrameScheduler(() => Time.realtimeSinceStartupAsDouble);
            Chat = new HelpwingChat(new ChatOptions
            {
                ApiUrl = apiUrl,
                ProjectKey = projectKey,
                Storage = new PlayerPrefsStorage(),
                Http = new UnityWebRequestHttp(),
                Scheduler = scheduler,
                PollInterval = TimeSpan.FromSeconds(pollIntervalSeconds),
                BackgroundPollInterval = TimeSpan.FromSeconds(backgroundPollIntervalSeconds),
                Locale = Locale,
                Timezone = DeviceInfo.Timezone(),
            });
            Chat.StateChanged += Relay;
            if (IsOpen) Chat.SetPresent(true);
            _ = StartThenIdentify();
        }

        private async Task StartThenIdentify()
        {
            await Chat.StartAsync();
            if (identity != null) await Chat.IdentifyAsync(identity);
        }

        private void Relay(ChatState state)
        {
            StateChanged?.Invoke(state);
        }

        private void Update()
        {
            scheduler?.Tick();
        }

        private void OnApplicationPause(bool paused)
        {
            Chat?.SetActive(!paused);
            if (!paused) RecheckAppearance();
        }

        private void OnApplicationFocus(bool focused)
        {
            // On a phone, losing focus is the notification shade or the app switcher.
            if (Application.isMobilePlatform) Chat?.SetActive(focused);
            if (focused) RecheckAppearance();
        }

        private void RecheckAppearance()
        {
            var dark = DeviceInfo.PrefersDark();
            if (dark == deviceDark) return;
            deviceDark = dark;
            AppearanceChanged?.Invoke();
        }

        private void OnDestroy()
        {
            if (Chat != null)
            {
                Chat.StateChanged -= Relay;
                Chat.Dispose();
            }
            if (Instance == this) Instance = null;
        }
    }
}
