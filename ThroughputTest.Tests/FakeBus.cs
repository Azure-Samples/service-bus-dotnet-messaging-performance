using Azure.Messaging.ServiceBus;
using System.Collections.Concurrent;

namespace Offline;

sealed class FakeBus : ServiceBusClient
{
    public ConcurrentBag<FakeSender> Senders { get; } = [];
    public ConcurrentBag<FakeReceiver> Receivers { get; } = [];
    public ConcurrentDictionary<string, ConcurrentQueue<ServiceBusReceivedMessage>> Queues { get; } = new();
    public ConcurrentBag<int> SentBatches { get; } = [];
    public int BatchCapacity { get; set; } = int.MaxValue;
    public int PayloadSize { get; private set; }
    public bool Closed { get; private set; }
    public string[] Destinations { get; set; } = ["queue"];
    public Func<CancellationToken, Task>? OnSend { get; set; }
    public Func<CancellationToken, Task>? OnReceive { get; set; }
    public Func<CancellationToken, Task>? OnComplete { get; set; }
    public Func<CancellationToken, Task>? OnClose { get; set; }
    public int Received;
    public int Completed;
    public ConcurrentQueue<ServiceBusReceivedMessage> Queue(string path) => Queues.GetOrAdd(path, _ => new());
    public void Seed(string path, int count)
    {
        for (var i = 0; i < count; i++) Queue(path).Enqueue(ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: Guid.NewGuid().ToString()));
    }
    public override ServiceBusSender CreateSender(string queueOrTopicName)
    {
        var sender = new FakeSender(this, queueOrTopicName); Senders.Add(sender); return sender;
    }
    public override ServiceBusReceiver CreateReceiver(string queueName, ServiceBusReceiverOptions options)
        => NewReceiver(queueName, options);
    public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName, ServiceBusReceiverOptions options)
        => NewReceiver($"{topicName}/subscriptions/{subscriptionName}", options);
    private FakeReceiver NewReceiver(string path, ServiceBusReceiverOptions options)
    {
        var receiver = new FakeReceiver(this, path, options); Receivers.Add(receiver); return receiver;
    }
    public override async ValueTask DisposeAsync()
    {
        if (OnClose != null) await OnClose(CancellationToken.None);
        Closed = true;
    }
    internal async Task Send(int count, int size, CancellationToken token)
    {
        await Task.Yield();
        if (OnSend != null) await OnSend(token);
        token.ThrowIfCancellationRequested();
        PayloadSize = size;
        SentBatches.Add(count);
        foreach (var path in Destinations) Seed(path, count);
    }
}

sealed class FakeSender(FakeBus bus, string path) : ServiceBusSender
{
    public bool Closed { get; private set; }
    public string Path { get; } = path;
    private readonly ConcurrentDictionary<ServiceBusMessageBatch, List<ServiceBusMessage>> batches = new();
    public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        => bus.Send(1, message.Body.ToArray().Length, cancellationToken);
    public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = new List<ServiceBusMessage>();
        var batch = ServiceBusModelFactory.ServiceBusMessageBatch(1024, messages, tryAddCallback: _ => messages.Count < bus.BatchCapacity);
        batches[batch] = messages;
        return ValueTask.FromResult(batch);
    }
    public override Task SendMessagesAsync(ServiceBusMessageBatch messageBatch, CancellationToken cancellationToken = default)
    {
        if (!batches.TryRemove(messageBatch, out var messages)) throw new InvalidOperationException("Unknown batch.");
        return bus.Send(messages.Count, messages[0].Body.ToArray().Length, cancellationToken);
    }
    public override Task CloseAsync(CancellationToken cancellationToken = default) { Closed = true; return Task.CompletedTask; }
}

sealed class FakeReceiver(FakeBus bus, string path, ServiceBusReceiverOptions options) : ServiceBusReceiver
{
    public bool Closed { get; private set; }
    public string Path { get; } = path;
    public ServiceBusReceiverOptions Options { get; } = options;
    public ConcurrentBag<int> Requests { get; } = [];
    public override async Task<ServiceBusReceivedMessage> ReceiveMessageAsync(TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
    {
        var messages = await ReceiveMessagesAsync(1, maxWaitTime, cancellationToken);
        return messages.Count == 0 ? null! : messages[0];
    }
    public override async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        Requests.Add(maxMessages);
        if (bus.OnReceive != null) await bus.OnReceive(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<ServiceBusReceivedMessage>();
        while (result.Count < maxMessages && bus.Queue(Path).TryDequeue(out var message)) result.Add(message);
        Interlocked.Add(ref bus.Received, result.Count);
        return result;
    }
    public override async Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
    {
        if (Options.ReceiveMode != ServiceBusReceiveMode.PeekLock) throw new InvalidOperationException("ReceiveAndDelete cannot be settled.");
        if (bus.OnComplete != null) await bus.OnComplete(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref bus.Completed);
    }
    public override Task CloseAsync(CancellationToken cancellationToken = default) { Closed = true; return Task.CompletedTask; }
}
