//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    using System;
    using System.Collections.Generic;
    using CommandLine;
    using CommandLine.Text;
    using Microsoft.Azure.ServiceBus;

    class Settings
    {
        [Option('C', "connection-string", Required = true, HelpText = "Connection string")]
        public string ConnectionString { get; set; }

        [Option('S', "send-path", Required = false, HelpText = "Send path. Queue or topic name, unless set in connection string EntityPath.")]
        public string SendPath { get; set; }

        [Option('R', "receive-paths", Required = false, HelpText = "Receive paths. Mandatory for receiving from topic subscriptions. Must be {topic}/subscriptions/{subscription-name} or {queue-name}")]
        public IEnumerable<string> ReceivePaths { get; set; }

        [Option('n', "number-of-messages", Required = false, HelpText = "Number of messages to send (default 1000000)")]
        public long MessageCount { get; set; } = 1000000;

        [Option('b', "message-size-bytes", Required = false, HelpText = "Bytes per message (default 1024)")]
        public int MessageSizeInBytes { get; set; } = 1024;

        [Option('f', "frequency-metrics", Required = false, HelpText = "Frequency of metrics display (seconds, default 10s)")]
        public int MetricsDisplayFrequency { get; set; } = 10;

        [Option('m', "receive-mode", Required = false, HelpText = "Receive mode.'PeekLock' (default) or 'ReceiveAndDelete'")]
        public ReceiveMode ReceiveMode { get; set; } = ReceiveMode.PeekLock;

        [Option('r', "receiver-count", Required = false, HelpText = "Number of concurrent receivers (default 1)")]
        public int ReceiverCount { get; set; } = 5;

        [Option('e', "prefetch-count", Required = false, HelpText = "Prefetch count (default 0)")]
        public int PrefetchCount { get; set; } = 100;

        [Option('t', "send-batch-count", Required = false, HelpText = "Number of messages per batch (default 0, no batching)")]
        public int SendBatchCount { get; set; } = 0;

        [Option('s', "sender-count", Required = false, HelpText = "Number of concurrent senders (default 1)")]
        public int SenderCount { get; set; } = 1;

        [Option('d', "send-delay", Required = false, HelpText = "Delay between sends of any sender (milliseconds, default 0)")]
        public int SendDelay { get; private set; } = 0;

        [Option('i', "inflight-sends", Required = false, HelpText = "Maximum numbers of concurrent in-flight send operations (default 1)")]
        public int CfgMaxInflightSends { get { return MaxInflightSends.Value; } set { MaxInflightSends = new Observable<int>(value); } }

        public Observable<int> MaxInflightSends { get; internal set; } = new Observable<int>(1);

        [Option('j', "inflight-receives", Required = false, HelpText = "Maximum number of concurrent in-flight receive operations per receiver (default 1)")]
        public int CfgMaxInflightReceives { get { return MaxInflightReceives.Value; } set { MaxInflightReceives = new Observable<int>(value); } }
        public Observable<int> MaxInflightReceives { get; internal set; } = new Observable<int>(1);

        [Option('v', "receive-batch-count", Required = false, HelpText = "Max number of messages per batch (default 0, no batching)")]
        public int ReceiveBatchCount { get; private set; } = 0;

        [Option('w', "receive-work-duration", Required = false, HelpText = "Work simulation delay between receive and completion (milliseconds, default 0, no work)")]
        public int WorkDuration { get; private set; } = 0;

        public void PrintSettings()
        {
            Console.WriteLine("Settings:");
            Console.WriteLine("{0}: {1}", "ReceivePaths", string.Join(",", this.ReceivePaths));
            Console.WriteLine("{0}: {1}", "SendPaths", this.SendPath);
            Console.WriteLine("{0}: {1}", "MessageCount", this.MessageCount);
            Console.WriteLine("{0}: {1}", "MessageSizeInBytes", this.MessageSizeInBytes);
            Console.WriteLine("{0}: {1}", "SenderCount", this.SenderCount);
            Console.WriteLine("{0}: {1}", "SendBatchCount", this.SendBatchCount);
            Console.WriteLine("{0}: {1}", "MaxInflightSends", this.CfgMaxInflightSends);
            Console.WriteLine("{0}: {1}", "ReceiveMode", this.ReceiveMode);
            Console.WriteLine("{0}: {1}", "ReceiverCount", this.ReceiverCount);
            Console.WriteLine("{0}: {1}", "ReceiveBatchCount", this.ReceiveBatchCount);
            Console.WriteLine("{0}: {1}", "ReceiveMode", this.ReceiveMode);
            Console.WriteLine("{0}: {1}", "MaxInflightReceives", this.CfgMaxInflightReceives);
            Console.WriteLine("{0}: {1}", "MetricsDisplayFrequency", this.MetricsDisplayFrequency);
            Console.WriteLine("{0}: {1}", "WorkDuration", this.WorkDuration);

            Console.WriteLine();
        }

        [Usage()]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("queue scenario", new Settings { ConnectionString = "{Connection-String-with-EntityPath}" });
                yield return new Example("topic scenario", new Settings { ConnectionString = "{Connection-String}", SendPath = "{Topic-Name}", ReceivePaths = new string[] { "{Topic-Name}/subscriptions/{Subscription-Name-1}", "{Topic-Name}/subscriptions/{Subscription-Name-2}" } });
            }
        }
    }
}
