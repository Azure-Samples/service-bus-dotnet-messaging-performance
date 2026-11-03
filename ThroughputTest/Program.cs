using Azure.Messaging.ServiceBus;
using CommandLine;
using CommandLine.Text;

namespace ThroughputTest;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => CancelFromConsole(stop, () => e.Cancel = true);
        Console.CancelKeyPress += handler;
        using var inputStop = new CancellationTokenSource();
        var input = Console.IsInputRedirected ? Task.CompletedTask : WatchEnterAsync(stop, inputStop.Token);
        try { return await ExecuteAsync(args, Console.Out, Console.Error, stop.Token).ConfigureAwait(false); }
        finally
        {
            inputStop.Cancel();
            await input.ConfigureAwait(false);
            Console.CancelKeyPress -= handler;
        }
    }

    internal static void CancelFromConsole(CancellationTokenSource stop, Action suppressTermination)
    {
        suppressTermination();
        stop.Cancel();
    }

    internal static async Task WatchEnterAsync(CancellationTokenSource stop, CancellationToken token,
        Func<bool>? available = null, Func<ConsoleKey>? read = null)
    {
        available ??= () => Console.KeyAvailable;
        read ??= () => Console.ReadKey(true).Key;
        try
        {
            while (!token.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                if (available() && read() == ConsoleKey.Enter) { stop.Cancel(); return; }
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token,
        Func<Settings, ServiceBusClient>? factory = null, Func<string, string?>? environment = null, TimeProvider? time = null)
    {
        using var parser = new Parser(config => { config.HelpWriter = null; config.CaseInsensitiveEnumValues = true; });
        var parsed = parser.ParseArguments<Settings>(args);
        if (parsed is NotParsed<Settings> invalid)
        {
            if (invalid.Errors.All(e => e.Tag is ErrorType.HelpRequestedError or ErrorType.VersionRequestedError))
            { output.WriteLine(HelpText.AutoBuild(parsed)); return 0; }
            // Never echo token values: an unknown/malformed option may itself contain a secret.
            error.WriteLine("Invalid command line. Use --help for supported options.");
            return 2;
        }
        var settings = ((Parsed<Settings>)parsed).Value;
        try { settings.Validate(environment); }
        catch (ArgumentException ex) { error.WriteLine(ex.Message); return 2; }

        time ??= TimeProvider.System;
        output.WriteLine("Press ENTER or Ctrl+C to stop. The overall deadline also stops incomplete workloads.");
        try
        {
            await new ServiceBusPerformanceApp(settings, new Metrics(time), output, time, factory).Run(token).ConfigureAwait(false);
            output.WriteLine("Complete");
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { error.WriteLine("Stopped before completion."); return 130; }
        catch (TimeoutException)
        { error.WriteLine("Run or cleanup deadline exceeded; results are incomplete."); return 1; }
        catch (Exception ex)
        {
            error.WriteLine(ex is ServiceBusException sb
                ? $"Service Bus failed: {sb.Reason}. Results are incomplete."
                : $"Run failed: {ex.GetType().Name}. Results are incomplete.");
            return 1;
        }
    }
}
