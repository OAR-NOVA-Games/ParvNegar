using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ParvNegar.Licensing
{
    /// <summary>
    /// Reads the binding signals with plain Win32 calls available on Windows 7 SP1 and later, without
    /// administrator rights, WMI or network access:
    /// <list type="bullet">
    /// <item>SMBIOS type-1 system UUID — <c>GetSystemFirmwareTable('RSMB')</c> (Vista SP1+).</item>
    /// <item>Serial number of the physical disk that holds the Windows volume — <c>IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS</c> then <c>IOCTL_STORAGE_QUERY_PROPERTY</c>.</item>
    /// <item><c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c> from the 64-bit registry view, so x86 and x64 processes read the same value.</item>
    /// </list>
    /// Any failure yields an unavailable reading; it never throws and never substitutes another value.
    /// </summary>
    public sealed class WindowsDeviceSignals : IDeviceSignalSource
    {
        public IReadOnlyList<SignalReading> Read() => new[]
        {
            new SignalReading(DeviceSignal.Smbios, Try(ReadSmbiosUuid)),
            new SignalReading(DeviceSignal.Disk, Try(ReadSystemDiskSerial)),
            new SignalReading(DeviceSignal.MachineGuid, Try(ReadMachineGuid)),
        };

        private static string Try(Func<string> f)
        {
            try { return f(); }
            catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is UnauthorizedAccessException || ex is EntryPointNotFoundException || ex is DllNotFoundException || ex is ArgumentException || ex is IndexOutOfRangeException) { return null; }
        }

        // ---- SMBIOS ----
        internal static string ReadSmbiosUuid()
        {
            const uint RSMB = 0x52534D42;
            uint size = GetSystemFirmwareTable(RSMB, 0, IntPtr.Zero, 0);
            if (size == 0 || size > 1024 * 1024) return null;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetSystemFirmwareTable(RSMB, 0, buf, size) != size) return null;
                var data = new byte[size];
                Marshal.Copy(buf, data, 0, (int)size);
                return ParseSmbiosUuid(data);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Parses RawSMBIOSData (8-byte header, then structures) and returns the type-1 UUID.</summary>
        internal static string ParseSmbiosUuid(byte[] raw)
        {
            if (raw.Length < 8) return null;
            int tableLen = BitConverter.ToInt32(raw, 4);
            int end = Math.Min(raw.Length, 8 + Math.Max(0, tableLen));
            int p = 8;
            while (p + 4 <= end)
            {
                byte type = raw[p], len = raw[p + 1];
                if (len < 4 || p + len > end) return null;
                if (type == 1 && len >= 0x19)
                {
                    var u = new byte[16];
                    Buffer.BlockCopy(raw, p + 8, u, 0, 16);
                    // SMBIOS 2.6+: first three fields little-endian, which is exactly System.Guid's layout
                    // (same text as Win32_ComputerSystemProduct.UUID).
                    return new Guid(u).ToString("D");
                }
                if (type == 127) return null;
                int q = p + len;
                while (q + 1 < end && !(raw[q] == 0 && raw[q + 1] == 0)) q++;
                p = q + 2;
            }
            return null;
        }

        // ---- System disk ----
        private static string ReadSystemDiskSerial()
        {
            var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var root = Path.GetPathRoot(sysDir);
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return null;
            int disk;
            using (var vol = Open(@"\\.\" + root.Substring(0, 2)))
            {
                if (vol.IsInvalid) return null;
                var ext = new byte[1024];
                if (!DeviceIoControl(vol, 0x00560000 /* IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS */, null, 0, ext, ext.Length, out int got, IntPtr.Zero) || got < 16) return null;
                if (BitConverter.ToInt32(ext, 0) < 1) return null;
                disk = BitConverter.ToInt32(ext, 8); // first DISK_EXTENT.DiskNumber (after 4-byte count + 4 padding)
            }
            using (var drive = Open(@"\\.\PhysicalDrive" + disk))
            {
                if (drive.IsInvalid) return null;
                var query = new byte[12]; // STORAGE_PROPERTY_QUERY { StorageDeviceProperty, PropertyStandardQuery, 0 }
                var desc = new byte[4096];
                if (!DeviceIoControl(drive, 0x002D1400 /* IOCTL_STORAGE_QUERY_PROPERTY */, query, query.Length, desc, desc.Length, out int got, IntPtr.Zero) || got < 28) return null;
                int serialOffset = BitConverter.ToInt32(desc, 24);
                if (serialOffset <= 0 || serialOffset >= got) return null;
                int e = serialOffset;
                while (e < got && desc[e] != 0) e++;
                return Encoding.ASCII.GetString(desc, serialOffset, e - serialOffset);
            }
        }

        private static SafeFileHandle Open(string path)
            => CreateFile(path, 0 /* no data access: metadata IOCTLs only */, 3 /* FILE_SHARE_READ|WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);

        // ---- MachineGuid ----
        private static string ReadMachineGuid()
        {
            const uint KEY_READ = 0x20019, KEY_WOW64_64KEY = 0x0100;
            var HKLM = new UIntPtr(0x80000002u);
            if (RegOpenKeyEx(HKLM, @"SOFTWARE\Microsoft\Cryptography", 0, KEY_READ | KEY_WOW64_64KEY, out var key) != 0) return null;
            try
            {
                int size = 256;
                var sb = new StringBuilder(size / 2);
                if (RegQueryValueEx(key, "MachineGuid", IntPtr.Zero, out uint type, sb, ref size) != 0 || type != 1 /* REG_SZ */) return null;
                return sb.ToString();
            }
            finally { RegCloseKey(key); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint provider, uint tableId, IntPtr buffer, uint size);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegOpenKeyEx(UIntPtr root, string subKey, uint options, uint sam, out IntPtr key);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegQueryValueEx(IntPtr key, string name, IntPtr reserved, out uint type, StringBuilder data, ref int size);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr key);
    }
}
