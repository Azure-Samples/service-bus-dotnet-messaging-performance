# Azure Service Bus Performance Sample

This code sample illustrates the available throughput performance options for Azure Service Bus clients 
and also allows for running experiments that dynamically adjust some tuning parameters.

The sample can either send and receive messages from a single instance, or just act as either sender or receiver,
allowing simulation of different scenarios. 

The sending side supports sending messages singly or in batches, and it supports pipelining of send operations 
whereby up to a certain number of messages are kept in flight and their acknowledgement is handled asynchronously. 
You can start one or multiple concurrent senders, and each sender can be throttled by imposing a pause between
individual send operations.

The receive side supports the "ReceiveAndDelete" and "PeekLock" receive modes, one or multiple concurrent 
receive loops, single or batch receive operations, and single or batch completion. You can simulate having
multiple concurrent handlers on a receiver and you can also impose a delay to simulate work.

## What to expect

This sample is specifically designed to test throughput limits of queues and topics/subscriptions in Service Bus 
namespaces. It does not measure end-to-end latency, meaning how fast messages can pass through Service Bus under 
optimal conditions. The goals of achieving maximum throughput and the lowest end-to-end latency are fundamentally 
at odds, as you might know first-hand from driving. Either you can go fast on a street with light traffic, or 
the street can handle a maximum capacity of cars at the same time, but at the cost of individual speed. 
The goal of this sample is to find out where the capacity limits are. 

As discussed in the [product documentation](https://docs.microsoft.com/azure/service-bus-messaging/message-transfers-locks-settlement), 
network latency has very significant impact on the achievable throughput. If you are running this sample from your
notebook or workstation, you
 






