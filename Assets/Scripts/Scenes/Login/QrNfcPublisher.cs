using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Scenes.Login
{
    // The login scene owns a direct PN532 session. No socket, bridge or service.
    internal sealed class QrNfcPublisher : IDisposable
    {
        static readonly object DeviceOwner = new object();
        readonly object _gate = new object();
        readonly Func<CancellationToken, IPn532Port> _open;
        readonly Func<DateTime> _now;
        readonly string _host;
        readonly string _path;
        Task _worker = Task.CompletedTask;
        TaskCompletionSource<bool> _prepared = new TaskCompletionSource<bool>();
        CancellationTokenSource _stop;
        string _location;
        DateTime _expiry;
        long _generation;
        long _qrGeneration;
        long _checkGeneration;
        long _checkQrGeneration;
        DateTime _checkStarted;
        bool _faulted;
        bool _disposed;

        public static QrNfcPublisher CreateFromEnvironment()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("MAJ_PN532_NFC") != "1" ||
                    Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
                var port = Environment.GetEnvironmentVariable("MAJ_PN532_PORT");
                if (!Pn532NativeCdc.IsPortName(port)) return null;
                var host = Environment.GetEnvironmentVariable("MAJ_PN532_AUTH_HOST") ?? "majdata.net";
                return new QrNfcPublisher(token => new Pn532NativeCdc(port, token), null, host, "/qrauth");
            }
            catch { return null; }
        }

        internal QrNfcPublisher(Func<CancellationToken, IPn532Port> open, Func<DateTime> now,
            string host = "majdata.net", string path = "/qrauth")
        {
            if (open == null || Uri.CheckHostName(host) != UriHostNameType.Dns ||
                host.Contains("*") || !path.StartsWith("/", StringComparison.Ordinal)) throw new ArgumentException();
            _open = open; _host = host.ToLowerInvariant(); _path = path;
            var origin = DateTime.UtcNow;
            var elapsed = Stopwatch.StartNew();
            _now = now ?? (() => origin + elapsed.Elapsed);
        }
        bool Allows(string location)
        {
            if (location == null || location.Length > 2048 || location.IndexOf('\\') >= 0) return false;
            foreach (var c in location) if (char.IsControl(c)) return false;
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.IdnHost != _host || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != _path || !uri.Query.StartsWith("?auth-id=", StringComparison.Ordinal)) return false;
            var value = uri.Query.Substring(9);
            if (value.Length == 0 || value.Length > 1024) return false;
            foreach (var c in value) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' ||
                c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.' || c == '~')) return false;
            return true;
        }
        public void Publish(string location)
        {
            lock (_gate)
            {
                if (_disposed) return;
                ClearLocked();
                if (!Allows(location)) return;
                _location = location;
                _expiry = _now().AddSeconds(8);
                StartLocked();
            }
        }
        public long BeginCheck()
        {
            lock (_gate)
            {
                if (_disposed || _location == null) return -1;
                _checkQrGeneration = _qrGeneration;
                _checkStarted = _now();
                return ++_checkGeneration;
            }
        }
        public void ConfirmPending(long generation)
        {
            lock (_gate)
            {
                if (_disposed || _faulted || _location == null || generation < 0 || generation != _checkGeneration ||
                    _checkQrGeneration != _qrGeneration || _now() >= _checkStarted.AddSeconds(8)) return;
                _expiry = _now().AddSeconds(8);
                if (_stop == null || _worker.IsCompleted) StartLocked();
            }
        }
        public void Tick()
        {
            lock (_gate)
            {
                if (!_disposed && _location != null && _now() >= _expiry) StopLocked();
            }
        }
        void StartLocked()
        {
            StopLocked();
            var generation = _generation;
            var previous = _worker;
            var stop = new CancellationTokenSource();
            _stop = stop;
            var ready = _prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ndef = Pn532Type4.UriRecord(_location);
            _worker = Task.Run(() =>
            {
                bool owned = false;
                try
                {
                    var deadline = Stopwatch.StartNew();
                    while (!previous.IsCompleted)
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        if (!IsLive(generation)) return;
                        if (deadline.ElapsedMilliseconds >= 5000) throw new InvalidOperationException("pn532_previous_worker_not_stopped");
                        previous.Wait(20);
                    }
                    while (!(owned = Monitor.TryEnter(DeviceOwner, 20)))
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        if (!IsLive(generation)) return;
                        if (deadline.ElapsedMilliseconds >= 5000) throw new InvalidOperationException("pn532_device_owned");
                    }
                    if (!IsLive(generation)) return;
                    using (var port = _open(stop.Token))
                        Pn532CdcDriver.Run(port, ndef, () => IsLive(generation), stop.Token, () => ready.TrySetResult(true));
                }
                catch (OperationCanceledException) { }
                catch
                {
                    lock (_gate) if (generation == _generation) _faulted = true;
                }
                finally
                {
                    if (owned) Monitor.Exit(DeviceOwner);
                    Array.Clear(ndef, 0, ndef.Length);
                    ready.TrySetResult(false);
                    stop.Dispose();
                }
            });
        }
        bool IsLive(long generation)
        {
            lock (_gate) return !_disposed && !_faulted && _location != null &&
                generation == _generation && _now() < _expiry;
        }
        void StopLocked()
        {
            _generation++;
            try { _stop?.Cancel(); } catch (ObjectDisposedException) { }
            _stop = null;
        }
        void ClearLocked() { _qrGeneration++; _location = null; _faulted = false; StopLocked(); }
        public void Clear() { lock (_gate) if (!_disposed) ClearLocked(); }
        public void Dispose() { lock (_gate) { if (_disposed) return; ClearLocked(); _disposed = true; } }
        public void DisposeAndFlush()
        {
            Task worker;
            lock (_gate) { Dispose(); worker = _worker; }
            try { worker.Wait(600); } catch { }
        }
        internal Task WhenIdleAsync() { lock (_gate) return _worker; }
        internal Task<bool> WhenPreparedAsync() { lock (_gate) return _prepared.Task; }
        internal bool Faulted { get { lock (_gate) return _faulted; } }
    }
}
