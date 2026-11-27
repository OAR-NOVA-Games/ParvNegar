using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ParvNegar.Licensing;
using ParvNegar.Security;

namespace ParvNegar.Application.Security
{
    /// <summary>Wall clock for records and a monotonic clock for elapsed-time decisions (inactivity, throttling within a process).</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        long MonotonicMilliseconds { get; }
    }

    public sealed class SystemClock : IClock
    {
        private static readonly Stopwatch Mono = Stopwatch.StartNew();
        public DateTime UtcNow => DateTime.UtcNow;
        public long MonotonicMilliseconds => Mono.ElapsedMilliseconds;
    }

    /// <summary>
    /// On-disk layout of one installation (AUTHENTICATION-ARCHITECTURE.md §2). Default root:
    /// %ProgramData%\OARNOVA\ParvNegar. Tests pass a temporary root.
    /// <code>
    /// root\license\license.pnlic             signed license (no secrets)
    /// root\activation\pending-request.pnreq  last generated activation request (no secrets)
    /// root\security\guard.key                DPAPI-LocalMachine-protected key for pre-unlock integrity
    /// root\security\throttle.pnth            login throttling state (HMAC under the guard key)
    /// root\security\journal.pnj              bounded pre-unlock event journal (HMAC chain under the guard key)
    /// root\installation\                     created atomically by setup (rename of a staging folder)
    ///     installation.json                  marker: install id, created, format
    ///     device.pnds                        DPAPI LocalMachine device secret
    ///     accounts\&lt;envelopeId&gt;.pnke       PNKE v2 account envelopes
    ///     recovery\&lt;envelopeId&gt;.pnke       PNKE v2 recovery envelope
    ///     data\parvnegar.db                  encrypted database
    /// root\installation.staging-&lt;guid&gt;\    incomplete setup, deleted on next start
    /// </code>
    /// </summary>
    public sealed class InstallationPaths
    {
        public InstallationPaths(string root) { Root = Path.GetFullPath(root); }

        public static InstallationPaths Default()
            => new InstallationPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OARNOVA", "ParvNegar"));

        public string Root { get; }
        public string LicenseFile => Path.Combine(Root, "license", "license.pnlic");
        public string PendingRequestFile => Path.Combine(Root, "activation", "pending-request.pnreq");
        public string SecurityDir => Path.Combine(Root, "security");
        public string GuardKeyFile => Path.Combine(SecurityDir, "guard.key");
        public string ThrottleFile => Path.Combine(SecurityDir, "throttle.pnth");
        public string JournalFile => Path.Combine(SecurityDir, "journal.pnj");
        public string SetupLockFile => Path.Combine(Root, "setup.lock");
        public string InstallationDir => Path.Combine(Root, "installation");
        public string MarkerFile => Path.Combine(InstallationDir, "installation.json");

        public InstallationLayout Installed => new InstallationLayout(InstallationDir);
        public bool IsInitialized => File.Exists(MarkerFile);

        public IEnumerable<string> StagingDirs()
            => Directory.Exists(Root) ? Directory.GetDirectories(Root, "installation.staging-*") : Enumerable.Empty<string>();
    }

    /// <summary>Files inside one installation folder (the live folder, or a staging folder during setup).</summary>
    public sealed class InstallationLayout
    {
        public InstallationLayout(string dir) { Dir = dir; }
        public string Dir { get; }
        public string Marker => Path.Combine(Dir, "installation.json");
        public string DeviceSecretFile => Path.Combine(Dir, "device.pnds");
        public string AccountsDir => Path.Combine(Dir, "accounts");
        public string RecoveryDir => Path.Combine(Dir, "recovery");
        public string DataDir => Path.Combine(Dir, "data");
        public string Database => Path.Combine(DataDir, "parvnegar.db");
        public string AccountEnvelope(Guid envelopeId) => Path.Combine(AccountsDir, envelopeId.ToString("D") + ".pnke");
        public string RecoveryEnvelope(Guid envelopeId) => Path.Combine(RecoveryDir, envelopeId.ToString("D") + ".pnke");
    }

    /// <summary>Installation marker (not secret, not authoritative for accounts).</summary>
    public sealed class InstallationMarker
    {
        public const string Format = "PARVNEGAR-INSTALLATION-V1";
        public Guid InstallId { get; set; }
        public DateTime CreatedUtc { get; set; }

        public byte[] Serialize() => StrictJson.Canonical(JsonValue.Obj(
            ("created_at", JsonValue.Str(CreatedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))),
            ("format", JsonValue.Str(Format)),
            ("install_id", JsonValue.Str(InstallId.ToString("D")))));

        public static InstallationMarker Read(string path)
        {
            var v = StrictJson.Parse(File.ReadAllBytes(path));
            v.RequireExactKeys("created_at", "format", "install_id");
            if (v.Get("format").AsString("format") != Format) throw new LicenseFormatException("installation marker format");
            return new InstallationMarker
            {
                InstallId = Guid.ParseExact(v.Get("install_id").AsString("install_id"), "D"),
                CreatedUtc = DateTime.ParseExact(v.Get("created_at").AsString("created_at"), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            };
        }
    }

    /// <summary>
    /// Stable permission identifiers (AUTHORIZATION-MATRIX.md). Grantable permissions may be given to
    /// delegated administrators; owner-only operations can never be granted. Identifiers are stored in
    /// the database and must never be renamed.
    /// </summary>
    public static class Permissions
    {
        public const string StudentView = "student.view";
        public const string StudentCreate = "student.create";
        public const string StudentEdit = "student.edit";
        public const string StudentDeleteRestore = "student.delete_restore";
        public const string EducationalRecords = "record.educational";
        public const string PastoralRecords = "record.pastoral";
        public const string Documents = "document.manage";
        public const string Print = "print";
        public const string ExportPdf = "export.pdf";
        public const string YearPromote = "year.promote";
        public const string YearManage = "year.manage";
        public const string BackupCreate = "backup.create";

        /// <summary>Grantable now (deny by default for delegates).</summary>
        public static readonly string[] Grantable =
        {
            StudentView, StudentCreate, StudentEdit, StudentDeleteRestore, EducationalRecords, PastoralRecords,
            Documents, Print, ExportPdf, YearPromote, YearManage, BackupCreate,
        };

        /// <summary>Reserved for future modules: identifiers fixed now, not grantable until the module exists.</summary>
        public static readonly string[] Reserved = { "report.view", "assessment.manage", "data.import", "attendance.manage" };

        // Owner-only operations: never stored as grants, never grantable.
        public const string AccountsManage = "owner.accounts";
        public const string PermissionsAssign = "owner.permissions";
        public const string DelegatePasswordReset = "owner.password_reset";
        public const string RecoveryManage = "owner.recovery";
        public const string BackupRestore = "owner.backup_restore";
        public const string LicenseManage = "owner.license";
        public const string SchoolIdentity = "owner.school_identity";
        public const string AuditView = "owner.audit";

        public static readonly string[] OwnerOnly =
        {
            AccountsManage, PermissionsAssign, DelegatePasswordReset, RecoveryManage, BackupRestore, LicenseManage, SchoolIdentity, AuditView,
        };

        public static bool IsGrantable(string p) => Array.IndexOf(Grantable, p) >= 0;
        public static bool IsOwnerOnly(string p) => Array.IndexOf(OwnerOnly, p) >= 0;
    }

    /// <summary>
    /// 32-byte key for pre-unlock integrity (throttle store, pre-unlock journal), protected with DPAPI
    /// LocalMachine. It protects against casual editing of those files; any local administrator can still
    /// read or delete them (SESSION-SECURITY.md §5).
    /// </summary>
    public sealed class GuardKey
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ParvNegar/v1/dpapi/guard-key");
        private readonly InstallationPaths _paths;
        private readonly IDeviceSecretProtector _dpapi;
        private byte[] _key;

        public GuardKey(InstallationPaths paths, IDeviceSecretProtector dpapi) { _paths = paths; _dpapi = dpapi; }

        public byte[] Key
        {
            get
            {
                if (_key != null) return _key;
                Directory.CreateDirectory(_paths.SecurityDir);
                if (File.Exists(_paths.GuardKeyFile))
                {
                    try { _key = _dpapi.Unprotect(File.ReadAllBytes(_paths.GuardKeyFile), Entropy); }
                    catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException || ex is IOException) { _key = null; }
                    if (_key != null && _key.Length == 32) return _key;
                }
                var k = CryptoRandom.Bytes(32);
                AtomicFile.Write(_paths.GuardKeyFile, _dpapi.Protect(k, Entropy));
                _key = k;
                return _key;
            }
        }

        public byte[] Mac(string label, byte[] data)
        {
            using (var h = new System.Security.Cryptography.HMACSHA256(Hkdf.DeriveKey(Key, null, "ParvNegar/v1/guard/" + label, 32)))
                return h.ComputeHash(data);
        }
    }

    public static class AtomicFile
    {
        /// <summary>Write-through to a unique temp file in the same folder, then replace.</summary>
        public static void Write(string path, byte[] data)
        {
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }

    /// <summary>
    /// Persistent progressive login throttling (SESSION-SECURITY.md §4). Keys are "global" and
    /// per-username hashes. Delay after n consecutive failures: 0 for n ≤ 3, then 5, 15, 30, 60, 120, 300 s,
    /// capped at 600 s. Never a permanent lockout. Clock moved backwards or a tampered file ⇒ the maximum delay
    /// from now (never a reset). Within a process the monotonic clock also enforces the wait.
    /// </summary>
    public sealed class LoginThrottle
    {
        public const int FreeAttempts = 3;
        public const int MaxDelaySeconds = 600;
        private static readonly int[] Steps = { 5, 15, 30, 60, 120, 300, 600 };
        private readonly InstallationPaths _paths;
        private readonly GuardKey _guard;
        private readonly IClock _clock;
        private readonly Dictionary<string, long> _monoNotBefore = new Dictionary<string, long>(StringComparer.Ordinal);

        public LoginThrottle(InstallationPaths paths, GuardKey guard, IClock clock) { _paths = paths; _guard = guard; _clock = clock; }

        public static int DelayFor(int failures) => failures <= FreeAttempts ? 0 : Steps[Math.Min(failures - FreeAttempts - 1, Steps.Length - 1)];

        /// <summary>Remaining wait for this key (and the global key); zero when an attempt is allowed now.</summary>
        public TimeSpan RemainingWait(string key)
        {
            var state = Load();
            return Max(Remaining(state, key), Remaining(state, "global"));
        }

        public void RecordFailure(string key)
        {
            var state = Load();
            foreach (var k in new[] { key, "global" })
            {
                state.TryGetValue(k, out var e);
                e = e ?? new Entry();
                e.Failures++;
                var delay = DelayFor(k == "global" ? Math.Max(0, e.Failures - 7) : e.Failures); // global: 10 free failures across all names
                var notBefore = _clock.UtcNow.AddSeconds(delay);
                if (notBefore > e.NotBeforeUtc) e.NotBeforeUtc = notBefore;
                e.LastUtc = _clock.UtcNow;
                state[k] = e;
                _monoNotBefore[k] = _clock.MonotonicMilliseconds + delay * 1000L;
            }
            Save(state);
        }

        public void RecordSuccess(string key)
        {
            var state = Load();
            state.Remove(key);
            if (state.TryGetValue("global", out var g)) { g.Failures = Math.Max(0, g.Failures - 1); }
            _monoNotBefore.Remove(key);
            Save(state);
        }

        private TimeSpan Remaining(Dictionary<string, Entry> state, string key)
        {
            var now = _clock.UtcNow;
            var wall = TimeSpan.Zero;
            if (state.TryGetValue(key, out var e))
            {
                if (now < e.LastUtc) // clock moved backwards: never a reset, apply the maximum delay from now
                {
                    e.NotBeforeUtc = now.AddSeconds(MaxDelaySeconds);
                    e.LastUtc = now;
                    Save(state);
                }
                if (e.NotBeforeUtc > now) wall = e.NotBeforeUtc - now;
            }
            var mono = TimeSpan.Zero;
            if (_monoNotBefore.TryGetValue(key, out var nb) && nb > _clock.MonotonicMilliseconds) mono = TimeSpan.FromMilliseconds(nb - _clock.MonotonicMilliseconds);
            return Max(wall, mono);
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

        private sealed class Entry { public int Failures; public DateTime NotBeforeUtc; public DateTime LastUtc; }

        private Dictionary<string, Entry> Load()
        {
            var d = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (!File.Exists(_paths.ThrottleFile)) return d;
            try
            {
                var bytes = File.ReadAllBytes(_paths.ThrottleFile);
                if (bytes.Length < 33 || bytes.Length > 1024 * 1024) throw new LicenseFormatException("size");
                var body = new byte[bytes.Length - 32];
                Buffer.BlockCopy(bytes, 32, body, 0, body.Length);
                var mac = new byte[32];
                Buffer.BlockCopy(bytes, 0, mac, 0, 32);
                if (!Bytes.FixedTimeEquals(mac, _guard.Mac("throttle", body))) throw new LicenseFormatException("mac");
                var v = StrictJson.Parse(body);
                foreach (var m in v.Get("entries").Members)
                {
                    m.Value.RequireExactKeys("failures", "last", "not_before");
                    d[m.Key] = new Entry
                    {
                        Failures = (int)Math.Min(int.MaxValue, m.Value.Get("failures").AsInteger("f")),
                        NotBeforeUtc = new DateTime(m.Value.Get("not_before").AsInteger("n") * 10000000, DateTimeKind.Utc),
                        LastUtc = new DateTime(m.Value.Get("last").AsInteger("l") * 10000000, DateTimeKind.Utc),
                    };
                }
                return d;
            }
            catch (Exception ex) when (ex is LicenseFormatException || ex is IOException || ex is ArgumentException)
            {
                // Tampered or unreadable: every key waits the maximum delay (no reset).
                var t = _clock.UtcNow;
                d["global"] = new Entry { Failures = 0, NotBeforeUtc = t.AddSeconds(MaxDelaySeconds), LastUtc = t }; // one maximum wait, then normal counting
                Save(d);
                return d;
            }
        }

        private void Save(Dictionary<string, Entry> d)
        {
            var body = StrictJson.Canonical(JsonValue.Obj(("entries", JsonValue.Obj(d.Select(kv => new KeyValuePair<string, JsonValue>(kv.Key, JsonValue.Obj(
                ("failures", JsonValue.Int(kv.Value.Failures)),
                ("last", JsonValue.Int(kv.Value.LastUtc.Ticks / 10000000)),
                ("not_before", JsonValue.Int((kv.Value.NotBeforeUtc.Ticks + 9999999) / 10000000)))))))));
            var mac = _guard.Mac("throttle", body);
            var all = new byte[32 + body.Length];
            Buffer.BlockCopy(mac, 0, all, 0, 32);
            Buffer.BlockCopy(body, 0, all, 32, body.Length);
            AtomicFile.Write(_paths.ThrottleFile, all);
        }
    }

    /// <summary>
    /// Bounded pre-unlock event journal (AUDIT-LOG-FORMAT.md §5): at most <see cref="Capacity"/> records,
    /// each MAC-chained under the guard key. It is NOT authenticated by the LMK-derived audit key; it is
    /// imported into the audit chain after the next successful unlock and marked source = 'pre-unlock'.
    /// </summary>
    public sealed class PreUnlockJournal
    {
        public const int Capacity = 256;
        private readonly InstallationPaths _paths;
        private readonly GuardKey _guard;
        private readonly IClock _clock;

        public PreUnlockJournal(InstallationPaths paths, GuardKey guard, IClock clock) { _paths = paths; _guard = guard; _clock = clock; }

        public sealed class Record
        {
            public DateTime Utc; public string Kind; public string Subject; public string Outcome; public bool Verified;
        }

        public void Append(string kind, string subject, string outcome)
        {
            var list = ReadAll(out _).ToList();
            list.Add(new Record { Utc = _clock.UtcNow, Kind = kind, Subject = subject, Outcome = outcome, Verified = true });
            if (list.Count > Capacity) list = list.Skip(list.Count - Capacity).ToList();
            Write(list);
        }

        /// <summary>Returns the records; <paramref name="intact"/> is false when the chain was broken (records after the break are marked unverified).</summary>
        public IReadOnlyList<Record> ReadAll(out bool intact)
        {
            intact = true;
            var result = new List<Record>();
            if (!File.Exists(_paths.JournalFile)) return result;
            string[] lines;
            try { lines = File.ReadAllLines(_paths.JournalFile, new UTF8Encoding(false, true)); }
            catch (Exception ex) when (ex is IOException || ex is DecoderFallbackException) { intact = false; return result; }
            var prev = new byte[32];
            foreach (var line in lines.Take(Capacity + 1))
            {
                var parts = line.Split('\t');
                if (parts.Length != 6) { intact = false; continue; }
                var body = Encoding.UTF8.GetBytes(string.Join("\t", parts.Take(5)));
                var ok = Bytes.ToHex(_guard.Mac("journal", Concat(prev, body))) == parts[5];
                if (!ok) intact = false;
                long ticks;
                result.Add(new Record
                {
                    Utc = long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue,
                    Kind = parts[1], Subject = parts[2], Outcome = parts[3], Verified = ok && intact,
                });
                prev = Bytes.FromHex(parts[5].Length == 64 ? parts[5] : new string('0', 64));
            }
            return result;
        }

        public void Clear() { if (File.Exists(_paths.JournalFile)) File.Delete(_paths.JournalFile); }

        private void Write(List<Record> records)
        {
            var sb = new StringBuilder();
            var prev = new byte[32];
            foreach (var r in records)
            {
                var body = r.Utc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t" + Clean(r.Kind) + "\t" + Clean(r.Subject) + "\t" + Clean(r.Outcome) + "\t" + (r.Verified ? "1" : "0");
                var mac = _guard.Mac("journal", Concat(prev, Encoding.UTF8.GetBytes(body)));
                sb.Append(body).Append('\t').Append(Bytes.ToHex(mac)).Append('\n');
                prev = mac;
            }
            AtomicFile.Write(_paths.JournalFile, new UTF8Encoding(false).GetBytes(sb.ToString()));
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        private static byte[] Concat(byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; Buffer.BlockCopy(a, 0, r, 0, a.Length); Buffer.BlockCopy(b, 0, r, a.Length, b.Length); return r; }
    }
}
