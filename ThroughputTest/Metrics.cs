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
    using System.Threading.Tasks;

    sealed class Metrics : IObservable<SendMetrics>, IObservable<ReceiveMetrics>
    {
        object sendMetricsObserverLock = new object();
        List<IObserver<SendMetrics>> sendMetricsObservers = new List<IObserver<SendMetrics>>();
        object receiveMetricsObserverLock = new object();
        List<IObserver<ReceiveMetrics>> receiveMetricsObservers = new List<IObserver<ReceiveMetrics>>();
     
        public Metrics(Settings settings)
        {
        }
        
        public void PushSendMetrics(SendMetrics sendMetrics)
        {
            Task.Run(() =>
            {
                IObserver<SendMetrics>[] observers;
                lock (sendMetricsObserverLock)
                {
                    observers = sendMetricsObservers.ToArray();
                }

                foreach (var observer in observers)
                {
                    observer.OnNext(sendMetrics);
                }                
            }).Fork();
        }

        public void PushReceiveMetrics(ReceiveMetrics receiveMetrics)
        {
            Task.Run(() =>
            {
                IObserver<ReceiveMetrics>[] observers;
                lock (receiveMetricsObserverLock)
                {
                    observers = receiveMetricsObservers.ToArray();
                }

                foreach (var observer in observers)
                {
                    observer.OnNext(receiveMetrics);
                }
            }).Fork();
        }

        public IDisposable Subscribe(IObserver<ReceiveMetrics> observer)
        {
            lock (receiveMetricsObserverLock)
            {
                receiveMetricsObservers.Add(observer);
            }
            return System.Reactive.Disposables.Disposable.Create(() =>
            {
                lock (receiveMetricsObserverLock)
                {
                    receiveMetricsObservers.Remove(observer);
                }
            });
        }

        public IDisposable Subscribe(IObserver<SendMetrics> observer)
        {
            lock (sendMetricsObserverLock)
            {
                sendMetricsObservers.Add(observer);
            }
            return System.Reactive.Disposables.Disposable.Create(() =>
            {
                lock (sendMetricsObserverLock)
                {
                    sendMetricsObservers.Remove(observer);
                }
            });
        }
    }
}
