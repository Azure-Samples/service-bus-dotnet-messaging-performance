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
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    static class TaskEx
    {
        public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action)
        {
            List<Task> tasks = new List<Task>();
            foreach (TSource i in source)
            {
                tasks.Add(action(i));
            }

            return Task.WhenAll(tasks.ToArray());
        }

        public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, long, Task> action)
        {
            List<Task> tasks = new List<Task>();

            long index = 0;
            foreach (TSource i in source)
            {
                tasks.Add(action(i, index));
                index++;
            }

            return Task.WhenAll(tasks.ToArray());
        }

        public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource, long> action)
        {
            long index = 0;
            foreach (TSource i in source)
            {
                action(i, index);
                index++;
            }
        }

        public static void Fork(this Task thisTask)
        {
            thisTask.ContinueWith(t => { });
        }

        public static void For(long start, long end, Action<long> action)
        {
            for (long i = start; i < end; i++)
            {
                action(i);
            }
        }

        public static async Task ParallelForAsync(long start, long end, Func<long, Task> action)
        {
            List<Task> tasks = new List<Task>();

            for (long i = start; i < end; i++)
            {
                tasks.Add(action(i));
            }

            await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false);
        }

        public static async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {

            }
        }

        public static async Task IgnoreExceptionAsync(Func<Task> task)
        {
            try
            {
                await task().ConfigureAwait(false);
            }
            catch
            {

            }
        }
    }
}
