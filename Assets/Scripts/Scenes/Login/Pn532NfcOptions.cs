using System;

namespace MajdataPlay.Scenes.Login
{
    // Serialized as settings.json's NFC section. Missing fields retain defaults.
    public sealed class Pn532NfcOptions
    {
        public bool Enabled { get; set; } = true;
        public string Port { get; set; } = "";
        public bool Diagnostics { get; set; } = true;
    }
    internal sealed class Pn532NfcConfiguration
    {
        internal Pn532NfcOptions Options;
        internal static Pn532NfcConfiguration Load()
        {
            try
            {
                Pn532NfcOptions options;
                options = MajEnv.Settings?.NFC;
                return new Pn532NfcConfiguration { Options = options ?? new Pn532NfcOptions() };
            }
            catch
            {
                // An unavailable settings model must not crash login or open a port.
                return new Pn532NfcConfiguration { Options = new Pn532NfcOptions { Enabled = false } };
            }
        }
        internal static bool Enabled(Pn532NfcOptions options, string value)
        {
            if (string.IsNullOrEmpty(value)) return options.Enabled;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        internal static string ResolvePort(string explicitPort, string[] detected)
        {
            if (!string.IsNullOrWhiteSpace(explicitPort))
            {
                var port = explicitPort.Trim().ToUpperInvariant();
                return Pn532NativeCdc.IsPortName(port) ? port : null;
            }
            // Auto selection never probes COM ports and refuses ambiguous readers.
            return detected != null && detected.Length == 1 && Pn532NativeCdc.IsPortName(detected[0]) ? detected[0] : null;
        }
    }
}
