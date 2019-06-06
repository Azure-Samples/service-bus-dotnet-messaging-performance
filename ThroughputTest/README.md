# Azure Service Bus Throughput Performance Test

This code sample illustrates the available throughput performance options for Azure Service Bus clients 
and also allows for running experiments that dynamically adjust some tuning parameters. 

> This tool is meant to be used with Azure Service Bus Premium and not with Azure Service Bus Standard.
> Azure Service Bus Premium is designed to provide predictable performance, meaning that the results 
> you measure with this tool are representative of the performance you can expect for your applications. 

The sample assumes that you are generally familiar with the Service Bus .NET SDK and with how to set up 
namespaces and entities within those namespaces. 

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
at odds, as you might know first-hand from driving a car. Either you can go fast on a street with light traffic, or 
the street can handle a maximum capacity of cars at the same time, but at the cost of individual speed. 
The goal of this sample is to find out where the capacity limits are. 

As discussed in the [product documentation](https://docs.microsoft.com/azure/service-bus-messaging/message-transfers-locks-settlement), 
network latency has very significant impact on the achievable throughput. If you are running this sample from your
development workstation, throughput will be substantially lower than from within an Azure VM. If you want to 
test limits for a scenario where the Service Bus client will reside inside Azure, you should also run this test 
inside Azure on a Windows or Linux VM that resides within the same region as the Service Bus namespace you 
want to test.

In an in-region setup, and with ideal parameters, you can achieve send rates exceeding 20000 msg/sec at 1024 bytes per message
from a single client, meaning that a 1GB queue will fill up in under a minute. Also be aware that receive operations are
generally more costly and therefore slower than send operations, which means that a test with maximum send 
pressure (several senders using batching) may not be sustainable for longer periods because the receivers might not be 
able to keep up.

## Building the Tool

The tool is a .NET Core project that can produce standalone executables for Linux and Windows, whereby we'll assume x64 targets. 

For Linux, with the .NET Core 3.0 SDK installed, run: 

```
	dotnet publish -c Release -f netcoreapp3.0 -r linux-x64
```
The output application can be found in the ```bin/Release/netcoreapp3.0/linux-x64/publish```subdirectory.

For Windows, run:

```
	dotnet publish -c Release -f netcoreapp3.0 -r win-x64
```
The output application can be found in the ```bin\Release\netcoreapp3.0\win-x64\publish```subdirectory.

## Running the Tool

You can run the tool locally from your own machine or you can run it from within an Azure VM. You should run the 
tool on the platform you are targeting, because performance differs between Linux and Windows due to their different
I/O architectures and different implementation of the platform layer in .NET Core.  

The only required command line arguments are a connection string and a send path. If the connection string includes 
an EntityPath property for a queue, the send path can be omitted. If the receive path is not given, the tool assumes
the send path and the receive path to be the same, which is sufficient for queues. For topics, the subscription(s) must 
be given with explicit receive path options.

You can test any valid Service Bus topology with this tool, including Topics with subscriptions and chained 
entities with auto-forward set up between them. Therefore, the send path and the receive path do not have to be 
on the same entity. You can also receive from dead-letter queues. 

| Scenario                     | Arguments                                                                     |
|------------------------------|-------------------------------------------------------------------------------|
| Send to and receive from a queue |```ThroughputTest -C {connection-string} -S myQueueName ```          |
| Send to a topic and receive from a subscription | ``` ThroughputTest -C {connection-string} -S myTopicName -R myTopicName/subscriptions/mySubName ``` |
| Send to a topic and receive from two subscriptions | ``` ThroughputTest -C {connection-string} -S myTopicName -R myTopicName/subscriptions/mySubNameA myTopicName/subscriptions/mySubNameB ``` |
| Send a queue |```ThroughputTest -C {connection-string} -S myQueueName -r 0 ```          |
| Receive from a queue |```ThroughputTest -C {connection-string} -S myQueueName -s 0 ```          |

## Output

The tool prints out interleaved statistics for sends and receives. Send information is prefixed with S (and in yellow),
receive information is prefixed with R and printed in cyan. The columns are separated with the pipe symbol and therefore
parseable. 

### Send output columns

| Column   | Description
|----------|---------------------------------------------------------------------------
| pstart   | Begin of the data recording for this row (seconds from start of run)
| pend     | End of data recording for this row
| sbc      | Send batch count
| mifs     | Max inflight sends
| snd.avg  | Average send duration (to acknowledgement receipt) in milliseconds
| snd.med  | Median send duration
| snd.dev  | Standard deviation for send duration
| snd.min  | Minimum send duration
| snd.max  | Maximum send duration
| gld.avg  | Average gate lock duration in milliseconds. This measure tracks whether the internal async thread pool queue gets backed up. If this value shoots up, you should reduce the number of concurrent inflight sends, because the application sends more than what can be put on the wire. 
| gld.med  | Median gate lock duration
| gld.dev  | Standard deviation for gate lock duration
| gld.min  | Minimum gate lock duration
| gld.max  | Maximum gate lock duration
| msg/s    | Throughput in messages per second 
| total    | Total messages sent in this period
| sndop    | Total send operations in this period 
| errs     | Errors
| busy     | Busy errors
| overall  | Total messages sent in this run

### Receive output columns 

| Column   | Description
|----------|---------------------------------------------------------------------------
| pstart   | Begin of the data recording for this row (seconds from start of run)
| pend     | End of data recording for this row
| rbc      | Receive batch count
| mifr     | Max inflight receives
| rcv.avg  | Average receive duration (to message receipt) in milliseconds
| rcv.med  | Median receive duration
| rcv.dev  | Standard deviation for receive duration
| rcv.min  | Minimum receive duration
| rcv.max  | Maximum receive duration
| cpl.avg  | Average completion duration in milliseconds. 
| cpl.med  | Median completion duration
| cpl.dev  | Standard deviation for completion duration
| cpl.min  | Minimum completion duration
| cpl.max  | Maximum completion duration
| msg/s    | Throughput in messages per second 
| total    | Total messages received in this period
| rcvop    | Total receive operations in this period 
| errs     | Errors
| busy     | Busy errors
| overall  | Total messages sent in this run


## Options 

When only called with a connection string and send/receive paths, the tool will concurrently send and receive messages 
to/from the chosen Service Bus entity.

If you just want to send messages, set the receiver count to zero with **-r 0**. If you only want to receive messages,
set the sender count to zero with **-s 0**. 

The biggest throughput boosts yield the enabling of send and receive batching, meaning the sending and receiving of 
several messages in one operation. This oprtion will maximize throughput and show you the practical limits, but you
should always keep an eye on whether batching is a practical approach for your specific solution.

The "inflight-sends" and "inflight-receives" options also have very significant throughput impact. "inflight-sends"
tracks how many messages are being sent asynchronously and in a pipelined fashion while waiting for the operations 
to complete. The "inflight-receives" option controls how many messages are being received and processed concurrently. 

The further options are listed below:

| Parameter                     | Description                                                                   
|-------------------------------|-------------------------------------------------------------------------------
|  **-C, --connection-string**  |  **Required**. Connection string                                              
|  -S, --send-path              |  Send path. Queue or topic name, unless set in connection string EntityPath.  
|  -R, --receive-paths          |  Receive paths. Mandatory for receiving from topic subscriptions. Must be {topic}/subscriptions/{subscription-name} or {queue-name}
|  -n, --number-of-messages     |  Number of messages to send (default 1000000)
|  -b, --message-size-bytes     |  Bytes per message (default 1024)
|  -f, --frequency-metrics      |  Frequency of metrics display (seconds, default 10s)
|  -m, --receive-mode           |  Receive mode.'PeekLock' (default) or 'ReceiveAndDelete'
|  -r, --receiver-count         |  Number of concurrent receivers (default 1)
|  -e, --prefetch-count         |  Prefetch count (default 0)
|  -t, --send-batch-count       |  Number of messages per batch (default 0, no batching)
|  -s, --sender-count           |  Number of concurrent senders (default 1)
|  -d, --send-delay             |  Delay between sends of any sender (milliseconds, default 0)
|  -i, --inflight-sends         |  Maximum numbers of concurrent in-flight send operations (default 1)
|  -j, --inflight-receives      |  Maximum number of concurrent in-flight receive operations per receiver (default 1)
| -v, --receive-batch-count     |  Max number of messages per batch (default 0, no batching)
|  -w, --receive-work-duration  |  Work simulation delay between receive and completion (milliseconds, default 0, no work)


 






