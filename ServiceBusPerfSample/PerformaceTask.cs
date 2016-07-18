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
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    abstract class PerformaceTask
    {
        protected PerformaceTask(Settings settings, Metrics metrics, CancellationToken cancellationToken)
        {
            this.Settings = settings;
            this.Metrics = metrics;
            this.Factories = new List<MessagingFactory>();
            this.CancellationToken = cancellationToken;
            this.ConnectionString = new ServiceBusConnectionStringBuilder(this.Settings.ConnectionString) { TransportType = settings.TransportType }.ToString();
        }

        protected Settings Settings { get; private set; }

        protected string ConnectionString { get; private set; }

        protected Metrics Metrics { get; private set; }

        protected List<MessagingFactory> Factories { get; private set; }

        protected CancellationToken CancellationToken { get; private set; }

        public void Close()
        {
            this.CloseAsync().Fork();
        }

        public async Task OpenAsync()
        {
            await OnOpenAsync();
        }

        public async Task StartAsync()
        {
            await OnStart();
        }

        public async Task CloseAsync()
        {
            await this.Factories.ParallelForEachAsync(async (f) => await Extensions.IgnoreExceptionAsync(async () => await f.CloseAsync()));
        }

        protected abstract Task OnOpenAsync();

        protected abstract Task OnStart();
       
        protected async Task ExecuteOperationAsync(Func<Task> action)
        {
            TimeSpan sleep = TimeSpan.Zero;
            try
            {
                await action();
            }
            catch (Exception ex)
            {

                if (ex is ServerBusyException)
                {
                    this.Metrics.IncreaseServerBusy(1);
                    sleep = TimeSpan.FromSeconds(10);
                }
                else
                {
                    this.Metrics.IncreaseErrorCount(1);
                    sleep = TimeSpan.FromSeconds(3);
                }
            }

            if (sleep > TimeSpan.Zero && !this.CancellationToken.IsCancellationRequested)
            {
                await Extensions.Delay(sleep, this.CancellationToken);
            }
        }
    }
}
