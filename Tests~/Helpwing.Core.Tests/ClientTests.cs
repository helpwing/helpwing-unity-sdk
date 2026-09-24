using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Helpwing.Tests
{
    public class ClientTests
    {
        private const string Project = "pk_test";
        private const string StorageKey = "helpwing:" + Project;

        private FakeServer server;
        private MemoryStorage storage;

        [SetUp]
        public void SetUp()
        {
            server = new FakeServer();
            storage = new MemoryStorage();
        }

        private HelpwingChat Chat()
        {
            return new HelpwingChat(new ChatOptions
            {
                ApiUrl = "https://api.helpwing.test",
                ProjectKey = Project,
                Storage = storage,
                Http = server,
                // Long enough that no timer fires during a test; every poll here is asked for.
                PollInterval = TimeSpan.FromMinutes(1),
                BackgroundPollInterval = TimeSpan.FromMinutes(1),
            });
        }

        private IDictionary<string, object> Stored()
        {
            var raw = storage.GetItem(StorageKey).Result;
            return raw == null ? null : (IDictionary<string, object>)Json.Parse(raw);
        }

        private List<string> Texts(HelpwingChat chat) => chat.State.Messages.Select(m => m.Text).ToList();

        private List<Delivery> Deliveries(HelpwingChat chat) => chat.State.Messages.Select(m => m.Delivery).ToList();

        private FakeServer.Call StartCall() => server.Calls.First(c => c.Method == "POST" && c.Path.EndsWith("/conversations/"));

        // -- starting up ------------------------------------------------------------------

        [Test]
        public async Task IsReadyOnceTheConfigIsLoaded()
        {
            var chat = Chat();
            await chat.StartAsync();

            Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Ready));
            Assert.That(chat.State.Config.ProjectName, Is.EqualTo("Acme Cloud"));
            Assert.That(chat.State.Conversation, Is.Null);
        }

        [Test]
        public async Task TreatsAWidgetTurnedOffAsNoSupport()
        {
            server.Config["is_enabled"] = false;
            var chat = Chat();
            await chat.StartAsync();

            Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Unconfigured));
            Assert.That(chat.State.Error, Is.Null);
        }

        [Test]
        public async Task TreatsAProjectHidingOutsideHoursTheSameWay()
        {
            server.Config["is_online"] = false;
            server.Config["hide_when_closed"] = true;
            var chat = Chat();
            await chat.StartAsync();

            Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Unconfigured));
        }

        [Test]
        public async Task KeepsTheChatUpWhileClosedWhenNotAskedToHide()
        {
            server.Config["is_online"] = false;
            server.Config["hide_when_closed"] = false;
            var chat = Chat();
            await chat.StartAsync();

            Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Ready));
        }

        [Test]
        public async Task ReportsBeingUnreachableWithoutThrowing()
        {
            server.FailAll = "network";
            var chat = Chat();
            await chat.StartAsync();

            Assert.That(chat.State.Offline, Is.True);
            Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Error));
        }

        [Test]
        public async Task ReadsAColorSchemeAndMissingFieldsTolerantly()
        {
            server.Config["color_scheme"] = "dark";
            server.Config["unknown_future_field"] = new List<object> { 1, 2 };
            var chat = Chat();
            await chat.StartAsync();

            Assert.That(chat.State.Config.ColorScheme, Is.EqualTo("dark"));
            Assert.That(chat.State.Config.Title, Is.EqualTo(""));
            Assert.That(chat.State.Config.HideWhenClosed, Is.False);
        }

        // -- the first message ------------------------------------------------------------

        [Test]
        public async Task OpensAConversationAndKeepsTheToken()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("The widget throws a CSP error.");

            Assert.That(chat.State.Conversation.TicketId, Is.EqualTo("ticket-1"));
            Assert.That(Texts(chat), Is.EqualTo(new[] { "The widget throws a CSP error." }));
            Assert.That(chat.State.Messages[0].Delivery, Is.EqualTo(Delivery.Sent));

            var stored = Stored();
            Assert.That(stored["token"], Is.EqualTo("visitor-token-1"));
            Assert.That(stored["ticketId"], Is.EqualTo("ticket-1"));
        }

        [Test]
        public async Task StartCarriesNoIdempotencyKey()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");

            Assert.That(StartCall().Body.ContainsKey("client_message_id"), Is.False);
        }

        [Test]
        public async Task AFailedStartIsShownAsFailedNotResent()
        {
            var chat = Chat();
            await chat.StartAsync();
            server.FailNext = "network";
            await chat.SendAsync("Hello");

            Assert.That(chat.State.Messages, Has.Count.EqualTo(1));
            Assert.That(chat.State.Messages[0].Delivery, Is.EqualTo(Delivery.Failed));
            Assert.That(chat.State.Conversation, Is.Null);
            Assert.That(chat.State.Offline, Is.True);
            Assert.That(Stored(), Is.Null);
        }

        [Test]
        public async Task AFailedStartGoesOutOnARetryTheVisitorAskedFor()
        {
            var chat = Chat();
            await chat.StartAsync();
            server.FailNext = "network";
            await chat.SendAsync("Hello");

            await chat.RetryAsync(chat.State.Messages[0].ClientMessageId);

            Assert.That(chat.State.Conversation.TicketId, Is.EqualTo("ticket-1"));
            Assert.That(Deliveries(chat), Is.EqualTo(new[] { Delivery.Sent }));
        }

        [Test]
        public async Task PassesTheAddressTheProjectAskedFor()
        {
            server.Config["require_email"] = true;
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello", "kim@shop.example.com");

            Assert.That(StartCall().Body["email"], Is.EqualTo("kim@shop.example.com"));
        }

        [Test]
        public async Task IgnoresABlankMessage()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("   ");

            Assert.That(chat.State.Messages, Is.Empty);
            Assert.That(server.Calls.Count(c => c.Method == "POST"), Is.Zero);
        }

        // -- sending on a conversation that exists ----------------------------------------

        [Test]
        public async Task AMessageSentTwiceIsStoredOnce()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("First");

            server.FailNext = "network";
            await chat.SendAsync("Second");
            Assert.That(Deliveries(chat), Is.EqualTo(new[] { Delivery.Sent, Delivery.Pending }));

            await chat.RefreshAsync();

            var sends = server.Calls.Where(c => c.Path.EndsWith("/messages/")).ToList();
            Assert.That(sends, Has.Count.EqualTo(2));
            Assert.That(sends[0].Body["client_message_id"], Is.EqualTo(sends[1].Body["client_message_id"]));
            Assert.That(server.Bodies.Count(b => b == "Second"), Is.EqualTo(1));
            Assert.That(Deliveries(chat), Is.EqualTo(new[] { Delivery.Sent, Delivery.Sent }));
        }

        [Test]
        public async Task KeepsAnUnsentMessageAcrossARelaunch()
        {
            var first = Chat();
            await first.StartAsync();
            await first.SendAsync("First");
            server.FailNext = "network";
            await first.SendAsync("Second");
            first.Dispose();

            var second = Chat();
            await second.StartAsync();

            Assert.That(server.Bodies.Count(b => b == "Second"), Is.EqualTo(1));
            Assert.That(Texts(second), Is.EqualTo(new[] { "First", "Second" }));
        }

        [Test]
        public async Task StopsAtTheFirstMessageThatCannotGo()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("First");

            server.FailAll = "network";
            await chat.SendAsync("Second");
            await chat.SendAsync("Third");
            server.FailAll = null;
            await chat.RefreshAsync();

            Assert.That(server.Bodies, Is.EqualTo(new[] { "First", "Second", "Third" }));
        }

        [Test]
        public async Task GivesUpOnAMessageTheServerRefused()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("First");

            server.FailNext = 400;
            await chat.SendAsync("Second");

            Assert.That(Deliveries(chat), Is.EqualTo(new[] { Delivery.Sent, Delivery.Failed }));
            Assert.That(chat.State.Error, Is.EqualTo("Refused by the fake server."));

            await chat.RefreshAsync();
            Assert.That(server.Calls.Count(c => c.Path.EndsWith("/messages/")), Is.EqualTo(1));
        }

        // -- following the conversation ---------------------------------------------------

        [Test]
        public async Task CountsAnAgentReplyUnreadUntilLookedAt()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("The widget throws a CSP error.");
            server.Reply("Add the CSP header.");

            await chat.RefreshAsync();

            Assert.That(Texts(chat), Is.EqualTo(new[] { "The widget throws a CSP error.", "Add the CSP header." }));
            Assert.That(chat.State.UnreadCount, Is.EqualTo(1));

            chat.SetPresent(true);
            Assert.That(chat.State.UnreadCount, Is.Zero);
        }

        [Test]
        public async Task MarksAReplyAsMarkdownUnlessItArrivedAsHtml()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("The widget throws a CSP error.");
            server.Reply("Add the **CSP** header.");
            server.Messages.Add(server.Message("agent", "", html: "<p>Sent from **my phone**</p>"));

            await chat.RefreshAsync();

            Assert.That(chat.State.Messages.Select(m => m.Markdown), Is.EqualTo(new[] { true, true, false }));
            Assert.That(chat.State.Messages[2].Text, Is.EqualTo("Sent from **my phone**"));
        }

        [Test]
        public async Task CountsWhatArrivedWhileClosed()
        {
            var first = Chat();
            await first.StartAsync();
            await first.SendAsync("The widget throws a CSP error.");
            first.Dispose();

            server.Reply("Add the CSP header.");
            server.Reply("Did that work?");

            var second = Chat();
            await second.StartAsync();

            Assert.That(second.State.UnreadCount, Is.EqualTo(2));
            Assert.That(second.State.Messages, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task SaysTheVisitorIsReadingOnlyWhileTheyAre()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");

            await chat.RefreshAsync();
            Assert.That(server.Calls.Last().Path, Does.Not.Contain("present=1"));

            chat.SetPresent(true);
            await chat.RefreshAsync();
            Assert.That(server.Calls.Last().Path, Does.Contain("present=1"));

            chat.SetPresent(false);
            await chat.RefreshAsync();
            Assert.That(server.Calls.Last().Path, Does.Not.Contain("present=1"));
        }

        [Test]
        public async Task AsksNothingWhileInTheBackground()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");
            var before = server.Calls.Count;

            chat.SetActive(false);
            await chat.RefreshAsync();

            Assert.That(server.Calls, Has.Count.EqualTo(before));
        }

        [Test]
        public async Task ShowsTheAgentTypingAndStops()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");

            server.Typing = new Dictionary<string, object> { ["name"] = "Ada" };
            await chat.RefreshAsync();
            Assert.That(chat.State.Typing, Is.EqualTo(new Typing("Ada")));

            server.Typing = null;
            await chat.RefreshAsync();
            Assert.That(chat.State.Typing, Is.Null);
        }

        [Test]
        public async Task NeverShowsAMessageTwice()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("First");
            await chat.SendAsync("Second");

            await chat.RefreshAsync();
            await chat.RefreshAsync();

            Assert.That(Texts(chat), Is.EqualTo(new[] { "First", "Second" }));
        }

        [Test]
        public async Task SendsTheCursorItWasGivenAsSince()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");
            var cursor = (string)Stored()["cursor"];

            await chat.RefreshAsync();

            Assert.That(server.Calls.Last().Path, Does.Contain("since=" + Uri.EscapeDataString(cursor)));
            Assert.That(server.Calls.Last().Token, Is.EqualTo("visitor-token-1"));
        }

        // -- a conversation the server no longer knows ------------------------------------

        [Test]
        public async Task LetsGoOfAConversationTheServerForgot()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");

            server.ConversationGone = true;
            await chat.RefreshAsync();
            server.ConversationGone = false;

            Assert.That(chat.State.Conversation, Is.Null);
            Assert.That(chat.State.Messages, Is.Empty);
            Assert.That(Stored(), Is.Null);

            await chat.SendAsync("Hello again");
            Assert.That(chat.State.Conversation.TicketId, Is.EqualTo("ticket-1"));
        }

        [Test]
        public async Task DoesNotRestoreAForgottenConversation()
        {
            var first = Chat();
            await first.StartAsync();
            await first.SendAsync("Hello");
            first.Dispose();

            server.ConversationGone = true;
            var second = Chat();
            await second.StartAsync();

            Assert.That(second.State.Status, Is.EqualTo(ChatStatus.Ready));
            Assert.That(second.State.Conversation, Is.Null);
        }

        // -- who the visitor is -----------------------------------------------------------

        [Test]
        public async Task SendsIdentityWithTheConversationWhenKnownBeforehand()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.IdentifyAsync(new Identity { Id = "user-42", Email = "kim@shop.example.com", UserHash = "abc" });
            await chat.SendAsync("Hello");

            var identity = (IDictionary<string, object>)StartCall().Body["identity"];
            Assert.That(identity, Is.EquivalentTo(new Dictionary<string, object>
            {
                ["id"] = "user-42",
                ["email"] = "kim@shop.example.com",
                ["userHash"] = "abc",
            }));
        }

        [Test]
        public async Task AttachesIdentityToAConversationAlreadyStarted()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");
            await chat.IdentifyAsync(new Identity { Id = "user-42", UserHash = "abc" });

            Assert.That(server.Calls.Any(c => c.Path.EndsWith("/identify/")), Is.True);
        }

        [Test]
        public async Task IdentifyIsNeverAnErrorTheAppHandles()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");
            server.FailNext = 500;

            Assert.DoesNotThrowAsync(() => chat.IdentifyAsync(new Identity { Id = "user-42" }));
            Assert.That(chat.State.Messages, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TakesTheConversationAwayWhenSomebodyElseSignsIn()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.IdentifyAsync(new Identity { Id = "user-a" });
            await chat.SendAsync("Our invoices are wrong");
            Assert.That(chat.State.Messages, Has.Count.EqualTo(1));

            await chat.IdentifyAsync(new Identity { Id = "user-b" });

            Assert.That(server.Calls.Any(c => c.Path.EndsWith("/identify/")), Is.False);
            Assert.That(chat.State.Conversation, Is.Null);
            Assert.That(chat.State.Messages, Is.Empty);
            Assert.That(Stored(), Is.Null);
        }

        [Test]
        public async Task LetsGoWhenTheServerSaysTheConversationIsSomebodyElses()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");
            server.FailNext = 409;
            await chat.IdentifyAsync(new Identity { Id = "user-b" });

            Assert.That(chat.State.Conversation, Is.Null);
            Assert.That(chat.State.Messages, Is.Empty);
            Assert.That(Stored(), Is.Null);
        }

        [Test]
        public async Task StaysAttachedWhenTheSameVisitorIsNamedAgain()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.IdentifyAsync(new Identity { Id = "user-a" });
            await chat.SendAsync("Hello");

            await chat.IdentifyAsync(new Identity { Id = "user-a", Name = "Ada" });

            Assert.That(chat.State.Messages, Has.Count.EqualTo(1));
            Assert.That(server.Calls.Any(c => c.Path.EndsWith("/identify/")), Is.True);
        }

        [Test]
        public async Task ForgetsEverythingOnReset()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.SendAsync("Hello");
            await chat.ResetAsync();

            Assert.That(chat.State.Conversation, Is.Null);
            Assert.That(chat.State.Messages, Is.Empty);
            Assert.That(Stored(), Is.Null);
        }

        // -- storage ----------------------------------------------------------------------

        [Test]
        public async Task StoresTheSameShapeAsTheReactNativeSdk()
        {
            var chat = Chat();
            await chat.StartAsync();
            await chat.IdentifyAsync(new Identity { Email = "Kim@Shop.example.com" });
            await chat.SendAsync("Hello");

            var stored = Stored();
            Assert.That(stored.Keys, Is.EquivalentTo(new[] { "token", "ticketId", "identityKey", "cursor", "lastReadCursor", "pending" }));
            Assert.That(stored["identityKey"], Is.EqualTo("email:kim@shop.example.com"));
        }

        [Test]
        public async Task TreatsAnUnreadableRecordAsNothingStored()
        {
            await storage.SetItem(StorageKey, "{not json");
            var chat = Chat();
            await chat.StartAsync();

            Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Ready));
            Assert.That(chat.State.Conversation, Is.Null);
        }

        // -- what subscribers see ---------------------------------------------------------

        [Test]
        public async Task SubscribersSeeTheStateNowAndEveryChange()
        {
            var seen = new List<ChatState>();
            var chat = Chat();
            var subscription = chat.Subscribe(seen.Add);

            Assert.That(seen, Has.Count.EqualTo(1));
            Assert.That(seen[0].Status, Is.EqualTo(ChatStatus.Idle));

            await chat.StartAsync();
            Assert.That(seen.Last().Status, Is.EqualTo(ChatStatus.Ready));

            subscription.Dispose();
            var count = seen.Count;
            await chat.SendAsync("Hello");
            Assert.That(seen, Has.Count.EqualTo(count));
        }

        [Test]
        public async Task AThrowingSubscriberDoesNotBreakTheChat()
        {
            var errors = new List<Exception>();
            var previous = HelpwingChat.ListenerError;
            HelpwingChat.ListenerError = errors.Add;
            try
            {
                var chat = Chat();
                chat.StateChanged += _ => throw new InvalidOperationException("drawing failed");
                await chat.StartAsync();

                Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Ready));
                Assert.That(errors, Is.Not.Empty);
            }
            finally
            {
                HelpwingChat.ListenerError = previous;
            }
        }

        [Test]
        public void FlattensOldHtmlOnlyMessages()
        {
            Assert.That(HelpwingChat.TextOf("<p>One &amp; two</p><p>Three<br>four</p>"), Is.EqualTo("One & two\nThree\nfour"));
        }
    }
}
