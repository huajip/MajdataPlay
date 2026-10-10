using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MajdataPlay.Scenes.Login
{
    // One worker owns open/read/write/close. Kept public for isolated transport tests.
    public interface IPn532Port : IDisposable
    {
        void SetDtr(bool enabled);
        void ClearReceived();
        void Write(byte[] bytes);
        int Read(byte[] buffer);
    }
    internal sealed class Pn532CdcDriver
    {
        readonly IPn532Port _port;
        readonly Func<bool> _live;
        readonly CancellationToken _stop;
        readonly List<byte> _input = new List<byte>();
        readonly byte[] _buffer = new byte[256];
        Pn532CdcDriver(IPn532Port port, Func<bool> live, CancellationToken stop) { _port = port; _live = live; _stop = stop; }
        void Check() { _stop.ThrowIfCancellationRequested(); if (!_live()) throw new OperationCanceledException(); }
        void RemoveInput(int count)
        {
            // List.RemoveRange alone leaves the URL bytes in its backing array.
            for (int i = 0; i < count; i++) _input[i] = 0;
            var remainder = _input.Count - count;
            for (int i = 0; i < remainder; i++) _input[i] = _input[i + count];
            for (int i = remainder; i < _input.Count; i++) _input[i] = 0;
            _input.RemoveRange(remainder, count);
        }
        internal static byte[] Frame(byte command, byte[] payload)
        {
            if (payload.Length > 253) throw new ArgumentException();
            var frame = new byte[payload.Length + 9];
            frame[2] = 0xFF; frame[3] = (byte)(payload.Length + 2); frame[4] = unchecked((byte)-frame[3]);
            frame[5] = 0xD4; frame[6] = command;
            int sum = 0xD4 + command;
            for (int i = 0; i < payload.Length; i++) { frame[i + 7] = payload[i]; sum += payload[i]; }
            frame[frame.Length - 2] = unchecked((byte)-sum); return frame;
        }
        byte[] NextFrame(Stopwatch elapsed, int milliseconds)
        {
            while (true)
            {
                Check();
                if (elapsed.ElapsedMilliseconds >= milliseconds) throw new TimeoutException("pn532_command_timeout");
                while (_input.Count >= 3 && (_input[0] != 0 || _input[1] != 0 || _input[2] != 0xFF)) RemoveInput(1);
                if (_input.Count >= 6)
                {
                    int length = _input[3];
                    if (length == 0 && _input[4] == 0xFF)
                    {
                        if (_input[5] != 0) throw new IOException("pn532_invalid_ack");
                        RemoveInput(6); return null;
                    }
                    if (length == 0xFF && _input[4] == 0) throw new IOException("pn532_nack");
                    if (length == 0xFF || ((length + _input[4]) & 255) != 0) throw new IOException("pn532_invalid_frame");
                    int total = length + 7;
                    if (_input.Count >= total)
                    {
                        int sum = _input[length + 5];
                        var data = new byte[length];
                        for (int i = 0; i < length; i++) { data[i] = _input[i + 5]; sum += data[i]; }
                        if ((sum & 255) != 0 || _input[total - 1] != 0) { Array.Clear(data, 0, data.Length); throw new IOException("pn532_invalid_checksum"); }
                        RemoveInput(total); return data;
                    }
                }
                int count = _port.Read(_buffer);
                if (count < 0 || count > _buffer.Length) throw new IOException("pn532_invalid_read");
                for (int i = 0; i < count; i++) _input.Add(_buffer[i]);
                Array.Clear(_buffer, 0, _buffer.Length);
                if (_input.Count > 512) throw new IOException("pn532_receive_overflow");
            }
        }
        byte[] Command(byte command, byte[] payload, bool waitForPhone = false, Action armed = null)
        {
            Check();
            var frame = Frame(command, payload);
            try { _port.Write(frame); } finally { Array.Clear(frame, 0, frame.Length); }
            var timer = Stopwatch.StartNew();
            bool acknowledged = false;
            while (true)
            {
                var data = NextFrame(timer, acknowledged && waitForPhone ? 2147483647 : 5000);
                if (data == null)
                {
                    if (!acknowledged) { acknowledged = true; armed?.Invoke(); }
                    continue;
                }
                try
                {
                    if (!acknowledged || data.Length < 2 || data[0] != 0xD5 || data[1] != unchecked((byte)(command + 1)))
                        throw new IOException("pn532_unexpected_response");
                    var result = new byte[data.Length - 2]; Buffer.BlockCopy(data, 2, result, 0, result.Length); return result;
                }
                finally { Array.Clear(data, 0, data.Length); }
            }
        }
        static void Wipe(byte[] bytes) { Array.Clear(bytes, 0, bytes.Length); }
        public static void Run(IPn532Port port, byte[] ndef, Func<bool> live, CancellationToken stop, Action armed, Action<string> diagnostic = null)
        {
            var driver = new Pn532CdcDriver(port, live, stop);
            try
            {
                driver.Check(); port.SetDtr(true);
                port.Write(new byte[] { 0x55, 0x55, 0, 0, 0 });
                if (stop.WaitHandle.WaitOne(100)) stop.ThrowIfCancellationRequested();
                driver.Check(); port.ClearReceived();
                Wipe(driver.Command(0x14, new byte[] { 1, 0x14, 1 }));
                Wipe(driver.Command(0x32, new byte[] { 1, 0 }));
                Wipe(driver.Command(0x12, new byte[] { 0x34 }));
                var target = new byte[37]; target[0] = 5; target[1] = 4; target[6] = 0x20;
                while (live())
                {
                    var activation = driver.Command(0x8C, target, true, armed);
                    bool isoDep = activation.Length != 0 && (activation[0] & 8) != 0; Wipe(activation);
                    if (!isoDep) continue;
                    diagnostic?.Invoke("type4_activated");
                    using (var tag = new Pn532Type4(ndef))
                    while (live())
                    {
                        var data = driver.Command(0x86, new byte[0], true);
                        if (data.Length == 0) throw new IOException("pn532_missing_status");
                        if (data[0] != 0) { Wipe(data); break; }
                        var apdu = new byte[data.Length - 1]; Buffer.BlockCopy(data, 1, apdu, 0, apdu.Length); Wipe(data);
                        byte[] reply = null, status = null;
                        try
                        {
                            reply = tag.Process(apdu); status = driver.Command(0x8E, reply);
                            if (status.Length == 0) throw new IOException("pn532_missing_status");
                            if (status[0] != 0) break;
                            diagnostic?.Invoke("apdu_processed");
                        }
                        finally { Wipe(apdu); if (reply != null) Wipe(reply); if (status != null) Wipe(status); }
                    }
                }
            }
            finally
            {
                // This CDC firmware releases a blocked target on DTR low. It
                // does not accept the HID ACK-abort route or concurrent commands.
                driver.RemoveInput(driver._input.Count); Wipe(driver._buffer);
                port.SetDtr(false);
            }
        }
    }
}
