using Azure.Messaging.ServiceBus;

namespace ThroughputTest;

sealed class SenderTask(Settings settings, Metrics metrics, ServiceBusClient client, TimeProvider time)
    : PerformanceTask(settings, metrics, client, time)
{
    public override Task StartAsync(CancellationToken token) => TaskEx.WhenAllFailFast(
        Enumerable.Range(0, Settings.SenderCount).Select<int, Func<CancellationToken, Task>>(_ => SendAsync), token);

    private async Task SendAsync(CancellationToken token)
    {
        var sender = Client.CreateSender(Settings.SendPath);
        using var gate = new DynamicSemaphoreSlim(Settings.MaxInflightSends);
        var payload = new byte[Settings.MessageSizeInBytes];
        try
        {
            await TaskEx.RunOperationsAsync(new WorkBudget(Settings.MessageCount), Math.Max(1, Settings.SendBatchCount),
                gate, SendOperation, Time, token).ConfigureAwait(false);
        }
        finally { await TaskEx.CloseAsync(sender.CloseAsync, Settings, Time).ConfigureAwait(false); }

        async Task<int> SendOperation(int count, double gateMs, CancellationToken ct)
        {
            var metric = new SendMetrics { GateMilliseconds = gateMs };
            var started = Time.GetTimestamp();
            try
            {
                if (Settings.SendDelay > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(Settings.SendDelay), Time, ct).ConfigureAwait(false);
                started = Time.GetTimestamp();
                if (Settings.SendBatchCount <= 1)
                {
                    await sender.SendMessageAsync(NewMessage(), ct).ConfigureAwait(false);
                    metric.Messages = metric.Sends = 1;
                }
                else
                {
                    // Respect the negotiated byte limit as well as the requested message-count limit.
                    using var batch = await sender.CreateMessageBatchAsync(ct).ConfigureAwait(false);
                    while (metric.Messages < count && batch.TryAddMessage(NewMessage())) metric.Messages++;
                    if (metric.Messages == 0)
                        throw new InvalidOperationException("A message does not fit in an SDK batch; reduce payload size or use single sends.");
                    var actual = metric.Messages;
                    metric.Messages = 0;
                    await sender.SendMessagesAsync(batch, ct).ConfigureAwait(false);
                    metric.Messages = actual;
                    metric.Sends = 1;
                }
                return metric.Messages;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { metric.Errors = 1; metric.BusyErrors = Metrics.IsBusy(ex) ? 1 : 0; throw; }
            finally { metric.SendMilliseconds = Time.GetElapsedTime(started).TotalMilliseconds; Metrics.PushSendMetrics(metric); }
        }

        ServiceBusMessage NewMessage() => new(payload) { TimeToLive = TimeSpan.FromMinutes(5) };
    }
}
