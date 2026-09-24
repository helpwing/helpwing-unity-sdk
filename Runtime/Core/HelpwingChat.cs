using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Helpwing
{
    /// <summary>
    /// The conversation, with no opinion about how it is drawn. A port of the React Native SDK's client:
    /// it polls, retries sends on an open conversation but never a start, and claims presence while shown.
    /// </summary>
    public sealed class HelpwingChat : IDisposable
    {
        /// <summary>Where an exception thrown by a StateChanged handler goes. The Unity layer logs it.</summary>
        public static Action<Exception> ListenerError = _ => { };

        private readonly Transport transport;
        private readonly SessionStore store;
        private readonly IScheduler scheduler;
        private readonly TimeSpan pollInterval;
        private readonly TimeSpan backgroundPollInterval;
        private readonly Func<string> uuid;
        private readonly string locale;
        private readonly string timezone;

        private ChatState current = new ChatState();
        private StoredSession session;
        private Identity identity;
        private bool present;
        private bool active = true;
        private IDisposable timer;
        private Task inFlight;
        private bool flushing;
        private bool disposed;

        public HelpwingChat(ChatOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            transport = new Transport(options.ApiUrl, options.ProjectKey, options.Http);
            store = new SessionStore(options.Storage ?? new MemoryStorage(), options.ProjectKey);
            scheduler = options.Scheduler ?? new DelayScheduler();
            pollInterval = options.PollInterval;
            backgroundPollInterval = options.BackgroundPollInterval;
            uuid = options.Uuid ?? (() => Guid.NewGuid().ToString());
            locale = options.Locale ?? "";
            timezone = options.Timezone ?? "";
        }

        /// <summary>Raised on every change, with the new state.</summary>
        public event Action<ChatState> StateChanged;

        public ChatState State => current;

        /// <summary>Called immediately with the state, then on every change. Dispose to stop.</summary>
        public IDisposable Subscribe(Action<ChatState> listener)
        {
            StateChanged += listener;
            listener(current);
            return new Unsubscribe(() => StateChanged -= listener);
        }

        // -- what anyone holding this can do ----------------------------------------------

        /// <summary>Load the config and any stored conversation. Safe to call again. Never throws.</summary>
        public async Task StartAsync()
        {
            Patch(s =>
            {
                s.Status = s.Config != null ? s.Status : ChatStatus.Loading;
                s.Error = null;
            });
            try
            {
                var config = await transport.Config();
                Patch(s =>
                {
                    s.Config = config;
                    s.Offline = false;
                });
                if (!config.IsEnabled || (!config.IsOnline && config.HideWhenClosed))
                {
                    Patch(s => s.Status = ChatStatus.Unconfigured);
                    return;
                }
            }
            catch (Exception error)
            {
                Fail(error);
                return;
            }

            session = await store.Read();
            if (session != null) await Restore();
            Patch(s => s.Status = ChatStatus.Ready);
            Schedule();
        }

        /// <summary>
        /// Say who the visitor is, now or later. Naming somebody else drops the stored conversation;
        /// <c>null</c> stops the next one being attributed to whoever left. Never throws.
        /// </summary>
        public async Task IdentifyAsync(Identity next)
        {
            var key = IdentityKey(next);
            if (key.Length > 0 && session != null && session.IdentityKey.Length > 0 && key != session.IdentityKey)
            {
                await DropConversation();
            }
            identity = next;
            if (next == null || session == null) return;
            try
            {
                await transport.Identify(session.TicketId, session.Token, next);
                session.IdentityKey = key;
                await Persist();
            }
            catch (HelpwingException error) when (error.Status == 409)
            {
                await DropConversation();
            }
            catch (Exception error)
            {
                // Never surfaced: a chat that errors because it could not attach a name is worse than one that retries later.
                Note(error);
            }
        }

        /// <summary>Send a message, drawing it before it has gone anywhere. <paramref name="email"/> is only read when opening.</summary>
        public async Task SendAsync(string text, string email = null)
        {
            var body = (text ?? "").Trim();
            if (body.Length == 0) return;

            var pending = new PendingMessage { ClientMessageId = uuid(), Text = body, CreatedAt = Now() };
            Patch(s =>
            {
                s.Messages = Append(s.Messages, Draw(pending));
                s.Error = null;
            });

            if (session == null)
            {
                await Open(pending, email ?? "");
                return;
            }
            session.Pending.Add(pending);
            await Persist();
            await Flush();
        }

        /// <summary>Try a failed message again. Free on an open conversation; a failed start may open a second one.</summary>
        public async Task RetryAsync(string clientMessageId)
        {
            var failed = current.Messages.FirstOrDefault(m => m.ClientMessageId == clientMessageId && m.Delivery == Delivery.Failed);
            if (failed == null) return;

            var pending = new PendingMessage { ClientMessageId = failed.ClientMessageId, Text = failed.Text, CreatedAt = failed.CreatedAt };
            Replace(clientMessageId, failed.With(Delivery.Pending));

            if (session == null)
            {
                await Open(pending, "");
                return;
            }
            if (!session.Pending.Any(item => item.ClientMessageId == clientMessageId))
            {
                session.Pending.Add(pending);
                await Persist();
            }
            await Flush();
        }

        /// <summary>Whether the visitor is looking at the conversation: sets the poll rate and stops replies being emailed.</summary>
        public void SetPresent(bool value)
        {
            if (present == value) return;
            present = value;
            if (value)
            {
                MarkRead();
                _ = Tick();
            }
            else
            {
                Schedule();
            }
        }

        /// <summary>Whether the app is in the foreground. Polling stops entirely when it is not.</summary>
        public void SetActive(bool value)
        {
            if (active == value) return;
            active = value;
            if (value) _ = Tick();
            else ClearTimer();
        }

        /// <summary>Mark everything on screen as seen, clearing the unread badge.</summary>
        public void MarkRead()
        {
            if (session != null)
            {
                session.LastReadCursor = session.Cursor;
                _ = Persist();
            }
            if (current.UnreadCount != 0) Patch(s => s.UnreadCount = 0);
        }

        /// <summary>Ask for new messages now. Completes after a request that started no earlier than this call.</summary>
        public async Task RefreshAsync()
        {
            if (inFlight != null) await inFlight;
            await Tick();
        }

        /// <summary>Forget the visitor and the conversation on this device. For sign-out.</summary>
        public async Task ResetAsync()
        {
            identity = null;
            await DropConversation();
        }

        /// <summary>Stop everything. The object is not usable afterwards.</summary>
        public void Dispose()
        {
            disposed = true;
            ClearTimer();
            StateChanged = null;
        }

        // -- the conversation -------------------------------------------------------------

        private async Task DropConversation()
        {
            ClearTimer();
            session = null;
            await store.Clear();
            Patch(s =>
            {
                s.Conversation = null;
                s.Messages = Array.Empty<ChatMessage>();
                s.Typing = null;
                s.UnreadCount = 0;
                s.Error = null;
            });
            Schedule();
        }

        private async Task Restore()
        {
            var restoring = session;
            if (restoring == null) return;
            try
            {
                var conversation = await transport.Conversation(restoring.TicketId, restoring.Token);
                restoring.Cursor = conversation.Cursor;
                await Persist();
                Adopt(conversation, true);
                await Flush();
            }
            catch (Exception error)
            {
                Handle(error);
            }
        }

        private async Task Open(PendingMessage pending, string email)
        {
            try
            {
                var conversation = await transport.Start(pending.Text, identity, email, locale, timezone);
                session = new StoredSession
                {
                    Token = conversation.VisitorToken,
                    TicketId = conversation.TicketId,
                    IdentityKey = IdentityKey(identity),
                    Cursor = conversation.Cursor,
                    // Read up to here by definition: the only message is the one just written.
                    LastReadCursor = conversation.Cursor,
                };
                await Persist();
                // The start endpoint takes no client id, so the early copy is dropped by hand.
                Forget(pending.ClientMessageId);
                Adopt(conversation, true);
                Patch(s => s.Offline = false);
                Schedule();
            }
            catch (Exception error)
            {
                Replace(pending.ClientMessageId, Draw(pending).With(Delivery.Failed));
                Fail(error, true);
            }
        }

        /// <summary>Send what is queued, oldest first, stopping at the first that cannot go.</summary>
        private async Task Flush()
        {
            var draining = session;
            if (draining == null || flushing) return;
            flushing = true;
            try
            {
                while (draining.Pending.Count > 0)
                {
                    var next = draining.Pending[0];
                    try
                    {
                        var conversation = await transport.Send(draining.TicketId, draining.Token, next.Text, next.ClientMessageId);
                        draining.Pending.RemoveAll(item => item.ClientMessageId == next.ClientMessageId);
                        await Persist();
                        Forget(next.ClientMessageId);
                        Adopt(conversation, false);
                        Patch(s => s.Offline = false);
                    }
                    catch (Exception error)
                    {
                        if (HandleSendFailure(error, next)) break;
                    }
                }
            }
            finally
            {
                flushing = false;
            }
        }

        /// <summary>A network error keeps the message queued; a refusal marks it failed. Returns whether to stop.</summary>
        private bool HandleSendFailure(Exception error, PendingMessage message)
        {
            var failure = error as HelpwingException;
            if (failure != null && failure.IsGone)
            {
                Handle(error);
                return true;
            }
            if (failure != null && failure.IsNetwork)
            {
                Patch(s => s.Offline = true);
                return true;
            }
            if (session != null)
            {
                session.Pending.RemoveAll(item => item.ClientMessageId == message.ClientMessageId);
                _ = Persist();
            }
            Replace(message.ClientMessageId, Draw(message).With(Delivery.Failed));
            Fail(error, true);
            return false;
        }

        // -- polling ----------------------------------------------------------------------

        private void Schedule()
        {
            ClearTimer();
            if (disposed || session == null || !active) return;
            var interval = present ? pollInterval : backgroundPollInterval;
            if (interval <= TimeSpan.Zero) return;
            timer = scheduler.Schedule(interval, () => { _ = Tick(); });
        }

        /// <summary>One round: send what is waiting, then ask what has arrived. Never two at once.</summary>
        private Task Tick()
        {
            if (disposed || inFlight != null || session == null || !active) return inFlight ?? Task.CompletedTask;
            var round = Round();
            // A round that finished synchronously has already cleared itself.
            if (!round.IsCompleted) inFlight = round;
            return round;
        }

        private async Task Round()
        {
            try
            {
                await Flush();
                await Poll();
            }
            finally
            {
                inFlight = null;
                Schedule();
            }
        }

        private async Task Poll()
        {
            var polling = session;
            if (polling == null) return;
            try
            {
                var updates = await transport.Updates(polling.TicketId, polling.Token, polling.Cursor, present);
                polling.Cursor = updates.Cursor;
                await Persist();
                Merge(updates.Messages, true, updates.Typing, updates.Status);
                if (current.Offline) Patch(s => s.Offline = false);
            }
            catch (Exception error)
            {
                Handle(error);
            }
        }

        // -- state ------------------------------------------------------------------------

        private void Adopt(ApiConversation conversation, bool replaceTranscript)
        {
            Patch(s => s.Conversation = new ConversationInfo(conversation.TicketId, conversation.TicketReference, conversation.Status, conversation.Subject));
            if (replaceTranscript)
            {
                // Whatever is still trying to be sent survives: the server has never heard of it.
                Patch(s => s.Messages = s.Messages.Where(m => m.Delivery != Delivery.Sent).ToList());
            }
            Merge(conversation.Messages, true, conversation.Typing, conversation.Status);
        }

        /// <summary>Fold server messages into the transcript, replacing any copy drawn early.</summary>
        private void Merge(IList<ApiMessage> incoming, bool setTyping, Typing typing, string status)
        {
            var order = new List<string>();
            var byId = new Dictionary<string, ChatMessage>();
            foreach (var message in current.Messages)
            {
                if (!byId.ContainsKey(message.Id)) order.Add(message.Id);
                byId[message.Id] = message;
            }
            var drawnEarly = new HashSet<string>();
            foreach (var message in incoming)
            {
                if (!string.IsNullOrEmpty(message.ClientMessageId)) drawnEarly.Add(message.ClientMessageId);
                if (!byId.ContainsKey(message.Id)) order.Add(message.Id);
                byId[message.Id] = Received(message);
            }

            var messages = order
                .Select(id => byId[id])
                .Where(m => !(m.ClientMessageId != null && drawnEarly.Contains(m.ClientMessageId) && m.Delivery != Delivery.Sent))
                .OrderBy(m => m.CreatedAt, StringComparer.Ordinal)
                .ToList();

            Patch(s =>
            {
                s.Messages = messages;
                if (setTyping) s.Typing = typing;
                if (!string.IsNullOrEmpty(status) && s.Conversation != null) s.Conversation = s.Conversation.WithStatus(status);
            });
            Recount();
        }

        private void Forget(string clientMessageId)
        {
            Patch(s => s.Messages = s.Messages.Where(m => m.ClientMessageId != clientMessageId || m.Delivery == Delivery.Sent).ToList());
        }

        private void Replace(string clientMessageId, ChatMessage message)
        {
            Patch(s => s.Messages = s.Messages
                .Select(m => m.ClientMessageId == clientMessageId && m.Delivery != Delivery.Sent ? message : m)
                .ToList());
        }

        /// <summary>Agent messages after the last-read cursor. Zero while the visitor is looking.</summary>
        private void Recount()
        {
            if (present)
            {
                MarkRead();
                return;
            }
            var since = session?.LastReadCursor ?? "";
            var unread = current.Messages.Count(m =>
                m.Author == MessageAuthor.Agent && m.Delivery == Delivery.Sent && string.CompareOrdinal(m.CreatedAt, since) > 0);
            if (unread != current.UnreadCount) Patch(s => s.UnreadCount = unread);
        }

        private void Patch(Action<ChatState> change)
        {
            var next = current.Clone();
            change(next);
            current = next;
            var handlers = StateChanged;
            if (handlers == null) return;
            foreach (Action<ChatState> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(next);
                }
                catch (Exception error)
                {
                    ListenerError(error);
                }
            }
        }

        private Task Persist()
        {
            return session != null ? store.Write(session) : Task.CompletedTask;
        }

        // -- failure ----------------------------------------------------------------------

        /// <summary>A conversation the server no longer knows is dropped; anything else is noted.</summary>
        private void Handle(Exception error)
        {
            if (error is HelpwingException failure && failure.IsGone)
            {
                session = null;
                _ = store.Clear();
                ClearTimer();
                Patch(s =>
                {
                    s.Conversation = null;
                    s.Messages = Array.Empty<ChatMessage>();
                    s.Typing = null;
                    s.UnreadCount = 0;
                });
                return;
            }
            Note(error);
        }

        private void Note(Exception error)
        {
            if (error is HelpwingException failure && failure.IsNetwork)
            {
                if (!current.Offline) Patch(s => s.Offline = true);
                return;
            }
            var message = string.IsNullOrEmpty(error?.Message) ? "Something went wrong." : error.Message;
            Patch(s => s.Error = message);
        }

        private void Fail(Exception error, bool keepStatus = false)
        {
            Note(error);
            if (!keepStatus && current.Status != ChatStatus.Ready) Patch(s => s.Status = ChatStatus.Error);
        }

        private void ClearTimer()
        {
            timer?.Dispose();
            timer = null;
        }

        // -- plumbing ---------------------------------------------------------------------

        /// <summary>Who an identity names, as one comparable string. The id when there is one.</summary>
        private static string IdentityKey(Identity who)
        {
            var id = (who?.Id ?? "").Trim();
            if (id.Length > 0) return "id:" + id;
            var email = (who?.Email ?? "").Trim().ToLowerInvariant();
            return email.Length > 0 ? "email:" + email : "";
        }

        private static IReadOnlyList<ChatMessage> Append(IReadOnlyList<ChatMessage> messages, ChatMessage message)
        {
            var list = messages.ToList();
            list.Add(message);
            return list;
        }

        private static string Now()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static ChatMessage Draw(PendingMessage pending)
        {
            return new ChatMessage
            {
                Id = pending.ClientMessageId,
                Author = MessageAuthor.Customer,
                Text = pending.Text,
                Markdown = true,
                CreatedAt = pending.CreatedAt,
                Delivery = Delivery.Pending,
                ClientMessageId = pending.ClientMessageId,
            };
        }

        private static ChatMessage Received(ApiMessage message)
        {
            return new ChatMessage
            {
                Id = message.Id,
                Author = AuthorOf(message.Author),
                AuthorName = message.AuthorName,
                AuthorAvatarUrl = message.AuthorAvatarUrl,
                Text = message.BodyText.Length > 0 ? message.BodyText : TextOf(message.BodyHtml),
                Markdown = message.BodyText.Length > 0,
                Attachments = message.Attachments,
                CreatedAt = message.CreatedAt,
                Delivery = Delivery.Sent,
                ClientMessageId = string.IsNullOrEmpty(message.ClientMessageId) ? null : message.ClientMessageId,
            };
        }

        private static MessageAuthor AuthorOf(string author)
        {
            switch (author)
            {
                case "agent": return MessageAuthor.Agent;
                case "system": return MessageAuthor.System;
                default: return MessageAuthor.Customer;
            }
        }

        private static readonly Regex LineBreak = new Regex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase);
        private static readonly Regex BlockEnd = new Regex(@"<\s*/\s*(p|div|li|tr|h[1-6])\s*>", RegexOptions.IgnoreCase);
        private static readonly Regex AnyTag = new Regex(@"<[^>]*>");
        private static readonly Regex ManyNewlines = new Regex(@"\n{3,}");

        /// <summary>Old HTML-only messages, flattened crudely: block tags become breaks, the rest is dropped.</summary>
        public static string TextOf(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var text = LineBreak.Replace(html, "\n");
            text = BlockEnd.Replace(text, "\n");
            text = AnyTag.Replace(text, "");
            text = Regex.Replace(text, "&nbsp;", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "&lt;", "<", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "&gt;", ">", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "&quot;", "\"", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "&#39;", "'", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "&amp;", "&", RegexOptions.IgnoreCase);
            return ManyNewlines.Replace(text, "\n\n").Trim();
        }

        private sealed class Unsubscribe : IDisposable
        {
            private Action action;

            public Unsubscribe(Action action)
            {
                this.action = action;
            }

            public void Dispose()
            {
                action?.Invoke();
                action = null;
            }
        }
    }
}
