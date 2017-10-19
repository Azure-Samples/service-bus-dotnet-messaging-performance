//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusPerfSample
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    sealed class ReceiverTask : PerformanceTask
    {
        readonly List<Task> receivers;

        public ReceiverTask(Settings settings, Metrics metrics, CancellationToken cancellationToken)
            : base(settings, metrics, cancellationToken)
        {
            this.receivers = new List<Task>();
        }

        protected override Task OnOpenAsync()
        {
            return Task.CompletedTask;
        }

        protected override Task OnStartAsync()
        {
            var receiverPaths = this.Settings.ReceivePaths;
            foreach (var receiverPath in receiverPaths)
            {
                for (int i = 0; i < this.Settings.ReceiverCount; i++)
                {
                    this.receivers.Add(Task.Run(() => ReceiveTask(receiverPath)));
                }
            }
            return Task.CompletedTask;
        }

        async Task ReceiveTask(string path)
        {
            var receiver = new MessageReceiver(this.Settings.ConnectionString, path, this.Settings.ReceiveMode);
            var semaphore = new SemaphoreSlim(this.Settings.MaxInflightReceives + 1);
            var sw = Stopwatch.StartNew();

            while (!this.CancellationToken.IsCancellationRequested)
            {
                await semaphore.WaitAsync();

                var msec = sw.ElapsedMilliseconds;

                if (Settings.ReceiveBatchCount <= 1)
                {
                    receiver.ReceiveAsync().ContinueWith(async (t) =>
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
                            semaphore.Release();
                        }
                        else
                        {
                            this.Metrics.IncreaseReceiveLatency((long)(sw.ElapsedMilliseconds - msec));
                            this.Metrics.IncreaseReceiveMessages(1);
                            msec = sw.ElapsedMilliseconds;
                            receiver.CompleteAsync(t.Result.SystemProperties.LockToken).ContinueWith(async (t1) =>
                            {
                                if (t1.IsFaulted)
                                {
                                    this.Metrics.IncreaseErrorCount(1);
                                    if (t1.Exception?.GetType() == typeof(ServerBusyException))
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
                                    this.Metrics.IncreaseCompleteLatency((long)(sw.ElapsedMilliseconds - msec));
                                    this.Metrics.IncreaseCompleteMessages(1);
                                }
                                semaphore.Release();
                            }).Fork();
                        };
                    }).Fork();
                }
                else
                {
                    receiver.ReceiveAsync(Settings.ReceiveBatchCount).ContinueWith(async (t) =>
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
                            semaphore.Release();
                        }
                        else
                        {
                            this.Metrics.IncreaseReceiveLatency((long)(sw.ElapsedMilliseconds - msec));
                            this.Metrics.IncreaseReceiveMessages(t.Result.Count);
                            msec = sw.ElapsedMilliseconds;
                            receiver.CompleteAsync(t.Result.Select((m) => { return m.SystemProperties.LockToken; })).ContinueWith(async (t1) =>
                            {
                                if (t1.IsFaulted)
                                {
                                    this.Metrics.IncreaseErrorCount(1);
                                    if (t1.Exception?.GetType() == typeof(ServerBusyException))
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
                                    this.Metrics.IncreaseCompleteLatency((long)(sw.ElapsedMilliseconds - msec));
                                    this.Metrics.IncreaseCompleteMessages(t.Result.Count);
                                }
                                semaphore.Release();
                            }).Fork();
                        };
                    }).Fork();
                }
            }
        }
    }
}
