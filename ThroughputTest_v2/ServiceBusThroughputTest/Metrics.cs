using ServiceBusThroughputTest.Common;
using System.Diagnostics;

namespace ServiceBusThroughputTest;

public sealed class Metrics : ILogger
{
    private readonly object sync = new();
    private readonly bool isSend;
    private readonly int capacity;
    private readonly TimeProvider time;
    private readonly long started;
    private long windowStarted;
    private readonly CancellationTokenSource stop;
    private readonly Dictionary<Guid, SendMetrics> sendSessions = [];
    private readonly Dictionary<Guid, ReceiveMetrics> receiveSessions = [];
    private readonly List<SendMetrics> sends = [];
    private readonly List<ReceiveMetrics> receives = [];
    private long messages, actions, errors, busy, samples;
    private bool disposed;
    public event EventHandler<MetricsDisplayEventArgs>? OnMetricsDisplay;
    public Task Completion { get; }

    public Metrics(CancellationToken cancellationToken = default, bool isSendMetric = true,
        double metricsDisplayFrequency = 10, int maxMetricsSamplePerDisplayInterval = 5000)
        : this(cancellationToken, isSendMetric, metricsDisplayFrequency, maxMetricsSamplePerDisplayInterval, TimeProvider.System) { }

    internal Metrics(CancellationToken token, bool isSendMetric, double frequency, int maximum, TimeProvider time)
    {
        if (!double.IsFinite(frequency) || frequency <= 0 || frequency > 3600) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (maximum < 1 || maximum > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maximum));
        isSend = isSendMetric; capacity = maximum; this.time = time;
        started = windowStarted = time.GetTimestamp();
        stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Completion = DisplayLoop(frequency);
    }

    private async Task DisplayLoop(double frequency)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(frequency), time);
        try { while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false)) Flush(); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    public void Flush()
    {
        lock (sync)
        {
            var now = time.GetTimestamp();
            var args = new MetricsDisplayEventArgs
            {
                SendMetricsList = sends.ToArray(), ReceiveMetricsList = receives.ToArray(),
                StartSeconds = time.GetElapsedTime(started, windowStarted).TotalSeconds,
                EndSeconds = time.GetElapsedTime(started, now).TotalSeconds,
                Messages = messages, Actions = actions, Errors = errors, BusyErrors = busy
            };
            sends.Clear(); receives.Clear();
            messages = actions = errors = busy = samples = 0;
            windowStarted = now;
            OnMetricsDisplay?.Invoke(this, args);
        }
    }

    private long TicksSince(long timestamp) => (long)(time.GetElapsedTime(timestamp).TotalSeconds * Stopwatch.Frequency);

    public Guid StartRecordTime()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var id = Guid.NewGuid();
            // Tick properties retain Stopwatch tick units for existing ILogger consumers.
            if (isSend) sendSessions.Add(id, new SendMetrics { Tick = time.GetTimestamp() });
            else receiveSessions.Add(id, new ReceiveMetrics { Tick = time.GetTimestamp() });
            return id;
        }
    }

    public double StopRecordTimeAndGetElapsedMs(Guid sessionId)
    {
        lock (sync)
        {
            var ticks = isSend
                ? sendSessions[sessionId].SendDuration100ns = TicksSince(sendSessions[sessionId].Tick)
                : receiveSessions[sessionId].ReceiveDuration100ns = TicksSince(receiveSessions[sessionId].Tick);
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }

    public void StartCompletionRecordTime(Guid sessionId)
    {
        lock (sync) if (!isSend) receiveSessions[sessionId].CompletionStartTick = time.GetTimestamp();
    }

    public double StopCompletionRecordTimeAndGetElapsedMs(Guid sessionId)
    {
        lock (sync)
        {
            if (isSend) return 0;
            return (receiveSessions[sessionId].CompleteDuration100ns = TicksSince(receiveSessions[sessionId].CompletionStartTick))
                * 1000.0 / Stopwatch.Frequency;
        }
    }

    public void IncrementActionCount(Guid sessionId)
    {
        lock (sync) { if (isSend) sendSessions[sessionId].Sends++; else receiveSessions[sessionId].Receives++; }
    }
    public void IncrementMetricValueBy(Guid sessionId, int count)
    {
        lock (sync) { if (isSend) sendSessions[sessionId].Messages += count; else receiveSessions[sessionId].Messages += count; }
    }
    public void IncrementErrorCount(Guid sessionId)
    {
        lock (sync) { if (isSend) sendSessions[sessionId].Errors++; else receiveSessions[sessionId].Errors++; }
    }
    public void IncrementBusyErrorCount(Guid sessionId)
    {
        lock (sync) { if (isSend) sendSessions[sessionId].BusyErrors++; else receiveSessions[sessionId].BusyErrors++; }
    }

    public void DisposeSession(Guid sessionId)
    {
        lock (sync)
        {
            samples++;
            var index = samples <= capacity ? samples - 1 : Random.Shared.NextInt64(samples);
            if (isSend)
            {
                if (!sendSessions.Remove(sessionId, out var metric)) throw new InvalidOperationException("Unknown metric session.");
                metric.Tick = (long)(time.GetElapsedTime(started, metric.Tick).TotalSeconds * Stopwatch.Frequency);
                messages += metric.Messages; actions += metric.Sends; errors += metric.Errors; busy += metric.BusyErrors;
                if (sends.Count < capacity) sends.Add(metric);
                else if (index < capacity) sends[(int)index] = metric;
            }
            else
            {
                if (!receiveSessions.Remove(sessionId, out var metric)) throw new InvalidOperationException("Unknown metric session.");
                metric.Tick = (long)(time.GetElapsedTime(started, metric.Tick).TotalSeconds * Stopwatch.Frequency);
                messages += metric.Messages; actions += metric.Receives; errors += metric.Errors; busy += metric.BusyErrors;
                if (receives.Count < capacity) receives.Add(metric);
                else if (index < capacity) receives[(int)index] = metric;
            }
        }
    }

    public void AddTrace(string trace) => Console.Error.WriteLine(trace);

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        stop.Cancel();
        try { Completion.GetAwaiter().GetResult(); }
        finally
        {
            stop.Dispose();
            Flush();
            lock (sync)
                if (sendSessions.Count != 0 || receiveSessions.Count != 0)
                    throw new InvalidOperationException("Drain operations before disposing metrics.");
        }
    }
}

public class ObjectPool<T>
{
    private readonly object sync = new();
    private readonly Stack<T> items = [];
    private readonly Func<T> generator;
    private readonly Action<T> cleaner;
    private readonly long maximum;
    private long loaned;
    public ObjectPool(Func<T> objectGenerator, Action<T> objectCleaner, long maxCount = 5000)
    {
        ArgumentNullException.ThrowIfNull(objectGenerator);
        if (maxCount < 1) throw new ArgumentOutOfRangeException(nameof(maxCount));
        generator = objectGenerator; cleaner = objectCleaner; maximum = maxCount;
    }
    public T? Get()
    {
        lock (sync)
        {
            if (loaned == maximum) return default;
            var item = items.Count > 0 ? items.Pop() : generator();
            loaned++;
            return item;
        }
    }
    public void Return(T item)
    {
        lock (sync)
        {
            if (loaned == 0) throw new InvalidOperationException("No outstanding loan.");
            cleaner(item); items.Push(item); loaned--;
        }
    }
}

public class MetricsDisplayEventArgs : EventArgs
{
    public ArraySegment<SendMetrics> SendMetricsList { get; set; }
    public ArraySegment<ReceiveMetrics> ReceiveMetricsList { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public long Messages { get; set; }
    public long Actions { get; set; }
    public long Errors { get; set; }
    public long BusyErrors { get; set; }
}
