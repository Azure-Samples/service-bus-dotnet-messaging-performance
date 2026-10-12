using Azure.Messaging.ServiceBus;

namespace ThroughputTest;

sealed class ReceiverTask(Settings settings, Metrics metrics, ServiceBusClient client, TimeProvider time)
    : PerformanceTask(settings, metrics, client, time)
{
    public override Task StartAsync(CancellationToken token)
    {
        var operations = new List<Func<CancellationToken, Task>>();
        foreach (var path in Settings.ReceivePaths)
        {
            var address = EntityAddress.Parse(path);
            var budget = new WorkBudget(Settings.ReceiveTarget);
            for (var i = 0; i < Settings.ReceiverCount; i++)
                operations.Add(ct => ReceiveAsync(address, budget, ct));
        }
        return TaskEx.WhenAllFailFast(operations, token);
    }

    private async Task ReceiveAsync(EntityAddress address, WorkBudget budget, CancellationToken token)
    {
        var receiver = address.CreateReceiver(Client, Settings);
        using var gate = new DynamicSemaphoreSlim(Settings.MaxInflightReceives);
        try
        {
            await TaskEx.RunOperationsAsync(budget, Math.Max(1, Settings.ReceiveBatchCount), gate,
                ReceiveOperation, Time, token).ConfigureAwait(false);
        }
        finally { await TaskEx.CloseAsync(receiver.CloseAsync, Settings, Time).ConfigureAwait(false); }

        async Task<int> ReceiveOperation(int count, double gateMs, CancellationToken ct)
        {
            var metric = new ReceiveMetrics();
            var started = Time.GetTimestamp();
            try
            {
                IReadOnlyList<ServiceBusReceivedMessage> messages;
                if (Settings.ReceiveBatchCount <= 1)
                {
                    var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    messages = message == null ? [] : [message];
                }
                else
                    messages = await receiver.ReceiveMessagesAsync(count, TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                metric.ReceiveMilliseconds = Time.GetElapsedTime(started).TotalMilliseconds;
                metric.Receives = 1;
                metric.Messages = messages.Count;
                if (messages.Count == 0)
                {
                    // Empty polls are operations, not messages or errors; avoid a busy loop on immediate empty returns.
                    await Task.Delay(TimeSpan.FromMilliseconds(10), Time, ct).ConfigureAwait(false);
                }
                foreach (var message in messages)
                {
                    if (Settings.WorkDuration > 0)
                        await Task.Delay(TimeSpan.FromMilliseconds(Settings.WorkDuration), Time, ct).ConfigureAwait(false);
                    if (Settings.ReceiveMode == ServiceBusReceiveMode.PeekLock)
                    {
                        started = Time.GetTimestamp();
                        metric.CompleteCalls++;
                        try { await receiver.CompleteMessageAsync(message, ct).ConfigureAwait(false); metric.Completions++; }
                        finally { metric.CompleteMilliseconds += Time.GetElapsedTime(started).TotalMilliseconds; }
                    }
                }
                return messages.Count;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { metric.Errors = 1; metric.BusyErrors = Metrics.IsBusy(ex) ? 1 : 0; throw; }
            finally { Metrics.PushReceiveMetrics(metric); }
        }
    }
}
