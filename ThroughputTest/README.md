# Azure Service Bus throughput performance test

This **.NET 10** sample measures throughput on existing **Service Bus Premium**
queues and topics using Azure.Messaging.ServiceBus 7.21.0 and Azure.Identity 1.21.0.
It supports finite send/receive runs, separate senders and receivers, batching,
pipelining, prefetch, simulated work, and optional concurrency experiments.
For a continuous workload and reusable hosting libraries, see [v2](../ThroughputTest_v2).

Throughput is not end-to-end latency. Maximum send pressure can fill a queue faster
than consumers drain it. Results depend on message size, namespace capacity, topology,
network proximity, and host resources. Run on the target OS and in the namespace's
region for representative results; do not interpret historical rates as guarantees.

## Build and publish

Install a supported .NET 10 SDK (verified with 10.0.401). From the repository root:

```powershell
dotnet build .\ThroughputTest\ThroughputTest.csproj -c Release
dotnet test .\ThroughputTest.Tests\ThroughputTest.Tests.csproj -c Release
dotnet publish .\ThroughputTest\ThroughputTest.csproj -c Release -r win-x64 --self-contained true
dotnet publish .\ThroughputTest\ThroughputTest.csproj -c Release -r linux-x64 --self-contained true
```

Output is under `ThroughputTest\bin\Release\net10.0\<rid>\publish`.
On Linux, use the equivalent native path separators. The IDE launch profile runs
`--help` without contacting Azure; the folder publish profile targets `net10.0`,
`win-x64`, self-contained. Old .NET Core 2/3, netstandard executable and .NET
Framework targets have been replaced, not retained as unsupported fallbacks.

## Authentication and safety

The tool does **not** provision, purge or delete entities, configure forwarding,
create subscriptions, or assign roles. A real run sends/consumes messages and may
incur charges. Obtain authorization and use isolated, pre-created test entities
with no unrelated producers/consumers. Configure forwarding and subscription filters
separately. Count targets assume one copy reaches each specified receive path;
filters, forwarding into a shared destination, duplicate detection, competing
processes or stale messages can invalidate that assumption.

Prefer `--namespace your-namespace.servicebus.windows.net` (or `SERVICEBUS_NAMESPACE`).
This uses `DefaultAzureCredential`; configure an appropriate local developer or
managed identity separately and grant only Service Bus Data Sender and/or Data
Receiver as needed. The sample does not obtain credentials until a real SDK
operation requires them.

For SAS, set `SERVICEBUS_CONNECTION_STRING` through your approved secret mechanism.
`-C/--connection-string` remains supported but can expose secrets through shell
history and process arguments. Never commit connection strings or put them in launch
profiles. Explicit authentication options override environment defaults; when both
environment variables exist, the namespace takes precedence. Explicit namespace
and connection string options cannot be combined. Errors do not print raw
connection strings, command-line token values or exception messages.

An `EntityPath` in a connection string supplies the default send path and restricts
the credentials to that entity. A topic-scoped string can receive from that topic's
subscriptions. Use namespace-scoped credentials for different send/receive entities.
If no receive path is specified, it defaults to the send path (appropriate for queues,
not topics). In receiver-only mode, `-R` alone is sufficient.

## Scenarios

These examples assume authentication is configured in the environment. **Except
`--help` and `-n 0`, they perform real messaging when executed.** Prefix arguments
with `dotnet run --project .\ThroughputTest --no-launch-profile --`.

| Scenario | Arguments |
|---|---|
| Queue send and receive | `-S queue -n 1000` |
| Topic, one subscription | `-S topic -R topic/subscriptions/one -n 1000` |
| Topic, multiple subscriptions | `-S topic -R topic/subscriptions/one topic/subscriptions/two -n 1000` |
| Sender only | `-S queue -r 0 -n 1000` |
| Receiver only | `-R queue -s 0 -n 1000` |
| Single send and receive | `-S queue -t 0 -v 0 -n 10` |
| Full and partial batches | `-S queue -t 3 -v 3 -n 10` |
| PeekLock with simulated work | `-S queue -m PeekLock -w 10 -n 1000` |
| At-most-once receive, without settlement | `-S queue -m ReceiveAndDelete -w 10 -n 1000` |
| Delays, prefetch and pipelines | `-S queue -d 10 -e 100 -i 4 -j 3 -n 1000` |
| Existing forwarded topology | `-S source -R destination -n 1000` |
| Queue dead-letter receiver | `-R 'queue/$DeadLetterQueue' -s 0 -n 10` |
| Subscription dead-letter receiver | `-R 'topic/subscriptions/one/$DeadLetterQueue' -s 0 -n 10` |
| Transfer dead-letter receiver | `-R 'queue/$Transfer/$DeadLetterQueue' -s 0 -n 10` |
| Offline zero work | `-S queue -n 0` |

`-n` is the number of messages **per sender**. Each receive path shares a single
target of `n * sender-count` across its competing receivers and pipelines. In
receiver-only mode the target is `n` per path. Two subscriptions therefore each
receive the full sent count; five receivers on one queue share it, rather than
each trying to receive that count. There is no unmeasured warm-up message.

Batch size is a maximum, not a promise: the final batch can be partial, SDK
negotiated byte limits may split sends, and receive batches can be short or empty.
An oversized single message that cannot fit a batch fails explicitly; use a smaller
payload or a supported single-message send. Service/namespace limits still apply.
Empty receives count as operations, not errors or completed messages.

PeekLock completes each message after simulated work. **The modern SDK does not
offer bulk completion**: batch receives use awaited per-message completion, not
the old batch-settlement claim. ReceiveAndDelete never completes messages and is
at-most-once: a crash during simulated work loses already-received messages.
Work delay is per message in both modes. No automatic lock renewal is enabled;
keep work/prefetch below the entity lock duration or expect a failed run.

## Options and termination

All original short/long options remain available; `--help` prints current defaults.

| Option | Default / meaning |
|---|---|
| `-C, --connection-string` | Optional SAS string; environment or namespace preferred |
| `--namespace` | Fully qualified namespace, without scheme or path |
| `-S, --send-path` | Queue/topic, or connection string EntityPath |
| `-R, --receive-paths` | Space-separated queues/subscriptions; default send path |
| `-n, --number-of-messages` | 1000000 per sender, or per path in receive-only mode; 0 is offline |
| `-b, --message-size-bytes` | 1024; 1..104857600, subject to service limits |
| `-f, --frequency-metrics` | 10 seconds; 1..3600 |
| `-m, --receive-mode` | PeekLock or ReceiveAndDelete |
| `-r, --receiver-count` | 5 per receive path; 0..1024 |
| `-s, --sender-count` | 1; 0..1024; at least one side must be enabled |
| `-e, --prefetch-count` | 100 per receiver; 0..1000000 |
| `-t, --send-batch-count` | 0; 0/1 sends singly, 2..10000 batches |
| `-v, --receive-batch-count` | 0; 0/1 receives singly, 2..10000 batches |
| `-d, --send-delay` | 0 milliseconds before each operation; 0..3600000 |
| `-w, --receive-work-duration` | 0 milliseconds per received message; 0..3600000 |
| `-i, --inflight-sends` | 1 operation per sender; 1..10000 |
| `-j, --inflight-receives` | 1 operation per receiver; 1..10000 |
| `--timeout-seconds` | 300 overall; 1..86400 |
| `--cleanup-timeout-seconds` | 10 per resource close; 1..60 |
| `--experiment` | None, Sends, Receives or Both |
| `--experiment-steps` | 3 per selected experiment; 1..100 |

ENTER in an interactive terminal or Ctrl+C requests cancellation. Redirected input
does not create a blocked console reader. The overall deadline also cancels work,
including empty polling. All issued operations are observed and drained; sender,
receiver and client shutdown is awaited with a separate deadline per resource.
If a transport does not finish closing within that deadline, the run fails and
reports incomplete results instead of claiming disposal succeeded.

Exit codes: **0** completed/zero work/help; **2** invalid arguments or settings;
**130** user cancellation; **1** operation, deadline, output or cleanup failure.
After an SDK retry policy is exhausted, errors stop sibling operations. The tool
does not retry whole batches itself, which could duplicate successful sends.

## Output and experiments

Pipe-delimited S/R rows retain `pstart`, `pend`, batch count, in-flight setting,
duration statistics (average, median, population deviation, minimum, maximum),
`msg/s`, interval message total, successful operation total, `errs`, `busy`, and
`overall`. Receive rows append `completed` and `cplop`. All durations are
milliseconds. Gate duration measures admission/budget wait, not thread-pool health.
Completion statistics are per-receive-operation averages of its per-message calls,
excluding simulated work. Failed sends do not contribute successful messages.
Receipt totals include messages whose later work/settlement fails; completion
totals distinguish these. Busy errors are a subset of errors.

Windows are **nonoverlapping**, cumulative totals count each event exactly once,
and rates use the actual elapsed window duration, including the final partial
window. Empty/single-sample windows have the same columns as full windows.

Optional send/receive experiments are still available, now explicitly enabled with
`--experiment`. Each step waits **10 seconds**, measures a **5-second** baseline,
raises concurrency (send +50, receive +10), waits **20 seconds**, and measures
another **5 seconds**. It keeps the change only if throughput improves, otherwise
restores the prior value. Steps are bounded and awaited; cancellation restores an
unmeasured adjustment. Experiments end with the workload, so a short run may not
finish a tuning step. These are exploratory measurements, not an optimizer guarantee.

The offline tests use SDK fakes and virtual time; they cover modes, counts, byte
splitting, topology mapping, parser failures, stop/deadline, cleanup, metrics and
experiments. They do not establish broker throughput, authorization, real forwarding
or dead-letter behavior, Linux execution, or a completed external review.
