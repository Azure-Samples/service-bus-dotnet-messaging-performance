namespace ThroughputTest;

sealed class IncreaseInflightReceivesExperiment(int count, Metrics metrics, Settings settings, TimeProvider time)
    : Experiment(count, metrics, settings, time)
{
    protected override Observable<int> Concurrency => Settings.MaxInflightReceives;
    protected override long MessageCount => Metrics.TotalReceived;
}
