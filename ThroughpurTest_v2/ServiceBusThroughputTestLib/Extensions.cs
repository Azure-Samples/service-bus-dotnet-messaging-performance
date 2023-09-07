//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusThroughputTestLib.Extensions
{
    using Azure.Messaging.ServiceBus;
    using ServiceBusThroughputTest.Common;
    using System;
    using System.Threading;

    static class Extensions
    {
        public static void HandleExceptions(this AggregateException ex, ILogger logger, Guid? sessionId, CancellationToken cancellationToken)
        {
            // bool wait = false;

            logger?.AddTrace(ex.InnerException.ToString());

            ex.Handle((x) =>
            {
                if (sessionId != null)
                {
                    if ((x as ServiceBusException)?.Reason == ServiceBusFailureReason.ServiceBusy)
                    {
                        logger?.IncrementBusyErrorCount(sessionId.Value);
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            // wait = true;
                        }
                    }
                    else
                    {
                        logger?.IncrementErrorCount(sessionId.Value);
                    }
                }

                return true;
            });

            //if (wait)
            //{
            //    await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
            //}

            if (sessionId != null)
                logger?.DisposeSession(sessionId.Value);
        }
    }
}
