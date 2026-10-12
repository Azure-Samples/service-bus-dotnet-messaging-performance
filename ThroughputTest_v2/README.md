# Continuous Service Bus throughput test and hosting libraries

v2 preserves the continuous workload and reusable `Sender`, `Receiver` and `ILogger`
API. The console and both hosting libraries target **.NET 10**; the messaging
library uses Azure.Messaging.ServiceBus 7.21.0 and Azure.Identity 1.21.0.
Windows and Linux publishing are supported. For finite message-count runs, multiple
receive paths, forwarding with separate send/receive paths, simulated receive work,
or dynamic concurrency experiments, use [ThroughputTest](../ThroughputTest).

## Build

From the repository root with a .NET 10 SDK (verified with 10.0.401):

```powershell
dotnet build .\ThroughputTest_v2\ServiceBusThroughputTest\ServiceBusThroughputTest.sln -c Release
dotnet test .\ThroughputTest.V2.Tests\ThroughputTest.V2.Tests.csproj -c Release
dotnet publish .\ThroughputTest_v2\ServiceBusThroughputTest\ServiceBusThroughputTest.csproj -c Release -r win-x64 --self-contained true
dotnet publish .\ThroughputTest_v2\ServiceBusThroughputTest\ServiceBusThroughputTest.csproj -c Release -r linux-x64 --self-contained true
```

Output is under `ThroughputTest_v2\ServiceBusThroughputTest/bin/Release/net10.0/<rid>/publish`.
Use native path separators on your host. The old .NET Framework startup configuration
is no longer used. `App.config` retains the two latency-sampling capacities and is
copied to build/publish output; malformed values fail before clients are created.

## Running safely

Real runs send/consume messages and may incur charges. Use authorized, isolated,
pre-existing Service Bus Premium entities with no unrelated traffic. The sample
does not create/delete resources, purge queues, assign roles or configure topology.
Read the [authentication and safety guidance](../ThroughputTest/README.md#authentication-and-safety).
`--namespace`/`SERVICEBUS_NAMESPACE` uses `DefaultAzureCredential`.
`SERVICEBUS_CONNECTION_STRING` is supported; `-C` remains compatible but should not
be used for secrets in scripts, launch profiles or shell history. Explicit namespace
and connection-string arguments are mutually exclusive. Namespace environment
configuration takes precedence over the connection-string environment variable.

Prefix these arguments with
`dotnet run --project .\ThroughputTest_v2\ServiceBusThroughputTest --`.
Only `--help` is offline; the other examples perform messaging.

| Workload | Arguments |
|---|---|
| Queue send/receive | `-S queue --duration-seconds 60` |
| Topic and one subscription | `-S topic:subscription --duration-seconds 60` |
| Equivalent subscription path | `-S topic/subscriptions/subscription --duration-seconds 60` |
| Sender only | `-S queue -r 0 --duration-seconds 60` |
| Receiver only | `-S queue -s 0 --duration-seconds 60` |
| Single-message operations | `-S queue -t 1 -v 1` |
| Batch operations and pipelines | `-S queue -t 25 -v 25 -i 4 -j 3 -d 0` |
| ReceiveAndDelete | `-S queue -m false` |
| PeekLock | `-S queue -m true` |
| Dead-letter receiver | `-S 'queue/$DeadLetterQueue' -s 0` |
| Subscription dead-letter receiver | `-S 'topic/subscriptions/one/$DeadLetterQueue' -s 0` |

For a topic/subscription path, sends go to the topic and receives use the SDK
topic/subscription overload. An EntityPath connection string can supply the queue
when `-S` is omitted. v2 has one entity argument, not the finite sample's `-S/-R`
split or multiple-subscription CLI. Run separate v2 processes for independent
destinations; use the finite tool for its explicit topology scenarios.

## CLI and lifetime

The original v2 short/long options are preserved (they differ from the finite tool):

| Option | Actual default / meaning |
|---|---|
| `-C, --connection-string` | Optional SAS string; prefer environment/namespace |
| `--namespace` | Fully qualified namespace, no scheme |
| `-S, --entity-path` | Queue, topic:subscription, or subscription path |
| `-b, --payload-size-bytes` | 1024 bytes; 1..104857600, subject to service limits |
| `-f, --frequency-metrics` | 10 seconds; 1..3600 |
| `-m, --receive-mode` | `true` = PeekLock; `false` = ReceiveAndDelete |
| `-r, --receiver-count` | 5; zero disables receivers |
| `-s, --sender-count` | 1; zero disables senders |
| `-p, --prefetch-count` | 100 per receiver |
| `-t, --send-batch-size` | 1; 0/1 singles, >1 batches |
| `-v, --receive-batch-size` | 1; 0/1 singles, >1 batches |
| `-d, --send-callIntervalMs` | 1000 ms minimum start-to-start interval per pipeline, including send time |
| `-e, --receive-callIntervalMs` | 5000 ms receive wait; 1..3600000 |
| `-i, --inflight-sends` | 1 operation per sender |
| `-j, --inflight-receives` | 1 operation per receiver |
| `--duration-seconds` | 300 total; 1..86400 |
| `--experiment` | None, Receiver, Sender, LargeMessageIntervals |

Numeric values are validated before client construction. Counts, batch sizes,
prefetch and concurrency use the same upper bounds as the finite tool. At least
one side must be enabled. SDK byte limits split oversized batches and actual
message/operation counts are recorded. A message too large for a batch fails.

The continuous workload stops after the configured duration, or earlier on
ENTER/Ctrl+C or a failure. All operations (including settlements) are awaited
before clients are closed. Each resource close is bounded to 10 seconds.
ReceiveAndDelete never settles. PeekLock batch receives use awaited individual
completions; there is no bulk-completion API in the modern SDK. No automatic lock
renewal is enabled. Prefetch/work must fit within the entity lock duration.

Exit codes: **0** help or duration/experiment completion; **2** invalid CLI/config;
**130** user cancellation; **1** operation, output or cleanup failure.
Libraries propagate failure/cancellation rather than swallowing it. Their existing
constructors and `Run(CancellationToken)` methods remain; hosts must provide
cancellation and await `Run`. They own and close their clients, **not the caller's
logger**. Dispose a shared logger only after all runs using it have drained.
Passing an empty connection string permits documented environment authentication.
No new deployment or Service Fabric configuration is supplied.

## Metrics and optional experiments

S/R pipe-delimited columns retain period bounds, batch/in-flight values,
duration average/minimum/maximum/P99, throughput, interval message/operation totals,
errors, busy errors and overall totals. R rows also show completion duration.
Times are milliseconds; P99 uses linear interpolation. Completion time is the
duration of the receive operation's whole awaited settlement group, not per-message
latency. Busy errors are a subset of errors; empty polls are operations, not messages.
Receipt counts include messages whose later settlement fails.

`MetricsSamplesPerDisplayIntervalSender` (2000) and
`MetricsSamplesPerDisplayIntervalReceiver` (10000) limit latency samples using
reservoir sampling. **They no longer limit counters:** message/operation/error totals
include every finished operation even when its latency sample is not retained.
Window rates use actual elapsed time and final partial windows are flushed.
`MetricsDisplayEventArgs` retains its sample arrays and adds exact window counters
and elapsed bounds; consumers must not sum sampled messages as overall totals.
The display loop's `Completion` task exposes output-handler failures to hosts.

The former private, disabled test methods are now selectable and bounded:

| Experiment | Preserved workload |
|---|---|
| Receiver | 1 receiver, prefetch 10240, batch 10, inflight 1024, wait 60000 ms, PeekLock, up to 120 seconds |
| Sender | 1 sender, payload 600 bytes, batch 25, inflight 1, interval 8 ms, up to 300 seconds |
| LargeMessageIntervals | 1 sender, payload 25 KiB, batch 25, inflight 1; intervals 256,128,64,32,16,8,4,2,1 ms, up to 300 seconds each |

The old variable called `batchSize` was passed to `callIntervalMS`; the large-message
experiment therefore preserves the actual **interval sweep**, not a newly invented
batch sweep. The global `--duration-seconds` bounds all steps; allow at least 2700
seconds for the full nine-step experiment. Shorter durations intentionally stop it
early. No embedded namespace/key placeholders or unbounded experiment waits remain.
Each optional experiment step starts new metric headers, a new time origin and
its own overall counters, so measurements from different settings are not mixed.

Offline tests use fake SDK clients, real library entry points and virtual time.
They do not demonstrate real throughput, broker routing, credentials, deployment,
Linux execution or completion of an independent review.
