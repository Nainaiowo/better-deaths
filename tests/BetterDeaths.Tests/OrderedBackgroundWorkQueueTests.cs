using System.Collections.Concurrent;

namespace BetterDeaths.Tests;

public sealed class OrderedBackgroundWorkQueueTests
{
    [Fact]
    public async Task EnqueueDoesNotBlockAndWorkExecutesInOrder()
    {
        var failures = new ConcurrentQueue<Exception>();
        using var queue = new OrderedBackgroundWorkQueue(failures.Enqueue);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<int>();
        var first = queue.Enqueue(() =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            order.Enqueue(1);
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = queue.Enqueue(() => order.Enqueue(2));
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Empty(order);
            release.Set();
            await second.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new[] { 1, 2 }, order);
            Assert.Empty(failures);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task WorkFailureIsReportedAndDoesNotStopLaterEncounters()
    {
        var failures = new ConcurrentQueue<Exception>();
        using var queue = new OrderedBackgroundWorkQueue(failures.Enqueue);
        var expected = new IOException("Simulated save failure");
        _ = queue.Enqueue(() => throw expected);
        var completed = false;
        await queue.Enqueue(() => completed = true);
        Assert.True(completed);
        Assert.Same(expected, Assert.Single(failures));
    }

    [Fact]
    public async Task DisposeWaitsForAllAcceptedWorkAndRejectsNewWork()
    {
        var failures = new ConcurrentQueue<Exception>();
        var queue = new OrderedBackgroundWorkQueue(failures.Enqueue);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        _ = queue.Enqueue(() =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            finished = true;
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var disposal = Task.Run(queue.Dispose);
            Assert.False(finished);
            Assert.False(disposal.IsCompleted);
            release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(finished);
            Assert.Throws<ObjectDisposedException>(() => { _ = queue.Enqueue(() => { }); });
            Assert.Empty(failures);
        }
        finally { release.Set(); queue.Dispose(); }
    }

    [Fact]
    public async Task OrderedClearsCannotBeOvertakenByOlderSaves()
    {
        var failures = new ConcurrentQueue<Exception>();
        using var queue = new OrderedBackgroundWorkQueue(failures.Enqueue);
        var path = Path.Combine(Path.GetTempPath(), $"better-deaths-save-{Guid.NewGuid():N}.json");
        try
        {
            _ = queue.Enqueue(() => File.WriteAllText(path, "old encounter"));
            await queue.Enqueue(() => File.Delete(path));
            Assert.False(File.Exists(path));
            await queue.Enqueue(() => File.WriteAllText(path, "new encounter"));
            Assert.Equal("new encounter", File.ReadAllText(path));
            Assert.Empty(failures);
        }
        finally { queue.Drain(); File.Delete(path); }
    }

    [Fact]
    public async Task DrainAllowsPublicationToEnqueueFinalSavesBeforeShutdown()
    {
        var failures = new ConcurrentQueue<Exception>();
        using var queue = new OrderedBackgroundWorkQueue(failures.Enqueue);
        var completions = new ConcurrentQueue<int>();
        _ = queue.Enqueue(() => completions.Enqueue(1));
        _ = queue.Enqueue(() => completions.Enqueue(2));
        await Task.Run(queue.Drain);
        var saves = new ConcurrentQueue<int>();
        while (completions.TryDequeue(out var completion))
        {
            var captured = completion;
            _ = queue.Enqueue(() => saves.Enqueue(captured));
        }
        await Task.Run(queue.Dispose);
        Assert.Equal(new[] { 1, 2 }, saves);
        Assert.Empty(failures);
    }
}
