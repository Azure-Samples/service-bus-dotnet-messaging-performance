using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Time.Testing;
using Offline;
using Xunit;
using V2Program = ServiceBusThroughputTest.Program;

namespace ThroughputTest.V2.Tests;

public class ReceiveModeTests
{
    [Theory]
    [InlineData("-m", "false")]
    [InlineData("--receive-mode", "false")]
    [InlineData("--receive-mode=false", null)]
    public async Task Cli_false_receive_mode_never_settles_messages(string option, string? value)
    {
        var time = new FakeTimeProvider();
        var bus = new FakeBus();
        bus.Seed("queue", 1);
        var consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        bus.OnReceive = async token =>
        {
            if (Interlocked.Increment(ref calls) > 1)
            {
                consumed.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        var args = new List<string>
        {
            "--namespace", "offline.servicebus.windows.net", "-S", "queue", "-s", "0",
            "-r", "1", "--duration-seconds", "1", option
        };
        if (value is not null) args.Add(value);
        var run = V2Program.ExecuteAsync(args.ToArray(), TextWriter.Null, TextWriter.Null,
            default, _ => bus, time, _ => null);
        await consumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, bus.Received);
        Assert.Equal(0, bus.Completed);
    }

    [Theory]
    [InlineData("-m", "true", ServiceBusReceiveMode.PeekLock)]
    [InlineData("--receive-mode", "true", ServiceBusReceiveMode.PeekLock)]
    [InlineData("--receive-mode=true", null, ServiceBusReceiveMode.PeekLock)]
    [InlineData("-m", "false", ServiceBusReceiveMode.ReceiveAndDelete)]
    [InlineData("--receive-mode", "false", ServiceBusReceiveMode.ReceiveAndDelete)]
    [InlineData("--receive-mode=false", null, ServiceBusReceiveMode.ReceiveAndDelete)]
    [InlineData("-m", "FALSE", ServiceBusReceiveMode.ReceiveAndDelete)]
    [InlineData("--receive-mode", "TRUE", ServiceBusReceiveMode.PeekLock)]
    [InlineData("-m", null, ServiceBusReceiveMode.PeekLock)]
    [InlineData("--receive-mode", null, ServiceBusReceiveMode.PeekLock)]
    public async Task Cli_explicit_mode_selects_expected_receiver(
        string option, string? value, ServiceBusReceiveMode expected)
    {
        var args = new List<string> { option };
        if (value is not null) args.Add(value);
        await AssertReceiverMode(args.ToArray(), expected);
    }

    [Theory]
    [InlineData(null, ServiceBusReceiveMode.PeekLock)]
    [InlineData("true", ServiceBusReceiveMode.PeekLock)]
    [InlineData("false", ServiceBusReceiveMode.ReceiveAndDelete)]
    public async Task Cli_receive_mode_matches_documented_default_and_explicit_values(
        string? value, ServiceBusReceiveMode expected)
    {
        await AssertReceiverMode(value is null ? [] : ["-m", value], expected);
    }

    [Theory]
    [InlineData("-m", "invalid")]
    [InlineData("-m", "0")]
    [InlineData("-m", "1")]
    [InlineData("--receive-mode", "invalid")]
    [InlineData("--receive-mode=invalid")]
    [InlineData("--receive-mode=")]
    public async Task Cli_invalid_mode_fails_before_client_construction(params string[] modeArgs)
    {
        var args = new[]
        {
            "--namespace", "offline.servicebus.windows.net", "-S", "queue", "-s", "0",
            "--duration-seconds", "1"
        }.Concat(modeArgs).ToArray();
        var error = new StringWriter();
        var created = false;
        var code = await V2Program.ExecuteAsync(args, TextWriter.Null, error, default,
            _ => { created = true; return new FakeBus(); }, environment: _ => null);
        Assert.Equal(2, code);
        Assert.False(created);
        Assert.NotEmpty(error.ToString());
    }

    private static async Task AssertReceiverMode(string[] modeArgs, ServiceBusReceiveMode expected)
    {
        var time = new FakeTimeProvider();
        var bus = new FakeBus();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.OnReceive = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        var args = new List<string>
        {
            "--namespace", "offline.servicebus.windows.net", "-S", "queue", "-s", "0",
            "-r", "1", "--duration-seconds", "1"
        };
        args.AddRange(modeArgs);
        var run = V2Program.ExecuteAsync(args.ToArray(), TextWriter.Null, TextWriter.Null,
            default, _ => bus, time, _ => null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(bus.Closed);
        Assert.Equal(expected, Assert.Single(bus.Receivers).Options.ReceiveMode);
    }
}
