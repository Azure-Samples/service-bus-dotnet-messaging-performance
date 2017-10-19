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
    using Microsoft.Azure.ServiceBus;

    class Settings
    {
        Settings()
        {
            this.MaxInflightReceives = 20;
            this.MaxInflightSends = 20;
        }

        public string ConnectionString { get; set; }
        
        public long MessageCount { get; set; }

        public int MessageSizeInBytes { get; set; }

        public TimeSpan MetricsDisplayFrequency { get; set; }

        public ReceiveMode ReceiveMode { get; set; }

        public int ReceiverCount { get; set; }

        public int SendBatchCount { get; set; }

        public int SenderCount { get; set; }

        public IList<string> ReceivePaths { get; set; }

        public string SendPath { get; set; }
        public int MaxInflightSends { get; internal set; }
        public int MaxInflightReceives { get; internal set; }
        public int ReceiveBatchCount { get; private set; }

        public void PrintSettings()
        {
            Console.WriteLine("Settings:");
            Console.WriteLine("{0}: {1}", "ReceivePaths", string.Join(",", this.ReceivePaths));
            Console.WriteLine("{0}: {1}", "SendPaths", this.SendPath);
            Console.WriteLine("{0}: {1}", "MessageCount", this.MessageCount);
            Console.WriteLine("{0}: {1}", "MessageSizeInBytes", this.MessageSizeInBytes);
            Console.WriteLine("{0}: {1}", "SenderCount", this.SenderCount);
            Console.WriteLine("{0}: {1}", "ReceiveMode", this.ReceiveMode);
            Console.WriteLine("{0}: {1}", "ReceiverCount", this.ReceiverCount);
            Console.WriteLine("{0}: {1}", "MetricsDisplayFrequency", this.MetricsDisplayFrequency);
            Console.WriteLine();
        }

        public static Settings CreateQueueSettings(string connectionString)
        {
            Settings settings = new Settings()
            {
                ConnectionString = connectionString,
                SendPath  = string.Format("myqueue"),
                MessageCount = 1000000,
                MessageSizeInBytes = 1024,
                MetricsDisplayFrequency = TimeSpan.FromSeconds(10),
                ReceiveMode = ReceiveMode.PeekLock,
                ReceiverCount = 2,
                SenderCount = 0,
                SendBatchCount = 10,
                ReceiveBatchCount = 100,
                MaxInflightReceives = 3,
                MaxInflightSends = 50
            };
            settings.ReceivePaths = new string[] { settings.SendPath };

            return settings;
        }

        public static Settings CreateTopicSettings(string connectionString, int subscriptionCount)
        {
            Settings settings = new Settings()
            {
                ConnectionString = connectionString,
                SendPath = string.Format("mytopic"),
                MessageCount = 10000000,
                MessageSizeInBytes = 1024,
                MetricsDisplayFrequency = TimeSpan.FromSeconds(10),
                ReceiveMode = ReceiveMode.PeekLock,
                ReceiverCount = 1,
                SenderCount = 0,
            };

            var subscriptionNames = new List<string>();
            for (int i = 1; i <= subscriptionCount; i++)
            {
                subscriptionNames.Add(string.Format("sub_{0}", i.ToString("##")));
            }

            settings.ReceivePaths = subscriptionNames;

            return settings;
        }
    }
}
