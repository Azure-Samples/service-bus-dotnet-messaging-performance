//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    using LinqStatistics;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
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
            int windowLengthSecs = (int)this.settings.MetricsDisplayFrequency*2;
            if (this.settings.SenderCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("S|{0,10:0.00}|{1,10:0.00}|{2,5}|{3,5}|", "pstart", "pend", "sbc", "mifs");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "snd.avg", "snd.med", "snd.dev", "snd.min", "snd.max");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "gld.avg", "gld.med", "gld.dev", "gld.min", "gld.max");
                Console.WriteLine("{0,10}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", "msg/s", "total", "sndop", "errs", "busy", "overall");
                this.sendMetrics = ((IObservable<SendMetrics>)metrics)
                .Buffer(TimeSpan.FromSeconds(windowLengthSecs), TimeSpan.FromSeconds(windowLengthSecs/2))
                .Subscribe((list) =>
                {
                    if (list.Count == 0)
                        return;
                    lock (Console.Out)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("S|{0,10}|{1,10}|{2,5}|{3,5}|", list.First().Tick / Stopwatch.Frequency+1, list.Last().Tick / Stopwatch.Frequency+1, this.settings.SendBatchCount, this.settings.MaxInflightSends.Value);
                        WriteStat(list, i => i.SendDuration100ns, Stopwatch.Frequency / 1000.0);
                        WriteStat(list, i => i.GateLockDuration100ns, Stopwatch.Frequency / 10000.0);
                        var msgs = list.Sum(i => i.Messages);
                        sendTotal += msgs;
                        Console.WriteLine("{0,10:0.00}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", list.Sum(i => i.Messages) / (double)windowLengthSecs, msgs, list.Sum(i => i.Sends), list.Sum(i => i.Errors), list.Sum(i => i.BusyErrors), sendTotal);
                    }
                });
            }
            if (this.settings.ReceiverCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("R|{0,10:0.00}|{1,10:0.00}|{2,5}|{3,5}|", "pstart", "pend", "rbc", "mifr");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "rcv.avg", "rcv.med", "rcv.dev", "rcv.min", "rcv.max");
                Console.Write("{0,10:0.00}|{1,10:0.00}|{2,10:0.00}|{3,10:0.00}|{4,10:0.00}|", "cpl.avg", "cpl.med", "cpl.dev", "cpl.min", "cpl.max");
                Console.WriteLine("{0,10}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", "msg/s", "total", "rcvop", "errs", "busy", "overall");

                this.receiveMetrics = ((IObservable<ReceiveMetrics>)metrics)
                     .Buffer(TimeSpan.FromSeconds(windowLengthSecs), TimeSpan.FromSeconds(windowLengthSecs/2))
                    .Subscribe((list) =>
                    {
                        if (list.Count == 0)
                            return;
                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write("R|{0,10}|{1,10}|{2,5}|{3,5}|", list.First().Tick / Stopwatch.Frequency+1, list.Last().Tick / Stopwatch.Frequency+1, this.settings.ReceiveBatchCount, this.settings.MaxInflightReceives.Value);
                            WriteStat(list, i => i.ReceiveDuration100ns, Stopwatch.Frequency / 1000.0);
                            WriteStat(list, i => i.CompleteDuration100ns, Stopwatch.Frequency / 1000.0);
                            var msgs = list.Sum(i => i.Messages);
                            receiveTotal += msgs;
                            Console.WriteLine("{0,10:0.00}|{1,10}|{2,10}|{3,10}|{4,10}|{5,10}|", list.Sum(i => i.Messages) / (double)windowLengthSecs, msgs, list.Sum(i => i.Receives), list.Sum(i => i.Errors), list.Sum(i => i.BusyErrors), receiveTotal);
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
                foreach (var experiment in experiments)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith((t) => StartExperiment(experiment)).ConfigureAwait(false);
                }
            });
        }

        private Task StartExperiment(Experiment experiment)
        {
            return experiment.Run().ContinueWith(t =>
             {
                 if (t.Result != null)
                 {
                     return Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(t1 => StartExperiment(t.Result));
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
