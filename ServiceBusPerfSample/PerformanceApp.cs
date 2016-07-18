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
    using System.Threading;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    sealed class PerformanceApp
    {
        readonly Settings settings;
        readonly Metrics metrics;
        readonly CancellationTokenSource cancellationTokenSource;
        readonly List<PerformaceTask> tasks;

        public PerformanceApp(Settings settings)
        {
            ServiceBusEnvironment.SystemConnectivity.Mode = settings.ConnectivityMode;
            this.settings = settings;
            this.metrics = new Metrics(settings);
            this.cancellationTokenSource = new CancellationTokenSource();
            this.tasks = new List<PerformaceTask>();
        }

        public async void Start()
        {
            this.settings.PrintSettings();

            var namespaceManager = NamespaceManager.CreateFromConnectionString(this.settings.ConnectionString);
            if (this.settings.EntityType == EntityType.Topic)
            {
                Console.WriteLine("Creating topic: {0}...", this.settings.TopicPath);
                var topicDescription = new TopicDescription(this.settings.TopicPath)
                {
                    EnablePartitioning = true,
                };

                await namespaceManager.CreateTopicAsync(topicDescription);
                foreach (var subscriptionName in this.settings.SubscriptionNames)
                {
                    Console.WriteLine("Creating subscription: {0}...", subscriptionName);
                    await namespaceManager.CreateSubscriptionAsync(this.settings.TopicPath, subscriptionName);
                }
            }
            else
            {
                Console.WriteLine("Creating queue: {0}...", this.settings.QueuePath);
                var queueDescription = new QueueDescription(this.settings.QueuePath)
                {
                    EnablePartitioning = true,
                };

                await namespaceManager.CreateQueueAsync(queueDescription);
            }

            tasks.Add(new ReceiverTask(settings, this.metrics, this.cancellationTokenSource.Token));
            tasks.Add(new SenderTask(settings, this.metrics, this.cancellationTokenSource.Token));

            Console.WriteLine("Opening...");
            await tasks.ParallelForEachAsync(async (t) => await t.OpenAsync());
            Console.WriteLine("Opened");
            Console.WriteLine("Starting...");
            Console.WriteLine();
            this.metrics.StartMetricsTask(this.cancellationTokenSource.Token).Fork();

            await tasks.ParallelForEachAsync(async (t) => await t.StartAsync());
        }

        public void Stop()
        {
            this.metrics.WriteSummary();
            this.cancellationTokenSource.Cancel();
            this.tasks.ForEach((t) => t.Close());

            var namespaceManager = NamespaceManager.CreateFromConnectionString(this.settings.ConnectionString);
            if (this.settings.EntityType == EntityType.Topic)
            {
                Console.WriteLine("\nDeleting topic: {0}...", this.settings.TopicPath);
                namespaceManager.DeleteTopicAsync(this.settings.TopicPath).Fork();
            }
            else
            {
                Console.WriteLine("\nDeleting queue: {0}...", this.settings.QueuePath);
                namespaceManager.DeleteQueueAsync(this.settings.QueuePath).Fork();
            }
        }
    }
}
