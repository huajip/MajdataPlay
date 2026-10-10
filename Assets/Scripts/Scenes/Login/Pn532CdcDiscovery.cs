using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MajdataPlay.Scenes.Login
{
    internal static class Pn532CdcDiscovery
    {
        internal static string[] PresentHinataPorts()
        {
#if NET5_0_OR_GREATER
            if (!OperatingSystem.IsWindows()) return Array.Empty<string>();
#else
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return Array.Empty<string>();
#endif
            var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var set = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, 6); // present/all classes
            if (set == new IntPtr(-1)) return Array.Empty<string>();
            try
            {
                for (uint i = 0; i < 4096; i++)
                {
                    // SP_DEVINFO_DATA is 32 bytes in x64 and 28 in x86. No marshaled struct.
                    var data = new byte[IntPtr.Size == 8 ? 32 : 28]; data[0] = (byte)data.Length;
                    if (!SetupDiEnumDeviceInfo(set, i, data)) break;
                    var id = new StringBuilder(512);
                    if (!SetupDiGetDeviceInstanceId(set, data, id, id.Capacity, out var required)) continue;
                    if (!id.ToString().StartsWith("USB\\VID_F822&PID_0148&MI_00\\", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\" + id + "\\Device Parameters", false))
                    {
                        var port = key?.GetValue("PortName") as string;
                        if (Pn532NativeCdc.IsPortName(port)) ports.Add(port);
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            var result = new string[ports.Count]; ports.CopyTo(result); return result;
        }
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string enumerator, IntPtr window, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, [In, Out] byte[] data);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInstanceId(IntPtr set, [In] byte[] data, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
