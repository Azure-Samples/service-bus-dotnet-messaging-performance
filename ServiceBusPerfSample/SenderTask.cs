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

    sealed class SenderTask : PerformanceTask
    {
        readonly List<MessageSender> senders;

        public SenderTask(Settings settings, Metrics metrics, CancellationToken cancellationToken)
            : base(settings, metrics, cancellationToken)
        {
            this.senders = new List<MessageSender>();
        }

        protected override async Task OnOpenAsync()
        {
            Extensions.For(0, this.Settings.SenderCount, (i) => this.Factories.Add(MessagingFactory.CreateFromConnectionString(this.ConnectionString)));
            var senderPath = this.Settings.EntityType == EntityType.Topic ? this.Settings.TopicPath : this.Settings.QueuePath;
            for (int i = 0; i < this.Settings.SenderCount; i++)
            {
                var factory = MessagingFactory.CreateFromConnectionString(this.ConnectionString);
                factory.RetryPolicy = RetryPolicy.NoRetry;
                this.Factories.Add(factory);
                var sender = factory.CreateMessageSender(senderPath);
                sender.RetryPolicy = RetryPolicy.NoRetry;
                this.senders.Add(sender);
            }

            await this.senders.First().SendAsync(new BrokeredMessage());

            await this.senders.ParallelForEachAsync(async (sender, senderIndex) =>
            {
                await sender.SendAsync(new BrokeredMessage() { TimeToLive = TimeSpan.FromMilliseconds(1) });
            });
        }

        protected override async Task OnStart()
        {
            await SendTask();
        }

        async Task SendTask()
        {
            var payload = new byte[this.Settings.MessageSizeInBytes];
            await senders.ParallelForEachAsync(async (sender, senderIndex) =>
            {
                while (!this.CancellationToken.IsCancellationRequested)
                {
                    await ExecuteOperationAsync(async () =>
                    {
                        List<BrokeredMessage> messages = new List<BrokeredMessage>();
                        Extensions.For(0, this.Settings.SendBatchCount, (messageIndex) => messages.Add(new BrokeredMessage(payload)));
                        try
                        {
                            Stopwatch sw = Stopwatch.StartNew();
                            await sender.SendBatchAsync(messages);
                            sw.Stop();
                            this.Metrics.IncreaseSendLatency(sw.Elapsed.TotalMilliseconds);
                            this.Metrics.IncreaseSendMessages(messages.Count);
                            this.Metrics.IncreaseSendBatch(1);
                        }
                        finally
                        {
                            messages.ForEach(m => m.Dispose());
                        }
                    });
                }
            });
        }
    }
}
