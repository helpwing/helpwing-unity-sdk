using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Helpwing.Tests
{
    public class SchedulerTests
    {
        [Test]
        public void RunsWhatIsDueAndNothingCancelled()
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
        public async Task PollsOnTheIntervalWhileReadingAndStopsInTheBackground()
        {
            var now = 0.0;
            var scheduler = new FrameScheduler(() => now);
            var server = new FakeServer();
            var chat = new HelpwingChat(new ChatOptions
            {
                ApiUrl = "https://api.helpwing.test",
                ProjectKey = "pk_test",
                Http = server,
                Scheduler = scheduler,
                PollInterval = TimeSpan.FromSeconds(5),
                BackgroundPollInterval = TimeSpan.FromSeconds(30),
            });
            await chat.StartAsync();
            await chat.SendAsync("Hello");
            int Polls() => server.Calls.Count(c => c.Path.Contains("/updates/"));

            now = 29;
            scheduler.Tick();
            Assert.That(Polls(), Is.Zero);
            now = 30;
            scheduler.Tick();
            Assert.That(Polls(), Is.EqualTo(1));

            chat.SetPresent(true);
            var afterPresent = Polls();
            now = 35;
            scheduler.Tick();
            Assert.That(Polls(), Is.EqualTo(afterPresent + 1));

            chat.SetActive(false);
            now = 1000;
            scheduler.Tick();
            Assert.That(Polls(), Is.EqualTo(afterPresent + 1));
        }

        [Test]
        public async Task StopsPollingWhenBackgroundIntervalIsZero()
        {
            var now = 0.0;
            var scheduler = new FrameScheduler(() => now);
            var server = new FakeServer();
            var chat = new HelpwingChat(new ChatOptions
            {
                ApiUrl = "https://api.helpwing.test",
                ProjectKey = "pk_test",
                Http = server,
                Scheduler = scheduler,
                BackgroundPollInterval = TimeSpan.Zero,
            });
            await chat.StartAsync();
            await chat.SendAsync("Hello");

            now = 10000;
            scheduler.Tick();
            Assert.That(server.Calls.Any(c => c.Path.Contains("/updates/")), Is.False);
        }
    }
}
