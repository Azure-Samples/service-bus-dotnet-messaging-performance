//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusPerfSample
{
    using Microsoft.Azure.ServiceBus;
    using System;
    
    class Program
    {
        // Paste connection string here.  Otherwise, it will be read from console.
        const string ConnectionString = null;

        static void Main(params string[] args)
        {
            Console.WriteLine("Microsoft Service Bus Performance Sample");
            Console.WriteLine("Copyright (c) Microsoft Corporation. All rights reserved.");

            string connectionString = ReadConnectionString();

            

            do
            {
                var settings = Settings.CreateQueueSettings(connectionString);
#if foo
                Console.WriteLine("\n\nOptions:");
                Console.WriteLine("\t1) Queue");
                Console.WriteLine("\t2) Topic with 1 Subscription");
                Console.WriteLine("\t3) Topic with 5 Subscription");
                Console.WriteLine("\tx) Exit");

                while (Console.KeyAvailable) { Console.ReadKey(true); }
                Console.Write("\nSelect an option: ");
                var option = Console.ReadLine();

                Settings settings = null;
                switch (option.ToLowerInvariant())
                {
                    case "1":
                        settings = Settings.CreateQueueSettings(connectionString);
                        break;

                    case "2":
                        settings = Settings.CreateTopicSettings(connectionString, 1);
                        break;

                    case "3":
                        settings = Settings.CreateTopicSettings(connectionString, 5);
                        break;

                    case "x":
                        return;
                }
#endif
                if (settings != null)
                {
                    Console.WriteLine("\n\nPress <ENTER> to STOP at anytime\n");
                    PerformanceApp app = new PerformanceApp(settings);
                    app.Start();
                    Console.ReadLine();
                    app.Stop();

                    Console.WriteLine();
                }
            } while (true);
        }

        static string ReadConnectionString()
        {
            string connectionString = ConnectionString;
            do
            {
                if (connectionString == null)
                {
                    connectionString = Properties.Settings.Default.connectionString;
                }

                if (connectionString == null)
                {
                    Console.Write("\nEnter Service Bus Connection String or type 'exit': ");
                    connectionString = Console.ReadLine();
                    if (string.Equals(connectionString, "exit", StringComparison.OrdinalIgnoreCase))
                    {
                        Environment.Exit(0);
                    }
                }

                try
                {
                    ServiceBusConnectionStringBuilder builder = new ServiceBusConnectionStringBuilder(connectionString);
                    connectionString = builder.ToString();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: {0}", ex.Message);
                    connectionString = null;
                }

            } while (connectionString == null);

            return connectionString;
        }
    }
}
