using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Helpwing
{
    public sealed class HttpRequestData
    {
        public string Method { get; set; } = "GET";
        public string Url { get; set; } = "";
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();
        /// <summary>JSON, or null for no body.</summary>
        public string Body { get; set; }
    }

    public sealed class HttpResponseData
    {
        public HttpResponseData(int status, string body)
        {
            Status = status;
            Body = body ?? "";
        }

        public int Status { get; }
        public string Body { get; }
    }

    /// <summary>
    /// Makes one HTTP request. Throw on a network failure (no answer at all);
    /// return any answer the server gave, whatever its status.
    /// </summary>
    public interface ITransportHttp
    {
        Task<HttpResponseData> SendAsync(HttpRequestData request);
    }

    /// <summary>A request that came back with an answer we did not want. Status 0 is no answer at all.</summary>
    public sealed class HelpwingException : Exception
    {
        public const int Network = 0;

        public HelpwingException(string message, int status, object payload = null) : base(message)
        {
            Status = status;
            Payload = payload;
        }

        public int Status { get; }
        public object Payload { get; }

        /// <summary>Airplane mode, a dead tunnel, a captive portal.</summary>
        public bool IsNetwork => Status == Network;

        /// <summary>The visitor token names nothing any more.</summary>
        public bool IsGone => Status == 404;
    }

    /// <summary>Every public widget endpoint, and nothing else.</summary>
    public sealed class Transport
    {
        /// <summary>The header the visitor token travels in.</summary>
        public const string VisitorHeader = "X-Helpwing-Visitor";

        private readonly string root;
        private readonly ITransportHttp http;

        public Transport(string apiUrl, string projectKey, ITransportHttp http)
        {
            this.http = http ?? throw new ArgumentNullException(nameof(http), "Pass an ITransportHttp in ChatOptions.Http.");
            root = (apiUrl ?? "").TrimEnd('/') + "/widget/" + Uri.EscapeDataString(projectKey ?? "");
        }

        public async Task<WidgetConfig> Config()
        {
            return WidgetConfig.FromJson(await Request("GET", "/config/"));
        }

        /// <summary>Open a conversation with its first message. Carries no idempotency key: the server accepts none.</summary>
        public async Task<ApiConversation> Start(string bodyText, Identity identity, string email, string locale, string timezone)
        {
            var body = new Dictionary<string, object> { ["body_text"] = bodyText };
            if (identity != null) body["identity"] = identity.ToJson();
            if (!string.IsNullOrEmpty(email)) body["email"] = email;
            if (!string.IsNullOrEmpty(locale)) body["locale"] = locale;
            if (!string.IsNullOrEmpty(timezone)) body["timezone"] = timezone;
            return ApiConversation.FromJson(await Request("POST", "/conversations/", null, body));
        }

        public async Task<ApiConversation> Conversation(string ticketId, string token)
        {
            return ApiConversation.FromJson(await Request("GET", "/conversations/" + ticketId + "/", token));
        }

        /// <summary>Idempotent by <paramref name="clientMessageId"/>: a retry stores nothing twice.</summary>
        public async Task<ApiConversation> Send(string ticketId, string token, string bodyText, string clientMessageId)
        {
            var body = new Dictionary<string, object> { ["body_text"] = bodyText, ["client_message_id"] = clientMessageId };
            return ApiConversation.FromJson(await Request("POST", "/conversations/" + ticketId + "/messages/", token, body));
        }

        public Task Identify(string ticketId, string token, Identity identity)
        {
            return Request("POST", "/conversations/" + ticketId + "/identify/", token, identity.ToJson());
        }

        /// <summary>What happened since <paramref name="since"/>. <paramref name="present"/> says the visitor is reading.</summary>
        public async Task<ApiUpdates> Updates(string ticketId, string token, string since, bool present)
        {
            var query = "?since=" + Uri.EscapeDataString(since ?? "") + (present ? "&present=1" : "");
            return ApiUpdates.FromJson(await Request("GET", "/conversations/" + ticketId + "/updates/" + query, token));
        }

        private async Task<IDictionary<string, object>> Request(string method, string path, string token = null, object body = null)
        {
            var request = new HttpRequestData { Method = method, Url = root + path };
            if (body != null)
            {
                request.Headers["Content-Type"] = "application/json";
                request.Body = Json.Serialize(body);
            }
            if (!string.IsNullOrEmpty(token)) request.Headers[VisitorHeader] = token;

            HttpResponseData response;
            try
            {
                response = await http.SendAsync(request);
            }
            catch (Exception error)
            {
                throw new HelpwingException(string.IsNullOrEmpty(error.Message) ? "The network request failed." : error.Message, HelpwingException.Network);
            }
            if (response == null || response.Status == 0)
            {
                throw new HelpwingException("The network request failed.", HelpwingException.Network);
            }

            var data = response.Status == 204 ? null : Json.TryParse(response.Body) as IDictionary<string, object>;
            if (response.Status < 200 || response.Status >= 300)
            {
                throw new HelpwingException(MessageIn(data) ?? "HTTP " + response.Status, response.Status, data);
            }
            return data ?? new Dictionary<string, object>();
        }

        /// <summary>The API's error envelope, <c>{ error: { message } }</c>.</summary>
        private static string MessageIn(IDictionary<string, object> data)
        {
            var message = Json.Str(Json.Obj(data, "error"), "message");
            return string.IsNullOrEmpty(message) ? null : message;
        }
    }
}
