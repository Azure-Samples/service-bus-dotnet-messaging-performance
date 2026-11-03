using Azure.Messaging.ServiceBus;

namespace ThroughputTest;

abstract class PerformanceTask(Settings settings, Metrics metrics, ServiceBusClient client, TimeProvider time)
{
    protected Settings Settings { get; } = settings;
    protected Metrics Metrics { get; } = metrics;
    protected ServiceBusClient Client { get; } = client;
    protected TimeProvider Time { get; } = time;
    public abstract Task StartAsync(CancellationToken token);
}
