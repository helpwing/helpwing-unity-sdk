using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Helpwing
{
    /// <summary>Where the session is kept between launches. The Unity layer uses PlayerPrefs.</summary>
    public interface IHelpwingStorage
    {
        Task<string> GetItem(string key);
        Task SetItem(string key, string value);
        Task RemoveItem(string key);
    }

    /// <summary>Forgets everything when the process ends. For tests.</summary>
    public sealed class MemoryStorage : IHelpwingStorage
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public Task<string> GetItem(string key)
        {
            return Task.FromResult(values.TryGetValue(key, out var value) ? value : null);
        }

        public Task SetItem(string key, string value)
        {
            values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveItem(string key)
        {
            values.Remove(key);
            return Task.CompletedTask;
        }
    }

    public sealed class PendingMessage
    {
        public string ClientMessageId { get; set; } = "";
        public string Text { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }

    /// <summary>What survives the app being closed. Same JSON shape as the React Native SDK.</summary>
    public sealed class StoredSession
    {
        /// <summary>The only credential this client has. Good for one conversation.</summary>
        public string Token { get; set; } = "";
        public string TicketId { get; set; } = "";
        /// <summary>Who the conversation belongs to, as identify() named them. Empty when anonymous.</summary>
        public string IdentityKey { get; set; } = "";
        public string Cursor { get; set; } = "";
        /// <summary>The server's cursor when the visitor last looked; everything after it is unread.</summary>
        public string LastReadCursor { get; set; } = "";
        /// <summary>Written but not yet acknowledged, on an existing conversation only.</summary>
        public List<PendingMessage> Pending { get; set; } = new List<PendingMessage>();
    }

    /// <summary>One stored session per project key.</summary>
    public sealed class SessionStore
    {
        private readonly IHelpwingStorage storage;
        private readonly string key;

        public SessionStore(IHelpwingStorage storage, string projectKey)
        {
            this.storage = storage;
            key = "helpwing:" + projectKey;
        }

        /// <summary>Anything unreadable is nothing stored: losing a conversation beats refusing to start.</summary>
        public async Task<StoredSession> Read()
        {
            string raw;
            try
            {
                raw = await storage.GetItem(key);
            }
            catch (Exception)
            {
                return null;
            }
            if (string.IsNullOrEmpty(raw)) return null;
            if (!(Json.TryParse(raw) is IDictionary<string, object> data)) return null;
            if (!(data.TryGetValue("token", out var token) && token is string) || !(data.TryGetValue("ticketId", out var ticket) && ticket is string)) return null;

            var session = new StoredSession
            {
                Token = (string)token,
                TicketId = (string)ticket,
                IdentityKey = Json.Str(data, "identityKey"),
                Cursor = Json.Str(data, "cursor"),
                LastReadCursor = Json.Str(data, "lastReadCursor"),
            };
            var pending = Json.List(data, "pending");
            if (pending != null)
            {
                foreach (var item in pending.OfType<IDictionary<string, object>>())
                {
                    if (!(item.TryGetValue("clientMessageId", out var id) && id is string) || !(item.TryGetValue("text", out var text) && text is string)) continue;
                    session.Pending.Add(new PendingMessage { ClientMessageId = (string)id, Text = (string)text, CreatedAt = Json.Str(item, "createdAt") });
                }
            }
            return session;
        }

        public async Task Write(StoredSession session)
        {
            var data = new Dictionary<string, object>
            {
                ["token"] = session.Token,
                ["ticketId"] = session.TicketId,
                ["identityKey"] = session.IdentityKey,
                ["cursor"] = session.Cursor,
                ["lastReadCursor"] = session.LastReadCursor,
                ["pending"] = session.Pending.Select(item => (object)new Dictionary<string, object>
                {
                    ["clientMessageId"] = item.ClientMessageId,
                    ["text"] = item.Text,
                    ["createdAt"] = item.CreatedAt,
                }).ToList(),
            };
            try
            {
                await storage.SetItem(key, Json.Serialize(data));
            }
            catch (Exception)
            {
                // A full disk: the chat still works for this run.
            }
        }

        public async Task Clear()
        {
            try
            {
                await storage.RemoveItem(key);
            }
            catch (Exception)
            {
                // Nothing here is worth failing a conversation over.
            }
        }
    }
}
