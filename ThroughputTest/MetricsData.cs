//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ThroughputTest
{
    sealed class SendMetrics
    {
        public long Tick { get; set; }
        public int Messages { get; set; }
        public int Sends { get; set; }
        public int Size { get; set; }
        public long SendDuration100ns { get; set; }
        public long GateLockDuration100ns { get; set; }
        public int Errors { get; set; }
        public int BusyErrors { get; set; }
        public int InflightSends { get; internal set; }
    }

    sealed class ReceiveMetrics
    {
        public long Tick { get; set; }
        public int Messages { get; set; }
        public int Receives { get; set; }
        public int Size { get; set; }
        public long ReceiveDuration100ns { get; set; }
        public long CompleteDuration100ns { get; set; }
        public long GateLockDuration100ns { get; set; }
        public int Errors { get; set; }
        public int BusyErrors { get; set; }
        public int Completions { get; internal set; }
        public int CompleteCalls { get; internal set; }
    }
}
