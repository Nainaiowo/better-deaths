namespace BetterDeaths;

using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class OrderedBackgroundWorkQueue(Action<Exception> reportFailure) : IDisposable
{
    private readonly object syncRoot = new();
    private Task tail = Task.CompletedTask;
    private bool closed;

    public Task Enqueue(Action work)
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            // Never run continuations inline on the game thread, even when the queue is empty.
            tail = tail.ContinueWith(previous =>
            {
                _ = previous.Exception;
                try
                {
                    work();
                }
                catch (Exception error)
                {
                    reportFailure(error);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return tail;
        }
    }

    public void Drain()
    {
        Task pending;
        lock (syncRoot)
            pending = tail;
        pending.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        lock (syncRoot)
            closed = true;
        Drain();
    }
}
