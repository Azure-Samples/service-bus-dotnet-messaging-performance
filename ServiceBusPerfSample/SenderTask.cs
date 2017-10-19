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
    using Microsoft.Azure.ServiceBus.Core;
    using Microsoft.Azure.ServiceBus;

    sealed class SenderTask : PerformanceTask
    {
        readonly List<Task> senders;

        public SenderTask(Settings settings, Metrics metrics, CancellationToken cancellationToken)
            : base(settings, metrics, cancellationToken)
        {
            this.senders = new List<Task>();
        }

        protected override Task OnOpenAsync()
        {
            return Task.CompletedTask;
        }

        protected override Task OnStartAsync()
        {
            for (int i = 0; i < this.Settings.SenderCount; i++)
            {
                this.senders.Add(Task.Run(SendTask));
            }
            return Task.CompletedTask;
        }

        async Task SendTask()
        {
            var sender = new MessageSender(this.Settings.ConnectionString, this.Settings.SendPath);
            var payload = new byte[this.Settings.MessageSizeInBytes];
            var semaphore = new SemaphoreSlim(this.Settings.MaxInflightSends + 1);
            var sw = Stopwatch.StartNew();

            while (!this.CancellationToken.IsCancellationRequested)
            {
                await semaphore.WaitAsync();

                var msec = sw.ElapsedMilliseconds;

                if (Settings.SendBatchCount <= 1)
                {
                    sender.SendAsync(new Message(payload) { TimeToLive = TimeSpan.FromMinutes(5) }).ContinueWith(async (t) =>
                    {
                        if (t.IsFaulted)
                        {
                            this.Metrics.IncreaseErrorCount(1);
                            if (t.Exception?.GetType() == typeof(ServerBusyException))
                            {
                                this.Metrics.IncreaseServerBusy(1);
                                if (!this.CancellationToken.IsCancellationRequested)
                                {
                                    await Task.Delay(3000, this.CancellationToken);
                                }
                            }
                        }
                        else
                        {
                            this.Metrics.IncreaseSendLatency(sw.ElapsedMilliseconds - msec);
                            this.Metrics.IncreaseSendMessages(1);
                        }
                        semaphore.Release();
                    }).Fork();
                }
                else
                {
                    List<Message> batch = new List<Message>();
                    for (int i = 0; i < Settings.SendBatchCount; i++)
                    {
                        batch.Add(new Message(payload) { TimeToLive = TimeSpan.FromMinutes(5) });
                    }
                    sender.SendAsync(batch).ContinueWith(async (t) =>
                    {
                        if (t.IsFaulted)
                        {
                            this.Metrics.IncreaseErrorCount(1);
                            if (t.Exception?.GetType() == typeof(ServerBusyException))
                            {
                                this.Metrics.IncreaseServerBusy(1);
                                if (!this.CancellationToken.IsCancellationRequested)
                                {
                                    await Task.Delay(3000, this.CancellationToken);
                                }
                            }
                        }
                        else
                        {
                            this.Metrics.IncreaseSendLatency(sw.ElapsedMilliseconds - msec);
                            this.Metrics.IncreaseSendMessages(Settings.SendBatchCount);
                        }
                        semaphore.Release();
                    }).Fork();
                }
            }
        }
    }
}
