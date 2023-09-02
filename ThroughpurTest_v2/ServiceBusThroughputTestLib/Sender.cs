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
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Messaging.ServiceBus;
    using Extensions;
    using ServiceBusThroughputTest.Common;

    public class Sender
    {
        string connectionString;
        string queueName;

        int payloadSize = 600;
        int callIntervalMS = 0;
        int batchSize = 1;
        int sendersCount = 1;
        int concurrentCalls = 10;
        ILogger logger = null;

        public Sender(string connectionString, string queueName, int payloadSize, int batchSize, int callIntervalMS, int concurrentCalls, int sendersCount, ILogger logger = null)
        {
            this.connectionString = connectionString;
            this.queueName = queueName;
            this.payloadSize = payloadSize;
            this.batchSize = batchSize;
            this.callIntervalMS = callIntervalMS;
            this.concurrentCalls = concurrentCalls;
            this.sendersCount = sendersCount;
            this.logger = logger;
        }

        public async Task Run(CancellationToken cancellationToken)
        {
            ServiceBusClient[] clients = new ServiceBusClient[this.sendersCount];
            try
            {
                Task[] sendTasks = new Task[this.sendersCount];
                var senders = new ServiceBusSender[this.sendersCount];

                for (var i = 0; i < this.sendersCount; i++)
                {
                    clients[i] = new ServiceBusClient(connectionString);

                    senders[i] = clients[i].CreateSender(queueName);
                    sendTasks[i] = Start(senders[i], cancellationToken);
                }

                await Task.WhenAll(sendTasks);
            }
            catch (Exception e)
            {
                logger?.AddTrace(e.ToString());
            }
            finally
            {
                Parallel.ForEach(clients, async client =>
                {
                    await client.DisposeAsync();
                });
            }

            if (logger != null)
            {
                logger.Dispose();
                logger = null;
            }
        }

        async Task Start(ServiceBusSender sender, CancellationToken cancellationToken)
        {
            // Create payload
            byte[] payload = UTF8Encoding.ASCII.GetBytes(new string('a', payloadSize));
            ServiceBusMessage msg = new ServiceBusMessage(payload);
            var batch = new List<ServiceBusMessage>();
            for (var i = 0; i < this.batchSize; i++)
            {
                batch.Add(msg);
            }

            var semaphore = new SemaphoreSlim(this.concurrentCalls);
            var sw = Stopwatch.StartNew();

            // Loop until cancelled
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);

                    var sessionId = logger?.StartRecordTime();

                    Fork(sender.SendMessagesAsync(batch, cancellationToken).ContinueWith(
                            async (t) =>
                            {
                                double timeSpentMS_d = 0.0;

                                if (sessionId != null)
                                {
                                    timeSpentMS_d = logger.StopRecordTimeAndGetElapsedMs(sessionId.Value);
                                }

                                if (t.IsFaulted)
                                {
                                    semaphore.Release();

                                    t.Exception.HandleExceptions(logger, sessionId, cancellationToken);
                                }
                                else
                                {
                                    // Delay upto time interval if needed
                                    if (callIntervalMS > 0)
                                    {
                                        if (timeSpentMS_d < callIntervalMS - 1)
                                        {
                                            await Task.Delay(callIntervalMS - (int)timeSpentMS_d - 1);
                                        }
                                    }

                                    semaphore.Release();

                                    if (sessionId != null)
                                    {
                                        logger.IncrementActionCount(sessionId.Value);
                                        logger.IncrementMetricValueBy(sessionId.Value, batchSize);
                                        logger.DisposeSession(sessionId.Value);
                                    }
                                }

                            }));
                }
                catch (Exception e)
                {
                    logger?.AddTrace(e.ToString());
                }
                finally
                {
                }
            }

            await sender.DisposeAsync();
        }

        static void Fork(Task t)
        {
            t.ContinueWith((_) => { });
        }
    }

}
