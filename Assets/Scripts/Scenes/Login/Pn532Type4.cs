using System;
using System.Text;

namespace MajdataPlay.Scenes.Login
{
    // NFC Forum Type 4 read-only NDEF application; no vendor firmware code.
    internal sealed class Pn532Type4 : IDisposable
    {
        byte[] _ndef;
        byte[] _cc;
        byte[] _file;
        bool _selected;
        public Pn532Type4(byte[] ndef)
        {
            if (ndef.Length == 0 || ndef.Length > 32766) throw new ArgumentException();
            _ndef = new byte[ndef.Length + 2];
            _ndef[0] = (byte)(ndef.Length >> 8); _ndef[1] = (byte)ndef.Length;
            Buffer.BlockCopy(ndef, 0, _ndef, 2, ndef.Length);
            _cc = new byte[] { 0, 15, 0x20, 0, 51, 0, 51, 4, 6, 0xE1, 4,
                (byte)(_ndef.Length >> 8), (byte)_ndef.Length, 0, 0xFF };
        }
        static byte[] Status(byte high, byte low = 0) => new byte[] { high, low };
        static bool DataEquals(byte[] command, byte[] data)
        {
            if (command.Length < 5 || command[4] != data.Length) return false;
            for (int i = 0; i < data.Length; i++) if (command[i + 5] != data[i]) return false;
            return true;
        }
        public byte[] Process(byte[] command)
        {
            if (command.Length < 4) return Status(0x67);
            if (command[0] != 0) return Status(0x6E);
            if (command[1] == 0xD6) return Status(0x69, 0x82);
            if (command[1] == 0xA4)
            {
                if (command.Length < 5 || (command.Length != 5 + command[4] && command.Length != 6 + command[4])) return Status(0x67);
                if (command[3] != 0 && command[3] != 0x0C) return Status(0x6A, 0x86);
                if (command[2] == 4)
                {
                    _selected = false; _file = null;
                    if (!DataEquals(command, new byte[] { 0xD2, 0x76, 0, 0, 0x85, 1, 1 })) return Status(0x6A, 0x82);
                    _selected = true; _file = null; return Status(0x90);
                }
                if (command[2] != 0) return Status(0x6A, 0x86);
                if (!_selected) return Status(0x69, 0x86);
                _file = null;
                if (DataEquals(command, new byte[] { 0xE1, 3 })) _file = _cc;
                else if (DataEquals(command, new byte[] { 0xE1, 4 })) _file = _ndef;
                else return Status(0x6A, 0x82);
                return Status(0x90);
            }
            if (command[1] != 0xB0) return Status(0x6D);
            if (command.Length != 5) return Status(0x67);
            if (_file == null) return Status(0x69, 0x86);
            int offset = command[2] * 256 + command[3];
            if ((command[2] & 0x80) != 0 || offset >= _file.Length) return Status(0x6B);
            int count = Math.Min(Math.Min(command[4] == 0 ? 256 : command[4], 51), _file.Length - offset);
            var response = new byte[count + 2];
            Buffer.BlockCopy(_file, offset, response, 0, count); response[count] = 0x90;
            return response;
        }
        public void Dispose()
        {
            Array.Clear(_ndef, 0, _ndef.Length); Array.Clear(_cc, 0, _cc.Length); _file = null; _selected = false;
        }
        public static byte[] UriRecord(string uri)
        {
            var text = Encoding.UTF8.GetBytes(uri.Substring(8));
            int length = text.Length + 1;
            bool shortRecord = length < 256;
            int header = shortRecord ? 5 : 8;
            var result = new byte[header + text.Length];
            result[0] = shortRecord ? (byte)0xD1 : (byte)0xC1; result[1] = 1;
            if (shortRecord) result[2] = (byte)length;
            else { result[2] = (byte)(length >> 24); result[3] = (byte)(length >> 16); result[4] = (byte)(length >> 8); result[5] = (byte)length; }
            result[header - 2] = 0x55; result[header - 1] = 4;
            Buffer.BlockCopy(text, 0, result, header, text.Length); Array.Clear(text, 0, text.Length);
            return result;
        }
    }
}
