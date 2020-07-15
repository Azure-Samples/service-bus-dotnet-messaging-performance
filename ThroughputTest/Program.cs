//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    using CommandLine;
    using System;
    using System.Linq;

    class Program
    {
        static int result = 0;
        static int Main(params string[] args)
        {
            CommandLine.Parser.Default.ParseArguments<Settings>(args)
                 .WithParsed<Settings>(opts => RunOptionsAndReturnExitCode(opts));
            return result;
        }
        
        static void RunOptionsAndReturnExitCode(Settings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ConnectionString) || string.IsNullOrWhiteSpace(settings.SendPath))
            {
                Console.WriteLine("--send-path option must be specified if there's no EntityPath in the connection string.");
                result = 1;
                return;
            }

            if (settings.ReceivePaths == null || !settings.ReceivePaths.Any())
            {
                settings.ReceivePaths = new[] { settings.SendPath };
            }
            Console.WriteLine("\n\nPress <ENTER> to STOP at anytime\n");
            Metrics metrics = new Metrics(settings);
            ServiceBusPerformanceApp app = new ServiceBusPerformanceApp(settings, metrics);
            var experiments = new Experiment[]
            {
                // new IncreaseInflightSendsExperiment(50, metrics, settings),
                // new IncreaseInflightReceivesExperiment(10, metrics, settings)
            };
            app.Run(experiments).Wait();
            Console.WriteLine("Complete");
        }
    }
}
