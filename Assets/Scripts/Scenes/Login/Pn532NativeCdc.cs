using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MajdataPlay.Scenes.Login
{
    // Win32 serial access also works in the player's stripped Mono runtime.
    // Explicit CDC only: a vendor HID endpoint is not a serial port.
    internal sealed class Pn532NativeCdc : IPn532Port
    {
        IntPtr _handle = new IntPtr(-1);
        readonly CancellationToken _stop;
        readonly byte[] _original = new byte[28];
        readonly byte[] _originalTimeouts = new byte[20];
        bool _saved;

        internal static bool IsPortName(string port)
        {
            if (port == null || !port.StartsWith("COM", StringComparison.Ordinal) || port.Length < 4 || port.Length > 6 || port[3] == '0') return false;
            for (int i = 3; i < port.Length; i++) if (port[i] < '0' || port[i] > '9') return false;
            return true;
        }
        internal Pn532NativeCdc(string port, CancellationToken stop)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !IsPortName(port)) throw new ArgumentException("pn532_invalid_cdc_port");
            _stop = stop;
            stop.ThrowIfCancellationRequested();
            try
            {
                // shareMode=0 prevents two applications from owning this port.
                _handle = CreateFile("\\\\.\\" + port, 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (_handle == new IntPtr(-1)) throw new IOException("pn532_cdc_open_failed");
                Put32(_original, 0, 28);
                if (!GetCommState(_handle, _original) || !GetCommTimeouts(_handle, _originalTimeouts)) throw new IOException("pn532_cdc_get_state_failed");
                _saved = true;
                var state = (byte[])_original.Clone();
                Put32(state, 4, 115200);
                state[18] = 8; state[19] = 0; state[20] = 0;
                // Binary, no parity/software/CTS/DSR filtering, manual DTR.
                // Preserve RTS control: this transport never pulses board reset.
                Put32(state, 8, (Get32(state, 8) & ~0x4F7Eu) | 1u);
                var timeouts = new byte[20];
                Put32(timeouts, 0, 0xFFFFFFFFu); Put32(timeouts, 8, 10); Put32(timeouts, 16, 100);
                if (!SetCommState(_handle, state) || !SetCommTimeouts(_handle, timeouts)) throw new IOException("pn532_cdc_configure_failed");
                stop.ThrowIfCancellationRequested();
            }
            catch { Dispose(); throw; }
        }
        void Check()
        {
            _stop.ThrowIfCancellationRequested();
            if (_handle == new IntPtr(-1)) throw new ObjectDisposedException("pn532_cdc");
        }
        public void SetDtr(bool enabled)
        {
            if (_handle == new IntPtr(-1)) return;
            if (!EscapeCommFunction(_handle, enabled ? 5u : 6u)) throw new IOException("pn532_cdc_dtr_failed");
        }
        public void ClearReceived()
        {
            Check();
            if (!PurgeComm(_handle, 8)) throw new IOException("pn532_cdc_clear_failed");
        }
        public void Write(byte[] bytes)
        {
            Check();
            // Treat a timed-out partial frame as a device fault. Retrying its
            // suffix could keep an expired target alive or corrupt the stream.
            if (bytes.Length == 0) return;
            if (!WriteFile(_handle, bytes, (uint)bytes.Length, out var written, IntPtr.Zero) || written != bytes.Length)
                throw new IOException("pn532_cdc_write_failed");
        }
        public int Read(byte[] buffer)
        {
            Check();
            if (!ReadFile(_handle, buffer, (uint)buffer.Length, out var count, IntPtr.Zero) || count > buffer.Length) throw new IOException("pn532_cdc_read_failed");
            return (int)count;
        }
        public void Dispose()
        {
            if (_handle == new IntPtr(-1)) return;
            var handle = _handle;
            _handle = new IntPtr(-1);
            try
            {
                if (_saved)
                {
                    var restore = (byte[])_original.Clone();
                    // Keep DTR disabled even when the saved mode was handshake;
                    // CLRDTR would otherwise be rejected by the serial driver.
                    Put32(restore, 8, Get32(restore, 8) & ~0x30u);
                    SetCommState(handle, restore);
                    SetCommTimeouts(handle, _originalTimeouts);
                }
            }
            finally
            {
                // DTR low releases the firmware's blocked target operation.
                try { EscapeCommFunction(handle, 6); }
                finally { CloseHandle(handle); }
            }
        }
        // Fixed Win32 DCB (28 bytes) and COMMTIMEOUTS (20 bytes). Buffers
        // avoid StructLayoutAttribute, absent in some stripped player refs.
        static uint Get32(byte[] data, int offset) => (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
        static void Put32(byte[] data, int offset, uint value)
        {
            for (int i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (8 * i));
        }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetCommState(IntPtr handle, [In, Out] byte[] state);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetCommState(IntPtr handle, byte[] state);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetCommTimeouts(IntPtr handle, [Out] byte[] timeouts);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetCommTimeouts(IntPtr handle, byte[] timeouts);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool EscapeCommFunction(IntPtr handle, uint function);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool PurgeComm(IntPtr handle, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr handle, [Out] byte[] buffer, uint count, out uint received, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr handle, byte[] buffer, uint count, out uint written, IntPtr overlapped);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    }
}
