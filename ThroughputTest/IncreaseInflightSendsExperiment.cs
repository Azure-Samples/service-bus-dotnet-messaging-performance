namespace ThroughputTest;

sealed class IncreaseInflightSendsExperiment(int count, Metrics metrics, Settings settings, TimeProvider time)
    : Experiment(count, metrics, settings, time)
{
    protected override Observable<int> Concurrency => Settings.MaxInflightSends;
    protected override long MessageCount => Metrics.TotalSent;
}
