//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusPerfSample
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    sealed class ReceiverTask : PerformanceTask
    {
        readonly List<MessageReceiver> receivers;

        public ReceiverTask(Settings settings, Metrics metrics, CancellationToken cancellationToken)
            :base(settings, metrics, cancellationToken)
        {
            this.receivers = new List<MessageReceiver>();
        }

        protected override async Task OnOpenAsync()
        {
            var receiverPaths = this.Settings.EntityType == EntityType.Topic ? this.Settings.SubscriptionNames.Select((n) => SubscriptionClient.FormatSubscriptionPath(this.Settings.TopicPath, n)) : new string[] { this.Settings.QueuePath };
            foreach (var receiverPath in receiverPaths)
            {
                for (int i = 0; i < this.Settings.ReceiverCount; i++)
                {
                    var factory = MessagingFactory.CreateFromConnectionString(this.ConnectionString);
                    factory.RetryPolicy = RetryPolicy.NoRetry;
                    this.Factories.Add(factory);
                    var receiver = factory.CreateMessageReceiver(receiverPath, this.Settings.ReceiveMode);
                    receiver.RetryPolicy = RetryPolicy.NoRetry;
                    this.receivers.Add(receiver);
                }
            }

            await this.receivers.First().ReceiveAsync();

            await this.receivers.ParallelForEachAsync(async (receiver, receiverIndex) =>
            {
                await Extensions.IgnoreExceptionAsync(async () => await receiver.ReceiveAsync(TimeSpan.Zero));
            });
        }

        protected override async Task OnStart()
        {
            await ReceiveTask();
        }

        async Task ReceiveTask()
        {
            await receivers.ParallelForEachAsync(async (receiver, receiverIndex) =>
            {
                while (!this.CancellationToken.IsCancellationRequested)
                {
                    await ExecuteOperationAsync(async () =>
                    {
                        Stopwatch receiveStopwatch = Stopwatch.StartNew();
                        var receivedMessages = await receiver.ReceiveBatchAsync(this.Settings.ReceiveBatchCount);
                        receiveStopwatch.Stop();
                        this.Metrics.IncreaseReceiveLatency(receiveStopwatch.Elapsed.TotalMilliseconds);
                        this.Metrics.IncreaseReceiveMessages(receivedMessages.Count());
                        this.Metrics.IncreaseReceiveBatch(1);

                        try
                        {
                            if (!this.CancellationToken.IsCancellationRequested && this.Settings.ReceiveMode == ReceiveMode.PeekLock && receivedMessages.Any())
                            {
                                Stopwatch completeStopwatch = Stopwatch.StartNew();
                                await receiver.CompleteBatchAsync(receivedMessages.Select(m => m.LockToken));
                                completeStopwatch.Stop();
                                this.Metrics.IncreaseCompleteLatency(completeStopwatch.Elapsed.TotalMilliseconds);
                            }
                        }
                        finally
                        {
                            if (receivedMessages != null)
                            {
                                receivedMessages.ForEach((m, i) => m.Dispose());
                            }
                        }
                    });
                }
            });
        }
    }
}
