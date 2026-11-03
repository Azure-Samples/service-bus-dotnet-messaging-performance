namespace ThroughputTest;

sealed class DynamicSemaphoreSlim : IDisposable
{
    private readonly object sync = new();
    private readonly Observable<int> setting;
    private TaskCompletionSource changed = NewSignal();
    private int limit;
    private int inUse;

    public DynamicSemaphoreSlim(Observable<int> setting)
    {
        this.setting = setting;
        setting.Changed += Resize;
        Resize(setting.Value);
    }

    public int InUse { get { lock (sync) return inUse; } }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Pulse() { var previous = changed; changed = NewSignal(); previous.TrySetResult(); }

    private void Resize(int value)
    {
        if (value < 1 || value > 10000) throw new ArgumentOutOfRangeException(nameof(value));
        lock (sync) { limit = value; Pulse(); }
    }

    public async Task WaitAsync(CancellationToken token)
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                token.ThrowIfCancellationRequested();
                if (inUse < limit) { inUse++; return; }
                wait = changed.Task;
            }
            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }

    public void Release()
    {
        lock (sync)
        {
            if (inUse == 0) throw new SemaphoreFullException();
            inUse--;
            Pulse();
        }
    }

    public void Dispose() => setting.Changed -= Resize;
}
