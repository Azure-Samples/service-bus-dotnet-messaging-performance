//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusThroughputTestLib
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Messaging.ServiceBus;
    using ServiceBusThroughputTest.Common;
    using ServiceBusThroughputTestLib.Extensions;

    public class Receiver
    {
        string connectionString;
        string queueName;
        int prefetchCount = 0;
        int batchSize = 1;
        int receiversCount = 1;
        int concurrentCalls = 1;
        ILogger logger = null;
        int callIntervalMS = 1000;
        bool isReceiveAndDelete = false;

        public Receiver(string connectionString, string queueName, int prefetchCount, int batchSize, int concurrentCalls, int receiversCount, int callIntervalMS, bool isReceiveAndDelete = false, ILogger logger = null)
        {
            this.connectionString = connectionString;
            this.queueName = queueName;
            this.prefetchCount = prefetchCount;
            this.concurrentCalls = concurrentCalls;
            this.receiversCount = receiversCount;
            this.batchSize = batchSize;
            this.callIntervalMS = callIntervalMS;
            this.isReceiveAndDelete = isReceiveAndDelete;
            this.logger = logger;
        }

        public async Task Run(CancellationToken cancellationToken)
        {
            ServiceBusClient[] clients = new ServiceBusClient[this.receiversCount];

            try
            {
                ServiceBusReceiveMode receiveMode = this.isReceiveAndDelete ? ServiceBusReceiveMode.ReceiveAndDelete : ServiceBusReceiveMode.PeekLock;

                // create the options to use for configuring the processor
                var options = new ServiceBusReceiverOptions
                {
                    // By default or when AutoCompleteMessages is set to true, the processor will complete the message after executing the message handler
                    // Set AutoCompleteMessages to false to [settle messages](https://docs.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement#peeklock) on your own.
                    // In both cases, if the message handler throws an exception without settling the message, the processor will abandon the message.
                    ReceiveMode = receiveMode,
                    PrefetchCount = prefetchCount,
                };

                Task[] receiveTasks = new Task[this.receiversCount];
                
                for (var i = 0; i < this.receiversCount; i++)
                {
                    clients[i] = new ServiceBusClient(connectionString);

                    ServiceBusReceiver receiver;
                    if (queueName.Contains(":"))
                    {
                        var topic = queueName.Split(':')[0];
                        var sub = queueName.Split(':')[1];
                        receiver = clients[i].CreateReceiver(topic, sub, options);
                    }
                    else
                    {
                        receiver = clients[i].CreateReceiver(queueName, options);
                    }

                    receiveTasks[i] = Start(receiver, cancellationToken);
                }

                await Task.WhenAll(receiveTasks);
            }
            catch (Exception e)
            {
                logger?.AddTrace(e.ToString());
            }
            finally
            {
                //Parallel.ForEach(clients, async client =>
                //{
                //    await client.DisposeAsync();
                //});
            }

            if (logger != null)
            {
                logger.AddTrace($"cancellationToken.IsCancellationRequested={cancellationToken.IsCancellationRequested}");
                logger.Dispose();
                logger = null;
            }
        }

        async Task Start(ServiceBusReceiver receiver, CancellationToken cancellationToken)
        {
            var semaphore = new SemaphoreSlim(this.concurrentCalls);
            var sw = Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);

                    var sessionId = logger?.StartRecordTime();

                    Fork(receiver.ReceiveMessagesAsync(this.batchSize, TimeSpan.FromMilliseconds(this.callIntervalMS)).ContinueWith(
                        async (t) =>
                        {
                            if (sessionId != null)
                            {
                                logger.StopRecordTimeAndGetElapsedMs(sessionId.Value);
                            }
                            
                            if (!t.IsFaulted && !this.isReceiveAndDelete && t.Result?.Any() == true)
                            {
                                // following function will release semaphore so no need to do it here
                                await CompleteMessagesAndReleaseSemaphore(t.Result, receiver, semaphore, cancellationToken, logger, sessionId);
                            }
                            else
                            {
                                semaphore.Release();

                                if (t.IsFaulted)
                                {
                                    t.Exception.HandleExceptions(logger, sessionId, cancellationToken);
                                }
                                else if (sessionId != null)
                                {
                                    logger.IncrementActionCount(sessionId.Value);
                                    logger.IncrementMetricValueBy(sessionId.Value, t.Result?.Count ?? 0);
                                    logger.DisposeSession(sessionId.Value);
                                }
                            }
                        }));
                }
                catch (Exception ex)
                {
                    logger?.AddTrace(ex.ToString());
                }
                finally
                {
                }
            }

            await receiver.CloseAsync();
        }

        static async Task CompleteMessagesAndReleaseSemaphore(IReadOnlyList<ServiceBusReceivedMessage> messages, ServiceBusReceiver receiver, SemaphoreSlim semaphore, CancellationToken cancellationToken, ILogger logger = null, Guid? sessionId = null)
        {
            try
            {
                var completeTasks = new Task[messages.Count];

                if (sessionId != null && logger != null)
                {
                    logger.StartCompletionRecordTime(sessionId.Value);
                }

                for (var i = 0; i < messages.Count; i++)
                {
                    completeTasks[i] = receiver.CompleteMessageAsync(messages[i]);
                }

                await Task.WhenAll(completeTasks).ContinueWith(t =>
                {
                    if (sessionId != null)
                    {
                        logger?.StopCompletionRecordTimeAndGetElapsedMs(sessionId.Value);
                    }

                    semaphore.Release();

                    if (t.IsFaulted)
                    {
                        t.Exception.HandleExceptions(logger, sessionId, cancellationToken);
                    }
                    else
                    {
                        if (sessionId != null)
                        {
                            logger.IncrementActionCount(sessionId.Value);
                            logger.IncrementMetricValueBy(sessionId.Value, messages.Count);
                            logger.DisposeSession(sessionId.Value);
                        }
                    }
                });
            }
            catch (Exception e)
            {
                semaphore.Release();

                logger?.AddTrace($"Error completing message/s - {e}");
            }
        }

        static void Fork(Task t)
        {
            t.ContinueWith((_) => { });
        }
    }
}
