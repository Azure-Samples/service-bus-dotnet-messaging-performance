//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
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
            return Task.WhenAll(this.receivers);
        }

        async Task ReceiveTask(string path)
        {
            var receiver = new MessageReceiver(this.Settings.ConnectionString, path, this.Settings.ReceiveMode);
            receiver.PrefetchCount = Settings.PrefetchCount;
            var semaphore = new DynamicSemaphoreSlim(this.Settings.MaxInflightReceives.Value + 1);
            var done = new SemaphoreSlim(1); done.Wait();
            var sw = Stopwatch.StartNew();
            long totalReceives = 0;
            await Task.Delay(TimeSpan.FromMilliseconds(Settings.WorkDuration));
            this.Settings.MaxInflightReceives.Changing += (a, e) => AdjustSemaphore(e, semaphore);

            for (int j = 0; j < Settings.MessageCount && !this.CancellationToken.IsCancellationRequested; j ++)
            {
                var receiveMetrics = new ReceiveMetrics() { Tick = sw.ElapsedTicks };

                var nsec = sw.ElapsedTicks;
                await semaphore.WaitAsync().ConfigureAwait(false);
                receiveMetrics.GateLockDuration100ns = sw.ElapsedTicks - nsec;

                if (Settings.ReceiveBatchCount <= 1)
                {
                    nsec = sw.ElapsedTicks;
                    // we're going to unblock the receives after 10 seconds if there's no pending message
                    receiver.ReceiveAsync(TimeSpan.FromSeconds(10)).ContinueWith(async (t) =>
                    {
                        receiveMetrics.ReceiveDuration100ns = sw.ElapsedTicks - nsec;
                        if (t.IsFaulted || t.IsCanceled || t.Result == null)
                        {
                            if (t.Exception?.GetType() == typeof(ServerBusyException))
                            {
                                receiveMetrics.BusyErrors = 1;
                                if (!this.CancellationToken.IsCancellationRequested)
                                {
                                    await Task.Delay(3000, this.CancellationToken).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                receiveMetrics.Errors = 1;
                            }
                            Metrics.PushReceiveMetrics(receiveMetrics);
                            semaphore.Release();
                            if (Interlocked.Increment(ref totalReceives) >= Settings.MessageCount)
                            {
                                done.Release();
                            }
                        }
                        else 
                        {
                            receiveMetrics.Receives = receiveMetrics.Messages = 1;
                            nsec = sw.ElapsedTicks;

                            // simulate work by introducing a delay, if needed
                            if (Settings.WorkDuration > 0)
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(Settings.WorkDuration));
                            }
                            receiver.CompleteAsync(t.Result.SystemProperties.LockToken).ContinueWith(async (t1) =>
                            {
                                receiveMetrics.CompleteDuration100ns = sw.ElapsedTicks - nsec;
                                if (t1.IsFaulted)
                                {
                                    if (t1.Exception?.GetType() == typeof(ServerBusyException))
                                    {
                                        receiveMetrics.BusyErrors = 1;
                                        if (!this.CancellationToken.IsCancellationRequested)
                                        {
                                            await Task.Delay(3000, this.CancellationToken).ConfigureAwait(false);
                                        }
                                    }
                                    else
                                    {
                                        receiveMetrics.Errors = 1;
                                    }
                                }
                                else
                                {
                                    receiveMetrics.Completions = receiveMetrics.CompleteCalls = 1;
                                }
                                Metrics.PushReceiveMetrics(receiveMetrics);
                                semaphore.Release();
                                if (Interlocked.Increment(ref totalReceives) >= Settings.MessageCount)
                                {
                                    done.Release();
                                }
                            }).Fork();
                        };

                    }).Fork();
                }
                else
                {
                    nsec = sw.ElapsedTicks;
                    // we're going to unblock the receives after 10 seconds if there's no pending message
                    receiver.ReceiveAsync(Settings.ReceiveBatchCount, TimeSpan.FromSeconds(10)).ContinueWith(async (t) =>
                    {
                        receiveMetrics.ReceiveDuration100ns = sw.ElapsedTicks - nsec;
                        if (t.IsFaulted || t.IsCanceled || t.Result == null)
                        {
                            if (t.Exception?.GetType() == typeof(ServerBusyException))
                            {
                                receiveMetrics.BusyErrors = 1;
                                if (!this.CancellationToken.IsCancellationRequested)
                                {
                                    await Task.Delay(3000, this.CancellationToken).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                receiveMetrics.Errors = 1;
                            }
                            Metrics.PushReceiveMetrics(receiveMetrics);
                            semaphore.Release();
                            if (Interlocked.Increment(ref totalReceives) >= Settings.MessageCount)
                            {
                                done.Release();
                            }
                        }
                        else
                        {
                            receiveMetrics.Messages = t.Result.Count;
                            receiveMetrics.Receives = 1;
                            nsec = sw.ElapsedTicks;
                            if (Settings.ReceiveMode == ReceiveMode.PeekLock)
                            {
                                if (Settings.WorkDuration > 0)
                                {
                                    // handle completes singly
                                    for (int i = 0; i < t.Result.Count; i++)
                                    {
                                        await Task.Delay(TimeSpan.FromMilliseconds(Settings.WorkDuration));
                                        await receiver.CompleteAsync(t.Result[i].SystemProperties.LockToken).ContinueWith(async (t1) =>
                                        {
                                            receiveMetrics.CompleteDuration100ns = sw.ElapsedTicks - nsec;
                                            if (t1.IsFaulted)
                                            {
                                                if (t1.Exception?.GetType() == typeof(ServerBusyException))
                                                {
                                                    receiveMetrics.BusyErrors = 1;
                                                    if (!this.CancellationToken.IsCancellationRequested)
                                                    {
                                                        await Task.Delay(3000, this.CancellationToken).ConfigureAwait(false);
                                                    }
                                                }
                                                else
                                                {
                                                    receiveMetrics.Errors = 1;
                                                }
                                            }
                                            else
                                            {
                                                receiveMetrics.CompleteCalls = 1;
                                                receiveMetrics.Completions = 1;
                                            }
                                            Metrics.PushReceiveMetrics(receiveMetrics);
                                            semaphore.Release();
                                            if (Interlocked.Increment(ref totalReceives) >= Settings.MessageCount)
                                            {
                                                done.Release();
                                            }
                                        });
                                    }
                                }
                                else
                                {
                                    // batch complete
                                    await receiver.CompleteAsync(t.Result.Select((m) => { return m.SystemProperties.LockToken; })).ContinueWith(async (t1) =>
                                    {
                                        receiveMetrics.CompleteDuration100ns = sw.ElapsedTicks - nsec;
                                        if (t1.IsFaulted)
                                        {
                                            if (t1.Exception?.GetType() == typeof(ServerBusyException))
                                            {
                                                receiveMetrics.BusyErrors = 1;
                                                if (!this.CancellationToken.IsCancellationRequested)
                                                {
                                                    await Task.Delay(3000, this.CancellationToken).ConfigureAwait(false);
                                                }
                                            }
                                            else
                                            {
                                                receiveMetrics.Errors = 1;
                                            }
                                        }
                                        else
                                        {
                                            receiveMetrics.CompleteCalls = 1;
                                            receiveMetrics.Completions = t.Result.Count;
                                        }
                                        Metrics.PushReceiveMetrics(receiveMetrics);
                                        semaphore.Release();

                                        // count all the messages
                                        for (int k = 0; k < t.Result.Count; k++)
                                        {
                                            if (Interlocked.Increment(ref totalReceives) >= Settings.MessageCount)
                                            {
                                                done.Release();
                                            }
                                        }
                                    });
                                }
                            }
                            else
                            {
                                if (Settings.WorkDuration > 0)
                                {
                                    await Task.Delay(TimeSpan.FromMilliseconds(Settings.WorkDuration));
                                }
                                Metrics.PushReceiveMetrics(receiveMetrics);
                                semaphore.Release();
                                if (Interlocked.Increment(ref totalReceives) >= Settings.MessageCount)
                                {
                                    done.Release();
                                }
                            }
                        };
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
    }
}
