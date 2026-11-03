namespace ThroughputTest;

abstract class Experiment(int increment, Metrics metrics, Settings settings, TimeProvider time)
{
    protected abstract Observable<int> Concurrency { get; }
    protected abstract long MessageCount { get; }
    protected Settings Settings { get; } = settings;
    protected Metrics Metrics { get; } = metrics;

    public async Task Run(CancellationToken token)
    {
        for (var step = 0; step < Settings.ExperimentSteps; step++)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), time, token).ConfigureAwait(false);
            var before = MessageCount;
            await Task.Delay(TimeSpan.FromSeconds(5), time, token).ConfigureAwait(false);
            var first = MessageCount - before;
            var original = Concurrency.Value;
            Concurrency.Value = Math.Clamp(original + increment, 1, 10000);
            if (Concurrency.Value == original) return;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), time, token).ConfigureAwait(false);
                before = MessageCount;
                await Task.Delay(TimeSpan.FromSeconds(5), time, token).ConfigureAwait(false);
                if (MessageCount - before <= first) Concurrency.Value = original;
            }
            catch { Concurrency.Value = original; throw; }
        }
    }
}
