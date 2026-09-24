using System;
using System.Collections.Generic;

namespace Helpwing
{
    // The shapes the public widget API returns.
    // Every reader tolerates missing fields: an older server simply sends fewer of them.

    /// <summary><c>GET /widget/{public_key}/config/</c></summary>
    public sealed class WidgetConfig
    {
        public bool IsEnabled { get; set; }
        public string ProjectName { get; set; } = "";
        public string AccentColor { get; set; } = "";
        /// <summary><c>system</c>, <c>light</c> or <c>dark</c>. Missing reads as <c>system</c>.</summary>
        public string ColorScheme { get; set; } = "system";
        public string LauncherIcon { get; set; } = "";
        public string LauncherIconUrl { get; set; } = "";
        public string LauncherPosition { get; set; } = "";
        public string LogoUrl { get; set; } = "";
        public bool ShowBranding { get; set; }
        /// <summary>The heading the project wrote. Blank when it wrote none.</summary>
        public string Title { get; set; } = "";
        public string Greeting { get; set; } = "";
        public string OfflineMessage { get; set; } = "";
        /// <summary>Language code → field (<c>title</c>, <c>greeting</c>, <c>offline_message</c>) → text.</summary>
        public Dictionary<string, Dictionary<string, string>> Translations { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        public string DefaultLocale { get; set; } = "";
        public bool RequireEmail { get; set; }
        public bool ShowAgentAvailability { get; set; }
        public bool ShowOnDesktop { get; set; } = true;
        public bool ShowOnMobile { get; set; } = true;
        public bool IdentityVerificationEnabled { get; set; }
        public bool IsOnline { get; set; } = true;
        /// <summary>Hide the chat outside operating hours rather than show the offline message.</summary>
        public bool HideWhenClosed { get; set; }

        public static WidgetConfig FromJson(IDictionary<string, object> data)
        {
            var config = new WidgetConfig
            {
                IsEnabled = Json.Bool(data, "is_enabled"),
                ProjectName = Json.Str(data, "project_name"),
                AccentColor = Json.Str(data, "accent_color"),
                ColorScheme = Json.Str(data, "color_scheme", "system"),
                LauncherIcon = Json.Str(data, "launcher_icon"),
                LauncherIconUrl = Json.Str(data, "launcher_icon_url"),
                LauncherPosition = Json.Str(data, "launcher_position"),
                LogoUrl = Json.Str(data, "logo_url"),
                ShowBranding = Json.Bool(data, "show_branding"),
                Title = Json.Str(data, "title"),
                Greeting = Json.Str(data, "greeting"),
                OfflineMessage = Json.Str(data, "offline_message"),
                DefaultLocale = Json.Str(data, "default_locale"),
                RequireEmail = Json.Bool(data, "require_email"),
                ShowAgentAvailability = Json.Bool(data, "show_agent_availability"),
                ShowOnDesktop = Json.Bool(data, "show_on_desktop", true),
                ShowOnMobile = Json.Bool(data, "show_on_mobile", true),
                IdentityVerificationEnabled = Json.Bool(data, "identity_verification_enabled"),
                IsOnline = Json.Bool(data, "is_online", true),
                HideWhenClosed = Json.Bool(data, "hide_when_closed"),
            };
            var translations = Json.Obj(data, "translations");
            if (translations != null)
            {
                foreach (var pair in translations)
                {
                    if (!(pair.Value is IDictionary<string, object> fields)) continue;
                    var copy = new Dictionary<string, string>();
                    foreach (var field in fields)
                    {
                        if (field.Value is string text) copy[field.Key] = text;
                    }
                    config.Translations[pair.Key] = copy;
                }
            }
            return config;
        }

        /// <summary>A field by its API name, for <see cref="Copy"/>.</summary>
        public string Field(string name)
        {
            switch (name)
            {
                case "title": return Title;
                case "greeting": return Greeting;
                case "offline_message": return OfflineMessage;
                default: return "";
            }
        }
    }

    public sealed class ApiAttachment
    {
        public string Id { get; set; } = "";
        public string Filename { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public string Url { get; set; } = "";
        /// <summary>Part of what was written (a picture in an email body) rather than sent alongside.</summary>
        public bool IsInline { get; set; }
        /// <summary>What the body points at with <c>![alt](cid:...)</c>.</summary>
        public string ContentId { get; set; } = "";

        public static ApiAttachment FromJson(IDictionary<string, object> data)
        {
            return new ApiAttachment
            {
                Id = Json.Str(data, "id"),
                Filename = Json.Str(data, "filename"),
                ContentType = Json.Str(data, "content_type"),
                SizeBytes = Json.Long(data, "size_bytes"),
                Url = Json.Str(data, "url"),
                IsInline = Json.Bool(data, "is_inline"),
                ContentId = Json.Str(data, "content_id"),
            };
        }
    }

    public sealed class ApiMessage
    {
        public string Id { get; set; } = "";
        /// <summary><c>agent</c>, <c>customer</c> or <c>system</c>.</summary>
        public string Author { get; set; } = "customer";
        public string AuthorName { get; set; } = "";
        public string AuthorAvatarUrl { get; set; } = "";
        public string BodyText { get; set; } = "";
        public string BodyHtml { get; set; } = "";
        public List<ApiAttachment> Attachments { get; set; } = new List<ApiAttachment>();
        public string CreatedAt { get; set; } = "";
        /// <summary>The id this client gave the message, when it sent it with one.</summary>
        public string ClientMessageId { get; set; } = "";

        public static ApiMessage FromJson(IDictionary<string, object> data)
        {
            var message = new ApiMessage
            {
                Id = Json.Str(data, "id"),
                Author = Json.Str(data, "author", "customer"),
                AuthorName = Json.Str(data, "author_name"),
                AuthorAvatarUrl = Json.Str(data, "author_avatar_url"),
                BodyText = Json.Str(data, "body_text"),
                BodyHtml = Json.Str(data, "body_html"),
                CreatedAt = Json.Str(data, "created_at"),
                ClientMessageId = Json.Str(data, "client_message_id"),
            };
            var attachments = Json.List(data, "attachments");
            if (attachments != null)
            {
                foreach (var item in attachments)
                {
                    if (item is IDictionary<string, object> attachment) message.Attachments.Add(ApiAttachment.FromJson(attachment));
                }
            }
            return message;
        }

        internal static List<ApiMessage> ListFrom(IDictionary<string, object> data, string key)
        {
            var messages = new List<ApiMessage>();
            var items = Json.List(data, key);
            if (items == null) return messages;
            foreach (var item in items)
            {
                if (item is IDictionary<string, object> message) messages.Add(FromJson(message));
            }
            return messages;
        }
    }

    /// <summary>An agent writing a reply right now.</summary>
    public sealed class Typing
    {
        public Typing(string name)
        {
            Name = name ?? "";
        }

        public string Name { get; }

        internal static Typing From(IDictionary<string, object> data, string key)
        {
            var typing = Json.Obj(data, key);
            return typing == null ? null : new Typing(Json.Str(typing, "name"));
        }

        public override bool Equals(object obj) => obj is Typing other && other.Name == Name;

        public override int GetHashCode() => Name.GetHashCode();
    }

    public sealed class ApiConversation
    {
        public string TicketId { get; set; } = "";
        public string TicketReference { get; set; } = "";
        public string Status { get; set; } = "";
        public string Subject { get; set; } = "";
        public string VisitorToken { get; set; } = "";
        public List<ApiMessage> Messages { get; set; } = new List<ApiMessage>();
        public Typing Typing { get; set; }
        public string Cursor { get; set; } = "";

        public static ApiConversation FromJson(IDictionary<string, object> data)
        {
            return new ApiConversation
            {
                TicketId = Json.Str(data, "ticket_id"),
                TicketReference = Json.Str(data, "ticket_reference"),
                Status = Json.Str(data, "status"),
                Subject = Json.Str(data, "subject"),
                VisitorToken = Json.Str(data, "visitor_token"),
                Messages = ApiMessage.ListFrom(data, "messages"),
                Typing = Typing.From(data, "typing"),
                Cursor = Json.Str(data, "cursor"),
            };
        }
    }

    public sealed class ApiUpdates
    {
        public string Cursor { get; set; } = "";
        public bool HasChanges { get; set; }
        public string Status { get; set; } = "";
        public List<ApiMessage> Messages { get; set; } = new List<ApiMessage>();
        public Typing Typing { get; set; }

        public static ApiUpdates FromJson(IDictionary<string, object> data)
        {
            return new ApiUpdates
            {
                Cursor = Json.Str(data, "cursor"),
                HasChanges = Json.Bool(data, "has_changes"),
                Status = Json.Str(data, "status"),
                Messages = ApiMessage.ListFrom(data, "messages"),
                Typing = Typing.From(data, "typing"),
            };
        }
    }

    /// <summary>Who the visitor is, spelled as the API spells it.</summary>
    public sealed class Identity
    {
        /// <summary>Your own user id. At most 120 characters.</summary>
        public string Id { get; set; }
        public string Email { get; set; }
        /// <summary>At most 150 characters.</summary>
        public string Name { get; set; }
        /// <summary>Strings, numbers and booleans.</summary>
        public Dictionary<string, object> Metadata { get; set; }
        /// <summary>
        /// Hex HMAC-SHA256 of <see cref="Id"/>, keyed with the project's identity secret.
        /// Compute it on your server; the secret must never ship inside the game.
        /// </summary>
        public string UserHash { get; set; }

        /// <summary>Only the fields that were set, as the server expects them.</summary>
        public Dictionary<string, object> ToJson()
        {
            var data = new Dictionary<string, object>();
            if (Id != null) data["id"] = Id;
            if (Email != null) data["email"] = Email;
            if (Name != null) data["name"] = Name;
            if (Metadata != null) data["metadata"] = Metadata;
            if (UserHash != null) data["userHash"] = UserHash;
            return data;
        }
    }

    public enum MessageAuthor
    {
        Customer,
        Agent,
        System,
    }

    public enum Delivery
    {
        Pending,
        Sent,
        Failed,
    }

    public enum ChatStatus
    {
        Idle,
        /// <summary>The first config fetch, and restoring a stored conversation.</summary>
        Loading,
        Ready,
        /// <summary>No support surface right now: the widget is off, or hidden outside hours.</summary>
        Unconfigured,
        Error,
    }

    /// <summary>A message as the app shows it, including ones that have not reached the server yet.</summary>
    public sealed class ChatMessage
    {
        /// <summary>The server's id once there is one, the client id until then.</summary>
        public string Id { get; internal set; } = "";
        public MessageAuthor Author { get; internal set; }
        public string AuthorName { get; internal set; } = "";
        public string AuthorAvatarUrl { get; internal set; } = "";
        public string Text { get; internal set; } = "";
        /// <summary>Whether <see cref="Text"/> is markdown. False only for old HTML-only messages.</summary>
        public bool Markdown { get; internal set; }
        public IReadOnlyList<ApiAttachment> Attachments { get; internal set; } = Array.Empty<ApiAttachment>();
        public string CreatedAt { get; internal set; } = "";
        /// <summary>Only ever <see cref="Delivery.Sent"/> for anything the server sent.</summary>
        public Delivery Delivery { get; internal set; }
        /// <summary>The id this client made up, kept so a retry is the same message.</summary>
        public string ClientMessageId { get; internal set; }

        internal ChatMessage With(Delivery delivery)
        {
            var copy = (ChatMessage)MemberwiseClone();
            copy.Delivery = delivery;
            return copy;
        }
    }

    public sealed class ConversationInfo
    {
        public ConversationInfo(string ticketId, string ticketReference, string status, string subject)
        {
            TicketId = ticketId;
            TicketReference = ticketReference;
            Status = status;
            Subject = subject;
        }

        public string TicketId { get; }
        public string TicketReference { get; }
        public string Status { get; }
        public string Subject { get; }

        internal ConversationInfo WithStatus(string status) => new ConversationInfo(TicketId, TicketReference, status, Subject);
    }

    /// <summary>Everything true about the chat at one moment. A new instance on every change.</summary>
    public sealed class ChatState
    {
        public ChatStatus Status { get; internal set; } = ChatStatus.Idle;
        public WidgetConfig Config { get; internal set; }
        public ConversationInfo Conversation { get; internal set; }
        public IReadOnlyList<ChatMessage> Messages { get; internal set; } = Array.Empty<ChatMessage>();
        public Typing Typing { get; internal set; }
        /// <summary>Agent messages since the visitor last had the conversation open.</summary>
        public int UnreadCount { get; internal set; }
        /// <summary>True between a failed request and the next one that works.</summary>
        public bool Offline { get; internal set; }
        /// <summary>The last thing that went wrong, in words a person could be shown.</summary>
        public string Error { get; internal set; }

        internal ChatState Clone() => (ChatState)MemberwiseClone();
    }

    public sealed class ChatOptions
    {
        /// <summary>Where the API lives, e.g. <c>https://api.helpwing.app</c> — the host serving <c>/widget.js</c>.</summary>
        public string ApiUrl { get; set; } = "";
        /// <summary>The project's public key, <c>pk_...</c>. Safe to ship in a build.</summary>
        public string ProjectKey { get; set; } = "";
        /// <summary>Where the session is kept between launches. Memory only when left out.</summary>
        public IHelpwingStorage Storage { get; set; }
        /// <summary>Makes the HTTP requests. Required: the Unity layer passes UnityWebRequest.</summary>
        public ITransportHttp Http { get; set; }
        /// <summary>Runs the polling timer. Defaults to Task.Delay on the calling sync context.</summary>
        public IScheduler Scheduler { get; set; }
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
        /// <summary>While the chat is closed but the app is open. Zero stops background polling.</summary>
        public TimeSpan BackgroundPollInterval { get; set; } = TimeSpan.FromSeconds(30);
        public Func<string> Uuid { get; set; }
        /// <summary>Sent when a conversation is opened, and used to pick the project's translated copy.</summary>
        public string Locale { get; set; } = "";
        public string Timezone { get; set; } = "";
    }
}
