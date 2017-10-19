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
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    sealed class Metrics
    {
        readonly object syncObject;
        readonly MetricsData metricsData;
        readonly Settings settings;
        Stopwatch stopwatch;

        public Metrics(Settings settings)
        {
            this.syncObject = new object();
            this.metricsData = new MetricsData();
            this.stopwatch = new Stopwatch();
            this.settings = settings;
        }

        public TimeSpan Elapsed
        {
            get
            {
                return this.stopwatch.Elapsed;
            }
        }

        public void IncreaseSendMessages(long count)
        {
            metricsData.IncreaseSendMessages(count);
        }

        public void IncreaseSendLatency(long ms)
        {
            metricsData.IncreaseSendLatency(ms);
        }

        public void IncreaseReceiveMessages(long count)
        {
            metricsData.IncreaseReceiveMessages(count);
        }

        public void IncreaseReceiveLatency(long ms)
        {
            metricsData.IncreaseReceiveLatency(ms);
        }

        public void IncreaseCompleteLatency(long ms)
        {
            metricsData.IncreaseCompleteLatency(ms);
        }

        public void IncreaseServerBusy(long count)
        {
            metricsData.IncreaseServerBusy(count);
        }

        public void IncreaseErrorCount(long count)
        {
            metricsData.IncreaseErrorCount(count);
        }

        public async Task StartMetricsTask(CancellationToken cancellationToken)
        {
            MetricsData.WriteHeader();
            var previous = this.GetSnapshot();
            this.Start();
            while (!cancellationToken.IsCancellationRequested)
            {
                await Extensions.Delay(this.settings.MetricsDisplayFrequency, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var current = this.GetSnapshot();
                var diff = current - previous;

                diff.WriteInfo(DateTime.Now.ToLongTimeString());

                previous = current;
            }
        }

        public void WriteSummary()
        {
            MetricsData summary = this.GetSnapshot();
            summary.WriteInfo("SUMMARY");
        }

        void Start()
        {
            this.stopwatch.Start();
        }

        public MetricsData GetSnapshot()
        {
            MetricsData metricsSnapshot;
            metricsSnapshot = this.metricsData.Clone();
            metricsSnapshot.Elapsed = this.Elapsed;
            return metricsSnapshot;
        }

        internal void IncreaseCompleteMessages(int v)
        {
            metricsData.IncreaseCompleteMessages(v);
        }
    }
}
