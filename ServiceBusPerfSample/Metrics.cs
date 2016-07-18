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
            lock (syncObject)
            {
                this.metricsData.SendMessageCount += count;
            }
        }

        public void IncreaseSendBatch(long count)
        {
            lock (syncObject)
            {
                this.metricsData.SendBatchCount += count;
            }
        }

        public void IncreaseSendLatency(double ms)
        {
            lock (syncObject)
            {
                this.metricsData.SendLatency += ms;
            }
        }

        public void IncreaseReceiveMessages(long count)
        {
            lock (syncObject)
            {
                this.metricsData.ReceiveMessageCount += count;
            }
        }

        public void IncreaseReceiveBatch(long count)
        {
            lock (syncObject)
            {
                this.metricsData.ReceiveBatchCount += count;
            }
        }

        public void IncreaseReceiveLatency(double ms)
        {
            lock (syncObject)
            {
                this.metricsData.ReceiveLatency += ms;
            }
        }

        public void IncreaseCompleteLatency(double ms)
        {
            lock (syncObject)
            {
                this.metricsData.CompleteLatency += ms;
            }
        }

        public void IncreaseServerBusy(long count)
        {
            lock (syncObject)
            {
                this.metricsData.ServerBusyCount += count;
            }
        }

        public void IncreaseErrorCount(long count)
        {
            lock (syncObject)
            {
                this.metricsData.ErrorCount += count;
            }
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

            lock (syncObject)
            {
                metricsSnapshot = this.metricsData.Clone();
                metricsSnapshot.Elapsed = this.Elapsed;
            }

            return metricsSnapshot;
        }
    }
}
