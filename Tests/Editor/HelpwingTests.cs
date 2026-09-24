using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helpwing.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Helpwing.Tests
{
    /// <summary>The fuller suite runs under dotnet (Tests~); these prove the same code inside the editor.</summary>
    public class HelpwingTests
    {
        private sealed class Server : ITransportHttp
        {
            public readonly List<string> Paths = new List<string>();
            private int clock;

            public Task<HttpResponseData> SendAsync(HttpRequestData request)
            {
                var path = new Uri(request.Url).AbsolutePath;
                Paths.Add(request.Method + " " + path);
                clock++;
                var cursor = "2026-08-09T12:00:" + clock.ToString("00") + ".000Z";
                if (path.EndsWith("/config/"))
                {
                    return Answer(200, "{\"is_enabled\":true,\"project_name\":\"Acme\",\"accent_color\":\"#2563eb\",\"greeting\":\"Hi\",\"is_online\":true}");
                }
                var message = "{\"id\":\"m" + clock + "\",\"author\":\"customer\",\"body_text\":\"Hello\",\"created_at\":\"" + cursor + "\",\"attachments\":[]}";
                return Answer(201, "{\"ticket_id\":\"t1\",\"visitor_token\":\"tok\",\"status\":\"open\",\"cursor\":\"" + cursor + "\",\"messages\":[" + message + "]}");
            }

            private static Task<HttpResponseData> Answer(int status, string body) => Task.FromResult(new HttpResponseData(status, body));
        }

        [Test]
        public async Task OpensAConversation()
        {
            var server = new Server();
            var chat = new HelpwingChat(new ChatOptions
            {
                ApiUrl = "https://api.helpwing.test",
                ProjectKey = "pk_test",
                Http = server,
                Scheduler = new FrameScheduler(() => 0),
            });
            await chat.StartAsync();
            await chat.SendAsync("Hello");

            Assert.That(chat.State.Status, Is.EqualTo(ChatStatus.Ready));
            Assert.That(chat.State.Conversation.TicketId, Is.EqualTo("t1"));
            Assert.That(chat.State.Messages.Select(m => m.Delivery), Is.EqualTo(new[] { Delivery.Sent }));
            Assert.That(server.Paths.Last(), Is.EqualTo("POST /widget/pk_test/conversations/"));
        }

        [Test]
        public void FrameSchedulerRunsWhatIsDueAndNothingCancelled()
        {
            var now = 0.0;
            var scheduler = new FrameScheduler(() => now);
            var ran = new List<string>();
            scheduler.Schedule(TimeSpan.FromSeconds(5), () => ran.Add("five"));
            scheduler.Schedule(TimeSpan.FromSeconds(1), () => ran.Add("cancelled")).Dispose();

            now = 4;
            scheduler.Tick();
            Assert.That(ran, Is.Empty);
            now = 5;
            scheduler.Tick();
            Assert.That(ran, Is.EqualTo(new[] { "five" }));
        }

        [Test]
        public void RefusesALinkThatWouldRunCode()
        {
            Assert.That(Markdown.SafeHref("javascript:alert(1)"), Is.Null);
            Assert.That(RichText.Render("[x](javascript:alert(1))").Links, Is.Empty);
        }

        [Test]
        public void ResolvesTheProjectsCopyForTheLocale()
        {
            var config = new WidgetConfig { Greeting = "Hi" };
            config.Translations["ru"] = new Dictionary<string, string> { ["greeting"] = "Привет" };
            Assert.That(Copy.ForLocale(config, Copy.Greeting, "ru-RU"), Is.EqualTo("Привет"));
        }

        [Test]
        public void BuildsTheChatViewWithoutAConfig()
        {
            var host = new GameObject("Helpwing test");
            try
            {
                var client = host.AddComponent<HelpwingClient>();
                var view = new SupportChatView(client, new SupportLabels());
                view.Rebuild();
                Assert.That(view.childCount, Is.GreaterThan(0));

                var launcher = new SupportLauncherView(client);
                launcher.Refresh();
                Assert.That(launcher.style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DrawsAMarkdownMessage()
        {
            var theme = new SupportTheme(Palette.ForAccent("#2563eb"), SupportTheme.DefaultFont());
            var view = new MarkdownView("**Hi** see [docs](https://helpwing.app)\n\n```\ncode\n```\n\n---", theme, Color.black, Color.gray, Color.blue, new Dictionary<string, string>());
            Assert.That(view.Query<Label>().ToList().Count, Is.GreaterThanOrEqualTo(2));
        }
    }
}
