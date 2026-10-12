using Azure.Messaging.ServiceBus;
using System.Globalization;

namespace ThroughputTest;

sealed class Metrics(TimeProvider time)
{
    private readonly object sync = new();
    private readonly long started = time.GetTimestamp();
    private readonly List<SendMetrics> sends = [];
    private readonly List<ReceiveMetrics> receives = [];
    private double previousSeconds;
    private long totalSent;
    private long totalReceived;
    public long TotalSent { get { lock (sync) return totalSent; } }
    public long TotalReceived { get { lock (sync) return totalReceived; } }

    public void PushSendMetrics(SendMetrics metric)
    {
        lock (sync) { sends.Add(metric); totalSent += metric.Messages; }
    }

    public void PushReceiveMetrics(ReceiveMetrics metric)
    {
        lock (sync) { receives.Add(metric); totalReceived += metric.Messages; }
    }

    public MetricWindow Snapshot()
    {
        lock (sync)
        {
            var now = time.GetElapsedTime(started).TotalSeconds;
            var window = new MetricWindow(previousSeconds, now, sends.ToArray(), receives.ToArray(), totalSent, totalReceived);
            previousSeconds = now;
            sends.Clear();
            receives.Clear();
            return window;
        }
    }

    public static bool IsBusy(Exception exception) => exception is ServiceBusException { Reason: ServiceBusFailureReason.ServiceBusy } ||
        exception is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(IsBusy);

    public static string Statistics(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) return "0.00|0.00|0.00|0.00|0.00|";
        var mean = values.Average();
        var median = (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
        var deviation = Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
        return FormattableString.Invariant($"{mean:F2}|{median:F2}|{deviation:F2}|{values[0]:F2}|{values[^1]:F2}|");
    }

    public void Write(TextWriter output, Settings settings)
    {
        var window = Snapshot();
        if (settings.SenderCount > 0)
        {
            var list = window.Sends;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"S|{window.StartSeconds:F2}|{window.EndSeconds:F2}|{settings.SendBatchCount}|{settings.CfgMaxInflightSends}|") +
                Statistics(list.Where(m => m.Sends > 0).Select(m => m.SendMilliseconds)) +
                Statistics(list.Select(m => m.GateMilliseconds)) +
                string.Create(CultureInfo.InvariantCulture,
                $"{window.SendRate:F2}|{list.Sum(m => (long)m.Messages)}|{list.Sum(m => (long)m.Sends)}|{list.Sum(m => m.Errors)}|{list.Sum(m => m.BusyErrors)}|{window.TotalSent}|"));
        }
        if (settings.ReceiverCount > 0)
        {
            var list = window.Receives;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"R|{window.StartSeconds:F2}|{window.EndSeconds:F2}|{settings.ReceiveBatchCount}|{settings.CfgMaxInflightReceives}|") +
                Statistics(list.Where(m => m.Receives > 0).Select(m => m.ReceiveMilliseconds)) +
                Statistics(list.Where(m => m.CompleteCalls > 0).Select(m => m.CompleteMilliseconds / m.CompleteCalls)) +
                string.Create(CultureInfo.InvariantCulture,
                $"{window.ReceiveRate:F2}|{list.Sum(m => (long)m.Messages)}|{list.Sum(m => (long)m.Receives)}|{list.Sum(m => m.Errors)}|{list.Sum(m => m.BusyErrors)}|{window.TotalReceived}|{list.Sum(m => (long)m.Completions)}|{list.Sum(m => (long)m.CompleteCalls)}|"));
        }
    }

    public static void WriteHeaders(TextWriter output, Settings settings)
    {
        if (settings.SenderCount > 0)
            output.WriteLine("S|pstart|pend|sbc|mifs|snd.avg|snd.med|snd.dev|snd.min|snd.max|gld.avg|gld.med|gld.dev|gld.min|gld.max|msg/s|total|sndop|errs|busy|overall|");
        if (settings.ReceiverCount > 0)
            output.WriteLine("R|pstart|pend|rbc|mifr|rcv.avg|rcv.med|rcv.dev|rcv.min|rcv.max|cpl.avg|cpl.med|cpl.dev|cpl.min|cpl.max|msg/s|total|rcvop|errs|busy|overall|completed|cplop|");
    }
}
