using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Scenes.Login
{
    // Optional loopback publisher. No device access, log output, config writes,
    // account credentials, RequestId or authorization response leave this class.
    internal sealed class QrNfcPublisher : IDisposable
    {
        const string BridgeUrl = "http://127.0.0.1:39741/v1/lease";
        readonly string _instance = Guid.NewGuid().ToString("N");
        readonly object _gate = new object();
        readonly HttpClient _client;
        readonly Func<DateTime> _now;
        Task _tail = Task.CompletedTask;
        string _session;
        DateTime _expiry;
        DateTime _nextHeartbeat;
        long _sequence;
        long _generation;
        long _qrGeneration;
        string _location;
        bool _deviceFault;
        bool _disposed;
        const int PendingProofSeconds = 8;
        // The bridge answers publish only after the reader is armed (its own
        // device budget is 4 s) and clear only after the worker is stopped.
        // Short lease renewals stay short so a dead bridge never piles up work.
        static readonly TimeSpan DeviceRequestTimeout = TimeSpan.FromSeconds(5);
        static readonly TimeSpan LeaseRequestTimeout = TimeSpan.FromMilliseconds(1000);
        static readonly TimeSpan QuitFlushTimeout = TimeSpan.FromMilliseconds(600);

        public static QrNfcPublisher CreateFromEnvironment()
        {
            try
            {
                return Environment.GetEnvironmentVariable("MAJ_NFC_BRIDGE") == "1"
                    ? new QrNfcPublisher() : null;
            }
            catch { return null; }
        }
        QrNfcPublisher() : this(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }, null) { }
        // Handler/clock injection only exists to simulate the exact Unity client
        // without launching Unity or communicating with the real login server.
        internal QrNfcPublisher(HttpMessageHandler handler, Func<DateTime> now)
        {
            // Per-request deadlines are applied in EnqueueLocked.
            _client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            var origin = DateTime.UtcNow;
            var elapsed = Stopwatch.StartNew();
            _now = now ?? (() => origin + elapsed.Elapsed);
        }
        public void Publish(string location)
        {
            lock (_gate)
            {
                if (_disposed) return;
                ClearLocked();
                if (location == null || location.Length > 2048 ||
                    !Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
                    uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return;
                _session = Guid.NewGuid().ToString("N");
                _location = location;
                _expiry = _now().AddSeconds(PendingProofSeconds); // Initial server 201 proof.
                _nextHeartbeat = _now().AddSeconds(2);
                EnqueueLocked("publish", _session, location, _expiry, _generation);
            }
        }
        // Capture BEFORE the existing server poll, then confirm ONLY its 202
        // response. A late check from a replaced QR cannot renew a newer QR.
        public long BeginCheck()
        {
            lock (_gate) { return !_disposed && _location != null ? _qrGeneration : -1; }
        }
        public void ConfirmPending(long qrGeneration)
        {
            lock (_gate)
            {
                if (_disposed || _location == null || _deviceFault || qrGeneration < 0 || qrGeneration != _qrGeneration) return;
                _expiry = _now().AddSeconds(PendingProofSeconds);
                if (_session == null)
                {
                    // A fresh authenticated 202 may resume a NETWORK-expired
                    // NFC lease. Device faults require a new normal QR instead.
                    _generation++;
                    _session = Guid.NewGuid().ToString("N");
                    _nextHeartbeat = _now().AddSeconds(2);
                    EnqueueLocked("publish", _session, _location, _expiry, _generation);
                }
                else EnqueueLocked("validated", _session, null, _expiry, _generation);
            }
        }
        public void Tick()
        {
            lock (_gate)
            {
                if (_disposed || _session == null) return;
                if (_now() >= _expiry) { StopLeaseLocked(); return; }
                if (_now() < _nextHeartbeat) return;
                _nextHeartbeat = _now().AddSeconds(2);
                EnqueueLocked("heartbeat", _session, null, null, _generation);
            }
        }
        public void Clear()
        {
            lock (_gate) { if (!_disposed) ClearLocked(); }
        }
        void ClearLocked()
        {
            _qrGeneration++;
            _location = null;
            _deviceFault = false;
            StopLeaseLocked();
        }
        void StopLeaseLocked()
        {
            _generation++;
            if (_session == null) return;
            EnqueueLocked("clear", _session, null, null, _generation);
            _session = null;
        }
        void EnqueueLocked(string action, string session, string url, DateTime? expiresAt, long generation)
        {
            var sequence = ++_sequence;
            var previous = _tail;
            _tail = Task.Run(() =>
            {
                previous.Wait();
                // Obsolete queued publications/heartbeats cannot resurrect a
                // cleared or replaced session. Clears remain ordered and scoped.
                lock (_gate) { if (action != "clear" && generation != _generation) return; }
                try
                {
                    var json = JsonConvert.SerializeObject(new
                    {
                        application = "MajdataPlay", instance = _instance, session,
                        sequence, action, url, expiresAt
                    });
                    var timeout = action is "publish" or "clear" ? DeviceRequestTimeout : LeaseRequestTimeout;
                    using (var cts = new CancellationTokenSource(timeout))
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var response = _client.PostAsync(BridgeUrl, content, cts.Token).GetAwaiter().GetResult())
                    {
                        lock (_gate)
                        {
                            if (action == "clear" || generation != _generation || session != _session) return;
                            var status = (int)response.StatusCode;
                            if (status == 503)
                            {
                                // Bridge device fault is sticky: stop renewing this
                                // session; only a new QR may try again.
                                _deviceFault = true;
                                _generation++; _session = null;
                            }
                            else if (status == 410 || (status == 409 && action == "publish"))
                            {
                                // Session gone, or another owner still holds the
                                // reader. Drop the session; the next server 202
                                // for this same QR republishes with a new one.
                                _generation++; _session = null;
                            }
                        }
                    }
                }
                catch { /* Optional NFC failure must never fail or block QR login. */ }
            });
        }
        internal Task WhenIdleAsync() { lock (_gate) return _tail; }
        // Application quit: give the final clear a short, bounded chance to
        // reach the bridge so the reader is released at once instead of after
        // the 8 s lease. Never blocks longer than QuitFlushTimeout.
        public void DisposeAndFlush()
        {
            Task tail;
            lock (_gate)
            {
                var hadSession = _session != null;
                Dispose();
                if (!hadSession) return;
                tail = _tail;
            }
            try { tail.Wait(QuitFlushTimeout); } catch { }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                ClearLocked();
                _disposed = true;
                _tail = _tail.ContinueWith(_ => _client.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }
}
