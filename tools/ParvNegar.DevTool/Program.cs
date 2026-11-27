using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using ParvNegar.Security;
using ParvNegar.Storage;

namespace ParvNegar.DevTool
{
    /// <summary>
    /// Usage:
    ///   ParvNegar.DevTool interop-produce &lt;dir&gt;   create a synthetic encrypted data set + backup with this edition/arch
    ///   ParvNegar.DevTool interop-consume &lt;dir&gt;   verify a data set produced by any edition/arch
    ///   ParvNegar.DevTool compat &lt;outdir&gt;          run isolated compatibility checks (one child process per check)
    ///   ParvNegar.DevTool license-signals x           print binding-signal quality and claim-digest prefixes (never raw values)
    ///   ParvNegar.DevTool license-request &lt;file&gt;    write an activation request for this edition/arch
    ///   ParvNegar.DevTool license-verify &lt;lic&gt; &lt;anchors&gt;  verify a license offline with this machine's live signals
    /// Exit code = number of failed checks. The keys below are synthetic scratch values for tests only.
    /// </summary>
    public static class Program
    {
        // Synthetic, publicly known test key. NEVER used for real data.
        internal const string ScratchLmkHex = "5061727654657374536372617463684b65792d6e6f742d666f722d7265616c21";
        internal static readonly byte[] ScratchPassphrase = Encoding.UTF8.GetBytes("synthetic interop passphrase ۴۲");
        internal static readonly Argon2idParameters Kdf = new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 2, 1);
        internal const string Title = "ثبت شاخص های آموزشی و پرورشی دانش آموزان جامعه هدف مدرسه شاهد شهیده شهناز کریمی";
        internal const string StudentName = "دانش‌آموز آزمایشی همگانی";

        private static readonly List<string[]> Results = new List<string[]>();

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.Error.WriteLine("usage: interop-produce|interop-consume|compat|--check <arg>"); return 64; }
            try
            {
                switch (args[0])
                {
                    case "interop-produce": Interop.Produce(Path.GetFullPath(args[1])); return 0;
                    case "interop-consume": Interop.Consume(Path.GetFullPath(args[1])); break;
                    case "compat": Compat.Run(Path.GetFullPath(args[1])); break;
                    case "--check": return Compat.RunSingle(args[1], args.Length > 2 ? args[2] : Path.GetTempPath());
                    case "license-signals": return LicenseInterop.Signals();
                    case "license-request": return LicenseInterop.Request(Path.GetFullPath(args[1]));
                    case "license-verify": return LicenseInterop.Verify(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
                    default: Console.Error.WriteLine("unknown command"); return 64;
                }
            }
            catch (Exception ex)
            {
                Record("TOOL", false, ex.GetType().Name + ": " + ex.Message);
            }
            foreach (var r in Results) Console.WriteLine(string.Join("\t", r));
            return Results.Count(r => r[1] == "FAIL");
        }

        internal static string Edition => (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal) ? "legacy-net48" : "modern-net10") + "-" + (Environment.Is64BitProcess ? "x64" : "x86");

        internal static void Record(string id, bool pass, string detail) =>
            Results.Add(new[] { id, pass ? "PASS" : "FAIL", (detail ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') });

        internal static void Check(string id, Func<string> f)
        {
            try { Record(id, true, f()); }
            catch (Exception ex) { Record(id, false, ex.GetType().Name + ": " + ex.Message); }
        }

        internal static void Expect<T>(string id, Action a) where T : Exception
        {
            try { a(); Record(id, false, "expected " + typeof(T).Name + " but the operation succeeded"); }
            catch (T ex) { Record(id, true, "rejected: " + ex.GetType().Name); }
            catch (Exception ex) { Record(id, false, "wrong exception " + ex.GetType().Name + ": " + ex.Message); }
        }
    }

    internal static class Interop
    {
        public static void Produce(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            using (var lmk = LocalMasterKey.FromBytes(Bytes.FromHex(Program.ScratchLmkHex)))
            {
                var db = Path.Combine(dir, "parvnegar.db");
                using (var c = EncryptedDatabase.Create(db, lmk.DatabaseKey()))
                {
                    MigrationRunner.Migrate(c, MigrationRunner.Embedded());
                    Seed(c);
                    EncryptedDatabase.Checkpoint(c);
                }

                // Attachment-style blob encrypted with the derived attachment key (stream index 0).
                var blobPlain = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("کارنامه آزمایشی synthetic ", 9000)));
                File.WriteAllBytes(Path.Combine(dir, "blob.plain"), blobPlain);
                using (var src = new MemoryStream(blobPlain))
                using (var dst = File.Create(Path.Combine(dir, "blob.enc")))
                    ChunkedAead.Encrypt(lmk.AttachmentKey(), 0, Bytes.Sha256(Encoding.ASCII.GetBytes("blob-header")), src, dst);

                // The LMK goes into the archive from memory only; no plaintext key file is written.
                var lmkBytes = Bytes.FromHex(Program.ScratchLmkHex);
                using (var keyEntry = BackupEntrySource.FromKeyMaterial("keys/lmk", lmkBytes))
                    BackupArchive.Create(Path.Combine(dir, "backup.pnbk"), new[]
                    {
                        new BackupEntrySource("database/parvnegar.db", db),
                        new BackupEntrySource("attachments/0001.blob", Path.Combine(dir, "blob.enc")),
                        keyEntry,
                    }, Program.ScratchPassphrase, MigrationRunner.Embedded().Count, Program.Kdf);
                Bytes.Wipe(lmkBytes);
            }
            File.WriteAllText(Path.Combine(dir, "producer.txt"), Program.Edition + "\n" + RuntimeInformation.FrameworkDescription + "\n", new UTF8Encoding(false));
            Console.WriteLine("PRODUCED\t" + Program.Edition + "\t" + dir);
        }

        private static void Seed(SqliteConnection c)
        {
            void X(string sql, params (string, object)[] p)
            {
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = sql;
                    foreach (var kv in p) cmd.Parameters.AddWithValue(kv.Item1, kv.Item2);
                    cmd.ExecuteNonQuery();
                }
            }
            var school = "11111111-1111-4111-8111-111111111111"; var year = "22222222-2222-4222-8222-222222222222";
            var cls = "33333333-3333-4333-8333-333333333333"; var stu = "44444444-4444-4444-8444-444444444444";
            X("INSERT INTO school_profile(id, school_name, home_title, created_utc, updated_utc) VALUES ($i, 'مدرسه آزمایشی', $t, 't', 't');", ("$i", school), ("$t", Program.Title));
            X("INSERT INTO academic_year(id, school_id, start_solar_year, state, start_date, end_date) VALUES ($i, $s, 1405, 1, '2026-09-23', '2027-06-21');", ("$i", year), ("$s", school));
            X("INSERT INTO classroom(id, academic_year_id, grade, code, name, teacher_name) VALUES ($i, $y, 3, '301', 'سوم الف', 'آموزگار آزمایشی');", ("$i", cls), ("$y", year));
            X("INSERT INTO student(id, full_name, full_name_key, national_id, is_target_group, created_utc, updated_utc) VALUES ($i, $n, $k, '0000000011', 1, 't', 't');",
              ("$i", stu), ("$n", Program.StudentName), ("$k", ParvNegar.Domain.PersianText.SearchKey(Program.StudentName)));
            X("INSERT INTO enrollment(id, student_id, school_id, academic_year_id, grade, classroom_id, status, snapshot_student_name, snapshot_classroom, snapshot_teacher, snapshot_school_title, created_utc) VALUES ('55555555-5555-4555-8555-555555555555', $st, $s, $y, 3, $c, 0, $n, 'سوم الف', 'آموزگار آزمایشی', $t, 't');",
              ("$st", stu), ("$s", school), ("$y", year), ("$c", cls), ("$n", Program.StudentName), ("$t", Program.Title));
        }

        public static void Consume(string dir)
        {
            var producer = File.ReadAllText(Path.Combine(dir, "producer.txt")).Split('\n')[0].Trim();
            Program.Record("CONTEXT", true, "producer=" + producer + " consumer=" + Program.Edition);
            using (var lmk = LocalMasterKey.FromBytes(Bytes.FromHex(Program.ScratchLmkHex)))
            {
                var work = Path.Combine(Path.GetTempPath(), "parvnegar-interop-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    var dbCopy = Path.Combine(work, "direct.db");
                    File.Copy(Path.Combine(dir, "parvnegar.db"), dbCopy);
                    Program.Check("DB_FORMAT_DIRECT_OPEN", () => VerifyDb(dbCopy, lmk.DatabaseKey()));
                    Program.Expect<DatabaseKeyException>("DB_WRONG_KEY_REJECTED", () => EncryptedDatabase.Open(dbCopy, CryptoRandom.Bytes(32)).Dispose());

                    Program.Check("ATTACHMENT_DECRYPT", () =>
                    {
                        using (var src = File.OpenRead(Path.Combine(dir, "blob.enc")))
                        using (var ms = new MemoryStream())
                        {
                            ChunkedAead.Decrypt(lmk.AttachmentKey(), 0, Bytes.Sha256(Encoding.ASCII.GetBytes("blob-header")), src, ms, 10 * 1024 * 1024);
                            if (!Bytes.FixedTimeEquals(Bytes.Sha256(ms.ToArray()), Bytes.Sha256(File.ReadAllBytes(Path.Combine(dir, "blob.plain"))))) throw new Exception("plaintext mismatch");
                            return ms.Length + " bytes, SHA-256 equal";
                        }
                    });

                    var arc = Path.Combine(dir, "backup.pnbk");
                    int schema = MigrationRunner.Embedded().Count;
                    Program.Check("BACKUP_RESTORE_AND_INTEGRITY", () =>
                    {
                        var stage = Path.Combine(work, "stage");
                        using (var info = BackupArchive.RestoreToStaging(arc, Program.ScratchPassphrase, stage, new BackupLimits { MaxSchemaVersion = schema }))
                        {
                            if (info.Entries.Any(e => e.Name.StartsWith("license", StringComparison.Ordinal) || e.Name.StartsWith("device", StringComparison.Ordinal)))
                                throw new Exception("backup carries a license or device secret");
                            if (Directory.Exists(Path.Combine(stage, "keys")) || Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Any(f => f.IndexOf("lmk", StringComparison.OrdinalIgnoreCase) >= 0))
                                throw new Exception("key material was written to the staging directory");
                            var restoredLmk = info.GetKeyEntry("keys/lmk");
                            try
                            {
                                using (var l2 = LocalMasterKey.FromBytes(restoredLmk))
                                    return "entries=" + info.Entries.Count + " schema=" + info.SchemaVersion + "; key entry in memory only; " + VerifyDb(Path.Combine(stage, "database", "parvnegar.db"), l2.DatabaseKey());
                            }
                            finally { Bytes.Wipe(restoredLmk); }
                        }
                    });

                    var raw = File.ReadAllBytes(arc);
                    Program.Expect<AuthenticationFailedException>("BACKUP_WRONG_PASSPHRASE", () => BackupArchive.RestoreToStaging(arc, Encoding.UTF8.GetBytes("wrong"), Path.Combine(work, "s1"), null));
                    Program.Expect<System.Security.Cryptography.CryptographicException>("BACKUP_TAMPER", () => RestoreBytes(work, "s2", Flip(raw, raw.Length / 2)));
                    Program.Expect<MalformedDataException>("BACKUP_TRUNCATED", () => RestoreBytes(work, "s3", raw.Take(raw.Length - 100).ToArray()));
                    var future = (byte[])raw.Clone(); future[6] = 0; future[7] = 9;
                    Program.Expect<UnsupportedBackupException>("BACKUP_UNSUPPORTED_FORMAT", () => RestoreBytes(work, "s4", future));
                    Program.Expect<UnsupportedBackupException>("BACKUP_UNSUPPORTED_SCHEMA", () => BackupArchive.RestoreToStaging(arc, Program.ScratchPassphrase, Path.Combine(work, "s5"), new BackupLimits { MaxSchemaVersion = schema - 1 }));
                    Program.Check("NO_STAGING_LEFT_AFTER_FAILURES", () =>
                    {
                        var left = new[] { "s1", "s2", "s3", "s4", "s5" }.Where(s => Directory.Exists(Path.Combine(work, s))).ToList();
                        if (left.Count > 0) throw new Exception("left: " + string.Join(",", left));
                        return "none";
                    });
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                    try { Directory.Delete(work, true); } catch (IOException) { }
                }
            }
        }

        private static string VerifyDb(string path, byte[] key)
        {
            using (var c = EncryptedDatabase.Open(path, key))
            {
                var title = Q(c, "SELECT home_title FROM school_profile;");
                if (!string.Equals(title, Program.Title, StringComparison.Ordinal)) throw new Exception("title mismatch");
                var name = Q(c, "SELECT snapshot_student_name FROM enrollment;");
                if (!string.Equals(name, Program.StudentName, StringComparison.Ordinal)) throw new Exception("student mismatch");
                var nid = Q(c, "SELECT national_id FROM student;");
                if (nid != "0000000011") throw new Exception("leading zeros lost");
                var ver = MigrationRunner.CurrentVersion(c);
                var ic = Q(c, "PRAGMA integrity_check;");
                if (ic != "ok") throw new Exception("integrity " + ic);
                return "title/student/leading-zero ID equal, schema " + ver + ", integrity ok";
            }
        }

        private static string Q(SqliteConnection c, string sql)
        {
            using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
        }

        private static void RestoreBytes(string work, string name, byte[] data)
        {
            var p = Path.Combine(work, name + ".pnbk");
            File.WriteAllBytes(p, data);
            BackupArchive.RestoreToStaging(p, Program.ScratchPassphrase, Path.Combine(work, name), null).Dispose();
        }

        private static byte[] Flip(byte[] b, int pos) { var c = (byte[])b.Clone(); c[pos] ^= 0x10; return c; }
    }

    /// <summary>Each check runs in its own child process, so a native crash in one cannot hide the others.</summary>
    internal static class Compat
    {
        private static readonly string[] Checks = { "env", "storage", "crypto", "dpapi", "argon2-64mib", "backup" };

        public static void Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            var sb = new StringBuilder();
            foreach (var id in Checks)
            {
                var psi = new ProcessStartInfo(exe, "--check " + id + " \"" + Path.Combine(outDir, "work-" + id) + "\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
                var sw = Stopwatch.StartNew();
                using (var p = Process.Start(psi))
                {
                    var stdout = p.StandardOutput.ReadToEnd();
                    var stderr = p.StandardError.ReadToEnd();
                    bool exited = p.WaitForExit(300000);
                    var line = stdout.Trim();
                    bool pass = exited && p.ExitCode == 0 && line.Length > 0;
                    Program.Record("COMPAT-" + id.ToUpperInvariant(), pass,
                        (pass ? line : "exit=" + (exited ? p.ExitCode.ToString(CultureInfo.InvariantCulture) : "timeout") + " " + line + " " + stderr.Trim()) + " (" + sw.ElapsedMilliseconds + " ms)");
                }
            }
        }

        public static int RunSingle(string id, string work)
        {
            Directory.CreateDirectory(work);
            string detail;
            switch (id)
            {
                case "env":
                    detail = "os=" + Environment.OSVersion.VersionString + " os64=" + Environment.Is64BitOperatingSystem + " proc=" + Program.Edition + " runtime=" + RuntimeInformation.FrameworkDescription + " cpus=" + Environment.ProcessorCount;
                    break;
                case "storage":
                    {
                        var db = Path.Combine(work, "c.db");
                        if (File.Exists(db)) File.Delete(db);
                        var key = CryptoRandom.Bytes(32);
                        using (var c = EncryptedDatabase.Create(db, key)) { MigrationRunner.Migrate(c, MigrationRunner.Embedded()); }
                        try { EncryptedDatabase.Open(db, CryptoRandom.Bytes(32)).Dispose(); throw new Exception("wrong key accepted"); } catch (DatabaseKeyException) { }
                        detail = EncryptedDatabase.InitializeProvider() + "; create/migrate/wrong-key ok";
                        break;
                    }
                case "crypto":
                    {
                        var ct = AesGcm256.Encrypt(Bytes.FromHex("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308"), Bytes.FromHex("cafebabefacedbaddecaf888"),
                            Bytes.FromHex("feedfacedeadbeeffeedfacedeadbeefabaddad2"),
                            Bytes.FromHex("d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39"));
                        if (!Bytes.ToHex(ct).EndsWith("76fc6ece0f4e1768cddf8853bb2d551b", StringComparison.Ordinal)) throw new Exception("GCM KAT mismatch");
                        var okm = Hkdf.DeriveKey(Enumerable.Repeat((byte)0x0b, 22).ToArray(), Bytes.FromHex("000102030405060708090a0b0c"), Bytes.FromHex("f0f1f2f3f4f5f6f7f8f9"), 42);
                        if (!Bytes.ToHex(okm).StartsWith("3cb25f25faacd57a", StringComparison.Ordinal)) throw new Exception("HKDF KAT mismatch");
                        detail = "AES-GCM and HKDF known answers ok";
                        break;
                    }
                case "dpapi":
                    {
                        var f = Path.Combine(work, "device.secret");
                        if (File.Exists(f)) File.Delete(f);
                        var inst = Guid.NewGuid();
                        var s = DeviceSecret.CreateFile(f, inst, new DpapiLocalMachineProtector());
                        if (!Bytes.FixedTimeEquals(s, DeviceSecret.LoadFile(f, inst, new DpapiLocalMachineProtector()))) throw new Exception("roundtrip mismatch");
                        detail = "DPAPI LocalMachine device secret roundtrip ok";
                        break;
                    }
                case "argon2-64mib":
                    {
                        var sw = Stopwatch.StartNew();
                        Argon2id.Derive(Encoding.UTF8.GetBytes("timing"), CryptoRandom.Bytes(16), Argon2idParameters.Default);
                        detail = "Argon2id m=64MiB t=3 p=1: " + sw.ElapsedMilliseconds + " ms; peak_private_bytes=" + PeakPrivate();
                        break;
                    }
                case "backup":
                    {
                        var src = Path.Combine(work, "src.bin"); var arc = Path.Combine(work, "b.pnbk"); var stage = Path.Combine(work, "stage");
                        foreach (var p in new[] { arc }) if (File.Exists(p)) File.Delete(p);
                        if (Directory.Exists(stage)) Directory.Delete(stage, true);
                        File.WriteAllBytes(src, CryptoRandom.Bytes(200000));
                        BackupArchive.Create(arc, new[] { new BackupEntrySource("data/x.bin", src) }, Encoding.UTF8.GetBytes("p"), 1, Program.Kdf);
                        BackupArchive.RestoreToStaging(arc, Encoding.UTF8.GetBytes("p"), stage, null).Dispose();
                        if (!Bytes.FixedTimeEquals(Bytes.Sha256(File.ReadAllBytes(src)), Bytes.Sha256(File.ReadAllBytes(Path.Combine(stage, "data", "x.bin"))))) throw new Exception("hash mismatch");
                        detail = "backup roundtrip ok";
                        break;
                    }
                default: throw new ArgumentException("unknown check " + id);
            }
            Console.WriteLine(detail);
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Pmc { public uint cb, PageFaultCount; public UIntPtr a, b, c, d, e, f, PagefileUsage, PeakPagefileUsage; }
        [DllImport("psapi.dll")] private static extern bool GetProcessMemoryInfo(IntPtr h, out Pmc c, uint size);
        private static ulong PeakPrivate()
        {
            var c = new Pmc { cb = (uint)Marshal.SizeOf(typeof(Pmc)) };
            return GetProcessMemoryInfo(Process.GetCurrentProcess().Handle, out c, c.cb) ? c.PeakPagefileUsage.ToUInt64() : 0;
        }
    }
}
