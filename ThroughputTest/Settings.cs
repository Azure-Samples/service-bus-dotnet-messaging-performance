using Azure.Messaging.ServiceBus;
using CommandLine;

namespace ThroughputTest;

enum ExperimentMode { None, Sends, Receives, Both }

sealed class Settings
{
    [Option('C', "connection-string", HelpText = "Connection string. Prefer SERVICEBUS_CONNECTION_STRING to avoid shell history.")]
    public string? ConnectionString { get; set; }

    [Option("namespace", HelpText = "Fully qualified namespace for DefaultAzureCredential, or SERVICEBUS_NAMESPACE.")]
    public string? FullyQualifiedNamespace { get; set; }

    [Option('S', "send-path", HelpText = "Queue or topic; defaults to connection string EntityPath.")]
    public string? SendPath { get; set; }

    [Option('R', "receive-paths", HelpText = "Queues or topic/subscriptions/name paths; defaults to send path.")]
    public IEnumerable<string> ReceivePaths { get; set; } = [];

    [Option('n', "number-of-messages", HelpText = "Messages per sender, or per receive path in receiver-only mode (default 1000000). Zero does no work.")]
    public long MessageCount { get; set; } = 1_000_000;

    [Option('b', "message-size-bytes", HelpText = "Payload bytes, 1..104857600 (default 1024). Broker limits also apply.")]
    public int MessageSizeInBytes { get; set; } = 1024;

    [Option('f', "frequency-metrics", HelpText = "Display interval in seconds (default 10).")]
    public int MetricsDisplayFrequency { get; set; } = 10;

    [Option('m', "receive-mode", HelpText = "PeekLock (default) or ReceiveAndDelete.")]
    public ServiceBusReceiveMode ReceiveMode { get; set; } = ServiceBusReceiveMode.PeekLock;

    [Option('r', "receiver-count", HelpText = "Competing receivers per path (default 5); zero sends only.")]
    public int ReceiverCount { get; set; } = 5;

    [Option('e', "prefetch-count", HelpText = "Prefetch per receiver (default 100).")]
    public int PrefetchCount { get; set; } = 100;

    [Option('t', "send-batch-count", HelpText = "Maximum messages per send batch (default 0, single send).")]
    public int SendBatchCount { get; set; }

    [Option('s', "sender-count", HelpText = "Concurrent senders (default 1); zero receives only.")]
    public int SenderCount { get; set; } = 1;

    [Option('d', "send-delay", HelpText = "Delay per send operation in milliseconds (default 0).")]
    public int SendDelay { get; set; }

    [Option('i', "inflight-sends", HelpText = "In-flight operations per sender (default 1).")]
    public int CfgMaxInflightSends { get => MaxInflightSends.Value; set => MaxInflightSends.Value = value; }
    public Observable<int> MaxInflightSends { get; } = new(1);

    [Option('j', "inflight-receives", HelpText = "In-flight operations per receiver (default 1).")]
    public int CfgMaxInflightReceives { get => MaxInflightReceives.Value; set => MaxInflightReceives.Value = value; }
    public Observable<int> MaxInflightReceives { get; } = new(1);

    [Option('v', "receive-batch-count", HelpText = "Maximum messages per receive (default 0, single receive).")]
    public int ReceiveBatchCount { get; set; }

    [Option('w', "receive-work-duration", HelpText = "Simulated work per message in milliseconds (default 0).")]
    public int WorkDuration { get; set; }

    [Option("timeout-seconds", HelpText = "Overall run deadline in seconds (default 300).")]
    public int TimeoutSeconds { get; set; } = 300;

    [Option("cleanup-timeout-seconds", HelpText = "Deadline for each resource close in seconds (default 10).")]
    public int CleanupTimeoutSeconds { get; set; } = 10;

    [Option("experiment", HelpText = "None (default), Sends, Receives or Both. Experimental bounded concurrency tuning.")]
    public ExperimentMode Experiment { get; set; }

    [Option("experiment-steps", HelpText = "Maximum tuning steps per experiment (default 3).")]
    public int ExperimentSteps { get; set; } = 3;

    public long ReceiveTarget => checked(MessageCount * Math.Max(1, SenderCount));

    public void Validate(Func<string, string?>? environment = null)
    {
        Range(MessageCount, 0, long.MaxValue / Math.Max(1, SenderCount), "number-of-messages");
        Range(MessageSizeInBytes, 1, 100 * 1024 * 1024, "message-size-bytes");
        Range(SenderCount, 0, 1024, "sender-count");
        Range(ReceiverCount, 0, 1024, "receiver-count");
        if (SenderCount + ReceiverCount == 0) throw new ArgumentException("Enable at least one sender or receiver.");
        Range(CfgMaxInflightSends, 1, 10000, "inflight-sends");
        Range(CfgMaxInflightReceives, 1, 10000, "inflight-receives");
        Range(SendBatchCount, 0, 10000, "send-batch-count");
        Range(ReceiveBatchCount, 0, 10000, "receive-batch-count");
        Range(PrefetchCount, 0, 1_000_000, "prefetch-count");
        Range(SendDelay, 0, 3_600_000, "send-delay");
        Range(WorkDuration, 0, 3_600_000, "receive-work-duration");
        Range(MetricsDisplayFrequency, 1, 3600, "frequency-metrics");
        Range(TimeoutSeconds, 1, 86400, "timeout-seconds");
        Range(CleanupTimeoutSeconds, 1, 60, "cleanup-timeout-seconds");
        Range(ExperimentSteps, 1, 100, "experiment-steps");
        if (!Enum.IsDefined(ReceiveMode) || !Enum.IsDefined(Experiment))
            throw new ArgumentException("Invalid receive mode or experiment.");
        if ((Experiment is ExperimentMode.Sends or ExperimentMode.Both && SenderCount == 0) ||
            (Experiment is ExperimentMode.Receives or ExperimentMode.Both && ReceiverCount == 0))
            throw new ArgumentException("An experiment requires its sender/receiver to be enabled.");

        environment ??= Environment.GetEnvironmentVariable;
        // Explicit authentication selection takes precedence over environment defaults.
        if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(FullyQualifiedNamespace))
        {
            FullyQualifiedNamespace = environment("SERVICEBUS_NAMESPACE");
            if (string.IsNullOrWhiteSpace(FullyQualifiedNamespace))
                ConnectionString = environment("SERVICEBUS_CONNECTION_STRING");
        }
        if (!string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(FullyQualifiedNamespace))
            throw new ArgumentException("Choose either a namespace or a connection string.");

        string? entityPath = null;
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            try { entityPath = ServiceBusConnectionStringProperties.Parse(ConnectionString).EntityPath; }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            { throw new ArgumentException("Invalid connection string."); }
        }
        else if (!string.IsNullOrWhiteSpace(FullyQualifiedNamespace))
        {
            if (Uri.CheckHostName(FullyQualifiedNamespace) != UriHostNameType.Dns ||
                !FullyQualifiedNamespace.Contains('.') || FullyQualifiedNamespace.Contains('/') ||
                FullyQualifiedNamespace.Contains(':'))
                throw new ArgumentException("Use a fully qualified namespace hostname, not a URL.");
        }
        else if (MessageCount != 0)
            throw new ArgumentException("Supply --namespace or a connection string, or its documented environment variable.");

        SendPath = string.IsNullOrWhiteSpace(SendPath) ? entityPath : SendPath;
        if (SenderCount > 0)
        {
            if (string.IsNullOrWhiteSpace(SendPath)) throw new ArgumentException("A send path is required.");
            var send = EntityAddress.Parse(SendPath);
            if (send.Subscription != null || send.SubQueue != SubQueue.None)
                throw new ArgumentException("Send path must be a queue or topic.");
        }
        var paths = ReceivePaths.ToArray();
        if (paths.Length == 0 && ReceiverCount > 0 && !string.IsNullOrWhiteSpace(SendPath))
            paths = [SendPath];
        if (ReceiverCount > 0 && paths.Length == 0) throw new ArgumentException("A receive path is required.");
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("Duplicate receive paths would compete for the same target.");
        foreach (var path in paths) EntityAddress.Parse(path);
        if (!string.IsNullOrWhiteSpace(entityPath))
        {
            var targets = paths.Select(p => EntityAddress.Parse(p).Entity).ToList();
            if (SenderCount > 0) targets.Add(SendPath!);
            if (targets.Any(p => !string.Equals(p, entityPath, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("EntityPath credentials cannot be used for a different entity. Use namespace-scoped credentials.");
        }
        ReceivePaths = paths;
    }

    private static void Range(long value, long minimum, long maximum, string option)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentException($"--{option} must be between {minimum} and {maximum}.");
    }
}

sealed record EntityAddress(string Entity, string? Subscription, SubQueue SubQueue)
{
    public static EntityAddress Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim() != path || path.Contains('\\') ||
            path.Contains(':') || path.Any(char.IsWhiteSpace))
            throw new ArgumentException("Invalid entity path.");
        var parts = path.Split('/').ToList();
        var subQueue = SubQueue.None;
        if (parts[^1].Equals("$DeadLetterQueue", StringComparison.OrdinalIgnoreCase))
        { subQueue = SubQueue.DeadLetter; parts.RemoveAt(parts.Count - 1); }
        if (parts.Count > 0 && parts[^1].Equals("$Transfer", StringComparison.OrdinalIgnoreCase) && subQueue == SubQueue.DeadLetter)
        { subQueue = SubQueue.TransferDeadLetter; parts.RemoveAt(parts.Count - 1); }
        if (parts.Count == 0 || parts.Any(p => p.Length == 0 || p.Contains('$')))
            throw new ArgumentException("Invalid entity path.");
        var subscriptionIndex = parts.FindIndex(p => p.Equals("subscriptions", StringComparison.OrdinalIgnoreCase));
        if (subscriptionIndex < 0) return new(string.Join('/', parts), null, subQueue);
        if (subscriptionIndex == 0 || subscriptionIndex != parts.Count - 2)
            throw new ArgumentException("Use topic/subscriptions/subscription-name.");
        return new(string.Join('/', parts.Take(subscriptionIndex)), parts[^1], subQueue);
    }

    public ServiceBusReceiver CreateReceiver(ServiceBusClient client, Settings settings)
    {
        var options = new ServiceBusReceiverOptions { ReceiveMode = settings.ReceiveMode, PrefetchCount = settings.PrefetchCount, SubQueue = SubQueue };
        return Subscription == null ? client.CreateReceiver(Entity, options) : client.CreateReceiver(Entity, Subscription, options);
    }
}
