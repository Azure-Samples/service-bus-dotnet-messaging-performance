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
    
    sealed class PerformanceApp
    {
        readonly Settings settings;
        readonly Metrics metrics;
        readonly CancellationTokenSource cancellationTokenSource;
        readonly List<PerformanceTask> tasks;

        public PerformanceApp(Settings settings)
        {
            this.settings = settings;
            this.metrics = new Metrics(settings);
            this.cancellationTokenSource = new CancellationTokenSource();
            this.tasks = new List<PerformanceTask>();
        }

        public async void Start()
        {
            this.settings.PrintSettings();
            
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
        }
    }
}
