using Azure.Messaging.ServiceBus;
using ServiceBusThroughputTest.Common;
using ThroughputTest;
using Helpers = ServiceBusThroughputTestLib.Extensions.Extensions;

namespace ServiceBusThroughputTestLib;

public class Sender
{
    private readonly Settings settings;
    private readonly ILogger? logger;
    private readonly Func<Settings, ServiceBusClient> clientFactory;
    private readonly TimeProvider time;

    public Sender(string connectionString, string queueName, int payloadSize, int batchSize, int callIntervalMS,
        int concurrentCalls, int sendersCount, ILogger? logger = null)
        : this(new Settings { ConnectionString = connectionString, SendPath = queueName, MessageSizeInBytes = payloadSize,
            SendBatchCount = batchSize, SendDelay = callIntervalMS, CfgMaxInflightSends = concurrentCalls,
            SenderCount = sendersCount, ReceiverCount = 0 }, logger, Helpers.CreateClient, TimeProvider.System) { }

    internal Sender(Settings settings, ILogger? logger, Func<Settings, ServiceBusClient> factory, TimeProvider time)
    { this.settings = settings; this.logger = logger; clientFactory = factory; this.time = time; }

    public async Task Run(CancellationToken cancellationToken)
    {
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var client = clientFactory(settings);
        try
        {
            await TaskEx.WhenAllFailFast(Enumerable.Range(0, settings.SenderCount)
                .Select<int, Func<CancellationToken, Task>>(_ => ct => Start(client, ct)), cancellationToken).ConfigureAwait(false);
        }
        finally { await TaskEx.CloseAsync(_ => client.DisposeAsync().AsTask(), settings, time).ConfigureAwait(false); }
    }

    private async Task Start(ServiceBusClient client, CancellationToken token)
    {
        var sender = client.CreateSender(settings.SendPath);
        var payload = new byte[settings.MessageSizeInBytes];
        try
        {
            await TaskEx.WhenAllFailFast(Enumerable.Range(0, settings.CfgMaxInflightSends)
                .Select<int, Func<CancellationToken, Task>>(_ => SendLoop), token).ConfigureAwait(false);
        }
        finally { await TaskEx.CloseAsync(sender.CloseAsync, settings, time).ConfigureAwait(false); }

        async Task SendLoop(CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var remaining = Math.Max(1, settings.SendBatchCount);
                while (remaining > 0)
                {
                    var started = time.GetTimestamp();
                    var session = logger?.StartRecordTime();
                    try
                    {
                        int sent;
                        if (settings.SendBatchCount <= 1)
                        {
                            await sender.SendMessageAsync(new ServiceBusMessage(payload), ct).ConfigureAwait(false);
                            sent = 1;
                        }
                        else
                        {
                            using var batch = await sender.CreateMessageBatchAsync(ct).ConfigureAwait(false);
                            sent = 0;
                            while (sent < remaining && batch.TryAddMessage(new ServiceBusMessage(payload))) sent++;
                            if (sent == 0) throw new InvalidOperationException("Payload does not fit in a batch.");
                            await sender.SendMessagesAsync(batch, ct).ConfigureAwait(false);
                        }
                        if (session is { } id)
                        {
                            logger!.StopRecordTimeAndGetElapsedMs(id);
                            logger.IncrementActionCount(id);
                            logger.IncrementMetricValueBy(id, sent);
                        }
                        remaining -= sent;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { Helpers.RecordFailure(ex, logger, session); throw; }
                    finally { if (session is { } id) logger!.DisposeSession(id); }
                    var delay = TimeSpan.FromMilliseconds(settings.SendDelay) - time.GetElapsedTime(started);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, time, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
