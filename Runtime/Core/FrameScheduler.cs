using System;
using System.Collections.Generic;

namespace Helpwing
{
    /// <summary>A scheduler driven by frames, so polling works on WebGL and always runs on the main thread.</summary>
    public sealed class FrameScheduler : IScheduler
    {
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Func<double> clock;

        /// <param name="clock">Seconds, e.g. <c>Time.realtimeSinceStartupAsDouble</c>.</param>
        public FrameScheduler(Func<double> clock)
        {
            this.clock = clock;
        }

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            var entry = new Entry { Due = clock() + delay.TotalSeconds, Action = action };
            entries.Add(entry);
            return entry;
        }

        /// <summary>Run whatever is due. Call once a frame.</summary>
        public void Tick()
        {
            if (entries.Count == 0) return;
            var now = clock();
            var due = entries.FindAll(entry => entry.Cancelled || entry.Due <= now);
            if (due.Count == 0) return;
            entries.RemoveAll(entry => entry.Cancelled || entry.Due <= now);
            foreach (var entry in due)
            {
                if (!entry.Cancelled) entry.Action();
            }
        }

        private sealed class Entry : IDisposable
        {
            public double Due;
            public Action Action;
            public bool Cancelled;

            public void Dispose()
            {
                Cancelled = true;
            }
        }
    }
}
