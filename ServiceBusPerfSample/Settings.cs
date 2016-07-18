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
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;

    class Settings
    {
        Settings()
        {
        }

        public string ConnectionString { get; set; }

        public ConnectivityMode ConnectivityMode { get; set; }

        public EntityType EntityType { get; set; }

        public long MessageCount { get; set; }

        public int MessageSizeInBytes { get; set; }

        public TimeSpan MetricsDisplayFrequency { get; set; }

        public string QueuePath { get; set; }

        public int ReceiveBatchCount { get; set; }

        public ReceiveMode ReceiveMode { get; set; }

        public int ReceiverCount { get; set; }

        public int SendBatchCount { get; set; }

        public int SenderCount { get; set; }

        public IList<string> SubscriptionNames { get; set; }

        public string TopicPath { get; set; }

        public TransportType TransportType { get; set; }

        public void PrintSettings()
        {
            Console.WriteLine("Settings:");
            Console.WriteLine("{0}: {1}", "EntityType", this.EntityType);
            if (this.EntityType == EntityType.Topic)
            {
                Console.WriteLine("{0}: {1}", "TopicPath", this.TopicPath);
                Console.WriteLine("{0}: {1}", "SubscriptionNames", string.Join(",", this.SubscriptionNames));
            }
            else
            {
                Console.WriteLine("{0}: {1}", "QueuePath", this.QueuePath);
            }
            Console.WriteLine("{0}: {1}", "TransportType", this.TransportType);
            Console.WriteLine("{0}: {1}", "ConnectivityMode", this.ConnectivityMode);
            Console.WriteLine("{0}: {1}", "MessageCount", this.MessageCount);
            Console.WriteLine("{0}: {1}", "MessageSizeInBytes", this.MessageSizeInBytes);
            Console.WriteLine("{0}: {1}", "SendBatchCount", this.SendBatchCount);
            Console.WriteLine("{0}: {1}", "SenderCount", this.SenderCount);
            Console.WriteLine("{0}: {1}", "ReceiveMode", this.ReceiveMode);
            Console.WriteLine("{0}: {1}", "ReceiveBatchCount", this.ReceiveBatchCount);
            Console.WriteLine("{0}: {1}", "ReceiverCount", this.ReceiverCount);
            Console.WriteLine("{0}: {1}", "MetricsDisplayFrequency", this.MetricsDisplayFrequency);
            Console.WriteLine();
        }

        public static Settings CreateQueueSettings(string connectionString, TransportType transportType)
        {
            Settings settings = new Settings()
            {
                ConnectionString = connectionString,
                QueuePath  = string.Format("q_{0}", DateTime.UtcNow.ToString("yyyy_MM_dd_hh_mm_ss_ff")),
                ConnectivityMode = ConnectivityMode.Tcp,
                EntityType = EntityType.Queue,
                MessageCount = 10000000,
                MessageSizeInBytes = 1024,
                MetricsDisplayFrequency = TimeSpan.FromSeconds(30),
                ReceiveBatchCount = 100,
                ReceiveMode = ReceiveMode.PeekLock,
                ReceiverCount = 40,
                SendBatchCount = 100,
                SenderCount = 20,
                TransportType = transportType
            };

            return settings;
        }

        public static Settings CreateTopicSettings(string connectionString, int subscriptionCount, TransportType transportType)
        {
            Settings settings = new Settings()
            {
                ConnectionString = connectionString,
                TopicPath = string.Format("t_{0}", DateTime.UtcNow.ToString("yyyy_MM_dd_hh_mm_ss_ff")),
                ConnectivityMode = ConnectivityMode.Tcp,
                EntityType = EntityType.Topic,
                MessageCount = 10000000,
                MessageSizeInBytes = 1024,
                MetricsDisplayFrequency = TimeSpan.FromSeconds(30),
                ReceiveBatchCount = 100,
                ReceiveMode = ReceiveMode.PeekLock,
                ReceiverCount = 20,
                SendBatchCount = 100,
                SenderCount = 10,
                TransportType = transportType
            };

            var subscriptionNames = new List<string>();
            for (int i = 1; i <= subscriptionCount; i++)
            {
                subscriptionNames.Add(string.Format("sub_{0}", i.ToString("##")));
            }

            settings.SubscriptionNames = subscriptionNames;

            return settings;
        }
    }
}
