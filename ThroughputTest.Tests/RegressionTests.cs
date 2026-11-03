using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Time.Testing;
using Offline;
using System.Net.Sockets;
using System.Text.Json;
using ThroughputTest;
using Xunit;

namespace ThroughputTest.Tests;

public class RegressionTests
{
    private static Settings Settings() => new() { FullyQualifiedNamespace = "offline.servicebus.windows.net", SendPath = "queue", MessageCount = 10 };
    private static async Task<Metrics> Run(Settings settings, FakeBus bus)
    {
        settings.Validate(_ => null);
        var metrics = new Metrics(TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await new ServiceBusPerformanceApp(settings, metrics, TextWriter.Null, TimeProvider.System, _ => bus).Run(timeout.Token);
        Assert.True(bus.Closed);
        Assert.All(bus.Senders, s => Assert.True(s.Closed));
        Assert.All(bus.Receivers, r => Assert.True(r.Closed));
        return metrics;
    }

    [Theory]
    [InlineData(10, 2, 5)]
    [InlineData(10, 3, 4)]
    [InlineData(100, 10, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 1, 10)]
    [InlineData(1, 20, 1)]
    [InlineData(0, 2, 0)]
    public async Task Exact_full_partial_single_and_zero_sends(int count, int batch, int operations)
    {
        var settings = Settings(); settings.MessageCount = count; settings.SendBatchCount = batch; settings.ReceiverCount = 0;
        settings.CfgMaxInflightSends = 4;
        var bus = new FakeBus();
        if (count == 0)
        {
            var metrics = new Metrics(TimeProvider.System);
            await new ServiceBusPerformanceApp(settings, metrics, TextWriter.Null, TimeProvider.System,
                _ => throw new InvalidOperationException("Zero work constructed a client")).Run(default);
            return;
        }
        var result = await Run(settings, bus);
        Assert.Equal(count, result.TotalSent);
        Assert.Equal(count, bus.SentBatches.Sum());
        Assert.Equal(operations, bus.SentBatches.Count);
    }

    [Fact]
    public async Task Byte_limited_batches_return_unsent_reservations()
    {
        var settings = Settings(); settings.SendBatchCount = 8; settings.ReceiverCount = 0;
        var bus = new FakeBus { BatchCapacity = 3 };
        var result = await Run(settings, bus);
        Assert.Equal(10, result.TotalSent);
        Assert.Equal(new[] { 1, 3, 3, 3 }, bus.SentBatches.Order().ToArray());
    }

    public static IEnumerable<object[]> ReceiveMatrix()
    {
        foreach (var receivers in new[] { 1, 5 })
        foreach (var batch in new[] { 0, 1, 3 })
        foreach (var mode in new[] { ServiceBusReceiveMode.PeekLock, ServiceBusReceiveMode.ReceiveAndDelete })
        foreach (var work in new[] { 0, 1 })
        foreach (var senders in new[] { 0, 1, 2 })
            yield return [receivers, batch, mode, work, senders];
    }

    [Theory, MemberData(nameof(ReceiveMatrix))]
    public async Task Receive_variants_share_targets_and_settle_only_peeklock(int receivers, int batch, ServiceBusReceiveMode mode, int work, int senders)
    {
        var settings = Settings();
        settings.ReceiverCount = receivers; settings.ReceiveBatchCount = batch; settings.ReceiveMode = mode;
        settings.WorkDuration = work; settings.SenderCount = senders; settings.CfgMaxInflightReceives = 3; settings.SendBatchCount = 3;
        var bus = new FakeBus();
        if (senders == 0) bus.Seed("queue", 10);
        var result = await Run(settings, bus);
        var expected = 10 * Math.Max(1, senders);
        Assert.Equal(expected, result.TotalReceived);
        Assert.Equal(expected, bus.Received);
        Assert.Equal(mode == ServiceBusReceiveMode.PeekLock ? expected : 0, bus.Completed);
        Assert.Equal(10 * senders, result.TotalSent);
    }

    [Theory]
    [InlineData("topic", "topic/subscriptions/one")]
    [InlineData("topic", "topic/subscriptions/one,topic/subscriptions/two")]
    [InlineData("source", "forwarded")]
    [InlineData("queue", "queue/$DeadLetterQueue")]
    [InlineData("topic", "topic/subscriptions/one/$DeadLetterQueue")]
    [InlineData("queue", "queue/$Transfer/$DeadLetterQueue")]
    public async Task Topology_paths_and_prefetch_use_correct_sdk_overloads(string send, string paths)
    {
        var settings = Settings(); settings.SendPath = send; settings.ReceivePaths = paths.Split(',');
        settings.PrefetchCount = 37;
        var destinations = settings.ReceivePaths.Select(p => EntityAddress.Parse(p))
            .Select(p => p.Subscription == null ? p.Entity : $"{p.Entity}/subscriptions/{p.Subscription}").ToArray();
        var bus = new FakeBus { Destinations = destinations };
        var result = await Run(settings, bus);
        Assert.Equal(10 * destinations.Length, result.TotalReceived);
        Assert.All(bus.Receivers, r => Assert.Equal(37, r.Options.PrefetchCount));
        if (paths.Contains("$DeadLetterQueue"))
            Assert.All(bus.Receivers, r => Assert.Equal(paths.Contains("$Transfer") ? SubQueue.TransferDeadLetter : SubQueue.DeadLetter, r.Options.SubQueue));
    }

    [Theory]
    [InlineData("send")]
    [InlineData("receive")]
    [InlineData("complete")]
    public async Task Busy_failures_cancel_siblings_observe_errors_and_close(string operation)
    {
        var settings = Settings(); settings.SendBatchCount = 3; settings.CfgMaxInflightReceives = 3;
        var bus = new FakeBus();
        Task Fail(CancellationToken _) => Task.FromException(new ServiceBusException("synthetic", ServiceBusFailureReason.ServiceBusy));
        if (operation == "send") bus.OnSend = Fail;
        if (operation == "receive") bus.OnReceive = Fail;
        if (operation == "complete") bus.OnComplete = Fail;
        var output = new StringWriter();
        var code = await Program.ExecuteAsync(["--namespace", settings.FullyQualifiedNamespace!, "-S", "queue", "-n", "10"],
            output, new StringWriter(), default, _ => bus, _ => null);
        Assert.Equal(1, code); Assert.True(bus.Closed);
        Assert.All(bus.Senders, s => Assert.True(s.Closed)); Assert.All(bus.Receivers, r => Assert.True(r.Closed));
        Assert.DoesNotContain("Complete\n", output.ToString());
        Assert.True(Metrics.IsBusy(new AggregateException(new ServiceBusException("synthetic", ServiceBusFailureReason.ServiceBusy))));
    }

    [Fact]
    public async Task Host_not_found_is_observed_and_not_exposed_with_secrets()
    {
        var bus = new FakeBus { OnSend = _ => Task.FromException(new SocketException((int)SocketError.HostNotFound)) };
        var error = new StringWriter();
        Assert.Equal(1, await Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue", "-r", "0"],
            TextWriter.Null, error, default, _ => bus, _ => null));
        Assert.Contains("SocketException", error.ToString()); Assert.True(bus.Closed);
    }

    [Theory]
    [InlineData("send")]
    [InlineData("receive")]
    [InlineData("complete")]
    public async Task Cancellation_drains_stalled_operation(string operation)
    {
        var bus = new FakeBus();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Stall(CancellationToken ct) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        if (operation == "send") bus.OnSend = Stall;
        if (operation == "receive") bus.OnReceive = Stall;
        if (operation == "complete") bus.OnComplete = Stall;
        using var stop = new CancellationTokenSource();
        var run = Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue"], TextWriter.Null,
            TextWriter.Null, stop.Token, _ => bus, _ => null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        Assert.Equal(130, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(bus.Closed);
        Assert.All(bus.Senders, s => Assert.True(s.Closed)); Assert.All(bus.Receivers, r => Assert.True(r.Closed));
    }

    [Fact]
    public async Task Deadline_and_empty_receives_do_not_fake_progress()
    {
        var time = new FakeTimeProvider();
        var bus = new FakeBus();
        var entered = new TaskCompletionSource();
        bus.OnReceive = ct => { entered.TrySetResult(); return Task.Delay(Timeout.InfiniteTimeSpan, ct); };
        var task = Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-R", "queue", "-s", "0", "--timeout-seconds", "1"],
            TextWriter.Null, TextWriter.Null, default, _ => bus, _ => null, time);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, bus.Received); Assert.True(bus.Closed);
    }

    [Fact]
    public async Task Empty_poll_is_an_operation_not_a_message_or_error()
    {
        var settings = Settings(); settings.SenderCount = 0; settings.ReceiverCount = 1; settings.MessageCount = 1;
        var bus = new FakeBus();
        var attempts = 0;
        bus.OnReceive = _ => { if (Interlocked.Increment(ref attempts) == 2) bus.Seed("queue", 1); return Task.CompletedTask; };
        settings.Validate(_ => null);
        var metrics = new Metrics(TimeProvider.System);
        await new ReceiverTask(settings, metrics, bus, TimeProvider.System).StartAsync(default);
        var snapshot = metrics.Snapshot();
        Assert.Equal(2, snapshot.Receives.Sum(m => m.Receives));
        Assert.Equal(1, snapshot.TotalReceived);
        Assert.Equal(0, snapshot.Receives.Sum(m => m.Errors));
    }

    [Fact]
    public async Task Gate_balances_failure_cancellation_resize_and_work()
    {
        var concurrency = new Observable<int>(3);
        using var gate = new DynamicSemaphoreSlim(concurrency);
        var active = 0; var peak = 0;
        await TaskEx.RunOperationsAsync(new WorkBudget(17), 3, gate, async (count, _, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref peak, current);
            try { await Task.Delay(5, ct); return count; }
            finally { Interlocked.Decrement(ref active); }
        }, TimeProvider.System, default);
        Assert.InRange(peak, 1, 3); Assert.Equal(0, gate.InUse);
        await gate.WaitAsync(default); await gate.WaitAsync(default);
        concurrency.Value = 1;
        using var stop = new CancellationTokenSource();
        var waiting = gate.WaitAsync(stop.Token);
        gate.Release(); Assert.False(waiting.IsCompleted);
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.Release(); Assert.Equal(0, gate.InUse);
        concurrency.Value = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => TaskEx.RunOperationsAsync(new WorkBudget(10), 2, gate,
            (_, _, _) => Task.FromException<int>(new InvalidOperationException()), TimeProvider.System, default));
        Assert.Equal(0, gate.InUse);
    }

    [Fact]
    public async Task Cleanup_is_bounded_and_observes_late_completion()
    {
        var time = new FakeTimeProvider(); var settings = Settings(); settings.CleanupTimeoutSeconds = 1;
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = TaskEx.CloseAsync(_ => close.Task, settings, time);
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => task);
        close.SetResult();
    }

    [Fact]
    public async Task Enter_and_external_stop_cancel_without_blocking_input_thread()
    {
        using var stop = new CancellationTokenSource();
        await Program.WatchEnterAsync(stop, default, () => true, () => ConsoleKey.Enter);
        Assert.True(stop.IsCancellationRequested);
        using var ctrlC = new CancellationTokenSource();
        var suppressed = false;
        Program.CancelFromConsole(ctrlC, () => suppressed = true);
        Assert.True(suppressed); Assert.True(ctrlC.IsCancellationRequested);
    }

    [Fact]
    public void Unique_metric_windows_and_millisecond_units()
    {
        var time = new FakeTimeProvider(); var metrics = new Metrics(time);
        metrics.PushSendMetrics(new SendMetrics { Messages = 3, Sends = 1, GateMilliseconds = 2, SendMilliseconds = 5 });
        time.Advance(TimeSpan.FromSeconds(2));
        var first = metrics.Snapshot(); Assert.Equal(3, first.TotalSent); Assert.Equal(1.5, first.SendRate);
        metrics.PushSendMetrics(new SendMetrics { Messages = 2, Sends = 1 });
        time.Advance(TimeSpan.FromSeconds(1));
        var second = metrics.Snapshot(); Assert.Equal(5, second.TotalSent); Assert.Equal(2, second.SendRate);
        Assert.Equal("2.00|2.00|0.00|2.00|2.00|", Metrics.Statistics(first.Sends.Select(m => m.GateMilliseconds)));
        Assert.Empty(metrics.Snapshot().Sends);
    }

    public static IEnumerable<object[]> InvalidArguments()
    {
        yield return [Array.Empty<string>()];
        yield return [new[] { "--unknown=secret" }];
        foreach (var pair in new[] { ("-n","-1"),("-n","9223372036854775808"),("-n","9223372036854775807"),
            ("-b","0"),("-b","104857601"),("-f","0"),("-r","-1"),("-s","-1"),("-e","-1"),("-t","-1"),("-v","-1"),
            ("-d","-1"),("-w","-1"),("-i","0"),("-j","0"),("-m","99"),("--timeout-seconds","0"),("--experiment","invalid"),("--experiment-steps","0") })
            yield return [new[] { "--namespace", "offline.servicebus.windows.net", "-S", "queue", "-s", "2", pair.Item1, pair.Item2 }];
        yield return [new[] { "-C", "secret-not-a-connection-string", "-S", "queue" }];
    }

    [Theory, MemberData(nameof(InvalidArguments))]
    public async Task Invalid_CLI_exits_nonzero_before_client_construction(string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter(); var created = false;
        var code = await Program.ExecuteAsync(args, output, error, default, _ => { created = true; return new FakeBus(); }, _ => null);
        Assert.Equal(2, code); Assert.False(created); Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task CLI_help_zero_work_and_every_short_option()
    {
        Assert.Equal(0, await Program.ExecuteAsync(["--help"], TextWriter.Null, TextWriter.Null, default, _ => throw new Exception(), _ => null));
        var args = new[] { "-S", "queue", "-n", "0", "-b", "12", "-f", "1", "-m", "ReceiveAndDelete", "-r", "1", "-s", "1",
            "-e", "0", "-t", "3", "-v", "3", "-d", "1", "-w", "1", "-i", "2", "-j", "2", "--experiment", "Both", "--experiment-steps", "1" };
        Assert.Equal(0, await Program.ExecuteAsync(args, TextWriter.Null, TextWriter.Null, default, _ => throw new Exception(), _ => null));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "launchSettings.json")));
        Assert.Equal("--help", json.RootElement.GetProperty("profiles").GetProperty("ServiceBusPerfSample").GetProperty("commandLineArgs").GetString());
    }

    [Fact]
    public async Task Long_options_connection_string_entity_path_and_environment_are_preserved()
    {
        const string connection = "Endpoint=sb://offline.servicebus.windows.net/;SharedAccessKeyName=offline;SharedAccessKey=dGVzdA==;EntityPath=queue";
        var bus = new FakeBus();
        Settings? actual = null;
        Assert.Equal(0, await Program.ExecuteAsync(["--connection-string", connection, "--number-of-messages", "10",
            "--message-size-bytes", "16", "--frequency-metrics", "1", "--receive-mode", "ReceiveAndDelete", "--receiver-count", "2",
            "--sender-count", "2", "--prefetch-count", "7", "--send-batch-count", "3", "--receive-batch-count", "3",
            "--send-delay", "0", "--receive-work-duration", "0", "--inflight-sends", "2", "--inflight-receives", "2"],
            TextWriter.Null, TextWriter.Null, default, settings => { actual = settings; return bus; }, _ => null));
        Assert.NotNull(actual); Assert.Equal("queue", actual.SendPath); Assert.Equal(connection, actual.ConnectionString);
        Assert.Equal(20, bus.SentBatches.Sum()); Assert.Equal(20, bus.Received); Assert.Equal(0, bus.Completed);
        Assert.Equal(16, bus.PayloadSize);
        var fromEnvironment = new Settings { SendPath = "queue", ReceiverCount = 0 };
        fromEnvironment.Validate(name => name == "SERVICEBUS_CONNECTION_STRING" ? connection : null);
        Assert.Equal(connection, fromEnvironment.ConnectionString);
        var conflicting = new Settings { ConnectionString = connection, SendPath = "different" };
        Assert.Throws<ArgumentException>(() => conflicting.Validate(_ => null));
    }

    [Fact]
    public async Task Send_delay_and_work_delay_use_milliseconds_and_cancel_cleanly()
    {
        var time = new FakeTimeProvider(); var settings = Settings(); settings.ReceiverCount = 0;
        settings.SendDelay = 100; settings.MessageCount = 1;
        var bus = new FakeBus(); var metrics = new Metrics(time);
        var sending = new SenderTask(settings, metrics, bus, time).StartAsync(default);
        time.Advance(TimeSpan.FromMilliseconds(99)); Assert.Empty(bus.SentBatches);
        time.Advance(TimeSpan.FromMilliseconds(1)); await sending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(bus.SentBatches);
        settings.SenderCount = 0; settings.ReceiverCount = 1; settings.ReceivePaths = ["queue"]; settings.WorkDuration = 100;
        var receiving = new ReceiverTask(settings, metrics, bus, time).StartAsync(default);
        await Until(() => bus.Received == 1);
        time.Advance(TimeSpan.FromMilliseconds(99)); Assert.Equal(0, bus.Completed);
        time.Advance(TimeSpan.FromMilliseconds(1)); await receiving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, bus.Completed);
    }

    [Fact]
    public async Task Oversized_batch_and_cleanup_failure_produce_nonzero_exit()
    {
        var bus = new FakeBus { BatchCapacity = 0 };
        Assert.Equal(1, await Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue", "-r", "0", "-t", "3", "-n", "1"],
            TextWriter.Null, TextWriter.Null, default, _ => bus, _ => null));
        Assert.Empty(bus.SentBatches); Assert.True(bus.Closed);
        bus = new FakeBus { OnClose = _ => Task.FromException(new IOException("secret")) };
        var error = new StringWriter();
        Assert.Equal(1, await Program.ExecuteAsync(["--namespace", "offline.servicebus.windows.net", "-S", "queue", "-r", "0", "-n", "1"],
            TextWriter.Null, error, default, _ => bus, _ => null));
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Experiments_use_seconds_bounded_steps_and_restore_on_cancel(bool sends)
    {
        var time = new FakeTimeProvider(); var metrics = new Metrics(time); var settings = Settings(); settings.ExperimentSteps = 1;
        Experiment experiment = sends ? new IncreaseInflightSendsExperiment(2, metrics, settings, time) : new IncreaseInflightReceivesExperiment(2, metrics, settings, time);
        var concurrency = sends ? settings.MaxInflightSends : settings.MaxInflightReceives;
        using var stop = new CancellationTokenSource();
        var run = experiment.Run(stop.Token);
        time.Advance(TimeSpan.FromMilliseconds(10)); Assert.Equal(1, concurrency.Value);
        time.Advance(TimeSpan.FromSeconds(10)); await Pump();
        Assert.Equal(1, concurrency.Value);
        time.Advance(TimeSpan.FromSeconds(5)); await Until(() => concurrency.Value == 3);
        time.Advance(TimeSpan.FromMilliseconds(20)); Assert.False(run.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, concurrency.Value);

        var complete = experiment.Run(default);
        time.Advance(TimeSpan.FromSeconds(10)); await Pump();
        time.Advance(TimeSpan.FromSeconds(5)); await Until(() => concurrency.Value == 3);
        time.Advance(TimeSpan.FromSeconds(20)); await Pump();
        if (sends) metrics.PushSendMetrics(new SendMetrics { Messages = 5 }); else metrics.PushReceiveMetrics(new ReceiveMetrics { Messages = 5 });
        time.Advance(TimeSpan.FromSeconds(5));
        await complete.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(3, concurrency.Value);
    }

    internal static async Task Pump() { await Task.Delay(20); }
    internal static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(1, timeout.Token);
        await Pump();
    }
}

static class InterlockedExtensions
{
    public static void Max(ref int target, int value)
    {
        int old;
        do { old = Volatile.Read(ref target); if (old >= value) return; } while (Interlocked.CompareExchange(ref target, value, old) != old);
    }
}
