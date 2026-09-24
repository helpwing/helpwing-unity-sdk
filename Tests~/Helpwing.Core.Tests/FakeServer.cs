using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Helpwing.Tests
{
    /// <summary>A stand-in for the public widget API.</summary>
    public sealed class FakeServer : ITransportHttp
    {
        public sealed class Call
        {
            public string Method = "";
            public string Path = "";
            public string Token;
            public IDictionary<string, object> Body;
        }

        public readonly List<Call> Calls = new List<Call>();
        public Dictionary<string, object> Config = DefaultConfig();
        public readonly List<Dictionary<string, object>> Messages = new List<Dictionary<string, object>>();
        public string Token = "visitor-token-1";
        public string TicketId = "ticket-1";
        public Dictionary<string, object> Typing;

        /// <summary>"network" throws; an int answers with that status. Next request only.</summary>
        public object FailNext;
        /// <summary>As <see cref="FailNext"/>, for every request until cleared.</summary>
        public object FailAll;
        public bool ConversationGone;

        private int clock;

        public static Dictionary<string, object> DefaultConfig()
        {
            return new Dictionary<string, object>
            {
                ["is_enabled"] = true,
                ["project_name"] = "Acme Cloud",
                ["accent_color"] = "#2563eb",
                ["launcher_icon"] = "chat",
                ["launcher_icon_url"] = "",
                ["launcher_position"] = "bottom_right",
                ["logo_url"] = "",
                ["show_branding"] = true,
                ["greeting"] = "Hi! How can we help?",
                ["offline_message"] = "We are offline right now.",
                ["require_email"] = false,
                ["show_agent_availability"] = true,
                ["show_on_desktop"] = true,
                ["show_on_mobile"] = true,
                ["identity_verification_enabled"] = false,
                ["is_online"] = true,
            };
        }

        public Task<HttpResponseData> SendAsync(HttpRequestData request)
        {
            var uri = new Uri(request.Url);
            request.Headers.TryGetValue(Transport.VisitorHeader, out var token);
            var body = request.Body == null ? null : Json.Parse(request.Body) as IDictionary<string, object>;
            Calls.Add(new Call { Method = request.Method, Path = uri.AbsolutePath + uri.Query, Token = token, Body = body });

            var failure = FailNext ?? FailAll;
            FailNext = null;
            if (failure is string) throw new HttpRequestException("Network request failed");
            if (failure is int status) return Answer(status, new Dictionary<string, object> { ["error"] = new Dictionary<string, object> { ["message"] = "Refused by the fake server." } });

            return Route(uri, request.Method, body);
        }

        /// <summary>An agent replies.</summary>
        public Dictionary<string, object> Reply(string text, string name = "Ada")
        {
            var message = Message("agent", text, name: name);
            Messages.Add(message);
            return message;
        }

        public IEnumerable<string> Bodies => Messages.Select(m => (string)m["body_text"]);

        private Task<HttpResponseData> Route(Uri uri, string method, IDictionary<string, object> body)
        {
            var path = uri.AbsolutePath;
            if (path.EndsWith("/config/")) return Answer(200, Config);

            if (ConversationGone && path.Contains("/conversations/") && !path.EndsWith("/conversations/"))
            {
                return Answer(404, new Dictionary<string, object> { ["error"] = new Dictionary<string, object> { ["message"] = "No conversation was found for that visitor token." } });
            }

            if (path.EndsWith("/conversations/") && method == "POST")
            {
                Messages.Add(Message("customer", Json.Str(body, "body_text")));
                return Answer(201, Conversation(Token, new List<Dictionary<string, object>> { Messages.Last() }));
            }

            if (path.EndsWith("/messages/") && method == "POST")
            {
                var clientMessageId = Json.Str(body, "client_message_id");
                var already = Messages.FirstOrDefault(m => clientMessageId.Length > 0 && (string)m["client_message_id"] == clientMessageId);
                var stored = already ?? Message("customer", Json.Str(body, "body_text"), clientMessageId);
                if (already == null) Messages.Add(stored);
                return Answer(201, Conversation("", new List<Dictionary<string, object>> { stored }));
            }

            if (path.EndsWith("/identify/")) return Answer(200, new Dictionary<string, object> { ["customer_id"] = "c1", ["is_identity_verified"] = false });

            if (path.EndsWith("/updates/"))
            {
                var since = Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).Where(p => p[0] == "since").Select(p => p.Length > 1 ? p[1] : "").FirstOrDefault() ?? "");
                var fresh = Messages.Where(m => string.CompareOrdinal((string)m["created_at"], since) > 0).ToList();
                return Answer(200, new Dictionary<string, object>
                {
                    ["cursor"] = Now(),
                    ["has_changes"] = fresh.Count > 0,
                    ["status"] = "open",
                    ["messages"] = fresh,
                    ["typing"] = Typing,
                });
            }

            if (method == "GET") return Answer(200, Conversation("", Messages));
            return Answer(404, new Dictionary<string, object> { ["error"] = new Dictionary<string, object> { ["message"] = "No such thing." } });
        }

        private Dictionary<string, object> Conversation(string token, List<Dictionary<string, object>> messages)
        {
            return new Dictionary<string, object>
            {
                ["ticket_id"] = TicketId,
                ["ticket_reference"] = "k7m2p9qx3r",
                ["status"] = "open",
                ["subject"] = "The widget throws a CSP error",
                ["visitor_token"] = token,
                ["messages"] = messages.ToList(),
                ["typing"] = Typing,
                ["cursor"] = Now(),
            };
        }

        public Dictionary<string, object> Message(string author, string text, string clientMessageId = "", string name = "", string html = "")
        {
            return new Dictionary<string, object>
            {
                ["id"] = "m" + (Messages.Count + 1),
                ["author"] = author,
                ["author_name"] = name,
                ["author_avatar_url"] = "",
                ["body_text"] = text,
                ["body_html"] = html,
                ["attachments"] = new List<object>(),
                ["created_at"] = Now(),
                ["client_message_id"] = clientMessageId,
            };
        }

        /// <summary>A clock that only goes forwards, so cursors order the way real timestamps do.</summary>
        public string Now()
        {
            clock += 1;
            return new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc).AddSeconds(clock).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static Task<HttpResponseData> Answer(int status, object payload)
        {
            return Task.FromResult(new HttpResponseData(status, Json.Serialize(payload)));
        }
    }
}
