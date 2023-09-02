//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusThroughputTest.Common
{
    using System;

    public interface ILogger : IDisposable
    {
        Guid StartRecordTime();
        void StartCompletionRecordTime(Guid sessionId);
        double StopCompletionRecordTimeAndGetElapsedMs(Guid sessionId);
        double StopRecordTimeAndGetElapsedMs(Guid sessionId);
        void IncrementActionCount(Guid sessionId);
        void IncrementMetricValueBy(Guid sessionId, int count);
        void IncrementErrorCount(Guid sessionId);
        void IncrementBusyErrorCount(Guid sessionId);
        void DisposeSession(Guid sessionId);
        void AddTrace(string trace);
    }
}
