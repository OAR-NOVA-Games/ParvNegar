using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ParvNegar.Security;

namespace ParvNegar.Licensing
{
    /// <summary>The three independent binding signals (DEVICE-BINDING.md §2).</summary>
    public enum DeviceSignal { Smbios, Disk, MachineGuid }

    public enum SignalQuality { Good, Unavailable, Placeholder }

    /// <summary>One raw signal reading. Raw values never leave the process: only claim digests are stored or sent.</summary>
    public sealed class SignalReading
    {
        public SignalReading(DeviceSignal signal, string raw)
        {
            Signal = signal;
            Normalized = raw == null ? null : DeviceBinding.Normalize(signal, raw);
            Quality = Normalized == null ? SignalQuality.Unavailable : DeviceBinding.IsPlaceholder(signal, Normalized) ? SignalQuality.Placeholder : SignalQuality.Good;
        }

        public DeviceSignal Signal { get; }
        internal string Normalized { get; }
        public SignalQuality Quality { get; }
    }

    /// <summary>Source of raw device signals (Windows implementation: <see cref="WindowsDeviceSignals"/>; tests use fakes).</summary>
    public interface IDeviceSignalSource
    {
        IReadOnlyList<SignalReading> Read();
    }

    /// <summary>Hashed device claims: lowercase hex SHA-256 per signal, or null when the signal was not usable.</summary>
    public sealed class DeviceClaims
    {
        public DeviceClaims(string smbios, string disk, string machineGuid)
        {
            Smbios = Check(smbios); Disk = Check(disk); MachineGuid = Check(machineGuid);
        }

        public string Smbios { get; }
        public string Disk { get; }
        public string MachineGuid { get; }

        public int UsableCount => new[] { Smbios, Disk, MachineGuid }.Count(x => x != null);

        public string Get(DeviceSignal s) => s == DeviceSignal.Smbios ? Smbios : s == DeviceSignal.Disk ? Disk : MachineGuid;

        internal JsonValue ToJson() => JsonValue.Obj(
            ("disk", Disk == null ? JsonValue.Null : JsonValue.Str(Disk)),
            ("machine_guid", MachineGuid == null ? JsonValue.Null : JsonValue.Str(MachineGuid)),
            ("smbios", Smbios == null ? JsonValue.Null : JsonValue.Str(Smbios)));

        internal static DeviceClaims FromJson(JsonValue v)
        {
            v.RequireExactKeys("disk", "machine_guid", "smbios");
            string S(string k) { var x = v.Get(k); return x.Kind == JsonKind.Null ? null : x.AsString(k); }
            return new DeviceClaims(S("smbios"), S("disk"), S("machine_guid"));
        }

        private static string Check(string h)
        {
            if (h == null) return null;
            if (h.Length != 64 || !h.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) throw new LicenseFormatException("device claim must be 64 lowercase hex digits");
            return h;
        }
    }

    public enum DeviceMatch { Match, Mismatch }

    /// <summary>
    /// Device binding version 1: three signals, each normalized and hashed with a product- and
    /// signal-specific domain; a license matches when at least <see cref="RequiredMatches"/> of the
    /// license's claims equal the current claims. An unavailable current signal counts as not matching:
    /// the threshold is never lowered because a signal cannot be read.
    /// </summary>
    public static class DeviceBinding
    {
        public const int Version = 1;
        public const int RequiredMatches = 2;
        public const string MatchPolicy = "2-of-3";

        private static readonly HashSet<string> PlaceholderUuids = new HashSet<string>(StringComparer.Ordinal)
        {
            "00000000-0000-0000-0000-000000000000", "ffffffff-ffff-ffff-ffff-ffffffffffff",
            "03000200-0400-0500-0006-000700080009", // common OEM template value
            "12345678-1234-5678-90ab-cddeefaabbcc", "4c4c4544-0000-0000-0000-000000000000",
        };

        private static readonly HashSet<string> PlaceholderText = new HashSet<string>(StringComparer.Ordinal)
        {
            "", "0", "none", "null", "n/a", "na", "default", "default string", "to be filled by o.e.m.", "to be filled by oem",
            "system serial number", "not specified", "not applicable", "unknown", "123456789", "0123456789", "serial",
            "oem", "o.e.m.", "invalid",
        };

        /// <summary>Deterministic normalization; null when nothing usable remains.</summary>
        public static string Normalize(DeviceSignal signal, string raw)
        {
            if (raw == null) return null;
            var s = raw.Normalize(NormalizationForm.FormKC);
            var sb = new StringBuilder();
            foreach (var c in s)
                if (!char.IsControl(c)) sb.Append(c);
            s = string.Join(" ", sb.ToString().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            if (signal == DeviceSignal.Smbios || signal == DeviceSignal.MachineGuid)
            {
                s = s.Trim('{', '}');
                if (!Guid.TryParseExact(s, "D", out var g)) return s.Length == 0 ? null : s;
                return g.ToString("D");
            }
            return s.Length == 0 ? null : s;
        }

        public static bool IsPlaceholder(DeviceSignal signal, string normalized)
        {
            if (normalized == null) return true;
            if (PlaceholderText.Contains(normalized)) return true;
            if (signal != DeviceSignal.Disk && PlaceholderUuids.Contains(normalized)) return true;
            if (signal != DeviceSignal.Disk && !Guid.TryParseExact(normalized, "D", out _)) return true; // UUID signals must be UUIDs
            var compact = normalized.Replace("-", "").Replace(" ", "").Replace(".", "");
            if (compact.Length == 0) return true;
            if (compact.All(c => c == '0') || compact.All(c => c == 'f')) return true;
            if (compact.All(c => c == compact[0])) return true; // e.g. "1111111111", "xxxxxxxx"
            if (signal == DeviceSignal.Disk && compact.Length < 4) return true;
            return false;
        }

        public static string Digest(DeviceSignal signal, string normalized)
        {
            var name = signal == DeviceSignal.Smbios ? "smbios" : signal == DeviceSignal.Disk ? "disk" : "machine-guid";
            return Bytes.ToHex(Bytes.Sha256(Encoding.UTF8.GetBytes("ParvNegar/v1/device/" + name + "\n" + normalized)));
        }

        public static DeviceClaims Claims(IReadOnlyList<SignalReading> readings)
        {
            string D(DeviceSignal s)
            {
                var r = readings.FirstOrDefault(x => x.Signal == s);
                return r != null && r.Quality == SignalQuality.Good ? Digest(s, r.Normalized) : null;
            }
            return new DeviceClaims(D(DeviceSignal.Smbios), D(DeviceSignal.Disk), D(DeviceSignal.MachineGuid));
        }

        /// <summary>Count of license claims that equal current claims. Null on either side never matches.</summary>
        public static int CountMatches(DeviceClaims licensed, DeviceClaims current)
        {
            int n = 0;
            foreach (DeviceSignal s in Enum.GetValues(typeof(DeviceSignal)))
            {
                var a = licensed.Get(s); var b = current.Get(s);
                if (a != null && b != null && Bytes.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b))) n++;
            }
            return n;
        }

        public static DeviceMatch Match(DeviceClaims licensed, DeviceClaims current)
            => CountMatches(licensed, current) >= RequiredMatches ? DeviceMatch.Match : DeviceMatch.Mismatch;

        public static string Describe(IReadOnlyList<SignalReading> readings)
            => string.Join(", ", readings.Select(r => r.Signal.ToString().ToLowerInvariant() + "=" + r.Quality.ToString().ToLowerInvariant()));

        internal static string Hex(string s) => string.Concat(Encoding.UTF8.GetBytes(s).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
