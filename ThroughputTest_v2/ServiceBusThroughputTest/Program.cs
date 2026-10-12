using Azure.Messaging.ServiceBus;
using CommandLine;
using CommandLine.Text;
using ServiceBusThroughputTestLib;
using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ThroughputTest;
using Helpers = ServiceBusThroughputTestLib.Extensions.Extensions;

namespace ServiceBusThroughputTest;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        using var inputStop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += handler;
        var input = Console.IsInputRedirected ? Task.CompletedTask : WatchInput();
        try { return await ExecuteAsync(args, Console.Out, Console.Error, stop.Token).ConfigureAwait(false); }
        finally { inputStop.Cancel(); await input.ConfigureAwait(false); Console.CancelKeyPress -= handler; }
        async Task WatchInput()
        {
            try
            {
                while (!inputStop.IsCancellationRequested && !stop.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter) { stop.Cancel(); return; }
                    await Task.Delay(50, inputStop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (inputStop.IsCancellationRequested) { }
        }
    }

    internal static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token,
        Func<ThroughputTest.Settings, ServiceBusClient>? factory = null, TimeProvider? time = null,
        Func<string, string?>? environment = null, string? configPath = null)
    {
        using var parser = new Parser(c => { c.HelpWriter = null; c.CaseInsensitiveEnumValues = true; });
        var parsed = parser.ParseArguments<Settings>(args);
        if (parsed is NotParsed<Settings> invalid)
        {
            if (invalid.Errors.All(e => e.Tag is ErrorType.HelpRequestedError or ErrorType.VersionRequestedError))
            { output.WriteLine(HelpText.AutoBuild(parsed)); return 0; }
            error.WriteLine("Invalid command line. Use --help."); return 2;
        }
        var options = ((Parsed<Settings>)parsed).Value;
        ThroughputTest.Settings settings;
        (int Send, int Receive) capacities;
        try
        {
            settings = options.ToWorkload(environment);
            capacities = ReadCapacities(configPath ?? Path.Combine(AppContext.BaseDirectory, "App.config"));
        }
        catch (Exception ex) when (ex is ArgumentException or XmlException or IOException or InvalidOperationException)
        { error.WriteLine($"Invalid settings or metrics configuration ({ex.GetType().Name}). Use --help."); return 2; }
        time ??= TimeProvider.System;
        factory ??= Helpers.CreateClient;
        using var duration = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds), time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, duration.Token);
        output.WriteLine("Continuous workload: ENTER/Ctrl+C stops early; --duration-seconds bounds the run.");
        try
        {
            foreach (var scenario in options.Scenarios(settings))
            {
                using var step = new CancellationTokenSource(TimeSpan.FromSeconds(scenario.Seconds), time);
                using var stepStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, step.Token);
                using var sendMetrics = new Metrics(default, true, settings.MetricsDisplayFrequency, capacities.Send, time);
                using var receiveMetrics = new Metrics(default, false, settings.MetricsDisplayFrequency, capacities.Receive, time);
                long sent = 0, received = 0;
                sendMetrics.OnMetricsDisplay += (_, e) => WriteMetrics(output, e, true, scenario.Settings, ref sent);
                receiveMetrics.OnMetricsDisplay += (_, e) => WriteMetrics(output, e, false, scenario.Settings, ref received);
                if (scenario.Settings.SenderCount > 0) output.WriteLine("S|pstart|pend|sbc|mifs|snd.avg|snd.min|snd.max|snd.p99|msg/s|total|sndop|errs|busy|overall|");
                if (scenario.Settings.ReceiverCount > 0) output.WriteLine("R|pstart|pend|rbc|mifr|rcv.avg|rcv.min|rcv.max|rcv.p99|msg/s|total|rcvop|errs|busy|overall|cpl.avg|cpl.min|cpl.max|cpl.p99|");
                var operations = new List<Func<CancellationToken, Task>>();
                if (scenario.Settings.SenderCount > 0)
                    operations.Add(ct => new Sender(scenario.Settings, sendMetrics, factory, time).Run(ct));
                if (scenario.Settings.ReceiverCount > 0)
                    operations.Add(ct => new Receiver(scenario.Settings, options.ReceiveCallIntervalMs, receiveMetrics, factory, time).Run(ct));
                var workload = TaskEx.WhenAllFailFast(operations, stepStop.Token);
                var completed = await Task.WhenAny(workload, sendMetrics.Completion, receiveMetrics.Completion).ConfigureAwait(false);
                if (completed != workload) stepStop.Cancel();
                try { await workload.ConfigureAwait(false); }
                catch (OperationCanceledException) when (step.IsCancellationRequested && !stop.IsCancellationRequested) { }
                await ObserveMetricFailure(sendMetrics.Completion).ConfigureAwait(false);
                await ObserveMetricFailure(receiveMetrics.Completion).ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
            }
            output.WriteLine("Complete"); return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { error.WriteLine("Stopped."); return 130; }
        catch (OperationCanceledException) when (duration.IsCancellationRequested)
        { output.WriteLine("Duration complete; operations drained."); return 0; }
        catch (Exception ex)
        { error.WriteLine(ex is ServiceBusException sb ? $"Service Bus failed: {sb.Reason}." : $"Run failed: {ex.GetType().Name}."); return 1; }
    }

    private static async Task ObserveMetricFailure(Task task) { if (task.IsCompleted) await task.ConfigureAwait(false); }

    internal static (int Send, int Receive) ReadCapacities(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var settings = XDocument.Load(reader).Root?.Element("appSettings")?.Elements("add").ToArray()
            ?? throw new InvalidOperationException("Missing appSettings.");
        return (Read("MetricsSamplesPerDisplayIntervalSender", 2000), Read("MetricsSamplesPerDisplayIntervalReceiver", 10000));
        int Read(string key, int fallback)
        {
            var values = settings.Where(e => (string?)e.Attribute("key") == key).ToArray();
            if (values.Length == 0) return fallback;
            if (values.Length != 1 || !int.TryParse((string?)values[0].Attribute("value"), out var value) || value < 1 || value > 1_000_000)
                throw new InvalidOperationException("Invalid metrics sample capacity.");
            return value;
        }
    }

    internal static double Percentile(long[] elements, double percentile)
    {
        if (elements.Length == 0) return 0;
        Array.Sort(elements);
        var index = percentile * (elements.Length - 1);
        var lower = (int)index;
        return elements[lower] * (1 - (index - lower)) + elements[(int)Math.Ceiling(index)] * (index - lower);
    }

    private static string Stat(IEnumerable<long> source)
    {
        var values = source.ToArray();
        if (values.Length == 0) return "0.00|0.00|0.00|0.00|";
        var scale = Stopwatch.Frequency / 1000.0;
        return FormattableString.Invariant($"{values.Average() / scale:F2}|{values.Min() / scale:F2}|{values.Max() / scale:F2}|{Percentile(values, .99) / scale:F2}|");
    }

    private static void WriteMetrics(TextWriter output, MetricsDisplayEventArgs e, bool send, ThroughputTest.Settings settings, ref long total)
    {
        if (send ? settings.SenderCount == 0 : settings.ReceiverCount == 0) return;
        total += e.Messages;
        var duration = e.EndSeconds - e.StartSeconds;
        var rate = duration > 0 ? e.Messages / duration : 0;
        lock (output)
        {
            output.Write(string.Create(CultureInfo.InvariantCulture, $"{(send ? "S" : "R")}|{e.StartSeconds:F2}|{e.EndSeconds:F2}|{(send ? settings.SendBatchCount : settings.ReceiveBatchCount)}|{(send ? settings.CfgMaxInflightSends : settings.CfgMaxInflightReceives)}|"));
            output.Write(Stat(send ? e.SendMetricsList.Where(m => m.Sends > 0).Select(m => m.SendDuration100ns) :
                e.ReceiveMetricsList.Where(m => m.Receives > 0).Select(m => m.ReceiveDuration100ns)));
            output.Write(string.Create(CultureInfo.InvariantCulture, $"{rate:F2}|{e.Messages}|{e.Actions}|{e.Errors}|{e.BusyErrors}|{total}|"));
            if (!send) output.Write(Stat(e.ReceiveMetricsList.Where(m => m.CompleteDuration100ns > 0).Select(m => m.CompleteDuration100ns)));
            output.WriteLine();
        }
    }
}

enum ExperimentMode { None, Receiver, Sender, LargeMessageIntervals }

sealed class Settings
{
    [Option('C', "connection-string", HelpText = "Connection string; prefer SERVICEBUS_CONNECTION_STRING.")]
    public string? ConnectionString { get; set; }
    [Option("namespace", HelpText = "Fully qualified namespace for DefaultAzureCredential; or SERVICEBUS_NAMESPACE.")]
    public string? FullyQualifiedNamespace { get; set; }
    [Option('S', "entity-path", HelpText = "Queue, topic:subscription, or topic/subscriptions/subscription.")]
    public string? QueueName { get; set; }
    [Option('b', "payload-size-bytes", HelpText = "Payload bytes (default 1024).")]
    public int PayloadSizeInBytes { get; set; } = 1024;
    [Option('f', "frequency-metrics", HelpText = "Display interval seconds (default 10).")]
    public int MetricsDisplayFrequency { get; set; } = 10;
    [Option('m', "receive-mode", HelpText = "true for PeekLock (default), false for ReceiveAndDelete.")]
    public bool IsPeekLock { get; set; } = true;
    [Option('r', "receiver-count", HelpText = "Receivers (default 5); zero sends only.")]
    public int ReceiverCount { get; set; } = 5;
    [Option('p', "prefetch-count", HelpText = "Prefetch per receiver (default 100).")]
    public int PrefetchCount { get; set; } = 100;
    [Option('t', "send-batch-size", HelpText = "Maximum batch messages (default 1).")]
    public int SendBatchSize { get; set; } = 1;
    [Option('s', "sender-count", HelpText = "Senders (default 1); zero receives only.")]
    public int SenderCount { get; set; } = 1;
    [Option('d', "send-callIntervalMs", HelpText = "Minimum operation interval per pipeline in ms (default 1000).")]
    public int SendCallIntervalMs { get; set; } = 1000;
    [Option('e', "receive-callIntervalMs", HelpText = "Receive wait in ms (default 5000).")]
    public int ReceiveCallIntervalMs { get; set; } = 5000;
    [Option('i', "inflight-sends", HelpText = "In-flight operations per sender (default 1).")]
    public int MaxInflightSends { get; set; } = 1;
    [Option('j', "inflight-receives", HelpText = "In-flight operations per receiver (default 1).")]
    public int MaxInflightReceives { get; set; } = 1;
    [Option('v', "receive-batch-size", HelpText = "Maximum receive batch (default 1).")]
    public int ReceiveBatchSize { get; set; } = 1;
    [Option("duration-seconds", HelpText = "Continuous run duration (default 300, max 86400).")]
    public int DurationSeconds { get; set; } = 300;
    [Option("experiment", HelpText = "None, Receiver, Sender, LargeMessageIntervals. Former optional code experiments.")]
    public ExperimentMode Experiment { get; set; }

    internal ThroughputTest.Settings ToWorkload(Func<string, string?>? environment = null)
    {
        if (DurationSeconds < 1 || DurationSeconds > 86400 || ReceiveCallIntervalMs < 1 || ReceiveCallIntervalMs > 3_600_000 || !Enum.IsDefined(Experiment))
            throw new ArgumentException("Invalid duration, receive wait or experiment.");
        var path = QueueName == null ? null : Receiver.NormalizePath(QueueName);
        var entity = path == null ? null : EntityAddress.Parse(path).Entity;
        var result = new ThroughputTest.Settings
        {
            ConnectionString = ConnectionString, FullyQualifiedNamespace = FullyQualifiedNamespace,
            SendPath = entity, ReceivePaths = path == null ? [] : [path], MessageSizeInBytes = PayloadSizeInBytes,
            MetricsDisplayFrequency = MetricsDisplayFrequency, ReceiveMode = IsPeekLock ? ServiceBusReceiveMode.PeekLock : ServiceBusReceiveMode.ReceiveAndDelete,
            SenderCount = SenderCount, ReceiverCount = ReceiverCount, SendBatchCount = SendBatchSize, ReceiveBatchCount = ReceiveBatchSize,
            CfgMaxInflightSends = MaxInflightSends, CfgMaxInflightReceives = MaxInflightReceives, PrefetchCount = PrefetchCount, SendDelay = SendCallIntervalMs
        };
        result.Validate(environment);
        return result;
    }

    internal IEnumerable<(ThroughputTest.Settings Settings, int Seconds)> Scenarios(ThroughputTest.Settings settings)
    {
        if (Experiment == ExperimentMode.None) { yield return (settings, DurationSeconds); yield break; }
        if (Experiment == ExperimentMode.Receiver)
        {
            settings.SenderCount = 0; settings.ReceiverCount = 1; settings.PrefetchCount = 10240;
            settings.ReceiveBatchCount = 10; settings.CfgMaxInflightReceives = 1024; settings.ReceiveMode = ServiceBusReceiveMode.PeekLock;
            ReceiveCallIntervalMs = 60000;
            yield return (settings, 120); yield break;
        }
        settings.SenderCount = 1; settings.ReceiverCount = 0; settings.CfgMaxInflightSends = 1; settings.SendBatchCount = 25;
        settings.MessageSizeInBytes = Experiment == ExperimentMode.Sender ? 600 : 25 * 1024;
        // The original "batchSize" variable was passed as callIntervalMS, not as batchSize.
        foreach (var interval in Experiment == ExperimentMode.Sender ? new[] { 8 } : new[] { 256, 128, 64, 32, 16, 8, 4, 2, 1 })
        { settings.SendDelay = interval; yield return (settings, 300); }
    }
}
