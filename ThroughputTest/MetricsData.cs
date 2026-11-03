namespace ThroughputTest;

sealed class SendMetrics
{
    public int Messages { get; set; }
    public int Sends { get; set; }
    public double SendMilliseconds { get; set; }
    public double GateMilliseconds { get; set; }
    public int Errors { get; set; }
    public int BusyErrors { get; set; }
}

sealed class ReceiveMetrics
{
    public int Messages { get; set; }
    public int Receives { get; set; }
    public double ReceiveMilliseconds { get; set; }
    public double CompleteMilliseconds { get; set; }
    public int Errors { get; set; }
    public int BusyErrors { get; set; }
    public int Completions { get; set; }
    public int CompleteCalls { get; set; }
}

sealed record MetricWindow(double StartSeconds, double EndSeconds, SendMetrics[] Sends, ReceiveMetrics[] Receives,
    long TotalSent, long TotalReceived)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
    public double SendRate => DurationSeconds > 0 ? Sends.Sum(m => (long)m.Messages) / DurationSeconds : 0;
    public double ReceiveRate => DurationSeconds > 0 ? Receives.Sum(m => (long)m.Messages) / DurationSeconds : 0;
}
