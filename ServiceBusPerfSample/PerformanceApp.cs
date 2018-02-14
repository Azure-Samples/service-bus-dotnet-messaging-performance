//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusPerfSample
{
    using LinqStatistics;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    sealed class ServiceBusPerformanceApp
    {
        readonly Settings settings;
        readonly Metrics metrics;
        readonly CancellationTokenSource cancellationTokenSource;
        readonly List<PerformanceTask> tasks;
        private IDisposable sendMetrics;
        private IDisposable receiveMetrics;

        public ServiceBusPerformanceApp(Settings settings, Metrics metrics)
        {
            this.settings = settings;
            this.metrics = metrics;
            this.cancellationTokenSource = new CancellationTokenSource();
            this.tasks = new List<PerformanceTask>();
        }


        public async Task Run(params Experiment[] experiments)
        {
            this.settings.PrintSettings();

            tasks.Add(new ReceiverTask(settings, this.metrics, this.cancellationTokenSource.Token));
            tasks.Add(new SenderTask(settings, this.metrics, this.cancellationTokenSource.Token));

            Console.WriteLine("Starting...");
            Console.WriteLine();

            long sendTotal = 0, receiveTotal = 0;
            int windowLengthSecs = (int)this.settings.MetricsDisplayFrequency.TotalSeconds;
            if (this.settings.SenderCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("S|{0,10}|{1,10}|{2,5}|{3,5}|", "pstart", "pend", "sbc", "mifs");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "snd.avg", "snd.med", "snd.dev", "snd.min", "snd.max");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "gld.avg", "gld.med", "gld.dev", "gld.min", "gld.max");
                Console.Write("{0,10:0.00}|", "msg/s");
                Console.Write("{0,10}|", "total");
                Console.Write("{0,10}|", "sndop");
                Console.Write("{0,10}|", "errs");
                Console.WriteLine("{0,10}|{1,10}|", "busy", "overall");
                this.sendMetrics = ((IObservable<SendMetrics>)metrics)
                .Buffer(TimeSpan.FromSeconds(windowLengthSecs), TimeSpan.FromSeconds(windowLengthSecs / 2))
                .Subscribe((list) =>
                {
                    if (list.Count == 0)
                        return;
                    lock (Console.Out)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("S|{0,10:0.00}|{1,10:0.00}|{2,5}|{3,5}|", list.Min(i => i.Tick) / 1000.0, list.Max(i => i.Tick) / 1000.0, this.settings.SendBatchCount, this.settings.MaxInflightSends.Value);
                        WriteStat(list, i => i.SendDuration100ns, 10000.0);
                        WriteStat(list, i => i.GateLockDuration100ns, 10000.0);
                        Console.Write("{0,10:0.00}|", list.Sum(i => i.Messages) / (double)windowLengthSecs);
                        var msgs = list.Sum(i => i.Messages);
                        sendTotal += msgs;
                        Console.Write("{0,10}|", msgs);
                        Console.Write("{0,10}|", list.Sum(i => i.Sends));
                        Console.Write("{0,10}|", list.Sum(i => i.Errors));
                        Console.Write("{0,10}|", list.Sum(i => i.BusyErrors));
                        Console.WriteLine("{0,10}|", sendTotal);
                    }
                });
            }
            if (this.settings.ReceiverCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("R|{0,10}|{1,10}|{2,5}|{3,5}|", "pstart", "pend", "rbc", "mifr");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "rcv.avg", "rcv.med", "rcv.dev", "rcv.min", "rcv.max");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "cpl.avg", "cpl.med", "cpl.dev", "cpl.min", "cpl.max");
                Console.Write("{0,10:0.00}|", "msg/s");
                Console.Write("{0,10}|", "total");
                Console.Write("{0,10}|", "rcvop");
                Console.Write("{0,10}|", "errs");
                Console.WriteLine("{0,10}|{1,10}|", "busy", "overall");

                this.receiveMetrics = ((IObservable<ReceiveMetrics>)metrics)
                     .Buffer(TimeSpan.FromSeconds(windowLengthSecs), TimeSpan.FromSeconds(windowLengthSecs / 2))
                    .Subscribe((list) =>
                    {
                        if (list.Count == 0)
                            return;
                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write("R|{0,10:0.00}|{1,10:0.00}|{2,5}|{3,5}|", list.Min(i => i.Tick) / 1000.0, list.Max(i => i.Tick) / 1000.0, this.settings.ReceiveBatchCount, this.settings.MaxInflightReceives);
                            WriteStat(list, i => i.ReceiveDuration100ns, 10000.0);
                            WriteStat(list, i => i.CompleteDuration100ns, 10000.0);
                            Console.Write("{0,10:0.00}|", list.Sum(i => i.Messages) / (double)windowLengthSecs);
                            var msgs = list.Sum(i => i.Messages);
                            receiveTotal += msgs;
                            Console.Write("{0,10}|", msgs);
                            Console.Write("{0,10}|", list.Sum(i => i.Receives));
                            Console.Write("{0,10}|", list.Sum(i => i.Errors));
                            Console.Write("{0,10}|", list.Sum(i => i.BusyErrors));
                            Console.WriteLine("{0,10}|", receiveTotal);
                        }
                    });
            }

            Task runTasks = tasks.ParallelForEachAsync((t) => t.StartAsync());
            if (experiments != null && experiments.Length > 0)
            {
                var experimentTask = RunExperiments(experiments, this.cancellationTokenSource.Token);
                await Task.WhenAll(new Task[] { experimentTask, runTasks });
            }
            else
            {
                await runTasks;
            }
            this.cancellationTokenSource.Cancel();
            this.tasks.ForEach((t) => t.Close());
            Console.ForegroundColor = ConsoleColor.White;
        }

        private Task RunExperiments(IEnumerable<Experiment> experiments, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                do
                {
                    foreach (var experiment in experiments)
                    {
                        await StartExperiment(experiment).ConfigureAwait(false);
                    }
                }
                while (!ct.IsCancellationRequested);
            });
        }

        private Task StartExperiment(Experiment experiment)
        {
            Console.WriteLine("--- Starting experiment:", experiment.ToString());
            return experiment.Run().ContinueWith(t =>
             {
                 Console.WriteLine("--- Completed experiment:", experiment.ToString());
                 if (t.Result != null)
                 {
                     return Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(t1 => StartExperiment(t.Result));
                 }
                 return Task.CompletedTask;
             });
        }

        void WriteStat<T>(IList<T> list, Func<T, long> f, double scale)
        {
            if (list.Count > 1)
            {
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", list.Average(f) / scale, list.Median(f) / scale, list.StandardDeviationP(f) / scale, list.Min(f) / scale, list.Max(f) / scale);
            }
        }

    }

}
