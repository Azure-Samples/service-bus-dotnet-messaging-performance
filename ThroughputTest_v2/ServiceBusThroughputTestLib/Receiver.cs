using Azure.Messaging.ServiceBus;
using ServiceBusThroughputTest.Common;
using ThroughputTest;
using Helpers = ServiceBusThroughputTestLib.Extensions.Extensions;

namespace ServiceBusThroughputTestLib;

public class Receiver
{
    private readonly Settings settings;
    private readonly int receiveWaitMilliseconds;
    private readonly ILogger? logger;
    private readonly Func<Settings, ServiceBusClient> clientFactory;
    private readonly TimeProvider time;

    public Receiver(string connectionString, string queueName, int prefetchCount, int batchSize, int concurrentCalls,
        int receiversCount, int callIntervalMS, bool isReceiveAndDelete = false, ILogger? logger = null)
        : this(new Settings { ConnectionString = connectionString, ReceivePaths = [NormalizePath(queueName)],
            SenderCount = 0, ReceiverCount = receiversCount, PrefetchCount = prefetchCount, ReceiveBatchCount = batchSize,
            CfgMaxInflightReceives = concurrentCalls, ReceiveMode = isReceiveAndDelete ? ServiceBusReceiveMode.ReceiveAndDelete : ServiceBusReceiveMode.PeekLock },
            callIntervalMS, logger, Helpers.CreateClient, TimeProvider.System) { }

    internal Receiver(Settings settings, int receiveWaitMilliseconds, ILogger? logger, Func<Settings, ServiceBusClient> factory, TimeProvider time)
    { this.settings = settings; this.receiveWaitMilliseconds = receiveWaitMilliseconds; this.logger = logger; clientFactory = factory; this.time = time; }

    internal static string NormalizePath(string path)
    {
        if (!path.Contains(':')) return path;
        var parts = path.Split(':');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Use topic:subscription.");
        return $"{parts[0]}/subscriptions/{parts[1]}";
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        settings.Validate();
        if (receiveWaitMilliseconds < 1 || receiveWaitMilliseconds > 3_600_000)
            throw new ArgumentOutOfRangeException(nameof(receiveWaitMilliseconds));
        cancellationToken.ThrowIfCancellationRequested();
        var client = clientFactory(settings);
        try
        {
            await TaskEx.WhenAllFailFast(settings.ReceivePaths.SelectMany(path => Enumerable.Range(0, settings.ReceiverCount)
                .Select<int, Func<CancellationToken, Task>>(_ => ct => Start(client, EntityAddress.Parse(path), ct))), cancellationToken).ConfigureAwait(false);
        }
        finally { await TaskEx.CloseAsync(_ => client.DisposeAsync().AsTask(), settings, time).ConfigureAwait(false); }
    }

    private async Task Start(ServiceBusClient client, EntityAddress address, CancellationToken token)
    {
        var receiver = address.CreateReceiver(client, settings);
        try
        {
            await TaskEx.WhenAllFailFast(Enumerable.Range(0, settings.CfgMaxInflightReceives)
                .Select<int, Func<CancellationToken, Task>>(_ => ReceiveLoop), token).ConfigureAwait(false);
        }
        finally { await TaskEx.CloseAsync(receiver.CloseAsync, settings, time).ConfigureAwait(false); }

        async Task ReceiveLoop(CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var session = logger?.StartRecordTime();
                try
                {
                    IReadOnlyList<ServiceBusReceivedMessage> messages;
                    if (settings.ReceiveBatchCount <= 1)
                    {
                        var message = await receiver.ReceiveMessageAsync(TimeSpan.FromMilliseconds(receiveWaitMilliseconds), ct).ConfigureAwait(false);
                        messages = message == null ? [] : [message];
                    }
                    else
                        messages = await receiver.ReceiveMessagesAsync(settings.ReceiveBatchCount, TimeSpan.FromMilliseconds(receiveWaitMilliseconds), ct).ConfigureAwait(false);
                    if (session is { } id)
                    {
                        logger!.StopRecordTimeAndGetElapsedMs(id);
                        logger.IncrementActionCount(id);
                        logger.IncrementMetricValueBy(id, messages.Count);
                    }
                    if (settings.ReceiveMode == ServiceBusReceiveMode.PeekLock && messages.Count > 0)
                    {
                        if (session is { } startId) logger!.StartCompletionRecordTime(startId);
                        try { await Task.WhenAll(messages.Select(m => receiver.CompleteMessageAsync(m, ct))).ConfigureAwait(false); }
                        finally { if (session is { } endId) logger!.StopCompletionRecordTimeAndGetElapsedMs(endId); }
                    }
                    if (messages.Count == 0) await Task.Delay(TimeSpan.FromMilliseconds(10), time, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { Helpers.RecordFailure(ex, logger, session); throw; }
                finally { if (session is { } id) logger!.DisposeSession(id); }
            }
        }
    }
}
