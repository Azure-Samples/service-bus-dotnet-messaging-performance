# Service Bus Premium Messaging .NET Performance Test

[The finite workload sample](./ThroughputTest) and the [continuous workload and hosting
libraries](./ThroughputTest_v2) benchmark Service Bus Premium Messaging throughput.
Both target .NET 10 and use Azure.Messaging.ServiceBus 7.21.0 / Azure.Identity 1.21.0.
Their READMEs document the distinct CLIs, authentication, Windows/Linux publishing,
optional experiments and credential-free regression tests.

Real runs send/consume messages and can incur charges. Use authorized, isolated,
pre-existing entities. The samples do not provision, purge or delete resources.
Builds and offline tests do not establish live throughput or authorization.

A latency-focused sample will be published in the near future, as measuring latency and throughput limits at the same time is not possible. Send operations are generally somewhat less expensive than receives, and therefore 10000 sends in a fast-as-possible burst create a prompt traffic jam in the queue that a receiver simply can’t keep up with. That means the end-to-end passthrough latency for each message goes up when the throughput limits are pushed. Optimal latency, meaning a minimal passthrough time of messages through the system, is not achievable under maximum throughput pressure.
