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

    class IncreaseInflightSendsExperiment : Experiment
    {
        private int count;

        public IncreaseInflightSendsExperiment(int count, Metrics metrics, Settings settings) : base(metrics, settings)
        {
            this.count = count;
        }

        public override async Task<Experiment> Run()
        {
            if (Settings.SenderCount == 0)
            {
                return null;
            }

            await Task.Delay(10).ConfigureAwait(false); // 10 seconds to meter
            var firstObservation = await ((IObservable<SendMetrics>)Metrics).Buffer(TimeSpan.FromSeconds(5)).FirstAsync();
            var firstMessageCount = firstObservation.Sum(i => i.Messages);
            if ((Settings.MaxInflightSends.Value + count) < 1)
            {
                Settings.MaxInflightSends.Value = 1;
                count = 1;
            }
            {
                Settings.MaxInflightSends.Value += count;
            }
            await Task.Delay(20).ConfigureAwait(false); // 20 seconds to settle

            var secondObservation = await ((IObservable<SendMetrics>)Metrics).Buffer(TimeSpan.FromSeconds(5)).FirstAsync();
            var secondMessageCount = secondObservation.Sum(i => i.Messages);

            if (secondMessageCount > firstMessageCount)
            {
                return new IncreaseInflightSendsExperiment(this.count, this.Metrics, this.Settings);
            }
            else
            {
                Settings.MaxInflightSends.Value -= count;
                return new IncreaseInflightSendsExperiment(this.count, this.Metrics, this.Settings);
            }
        }
    }

}
