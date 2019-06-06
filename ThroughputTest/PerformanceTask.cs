//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    using System.Threading;
    using System.Threading.Tasks;

    abstract class PerformanceTask
    {
        protected PerformanceTask(Settings settings, Metrics metrics, CancellationToken cancellationToken)
        {
            this.Settings = settings;
            this.Metrics = metrics;
            this.CancellationToken = cancellationToken;
        }

        protected Settings Settings { get; private set; }

        protected string ConnectionString { get; private set; }

        protected Metrics Metrics { get; private set; }
        
        protected CancellationToken CancellationToken { get; private set; }

        public void Close()
        {
            this.CloseAsync().Fork();
        }

        public async Task OpenAsync()
        {
            await OnOpenAsync().ConfigureAwait(false);
        }

        public Task StartAsync()
        {
            return OnStartAsync();
        }

        public Task CloseAsync()
        {
            return Task.CompletedTask;
        }

        protected abstract Task OnOpenAsync();

        protected abstract Task OnStartAsync();
       
        
    }
}
