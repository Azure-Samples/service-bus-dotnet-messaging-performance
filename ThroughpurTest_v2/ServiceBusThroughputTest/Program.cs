//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusThroughputTest
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using CommandLine;
    using CommandLine.Text;
    using System.Diagnostics;
    using ServiceBusThroughputTestLib;
    using System.Configuration;

    class Program
    {
        const string connString = "<ns-conn-str>";
        const string queue = "queue1";
        const string MetricsSamplesPerDisplayIntervalSenderKeyName = "MetricsSamplesPerDisplayIntervalSender";
        const string MetricsSamplesPerDisplayIntervalReceiverKeyName = "MetricsSamplesPerDisplayIntervalReceiver";
        const int MetricsSamplesPerDisplayIntervalSenderDefault = 2000;
        const int MetricsSamplesPerDisplayIntervalReceiverDefault = 10000;

        static void Main(string[] args)
        {
            CommandLine.Parser.Default.ParseArguments<Settings>(args)
                 .WithParsed<Settings>(opts => StartSendReceives(opts));
        }

        static void StartSendReceives(Settings settings)
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            var cancelTask = Task.Run(() =>
            {
                Console.ReadLine();
                cts.Cancel();
            });

            settings.PrintSettings();

            int metricsSamplesPerDisplayIntervalSender, metricsSamplesPerDisplayIntervalReceiver;
            GetConfigValue(MetricsSamplesPerDisplayIntervalSenderKeyName, int.Parse, out metricsSamplesPerDisplayIntervalSender, MetricsSamplesPerDisplayIntervalSenderDefault);
            GetConfigValue(MetricsSamplesPerDisplayIntervalReceiverKeyName, int.Parse, out metricsSamplesPerDisplayIntervalReceiver, MetricsSamplesPerDisplayIntervalReceiverDefault);

            Console.WriteLine("Starting senders and receivers. Press Enter key to stop at anytime...");

            if (settings.SenderCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.Write("S|{0,10:0.00}|{1,10:0.00}|{2,5}|{3,5}|", "pstart", "pend", "sbc", "mifs");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|", "snd.avg", "snd.min", "snd.max", "snd.p99");
                Console.WriteLine("{0,10}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", "msg/s", "total", "sndop", "errs", "busy", "overall");
            }

            if (settings.ReceiverCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;

                Console.Write("R|{0,10:0.00}|{1,10:0.00}|{2,5}|{3,5}|", "pstart", "pend", "rbc", "mifr");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|", "rcv.avg", "rcv.min", "rcv.max", "rcv.p99");

                Console.Write("{0,10}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", "msg/s", "total", "rcvop", "errs", "busy", "overall");
                Console.WriteLine("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|", "cpl.avg", "cpl.min", "cpl.max", "cpl.p99");
            }

            Console.ForegroundColor = ConsoleColor.White;

            var sndRxTasks = new List<Task> { cancelTask };
            long sendTotal = 0, receiveTotal = 0;

            if (settings.ReceiverCount > 0)
            {
                Metrics receiveMetrics = new Metrics(cts.Token, false, settings.MetricsDisplayFrequency, metricsSamplesPerDisplayIntervalReceiver);

                Receiver receiver = new Receiver(settings.ConnectionString, settings.QueueName, settings.PrefetchCount, settings.ReceiveBatchSize, settings.MaxInflightReceives, settings.ReceiverCount, settings.ReceiveCallIntervalMs, !settings.IsPeekLock, receiveMetrics);
                receiveMetrics.OnMetricsDisplay += (s, e) => Metrics_OnMetricsDisplay(s, e, settings, ref sendTotal, ref receiveTotal);

                sndRxTasks.Add(receiver.Run(cts.Token));
            }

            if (settings.SenderCount > 0)
            {
                Metrics sendMetrics = new Metrics(cts.Token, true, settings.MetricsDisplayFrequency, metricsSamplesPerDisplayIntervalSender);
                Sender sender = new Sender(settings.ConnectionString, settings.QueueName, settings.PayloadSizeInBytes, settings.SendBatchSize, settings.SendCallIntervalMs, settings.MaxInflightSends, settings.SenderCount, sendMetrics);

                sendMetrics.OnMetricsDisplay += (s, e) => Metrics_OnMetricsDisplay(s, e, settings, ref sendTotal, ref receiveTotal);

                sndRxTasks.Add(sender.Run(cts.Token));
            }

            Task.WhenAny(sndRxTasks).Wait();
        }

        private static void Metrics_OnMetricsDisplay(object _, MetricsDisplayEventArgs e, Settings settings, ref long sendTotal, ref long receiveTotal)
        {
            var list = e.SendMetricsList;
            if (list.Count > 0)
            {
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("S|{0,10}|{1,10}|{2,5}|{3,5}|", list.First().Tick / Stopwatch.Frequency + 1, list.Last().Tick / Stopwatch.Frequency + 1, settings.SendBatchSize, settings.MaxInflightSends);

                    WriteStat(list, i => i.SendDuration100ns, Stopwatch.Frequency / 1000.0);
                    var msgs = list.Sum(i => i.Messages);
                    sendTotal += msgs;

                    Console.WriteLine("{0,10:0.00}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", list.Sum(i => i.Messages) / (double)settings.MetricsDisplayFrequency, msgs, list.Sum(i => i.Sends), list.Sum(i => i.Errors), list.Sum(i => i.BusyErrors), sendTotal);
                }
            }

            var rList = e.ReceiveMetricsList;
            if (rList.Count > 0)
            {
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;

                    Console.Write("R|{0,10}|{1,10}|{2,5}|{3,5}|", rList.First().Tick / Stopwatch.Frequency + 1, rList.Last().Tick / Stopwatch.Frequency + 1, settings.ReceiveBatchSize, settings.MaxInflightReceives);
                    WriteStat(rList, i => i.ReceiveDuration100ns, Stopwatch.Frequency / 1000.0);
                    
                    var msgs = rList.Sum(i => i.Messages);
                    receiveTotal += msgs;
                    Console.Write("{0,10:0.00}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", rList.Sum(i => i.Messages) / (double)settings.MetricsDisplayFrequency, msgs, rList.Sum(i => i.Receives), rList.Sum(i => i.Errors), rList.Sum(i => i.BusyErrors), receiveTotal);

                    WriteStat(rList, i => i.CompleteDuration100ns, Stopwatch.Frequency / 1000.0);
                    Console.WriteLine();
                }
            }

            Console.ForegroundColor = ConsoleColor.White;
        }

        public static double Percentile(long[] elements, double percentile)
        {
            Array.Sort(elements);
            double realIndex = percentile * (elements.Length - 1);
            int index = (int)realIndex;
            double frac = realIndex - index;
            if (index + 1 < elements.Length)
                return elements[index] * (1 - frac) + elements[index + 1] * frac;
            else
                return elements[index];
        }

        static void WriteStat<T>(IList<T> list, Func<T, long> f, double scale)
        {
            Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|", list.Average(f) / scale, list.Min(f) / scale, list.Max(f) / scale, Percentile(list.Select(s => f(s)).ToArray(), 0.99) / scale);
        }

        static bool GetConfigValue<T>(string keyName, Func<string, T> parseFunc, out T val, T defaultVal = default(T))
        {
            if (ConfigurationManager.AppSettings.AllKeys.Contains(keyName))
            {
                try
                {
                    val = parseFunc(ConfigurationManager.AppSettings[keyName]);

                    return true;
                }
                catch { }
            }

            val = defaultVal;

            return false;
        }

        static void TestSBReceiver()
        {
            ServiceBusThroughputTestLib.Receiver receiver = new ServiceBusThroughputTestLib.Receiver(connString, "queue1", 1024*10, 10, 1024, 1, 60000, false);
            CancellationTokenSource cts = new CancellationTokenSource();
            Task rt = receiver.Run(cts.Token);
            Thread.Sleep(TimeSpan.FromMinutes(2));
            cts.Cancel();
            rt.Wait();
        }        

        static void TestSBSender()
        {
            int batchSize = 8;
            for (int i = 1; i < 2; i++)
            {
                Console.WriteLine("**** batchSize: " + batchSize);
                ServiceBusThroughputTestLib.Sender sender = new ServiceBusThroughputTestLib.Sender(connString, "queue1", 600, 25, batchSize, 1, 1);
                CancellationTokenSource cts = new CancellationTokenSource();
                Task st = sender.Run(cts.Token);
                Thread.Sleep(TimeSpan.FromMinutes(5));
                //cts.Cancel();
                st.Wait();
                batchSize *= 2;
            }
        }

        static void TestSBSenderLargeMsg()
        {
            int batchSize = 256;
            for (int i = 9; i > 0; i--)
            {
                Console.WriteLine("**** batchSize: " + batchSize);
                ServiceBusThroughputTestLib.Sender ehSender = new ServiceBusThroughputTestLib.Sender(connString, "queue1", 25 * 1024, 25, batchSize, 1, 1);
                CancellationTokenSource cts = new CancellationTokenSource();
                Task st = ehSender.Run(cts.Token);
                Thread.Sleep(TimeSpan.FromMinutes(5));
                cts.Cancel();
                st.Wait();
                batchSize /= 2;
            }
        }
    }

    class Settings
    {
        [Option('C', "connection-string", Required = true, HelpText = "Connection string")]
        public string ConnectionString { get; set; }

        [Option('Q', "entity-path", Required = false, HelpText = "Entity path. Queue or topic. For Topic/Subscription, provide in this format - {topic}:{subscription}")]
        public string QueueName { get; set; }

        [Option('b', "payload-size-bytes", Required = false, HelpText = "Bytes per message (default 1024)")]
        public int PayloadSizeInBytes { get; set; } = 1024;

        [Option('f', "frequency-metrics", Required = false, HelpText = "Frequency of metrics display (seconds, default 10s)")]
        public int MetricsDisplayFrequency { get; set; } = 10;

        [Option('m', "receive-mode", Required = false, HelpText = "Receive mode. 'true' for PeekLock (default) or 'false' for ReceiveAndDelete")]
        public bool IsPeekLock { get; set; } = true;

        [Option('r', "receiver-count", Required = false, HelpText = "Number of concurrent receivers (default 1)")]
        public int ReceiverCount { get; set; } = 5;

        [Option('p', "prefetch-count", Required = false, HelpText = "Prefetch count (default 0)")]
        public int PrefetchCount { get; set; } = 100;

        [Option('t', "send-batch-size", Required = false, HelpText = "Number of messages per batch (default 0, no batching)")]
        public int SendBatchSize { get; set; } = 1;

        [Option('s', "sender-count", Required = false, HelpText = "Number of concurrent senders (default 1)")]
        public int SenderCount { get; set; } = 1;

        [Option('d', "send-callIntervalMs", Required = false, HelpText = "Delay between sends of any sender (milliseconds, default 0)")]
        public int SendCallIntervalMs { get; private set; } = 1000;

        [Option('e', "receive-callIntervalMs", Required = false, HelpText = "Receive timeout for receiver (milliseconds, default 0)")]
        public int ReceiveCallIntervalMs { get; private set; } = 5000;

        [Option('i', "inflight-sends", Required = false, HelpText = "Maximum numbers of concurrent in-flight send operations (default 1)")]
        public int MaxInflightSends { get; internal set; } = 1;

        [Option('j', "inflight-receives", Required = false, HelpText = "Maximum number of concurrent in-flight receive operations per receiver (default 1)")]
        public int MaxInflightReceives { get; internal set; } = 1;

        [Option('v', "receive-batch-size", Required = false, HelpText = "Max number of messages per batch (default 0, no batching)")]
        public int ReceiveBatchSize { get; private set; } = 1;

        public void PrintSettings()
        {
            Console.WriteLine("Settings:");
            Console.WriteLine("{0}: {1}", "SendPaths", this.QueueName);
            Console.WriteLine("{0}: {1}", "PayloadSizeInBytes", this.PayloadSizeInBytes);
            Console.WriteLine("{0}: {1}", "SenderCount", this.SenderCount);
            Console.WriteLine("{0}: {1}", "SendBatchCount", this.SendBatchSize);
            Console.WriteLine("{0}: {1}", "SendDelay", this.SendCallIntervalMs);
            Console.WriteLine("{0}: {1}", "MaxInflightSends", this.MaxInflightSends);
            Console.WriteLine("{0}: {1}", "IsPeekLock", this.IsPeekLock);
            Console.WriteLine("{0}: {1}", "ReceiverCount", this.ReceiverCount);
            Console.WriteLine("{0}: {1}", "ReceiveBatchCount", this.ReceiveBatchSize);
            Console.WriteLine("{0}: {1}", "PrefetchCount", this.PrefetchCount);
            Console.WriteLine("{0}: {1}", "ReceiveTimeout", this.ReceiveCallIntervalMs);
            Console.WriteLine("{0}: {1}", "MaxInflightReceives", this.MaxInflightReceives);
            Console.WriteLine("{0}: {1}", "MetricsDisplayFrequency", this.MetricsDisplayFrequency);

            Console.WriteLine();
        }

        [Usage()]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("queue scenario", new Settings { ConnectionString = "{Connection-String}", QueueName = "{Queue-Name}" });
                yield return new Example("topic scenario", new Settings { ConnectionString = "{Connection-String}", QueueName = "{Topic-Name}:{Subsription-Name}"});
            }
        }
    }
}