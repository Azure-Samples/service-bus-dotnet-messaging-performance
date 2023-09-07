//---------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.  
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE. 
//---------------------------------------------------------------------------------

namespace ServiceBusThroughputTest
{
    using ServiceBusThroughputTest.Common;
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Data.SqlTypes;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Timers;
    using Timer = System.Timers.Timer;

    public sealed class Metrics : ILogger
    {
        public event EventHandler<MetricsDisplayEventArgs> OnMetricsDisplay;
        readonly bool isSendMetric;
        readonly long maxMetricsSamplesPerDisplayInterval = 5000;
        readonly CancellationToken cancellationToken;

        ConcurrentDictionary<Guid, SendMetrics> sendSessions = new ConcurrentDictionary<Guid, SendMetrics>();
        ConcurrentDictionary<Guid, ReceiveMetrics> receiveSessions = new ConcurrentDictionary<Guid, ReceiveMetrics>();
        object timerLock = new object();
        object metricsLock = new object();
        ArrayPool<SendMetrics> sendMetricsPool = null;
        ArrayPool<ReceiveMetrics> rxMetricsPool = null;

        SendMetrics[] activeSendMetricsBuffer = null; int sendMetricsBufferWriteIndex = -1;
        ReceiveMetrics[] activeReceiveMetricsBuffer = null; int rxMetricsBufferWriteIndex = -1;

        ObjectPool<SendMetrics> sendPool = null;
        ObjectPool<ReceiveMetrics> rxPool = null;
        Timer timer;
        Stopwatch sw = Stopwatch.StartNew();
        private bool disposedValue;

        public Metrics(CancellationToken cancellationToken = default, bool isSendMetric = true, double metricsDisplayFrequency = 10.0, int maxMetricsSamplePerDisplayInterval = 5000)
        {
            this.isSendMetric = isSendMetric;
            this.cancellationToken = cancellationToken;
            this.maxMetricsSamplesPerDisplayInterval = maxMetricsSamplePerDisplayInterval;

            sendPool = new ObjectPool<SendMetrics>(
                () => new SendMetrics(),
                (s) =>
                {
                    s.SendDuration100ns = s.Tick = 0;
                    s.Sends = s.InflightSends = s.BusyErrors = s.Errors = s.Messages = s.Size = 0;
                },
                maxMetricsSamplesPerDisplayInterval);

            rxPool = new ObjectPool<ReceiveMetrics>(
                () => new ReceiveMetrics(),
                (r) =>
                {
                    r.ReceiveDuration100ns = r.Tick = r.CompleteDuration100ns = 0;
                    r.Receives = r.Messages = r.CompleteCalls = r.Errors = r.BusyErrors = r.CompleteCalls = 0;
                },
                maxMetricsSamplesPerDisplayInterval);

            sendMetricsPool = ArrayPool<SendMetrics>.Create((int)maxMetricsSamplesPerDisplayInterval, 4);
            rxMetricsPool = ArrayPool<ReceiveMetrics>.Create((int)maxMetricsSamplesPerDisplayInterval, 4);

            activeSendMetricsBuffer = sendMetricsPool.Rent((int)maxMetricsSamplesPerDisplayInterval);
            activeReceiveMetricsBuffer = rxMetricsPool.Rent((int)maxMetricsSamplesPerDisplayInterval);
            this.rxMetricsBufferWriteIndex = this.sendMetricsBufferWriteIndex = -1;

            timer = new Timer(metricsDisplayFrequency * 1000);
            timer.Elapsed += Timer_Elapsed;

            timer.Enabled = true;
            timer.AutoReset = true;
            timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (timerLock)
            {
                int sendMetricsCount = 0, rxMetricsCount = 0;
                SendMetrics[] sendBuffer = this.activeSendMetricsBuffer;
                ReceiveMetrics[] rxBuffer = this.activeReceiveMetricsBuffer;

                lock (this.metricsLock)
                {
                    sendMetricsCount = this.sendMetricsBufferWriteIndex + 1;
                    rxMetricsCount = this.rxMetricsBufferWriteIndex + 1;
                    this.rxMetricsBufferWriteIndex = this.sendMetricsBufferWriteIndex = -1;
                    this.activeSendMetricsBuffer = sendMetricsPool.Rent((int)maxMetricsSamplesPerDisplayInterval);
                    this.activeReceiveMetricsBuffer = rxMetricsPool.Rent((int)maxMetricsSamplesPerDisplayInterval);
                }

                this.OnMetricsDisplay?.Invoke(
                    this,
                    new MetricsDisplayEventArgs
                    {
                        SendMetricsList = new ArraySegment<SendMetrics>(sendBuffer, 0, sendMetricsCount),
                        ReceiveMetricsList = new ArraySegment<ReceiveMetrics>(rxBuffer, 0, rxMetricsCount)
                    });

                if (sendMetricsCount > 0)
                {
                    Parallel.For(0, sendMetricsCount, index =>
                    {
                        sendPool.Return(sendBuffer[index]);
                    });
                }

                if (rxMetricsCount > 0)
                {
                    Parallel.For(0, rxMetricsCount, index =>
                    {
                        rxPool.Return(rxBuffer[index]);
                    });
                }

                this.sendMetricsPool.Return(sendBuffer, true);
                this.rxMetricsPool.Return(rxBuffer, true);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                timer.Enabled = false;
            }
        }

        public void DisposeSession(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
            {
                return;
            }

            if (isSendMetric)
            {
                sendSessions.TryRemove(sessionId, out SendMetrics sendMetrics);

                lock (metricsLock)
                {
                    if (sendMetricsBufferWriteIndex < this.activeSendMetricsBuffer.Length - 1)
                    {
                        sendMetricsBufferWriteIndex++;

                        activeSendMetricsBuffer[sendMetricsBufferWriteIndex] = sendMetrics;
                        return;
                    }
                }

                sendPool.Return(sendMetrics);
            }
            else
            {
                receiveSessions.TryRemove(sessionId, out ReceiveMetrics receiveMetrics);

                lock (metricsLock)
                {
                    if (rxMetricsBufferWriteIndex < this.activeReceiveMetricsBuffer.Length - 1)
                    {
                        rxMetricsBufferWriteIndex++;

                        activeReceiveMetricsBuffer[rxMetricsBufferWriteIndex] = receiveMetrics;
                        return;
                    }
                }

                rxPool.Return(receiveMetrics);
            }
        }

        Guid ILogger.StartRecordTime()
        {
            var sessionId = Guid.NewGuid();
            if (isSendMetric)
            {
                var sendMetrics = sendPool.Get();

                if (sendMetrics != null)
                {
                    sendSessions[sessionId] = sendMetrics;
                    sendSessions[sessionId].Tick = sw.ElapsedTicks;

                    return sessionId;
                }
            }
            else
            {
                var rxMetrics = rxPool.Get();

                if (rxMetrics != null)
                {
                    receiveSessions[sessionId] = rxMetrics;
                    receiveSessions[sessionId].Tick = sw.ElapsedTicks;

                    return sessionId;
                }
            }

            return Guid.Empty;
        }

        double ILogger.StopRecordTimeAndGetElapsedMs(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
            {
                return 0;
            }

            long elapsed;
            if (isSendMetric)
            {
                elapsed = sendSessions[sessionId].SendDuration100ns = sw.ElapsedTicks - sendSessions[sessionId].Tick;
            }
            else
            {
                elapsed = receiveSessions[sessionId].ReceiveDuration100ns = sw.ElapsedTicks - receiveSessions[sessionId].Tick;
            }

            return elapsed / (Stopwatch.Frequency / 1000.0);
        }

        void ILogger.StartCompletionRecordTime(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
            {
                return;
            }

            if (!isSendMetric)
            {
                receiveSessions[sessionId].CompletionStartTick = sw.ElapsedTicks;
            }
        }

        double ILogger.StopCompletionRecordTimeAndGetElapsedMs(Guid sessionId)
        {
            if (sessionId == Guid.Empty || isSendMetric)
            {
                return 0;
            }

            receiveSessions[sessionId].CompleteDuration100ns = sw.ElapsedTicks - receiveSessions[sessionId].CompletionStartTick;

            return receiveSessions[sessionId].CompleteDuration100ns / (Stopwatch.Frequency / 1000.0);
        }

        void ILogger.IncrementActionCount(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                return;

            if (isSendMetric)
            {
                sendSessions[sessionId].Sends = sendSessions[sessionId].Sends + 1;
            }
            else
            {
                receiveSessions[sessionId].Receives = receiveSessions[sessionId].Receives + 1;
            }
        }

        void ILogger.IncrementMetricValueBy(Guid sessionId, int count)
        {
            if (sessionId == Guid.Empty)
                return;

            if (isSendMetric)
            {
                sendSessions[sessionId].Messages = sendSessions[sessionId].Messages + count;
            }
            else
            {
                receiveSessions[sessionId].Messages = receiveSessions[sessionId].Messages + count;
            }
        }

        void ILogger.IncrementErrorCount(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                return;

            if (isSendMetric)
            {
                sendSessions[sessionId].Errors = sendSessions[sessionId].Errors + 1;
            }
            else
            {
                receiveSessions[sessionId].Errors = receiveSessions[sessionId].Errors + 1;
            }
        }

        void ILogger.IncrementBusyErrorCount(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                return;

            if (isSendMetric)
            {
                sendSessions[sessionId].BusyErrors = sendSessions[sessionId].BusyErrors + 1;
            }
            else
            {
                receiveSessions[sessionId].BusyErrors = receiveSessions[sessionId].BusyErrors + 1;
            }
        }

        void ILogger.AddTrace(string trace)
        {
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.timer.Enabled = false;

                    lock (metricsLock)
                    {
                        this.timer.Enabled = false;

                        this.sendSessions.Clear();
                        this.receiveSessions.Clear();
                        this.sendSessions = null;
                        this.receiveSessions = null;
                    }
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class ObjectPool<T>
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;
        private readonly Action<T> _objectCleaner;
        private readonly long _maxCount;
        int _loaned = 0;

        public ObjectPool(Func<T> objectGenerator, Action<T> objectCleaner, long maxCount = 5000)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _objectCleaner = objectCleaner;
            _objects = new ConcurrentBag<T>();
            _maxCount = maxCount;
        }

        public T Get()
        {
            if (this._loaned >= _maxCount)
            {
                return default;
            }

            Interlocked.Increment(ref this._loaned);

            if (_objects.TryTake(out T item))
            {
                return item;
            }

            return _objectGenerator();
        }

        public void Return(T item)
        {
            _objectCleaner?.Invoke(item);

            Interlocked.Decrement(ref this._loaned);
            _objects.Add(item);
        }
    }


    public class MetricsDisplayEventArgs : EventArgs
    {
        public ArraySegment<SendMetrics> SendMetricsList { get; set; }
        public ArraySegment<ReceiveMetrics> ReceiveMetricsList { get; set; }
    }
}
