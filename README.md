# Service Bus Premium Messaging .NET Performance Test

[The sample](./ThroughputTest) in this repo can be used to help benchmark Service Bus Premium Messaging throughput, 
and can be used to study performance best practices. 

A latency-focused sample will be published in the near future, as measuring latency and throughput limits at the same time is not possible. Send operations are generally somewhat less expensive than receives, and therefore 10000 sends in a fast-as-possible burst create a prompt traffic jam in the queue that a receiver simply can’t keep up with. That means the end-to-end passthrough latency for each message goes up when the throughput limits are pushed. Optimal latency, meaning a minimal passthrough time of messages through the system, is not achievable under maximum throughput pressure.
