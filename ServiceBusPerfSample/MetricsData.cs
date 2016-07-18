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

    sealed class MetricsData
    {
        public TimeSpan Elapsed { get; set; }

        public long SendBatchCount { get; set; }

        public long SendMessageCount { get; set; }

        public long ServerBusyCount { get; set; }

        public long ErrorCount { get; set; }

        public long SendMessageRate
        {
            get
            {
                return this.SendMessageCount / (long)this.Elapsed.TotalSeconds;
            }
        }

        public double SendLatency { get; set; }

        public double SendAverageLatency 
        {
            get
            {
                return Math.Round(this.SendLatency / this.SendBatchCount, 2);
            }
        }

        public long ReceiveMessageCount { get; set; }

        public long ReceiveBatchCount { get; set; }

        public long ReceiveMessageRate
        {
            get
            {
                return this.ReceiveMessageCount / (long)this.Elapsed.TotalSeconds;
            }
        }

        public double ReceiveLatency { get; set; }

        public double ReceiveAverageLatency
        {
            get
            {
                return Math.Round(this.ReceiveLatency / this.ReceiveBatchCount, 2);
            }
        }

        public double CompleteLatency { get; set; }

        public double CompleteAverageLatency
        {
            get
            {
                return Math.Round(this.CompleteLatency / this.ReceiveBatchCount, 2);
            }
        }

        public MetricsData Clone()
        {
            MetricsData metricsSnapshot = new MetricsData();

            metricsSnapshot.Elapsed = this.Elapsed;
            metricsSnapshot.SendMessageCount = this.SendMessageCount;
            metricsSnapshot.SendBatchCount = this.SendBatchCount;
            metricsSnapshot.SendLatency = this.SendLatency;
            metricsSnapshot.ReceiveMessageCount = this.ReceiveMessageCount;
            metricsSnapshot.ReceiveBatchCount= this.ReceiveBatchCount;
            metricsSnapshot.ReceiveLatency = this.ReceiveLatency;
            metricsSnapshot.CompleteLatency = this.CompleteLatency;
            metricsSnapshot.ServerBusyCount = this.ServerBusyCount;
            metricsSnapshot.ErrorCount = this.ErrorCount;

            return metricsSnapshot;
        }

        public static MetricsData operator -(MetricsData m1, MetricsData m2)
        {
            MetricsData metrics = new MetricsData();

            metrics.Elapsed = m1.Elapsed - m2.Elapsed;
            metrics.SendMessageCount = m1.SendMessageCount - m2.SendMessageCount;
            metrics.SendBatchCount = m1.SendBatchCount - m2.SendBatchCount;
            metrics.SendLatency = m1.SendLatency - m2.SendLatency;
            metrics.ReceiveMessageCount = m1.ReceiveMessageCount - m2.ReceiveMessageCount;
            metrics.ReceiveBatchCount = m1.ReceiveBatchCount - m2.ReceiveBatchCount;
            metrics.ReceiveLatency = m1.ReceiveLatency - m2.ReceiveLatency;
            metrics.CompleteLatency = m1.CompleteLatency - m2.CompleteLatency;
            metrics.ServerBusyCount = m1.ServerBusyCount - m2.ServerBusyCount;
            metrics.ErrorCount = m1.ErrorCount - m2.ErrorCount;

            return metrics;
        }

        public static void WriteHeader()
        {
            WriteInfo("", "Send", "Send", "Send|", "Receive", "Receive|", "Complete", "Receive", "", "");
            WriteInfo("", "Rate", "Latency", "Count|", "Rate", "Latency", "Latency", "Count|", "Server", "Other");
            WriteInfo("Label", "(msg/s)", "(ms)", "(msg)|", "(m/s)", "(ms)", "(ms)", "(msg)|", "Busy", "Exceptions");
        }

        public void WriteInfo(object label)
        {
            WriteInfo(label, this.SendMessageRate.ToString("N00"), this.SendAverageLatency.ToString("0.00"), this.SendMessageCount.ToString("N00"), this.ReceiveMessageRate.ToString("N00"), this.ReceiveAverageLatency.ToString("0.00"), this.CompleteAverageLatency.ToString("0.00"), this.ReceiveMessageCount.ToString("N00"), this.ServerBusyCount, this.ErrorCount);
        }

        static void WriteInfo(object label, object sendRate, object sendLatency, object sendCount, object receiveRate, object receiveLatency, object completeLatency, object receiveCount, object serverBusy, object otherError)
        {
            Console.WriteLine("{0,11} {1,9} {2,7} {3,10} {4,9} {5,7} {6,7} {7,10} {8,10} {9,10}", label, sendRate, sendLatency, sendCount, receiveRate, receiveLatency, completeLatency, receiveCount, serverBusy, otherError);
        }
    }
}
