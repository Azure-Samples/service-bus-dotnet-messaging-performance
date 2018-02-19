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
    using System.Threading;
    using System.Threading.Tasks;
    public class DynamicSemaphoreSlim : SemaphoreSlim
    {
        const int overallocationCount = 10000;
        long remainingOverallocation;
        public DynamicSemaphoreSlim(int initialCount) : base(initialCount + overallocationCount)
        {
            remainingOverallocation = overallocationCount;
            // grab the overallocatedCounts
            for (int i = 0; i < overallocationCount; i++)
            {
                this.Wait();
            }
        }

        /// <summary>
        /// Grant one more count
        /// </summary>
        public void Grant()
        {
            if (Interlocked.Decrement(ref remainingOverallocation) < 0)
            {
                throw new InvalidOperationException();
            }
            this.Release();
        }

        /// <summary>
        /// Revoke a count once possible
        /// </summary>
        /// <returns>Task that completes when revocation is complete</returns>
        public Task RevokeAsync()
        {
            if (Interlocked.Increment(ref remainingOverallocation) > overallocationCount)
            {
                throw new InvalidOperationException();
            }
            return this.WaitAsync();
        }
        
        /// <summary>
        /// Revoke a count once possible (does not block)
        /// </summary>
        public void Revoke()
        {
            if (Interlocked.Increment(ref remainingOverallocation) > overallocationCount)
            {
                throw new InvalidOperationException();
            }
            this.WaitAsync().ContinueWith((t)=> { }); // don't await
        }

        
    }
}
