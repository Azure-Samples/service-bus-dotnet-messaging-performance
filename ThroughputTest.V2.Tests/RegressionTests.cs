using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Time.Testing;
using Offline;
using ServiceBusThroughputTest;
using ServiceBusThroughputTest.Common;
using ServiceBusThroughputTestLib;
using Xunit;
using CoreSettings = ThroughputTest.Settings;
using V2Program = ServiceBusThroughputTest.Program;
using V2Settings = ServiceBusThroughputTest.Settings;

namespace ThroughputTest.V2.Tests;

public class RegressionTests
{
    private static CoreSettings Settings() => new() { FullyQualifiedNamespace = "offline.servicebus.windows.net", SendPath = "queue", ReceivePaths = ["queue"] };
    private static async Task Stop(Task run, CancellationTokenSource stop, FakeBus bus)
    {
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(bus.Closed);
        Assert.All(bus.Senders, s => Assert.True(s.Closed)); Assert.All(bus.Receivers, r => Assert.True(r.Closed));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 3, 2)]
    [InlineData(3, 1, 1)]
    [InlineData(10, 4, 2)]
    public async Task Continuous_sender_counts_real_messages_batches_and_owns_only_its_clients(int batch, int inflight, int senders)
    {
        var settings = Settings(); settings.ReceiverCount = 0; settings.SenderCount = senders;
        settings.SendBatchCount = batch; settings.CfgMaxInflightSends = inflight;
        var bus = new FakeBus { BatchCapacity = 3 };
        var log = new Logger();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        bus.OnSend = async ct => { if (Interlocked.Increment(ref calls) > 6) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); } };
        using var stop = new CancellationTokenSource();
        var run = new Sender(settings, log, _ => bus, TimeProvider.System).Run(stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Stop(run, stop, bus);
        Assert.Equal(bus.SentBatches.Sum(), log.Messages); Assert.Equal(bus.SentBatches.Count, log.Actions);
        Assert.InRange(log.Messages, 1, 18); Assert.Equal(0, log.Sessions); Assert.False(log.Disposed);
        Assert.All(bus.SentBatches, n => Assert.InRange(n, 1, 3));
    }

    public static IEnumerable<object[]> ReceiveMatrix()
    {
        foreach (var batch in new[] { 0, 1, 3 })
        foreach (var count in new[] { 1, 5 })
        foreach (var inflight in new[] { 1, 3 })
        foreach (var mode in new[] { ServiceBusReceiveMode.PeekLock, ServiceBusReceiveMode.ReceiveAndDelete })
            yield return [batch, count, inflight, mode];
    }

    [Theory, MemberData(nameof(ReceiveMatrix))]
    public async Task Continuous_receive_variants_drain_and_only_settle_peeklock(int batch, int count, int inflight, ServiceBusReceiveMode mode)
    {
        var settings = Settings(); settings.SenderCount = 0; settings.ReceiveBatchCount = batch; settings.ReceiverCount = count;
        settings.CfgMaxInflightReceives = inflight; settings.ReceiveMode = mode;
        var bus = new FakeBus(); bus.Seed("queue", 11);
        var log = new Logger(); using var stop = new CancellationTokenSource();
        var run = new Receiver(settings, 50, log, _ => bus, TimeProvider.System).Run(stop.Token);
        await Until(() => log.Messages == 11 && (mode == ServiceBusReceiveMode.ReceiveAndDelete || bus.Completed == 11));
        await Stop(run, stop, bus);
        Assert.Equal(11, log.Messages); Assert.Equal(0, log.Errors); Assert.Equal(0, log.Sessions);
        Assert.Equal(mode == ServiceBusReceiveMode.PeekLock ? 11 : 0, bus.Completed);
        Assert.All(bus.Receivers, r => Assert.Equal(100, r.Options.PrefetchCount));
    }

    [Theory]
    [InlineData("send")]
    [InlineData("receive")]
    [InlineData("complete")]
    public async Task Failures_propagate_without_disposing_caller_logger(string operation)
    {
        var settings = Settings(); var bus = new FakeBus(); var log = new Logger();
        Task Fail(CancellationToken _) => Task.FromException(new ServiceBusException("contains-secret", ServiceBusFailureReason.ServiceBusy));
        Task run;
        if (operation == "send")
        {
            settings.ReceiverCount = 0; bus.OnSend = Fail;
            run = new Sender(settings, log, _ => bus, TimeProvider.System).Run(default);
        }
        else
        {
            settings.SenderCount = 0; bus.Seed("queue", 10);
            if (operation == "receive") bus.OnReceive = Fail; else bus.OnComplete = Fail;
            run = new Receiver(settings, 50, log, _ => bus, TimeProvider.System).Run(default);
        }
        await Assert.ThrowsAsync<ServiceBusException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(bus.Closed); Assert.True(log.Busy > 0); Assert.True(log.Errors > 0);
        Assert.Equal(0, log.Sessions); Assert.DoesNotContain("contains-secret", log.Trace); Assert.False(log.Disposed);
    }

    [Fact]
    public async Task Continuous_send_pacing_works_without_a_logger_and_cancels()
    {
        var time = new FakeTimeProvider(); var settings = Settings(); settings.ReceiverCount = 0; settings.SendDelay = 1000;
        var bus = new FakeBus(); using var stop = new CancellationTokenSource();
        var run = new Sender(settings, null, _ => bus, time).Run(stop.Token);
        await Until(() => bus.SentBatches.Count == 1);
        time.Advance(TimeSpan.FromMilliseconds(999)); await Task.Delay(20); Assert.Single(bus.SentBatches);
        time.Advance(TimeSpan.FromMilliseconds(1)); await Until(() => bus.SentBatches.Count == 2);
        await Stop(run, stop, bus);
    }

    [Theory]
    [InlineData("topic:one", "topic", "topic/subscriptions/one")]
    [InlineData("topic/subscriptions/one", "topic", "topic/subscriptions/one")]
    [InlineData("queue/$DeadLetterQueue", "queue", "queue/$DeadLetterQueue")]
    public void V2_entity_path_syntax_preserved(string path, string send, string receive)
    {
        var options = new V2Settings { FullyQualifiedNamespace = "offline.servicebus.windows.net", QueueName = path, SenderCount = path.Contains('$') ? 0 : 1 };
        var settings = options.ToWorkload(_ => null);
        Assert.Equal(send, settings.SendPath); Assert.Equal(receive, settings.ReceivePaths.Single());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Metrics_keep_exact_totals_when_latency_samples_are_capped(bool sending)
    {
        var time = new FakeTimeProvider();
        using var metrics = new Metrics(default, sending, 100, 2, time);
        MetricsDisplayEventArgs? display = null;
        metrics.OnMetricsDisplay += (_, e) => display = e;
        for (var i = 0; i < 10; i++)
        {
            var id = metrics.StartRecordTime();
            time.Advance(TimeSpan.FromMilliseconds(2));
            Assert.Equal(2, metrics.StopRecordTimeAndGetElapsedMs(id), 4);
            metrics.IncrementActionCount(id); metrics.IncrementMetricValueBy(id, 3);
            metrics.DisposeSession(id);
        }
        metrics.Flush();
        Assert.NotNull(display); Assert.Equal(30, display.Messages); Assert.Equal(10, display.Actions);
        Assert.Equal(2, sending ? display.SendMetricsList.Count : display.ReceiveMetricsList.Count);
        Assert.Equal(1500, display.Messages / (display.EndSeconds - display.StartSeconds), 2);
        metrics.Flush(); Assert.Equal(0, display.Messages);
        Assert.Equal(99, V2Program.Percentile(Enumerable.Range(0, 101).Select(i => (long)i).ToArray(), .99));
    }

    [Fact]
    public async Task Metrics_callback_errors_are_observable()
    {
        var time = new FakeTimeProvider(); var metrics = new Metrics(default, true, 1, 2, time);
        metrics.OnMetricsDisplay += (_, _) => throw new InvalidOperationException("fake output failure");
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => metrics.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Throws<InvalidOperationException>(() => metrics.Dispose());
    }

    [Fact]
    public void App_config_and_optional_experiment_inventory()
    {
        Assert.Equal((2000, 10000), V2Program.ReadCapacities(Path.Combine(AppContext.BaseDirectory, "App.config")));
        foreach (var mode in Enum.GetValues<ServiceBusThroughputTest.ExperimentMode>())
        {
            var options = new V2Settings { Experiment = mode };
            var snapshots = options.Scenarios(Settings()).Select(x => (x.Settings.SendDelay, x.Settings.SendBatchCount,
                x.Settings.MessageSizeInBytes, x.Settings.ReceiverCount, x.Seconds)).ToArray();
            Assert.Equal(mode == ServiceBusThroughputTest.ExperimentMode.LargeMessageIntervals ? 9 : 1, snapshots.Length);
            if (mode == ServiceBusThroughputTest.ExperimentMode.LargeMessageIntervals)
            {
                Assert.Equal(new[] { 256, 128, 64, 32, 16, 8, 4, 2, 1 }, snapshots.Select(s => s.SendDelay));
                Assert.All(snapshots, s => { Assert.Equal(25, s.SendBatchCount); Assert.Equal(25 * 1024, s.MessageSizeInBytes); Assert.Equal(300, s.Seconds); });
            }
            if (mode == ServiceBusThroughputTest.ExperimentMode.Receiver) Assert.Equal(120, snapshots[0].Seconds);
        }
    }

    public static IEnumerable<object[]> InvalidArguments()
    {
        yield return [Array.Empty<string>()];
        yield return [new[] { "--unknown-secret" }];
        foreach (var pair in new[] { ("-b","0"),("-f","0"),("-r","-1"),("-s","-1"),("-p","-1"),("-t","-1"),
            ("-v","-1"),("-d","-1"),("-e","0"),("-i","0"),("-j","0"),("--duration-seconds","0"),("--experiment","invalid") })
            yield return [new[] { "--namespace", "offline.servicebus.windows.net", "-S", "queue", pair.Item1, pair.Item2 }];
    }

    [Theory, MemberData(nameof(InvalidArguments))]
    public async Task Invalid_v2_CLI_rejects_before_constructing_clients(string[] args)
    {
        var created = false; var error = new StringWriter();
        Assert.Equal(2, await V2Program.ExecuteAsync(args, TextWriter.Null, error, default,
            _ => { created = true; return new FakeBus(); }, environment: _ => null));
        Assert.False(created); Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Sender")]
    [InlineData("Receiver")]
    [InlineData("LargeMessageIntervals")]
    public async Task V2_CLI_duration_stops_all_work_and_preserves_experiments(string experiment)
    {
        var time = new FakeTimeProvider(); var buses = new List<FakeBus>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeBus Factory(CoreSettings _)
        {
            var bus = new FakeBus(); buses.Add(bus);
            async Task Stall(CancellationToken ct) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            bus.OnSend = bus.OnReceive = Stall; return bus;
        }
        var run = V2Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue", "-b", "64",
            "-f", "1", "-m", "false", "-r", "1", "-s", "1", "-p", "0", "-t", "3", "-v", "3", "-d", "0", "-e", "50",
            "-i", "2", "-j", "2", "--duration-seconds", "1", "--experiment", experiment],
            TextWriter.Null, TextWriter.Null, default, Factory, time, _ => null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotEmpty(buses); Assert.All(buses, b => Assert.True(b.Closed));
    }

    [Fact]
    public async Task V2_help_is_offline()
    {
        Assert.Equal(0, await V2Program.ExecuteAsync(["--help"], TextWriter.Null, TextWriter.Null, default,
            _ => throw new Exception("Unexpected client"), environment: _ => null));
    }

    [Theory]
    [InlineData("Receiver", 1, 120)]
    [InlineData("Sender", 1, 300)]
    [InlineData("LargeMessageIntervals", 9, 300)]
    public async Task Full_optional_experiment_chains_complete_with_virtual_time(string experiment, int steps, int seconds)
    {
        var time = new FakeTimeProvider(); var buses = new List<FakeBus>(); var intervals = new List<int>();
        var entered = 0;
        var run = V2Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue",
            "--duration-seconds", "86400", "--experiment", experiment], TextWriter.Null, TextWriter.Null, default, settings =>
            {
                var bus = new FakeBus(); buses.Add(bus); intervals.Add(settings.SendDelay);
                var first = 0;
                async Task Stall(CancellationToken ct)
                {
                    if (Interlocked.Exchange(ref first, 1) == 0) Interlocked.Increment(ref entered);
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                bus.OnSend = bus.OnReceive = Stall; return bus;
            }, time, _ => null);
        for (var i = 1; i <= steps; i++)
        {
            var expected = i;
            await Until(() => Volatile.Read(ref entered) == expected);
            time.Advance(TimeSpan.FromSeconds(seconds));
        }
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(steps, buses.Count); Assert.All(buses, b => Assert.True(b.Closed));
        if (steps == 9) Assert.Equal(new[] { 256, 128, 64, 32, 16, 8, 4, 2, 1 }, intervals);
    }

    [Fact]
    public async Task V2_CLI_long_options_failure_is_nonzero_and_cancels_sibling()
    {
        var buses = new List<FakeBus>();
        var error = new StringWriter();
        var receiveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var code = await V2Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "--entity-path", "topic:one",
            "--payload-size-bytes", "600", "--frequency-metrics", "1", "--receive-mode", "true", "--receiver-count", "1",
            "--sender-count", "1", "--prefetch-count", "8", "--send-batch-size", "3", "--receive-batch-size", "3",
            "--send-callIntervalMs", "0", "--receive-callIntervalMs", "50", "--inflight-sends", "2", "--inflight-receives", "2"],
            TextWriter.Null, error, default, _ =>
            {
                var bus = new FakeBus
                {
                    OnSend = async ct => { await receiveStarted.Task.WaitAsync(ct); throw new IOException("secret"); },
                    OnReceive = ct => { receiveStarted.TrySetResult(); return Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                };
                buses.Add(bus); return bus;
            }, environment: _ => null);
        Assert.Equal(1, code); Assert.DoesNotContain("secret", error.ToString());
        Assert.Equal(2, buses.Count); Assert.All(buses, b => Assert.True(b.Closed));
    }

    [Fact]
    public async Task V2_CLI_external_stop_is_nonzero_and_config_errors_are_explicit()
    {
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bus = new FakeBus();
        bus.OnSend = async ct => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); };
        var run = V2Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue", "-r", "0"],
            TextWriter.Null, TextWriter.Null, stop.Token, _ => bus, environment: _ => null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        Assert.Equal(130, await run.WaitAsync(TimeSpan.FromSeconds(5))); Assert.True(bus.Closed);
        var config = Path.GetTempFileName();
        try
        {
            File.WriteAllText(config, "<configuration><appSettings><add key=\"MetricsSamplesPerDisplayIntervalSender\" value=\"-1\"/></appSettings></configuration>");
            Assert.Equal(2, await V2Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue"],
                TextWriter.Null, TextWriter.Null, default, _ => throw new Exception("Unexpected client"), environment: _ => null, configPath: config));
        }
        finally { File.Delete(config); }
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(1, timeout.Token);
        await Task.Delay(20);
    }
}

sealed class Logger : ILogger
{
    public int Messages, Actions, Errors, Busy, Sessions;
    public bool Disposed;
    public string Trace = "";
    public Guid StartRecordTime() { Interlocked.Increment(ref Sessions); return Guid.NewGuid(); }
    public void StartCompletionRecordTime(Guid sessionId) { }
    public double StopCompletionRecordTimeAndGetElapsedMs(Guid sessionId) => 0;
    public double StopRecordTimeAndGetElapsedMs(Guid sessionId) => 0;
    public void IncrementActionCount(Guid sessionId) => Interlocked.Increment(ref Actions);
    public void IncrementMetricValueBy(Guid sessionId, int count) => Interlocked.Add(ref Messages, count);
    public void IncrementErrorCount(Guid sessionId) => Interlocked.Increment(ref Errors);
    public void IncrementBusyErrorCount(Guid sessionId) => Interlocked.Increment(ref Busy);
    public void DisposeSession(Guid sessionId) => Interlocked.Decrement(ref Sessions);
    public void AddTrace(string trace) { lock (this) Trace += trace; }
    public void Dispose() => Disposed = true;
}
