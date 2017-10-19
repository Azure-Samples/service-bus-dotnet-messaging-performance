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
    using System.Threading;

    sealed class MetricsData
    {
        private TimeSpan elapsed;

        public TimeSpan Elapsed
        {
            get { return elapsed; }
            set { elapsed = value; }
        }

        private long sendMessageCount;

        public long SendMessageCount
        {
            get { return sendMessageCount; }
            set { sendMessageCount = value; }
        }

        private long serverBusyCount;

        public long ServerBusyCount
        {
            get { return serverBusyCount; }
            set { serverBusyCount = value; }
        }

        private long errorCount;

        public long ErrorCount
        {
            get { return errorCount; }
            set { errorCount = value; }
        }
        
        public long SendMessageRate
        {
            get
            {
                return this.SendMessageCount / (long)this.Elapsed.TotalSeconds;
            }
        }

        private long sendLatency;

        public long SendLatency
        {
            get { return sendLatency; }
            set { sendLatency = value; }
        }

        
        public double SendAverageLatency 
        {
            get
            {
                return Math.Round(this.SendLatency / (double)this.SendMessageCount, 2);
            }
        }

        private long receiveMessageCount;

        public long ReceiveMessageCount
        {
            get { return receiveMessageCount; }
            set { receiveMessageCount = value; }
        }

        private long completeMessageCount;

        public long CompleteMessageCount
        {
            get { return completeMessageCount; }
            set { completeMessageCount = value; }
        }



        public long ReceiveMessageRate
        {
            get
            {
                return this.ReceiveMessageCount / (long)this.Elapsed.TotalSeconds;
            }
        }

        private long receiveLatency;

        public long ReceiveLatency
        {
            get { return receiveLatency; }
            set { receiveLatency = value; }
        }
        
        public long ReceiveAverageLatency
        {
            get
            {
                if (this.ReceiveMessageCount == 0) return 0;
                return this.ReceiveLatency / this.ReceiveMessageCount;
            }
        }

        private long completeLatency;

        public long CompleteLatency
        {
            get { return completeLatency; }
            set { completeLatency = value; }
        }

        
        public long CompleteAverageLatency
        {
            get
            {
                if (this.CompleteMessageCount == 0) return 0;
                return this.CompleteLatency / this.CompleteMessageCount;
            }
        }

        public MetricsData Clone()
        {
            MetricsData metricsSnapshot = new MetricsData();

            metricsSnapshot.Elapsed = this.Elapsed;
            metricsSnapshot.SendMessageCount = this.SendMessageCount;
            metricsSnapshot.SendLatency = this.SendLatency;
            metricsSnapshot.ReceiveMessageCount = this.ReceiveMessageCount;
            metricsSnapshot.CompleteMessageCount = this.CompleteMessageCount;
            metricsSnapshot.ReceiveLatency = this.ReceiveLatency;
            metricsSnapshot.CompleteLatency = this.CompleteLatency;
            metricsSnapshot.ServerBusyCount = this.ServerBusyCount;
            metricsSnapshot.ErrorCount = this.ErrorCount;

            return metricsSnapshot;
        }

        internal void IncreaseCompleteMessages(long count)
        {
            Interlocked.Add(ref completeMessageCount, count);
        }

        public static MetricsData operator -(MetricsData m1, MetricsData m2)
        {
            MetricsData metrics = new MetricsData();

            metrics.Elapsed = m1.Elapsed - m2.Elapsed;
            metrics.SendMessageCount = m1.SendMessageCount - m2.SendMessageCount;
            metrics.SendLatency = m1.SendLatency - m2.SendLatency;
            metrics.ReceiveMessageCount = m1.ReceiveMessageCount - m2.ReceiveMessageCount;
            metrics.CompleteMessageCount = m1.CompleteMessageCount - m2.CompleteMessageCount;
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

        public void IncreaseSendMessages(long count)
        {
            Interlocked.Add(ref this.sendMessageCount, count);
        }

        public void IncreaseSendLatency(long ms)
        {
            Interlocked.Add(ref this.sendLatency, ms);
        }

        public void IncreaseReceiveMessages(long count)
        {
            Interlocked.Add(ref this.receiveMessageCount, count);
        }
        
        public void IncreaseReceiveLatency(long ms)
        {
            Interlocked.Add(ref this.receiveLatency, ms);
        }

        public void IncreaseCompleteLatency(long ms)
        {
            Interlocked.Add(ref this.completeLatency, ms);
        }

        public void IncreaseServerBusy(long count)
        {
            Interlocked.Add(ref this.serverBusyCount, count);
        }

        public void IncreaseErrorCount(long count)
        {
            Interlocked.Add(ref this.errorCount, count);
        }
    }
}
