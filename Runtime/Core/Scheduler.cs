using System;
using System.Threading;
using System.Threading.Tasks;

namespace Helpwing
{
    /// <summary>Runs an action once after a delay. Disposing the handle cancels it.</summary>
    public interface IScheduler
    {
        IDisposable Schedule(TimeSpan delay, Action action);
    }

    /// <summary>
    /// Task.Delay, resuming on the sync context that scheduled it (Unity's main thread in a player).
    /// WebGL has no timers of this kind; the Unity layer uses a frame-driven scheduler instead.
    /// </summary>
    public sealed class DelayScheduler : IScheduler
    {
        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            var cancel = new CancellationTokenSource();
            var context = SynchronizationContext.Current;
            Task.Delay(delay, cancel.Token).ContinueWith(task =>
            {
                if (task.IsCanceled || cancel.IsCancellationRequested) return;
                if (context != null) context.Post(_ => { if (!cancel.IsCancellationRequested) action(); }, null);
                else action();
            }, TaskScheduler.Default);
            return new Cancel(cancel);
        }

        private sealed class Cancel : IDisposable
        {
            private readonly CancellationTokenSource source;

            public Cancel(CancellationTokenSource source)
            {
                this.source = source;
            }

            public void Dispose()
            {
                if (!source.IsCancellationRequested) source.Cancel();
            }
        }
    }
}
