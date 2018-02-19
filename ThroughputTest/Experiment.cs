//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    using System.Threading.Tasks;

    abstract class Experiment
    {
        public Experiment(Metrics metrics, Settings settings)
        {
            Metrics = metrics;
            Settings = settings;
        }

        public Metrics Metrics { get; }
        public Settings Settings { get; }

        public abstract Task<Experiment> Run();
    }

}
