//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Net.Sockets;
    using Azure.Messaging.ServiceBus;

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
            return Task.WhenAll(senders);
        }

        async Task SendTask()
        {
            var client = new ServiceBusClient(this.Settings.ConnectionString);
            ServiceBusSender sender = client.CreateSender(this.Settings.SendPath);
            var payload = new byte[this.Settings.MessageSizeInBytes];
            var semaphore = new DynamicSemaphoreSlim(this.Settings.MaxInflightSends.Value);
            var done = new SemaphoreSlim(1);
            done.Wait();
            long totalSends = 0;

            this.Settings.MaxInflightSends.Changing += (a, e) => AdjustSemaphore(e, semaphore);
            var sw = Stopwatch.StartNew();

            // first send will fail out if the cxn string is bad
            await sender.SendMessageAsync(new ServiceBusMessage(payload) { TimeToLive = TimeSpan.FromMinutes(5) });

            for (int j = 0; j < Settings.MessageCount && !this.CancellationToken.IsCancellationRequested; j++)
            {
                var sendMetrics = new SendMetrics() { Tick = sw.ElapsedTicks };

                var nsec = sw.ElapsedTicks;
                semaphore.Wait();
                //await semaphore.WaitAsync().ConfigureAwait(false);
                sendMetrics.InflightSends = this.Settings.MaxInflightSends.Value - semaphore.CurrentCount;
                sendMetrics.GateLockDuration100ns = sw.ElapsedTicks - nsec; 
                                
                if (Settings.SendDelay > 0)
                {
                    await Task.Delay(Settings.SendDelay);
                }
                if (Settings.SendBatchCount <= 1)
                {
                    sender.SendMessageAsync(new ServiceBusMessage(payload) { TimeToLive = TimeSpan.FromMinutes(5) })
                        .ContinueWith(async (t) =>
                        {
                            if (t.IsFaulted || t.IsCanceled)
                            {
                                await HandleExceptions(semaphore, sendMetrics, t.Exception);
                            }
                            else
                            {
                                sendMetrics.SendDuration100ns = sw.ElapsedTicks - nsec;
                                sendMetrics.Sends = 1;
                                sendMetrics.Messages = 1;
                                semaphore.Release();
                                Metrics.PushSendMetrics(sendMetrics);
                            }
                            if (Interlocked.Increment(ref totalSends) >= Settings.MessageCount)
                            {
                                done.Release();
                            }
                        }).Fork();
                }
                else
                {
                    var batch = new List<ServiceBusMessage>();
                    for (int i = 0; i < Settings.SendBatchCount && j < Settings.MessageCount && !this.CancellationToken.IsCancellationRequested; i++, j++)
                    {
                        batch.Add(new ServiceBusMessage(payload) { TimeToLive = TimeSpan.FromMinutes(5) });
                    }
                    sender.SendMessagesAsync(batch)
                       .ContinueWith(async (t) =>
                       {
                           if (t.IsFaulted || t.IsCanceled)
                           {
                               await HandleExceptions(semaphore, sendMetrics, t.Exception);
                           }
                           else
                           {
                               sendMetrics.SendDuration100ns = sw.ElapsedTicks - nsec;
                               sendMetrics.Sends = 1;
                               sendMetrics.Messages = Settings.SendBatchCount;
                               semaphore.Release();
                               Metrics.PushSendMetrics(sendMetrics);
                           }
                           if (Interlocked.Increment(ref totalSends) >= Settings.MessageCount)
                           {
                               done.Release();
                           }
                       }).Fork();
                }
            }
            await done.WaitAsync();
        }

        static void AdjustSemaphore(Observable<int>.ChangingEventArgs e, DynamicSemaphoreSlim semaphore)
        {
            if (e.NewValue > e.OldValue)
            {
                for (int i = e.OldValue; i < e.NewValue; i++)
                {
                    semaphore.Grant();
                }
            }
            else
            {
                for (int i = e.NewValue; i < e.OldValue; i++)
                {
                    semaphore.Revoke();
                }
            }
        }

        private async Task HandleExceptions(DynamicSemaphoreSlim semaphore, SendMetrics sendMetrics, AggregateException ex)
        {
            bool wait = false;
            ex.Handle((x) =>
            {
                if (x is ServiceBusException sbException)
                {
                    if (sbException.Reason == ServiceBusFailureReason.ServiceCommunicationProblem)
                    {
                        if (sbException.InnerException is SocketException socketException &&
                        socketException.SocketErrorCode == SocketError.HostNotFound)
                        {
                            return false;
                        }
                    }
                    if (sbException.Reason == ServiceBusFailureReason.ServiceBusy)
                    {
                        sendMetrics.BusyErrors = 1;
                        if (!this.CancellationToken.IsCancellationRequested)
                        {
                            wait = true;
                        }
                    }
                    else
                    {
                        sendMetrics.Errors = 1;
                    }
                }
                return true;
            });

            if (wait)
            {
                await Task.Delay(3000, this.CancellationToken).ConfigureAwait(false);
            }
            semaphore.Release();
            Metrics.PushSendMetrics(sendMetrics);

        }
    }
}
