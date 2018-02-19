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
    using System.Linq;
    using System.Reactive.Linq;
    using System.Threading.Tasks;

    class IncreaseInflightReceivesExperiment : Experiment
    {
        private int count;

        public IncreaseInflightReceivesExperiment(int count, Metrics metrics, Settings settings) : base(metrics, settings)
        {
            this.count = count;
        }

        public override async Task<Experiment> Run()
        {
            if (Settings.ReceiverCount == 0)
            {
                return null;
            }

            await Task.Delay(10).ConfigureAwait(false); // 10 seconds to meter
            var firstObservation = await ((IObservable<ReceiveMetrics>)Metrics).Buffer(TimeSpan.FromSeconds(5)).FirstAsync();
            var firstMessageCount = firstObservation.Sum(i => i.Messages);
            if ((Settings.MaxInflightReceives.Value + count) < 1)
            {
                Settings.MaxInflightReceives.Value = 1;
                count = 1;
            }
            {
                Settings.MaxInflightReceives.Value += count;
            }
            await Task.Delay(20).ConfigureAwait(false); // 20 seconds to settle and meter

            var secondObservation = await ((IObservable<ReceiveMetrics>)Metrics).Buffer(TimeSpan.FromSeconds(5)).FirstAsync();
            var secondMessageCount = secondObservation.Sum(i => i.Messages);

            if (secondMessageCount > firstMessageCount)
            {
                return new IncreaseInflightReceivesExperiment(this.count, this.Metrics, this.Settings);
            }
            else
            {
                Settings.MaxInflightReceives.Value -= count;
                return new IncreaseInflightReceivesExperiment(this.count, this.Metrics, this.Settings);
            }
        }
    }

}
