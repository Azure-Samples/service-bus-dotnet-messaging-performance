namespace ThroughputTest;

sealed class WorkBudget(long target)
{
    private readonly object sync = new();
    private long remaining = target;
    private long reserved;
    private TaskCompletionSource changed = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<int> ReserveAsync(int maximum, CancellationToken token)
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                token.ThrowIfCancellationRequested();
                if (remaining > 0)
                {
                    var count = (int)Math.Min(remaining, maximum);
                    remaining -= count;
                    reserved += count;
                    return count;
                }
                if (reserved == 0) return 0;
                wait = changed.Task;
            }
            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }

    public void Finish(int requested, int actual)
    {
        if (actual < 0 || actual > requested) throw new InvalidOperationException("Invalid operation count.");
        lock (sync)
        {
            reserved -= requested;
            remaining += requested - actual;
            var previous = changed;
            changed = NewSignal();
            previous.TrySetResult();
        }
    }
}

static class TaskEx
{
    public static async Task WhenAllFailFast(IEnumerable<Func<CancellationToken, Task>> operations, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var tasks = operations.Select(Run).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        async Task Run(Func<CancellationToken, Task> operation)
        {
            try { await operation(stop.Token).ConfigureAwait(false); }
            catch { stop.Cancel(); throw; }
        }
    }

    public static async Task RunOperationsAsync(WorkBudget budget, int batchSize, DynamicSemaphoreSlim gate,
        Func<int, double, CancellationToken, Task<int>> action, TimeProvider time, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var tasks = new List<Task>();
        Exception? schedulingError = null;
        try
        {
            while (true)
            {
                // Observe completed operations before admitting more work; storage is bounded by concurrency.
                for (var i = tasks.Count - 1; i >= 0; i--)
                    if (tasks[i].IsCompleted) { await tasks[i].ConfigureAwait(false); tasks.RemoveAt(i); }
                var started = time.GetTimestamp();
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                int count;
                try { count = await budget.ReserveAsync(batchSize, stop.Token).ConfigureAwait(false); }
                catch { gate.Release(); throw; }
                if (count == 0) { gate.Release(); break; }
                tasks.Add(Run(count, time.GetElapsedTime(started).TotalMilliseconds));
            }
        }
        catch (Exception ex) { schedulingError = ex; stop.Cancel(); }
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch when (schedulingError is not null and not OperationCanceledException)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(schedulingError).Throw(); throw; }
        if (schedulingError != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(schedulingError).Throw();

        async Task Run(int count, double gateMilliseconds)
        {
            var actual = 0;
            try { actual = await action(count, gateMilliseconds, stop.Token).ConfigureAwait(false); }
            catch { stop.Cancel(); throw; }
            finally { budget.Finish(count, actual); gate.Release(); }
        }
    }

    public static async Task CloseAsync(Func<CancellationToken, Task> close, Settings settings, TimeProvider time)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.CleanupTimeoutSeconds), time);
        // SDK close honors cancellation; the outer wait also bounds a transport that fails to respond.
        var task = close(timeout.Token);
        try { await task.WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _ = task.ContinueWith(t => Console.Error.WriteLine($"Late cleanup failure: {t.Exception!.GetBaseException().GetType().Name}"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new TimeoutException("Resource cleanup exceeded its deadline.");
        }
    }
}
