using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace ThroughputTest;

sealed class ServiceBusPerformanceApp(Settings settings, Metrics metrics, TextWriter output, TimeProvider time,
    Func<Settings, ServiceBusClient>? clientFactory = null)
{
    public static ServiceBusClient CreateClient(Settings settings)
    {
        var options = new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions { MaxRetries = 3, TryTimeout = TimeSpan.FromSeconds(30) }
        };
        return !string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? new ServiceBusClient(settings.ConnectionString, options)
            : new ServiceBusClient(settings.FullyQualifiedNamespace, new DefaultAzureCredential(), options);
    }

    public async Task Run(CancellationToken token)
    {
        Metrics.WriteHeaders(output, settings);
        if (settings.MessageCount == 0) { metrics.Write(output, settings); return; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds), time);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        using var background = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        var client = (clientFactory ?? CreateClient)(settings);
        Task auxiliaries = Task.CompletedTask;
        try
        {
            auxiliaries = TaskEx.WhenAllFailFast(AuxiliaryOperations(), background.Token);
            await TaskEx.WhenAllFailFast([
                ct => new ReceiverTask(settings, metrics, client, time).StartAsync(ct),
                ct => new SenderTask(settings, metrics, client, time).StartAsync(ct)
            ], run.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        { throw new TimeoutException("Run deadline reached before the workload completed."); }
        finally
        {
            background.Cancel();
            try
            {
                try { await auxiliaries.ConfigureAwait(false); }
                catch (OperationCanceledException) when (background.IsCancellationRequested) { }
            }
            finally
            {
                try { metrics.Write(output, settings); }
                finally { await TaskEx.CloseAsync(_ => client.DisposeAsync().AsTask(), settings, time).ConfigureAwait(false); }
            }
        }

        IEnumerable<Func<CancellationToken, Task>> AuxiliaryOperations()
        {
            yield return async ct =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.MetricsDisplayFrequency), time);
                    while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) metrics.Write(output, settings);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { run.Cancel(); throw; }
            };
            if (settings.Experiment is ExperimentMode.Sends or ExperimentMode.Both)
                yield return ct => RunExperiment(new IncreaseInflightSendsExperiment(50, metrics, settings, time), ct);
            if (settings.Experiment is ExperimentMode.Receives or ExperimentMode.Both)
                yield return ct => RunExperiment(new IncreaseInflightReceivesExperiment(10, metrics, settings, time), ct);
        }

        async Task RunExperiment(Experiment experiment, CancellationToken ct)
        {
            try { await experiment.Run(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { run.Cancel(); throw; }
        }
    }
}
