using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ParvNegar.Security;
using ParvNegar.Storage;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Storage.Tests
{
    public class BackupArchiveTests
    {
        private static readonly Argon2idParameters Fast = new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 2, 1);
        private static readonly byte[] Pass = Encoding.UTF8.GetBytes("synthetic backup passphrase ۱۲۳");

        public BackupArchiveTests() { TestEnvironment.AssertExpectedArchitecture(); }

        private static List<BackupEntrySource> MakeSources(string dir)
        {
            var files = new Dictionary<string, byte[]>
            {
                ["database/parvnegar.db"] = CryptoRandom.Bytes(300000),
                ["attachments/0001.blob"] = CryptoRandom.Bytes(ChunkedAead.ChunkSize),
                ["attachments/0002.blob"] = CryptoRandom.Bytes(1),
                ["attachments/0003.blob"] = new byte[0],
            };
            var list = new List<BackupEntrySource>();
            foreach (var kv in files)
            {
                var p = Path.Combine(dir, kv.Key.Replace('/', '_'));
                File.WriteAllBytes(p, kv.Value);
                list.Add(new BackupEntrySource(kv.Key, p));
            }
            return list;
        }

        [Fact]
        public void Roundtrip_restores_identical_files_and_names_are_encrypted()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var sources = MakeSources(src.Path);
                var arc = work.File("a.pnbk");
                BackupArchive.Create(arc, sources, Pass, 1, Fast);
                Assert.False(File.Exists(arc + ".partial"));
                var raw = File.ReadAllBytes(arc);
                Assert.True(raw.Length > 300000);
                foreach (var s in sources) Assert.True(Find(raw, Encoding.UTF8.GetBytes(s.Name)) < 0, "entry name visible: " + s.Name);

                using (var info = BackupArchive.RestoreToStaging(arc, Pass, work.File("stage"), new BackupLimits { MaxSchemaVersion = 1 }))
                {
                    Assert.Equal(1, info.SchemaVersion);
                    Assert.Equal(sources.Count, info.Entries.Count);
                    foreach (var s in sources)
                        Assert.Equal(Bytes.ToHex(Bytes.Sha256(File.ReadAllBytes(s.FilePath))),
                                     Bytes.ToHex(Bytes.Sha256(File.ReadAllBytes(Path.Combine(work.File("stage"), s.Name.Replace('/', Path.DirectorySeparatorChar))))));
                }
            }
        }

        [Fact]
        public void Wrong_passphrase_bit_flip_truncation_and_trailing_data_are_rejected_without_leftovers()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var arc = work.File("a.pnbk");
                BackupArchive.Create(arc, MakeSources(src.Path), Pass, 1, Fast);
                var raw = File.ReadAllBytes(arc);

                void Expect<T>(byte[] data, byte[] pass, string name) where T : Exception
                {
                    var p = work.File(name + ".pnbk");
                    File.WriteAllBytes(p, data);
                    var stage = work.File(name + "-stage");
                    Assert.ThrowsAny<T>(() => BackupArchive.RestoreToStaging(p, pass, stage, null));
                    Assert.False(Directory.Exists(stage), "staging directory must be removed after failure: " + name);
                }

                Expect<AuthenticationFailedException>(raw, Encoding.UTF8.GetBytes("wrong"), "wrongpass");
                foreach (var pos in new[] { 30, 70, 110, BackupArchive.HeaderLength + 3, raw.Length / 2, raw.Length - 3 })
                {
                    var f = (byte[])raw.Clone(); f[pos] ^= 0x20;
                    Expect<Exception>(f, Pass, "flip" + pos);
                }
                Expect<MalformedDataException>(raw.Take(raw.Length - 1000).ToArray(), Pass, "trunc");
                Expect<MalformedDataException>(raw.Take(50).ToArray(), Pass, "trunchdr");
                Expect<MalformedDataException>(raw.Concat(new byte[] { 1, 2, 3 }).ToArray(), Pass, "trailing");
            }
        }

        [Fact]
        public void Unsupported_format_and_newer_schema_are_rejected()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var arc = work.File("a.pnbk");
                BackupArchive.Create(arc, MakeSources(src.Path), Pass, 7, Fast);
                Assert.Throws<UnsupportedBackupException>(() => BackupArchive.RestoreToStaging(arc, Pass, work.File("s1"), new BackupLimits { MaxSchemaVersion = 6 }));
                var raw = File.ReadAllBytes(arc);
                var future = (byte[])raw.Clone(); future[6] = 0; future[7] = 2; // min reader version 2
                File.WriteAllBytes(work.File("f.pnbk"), future);
                Assert.Throws<UnsupportedBackupException>(() => BackupArchive.RestoreToStaging(work.File("f.pnbk"), Pass, work.File("s2"), null));
                var notOurs = (byte[])raw.Clone(); notOurs[0] = (byte)'X';
                File.WriteAllBytes(work.File("x.pnbk"), notOurs);
                Assert.Throws<MalformedDataException>(() => BackupArchive.RestoreToStaging(work.File("x.pnbk"), Pass, work.File("s3"), null));
                Assert.False(Directory.Exists(work.File("s1")) || Directory.Exists(work.File("s2")) || Directory.Exists(work.File("s3")));
            }
        }

        [Theory]
        [InlineData("license/device.lic")]
        [InlineData("device/secret")]
        [InlineData("secrets/x")]
        [InlineData("../escape")]
        [InlineData("a/../../b")]
        [InlineData("/abs")]
        [InlineData("Upper.txt")]
        [InlineData("a//b")]
        [InlineData("c:/x")]
        [InlineData("con")]
        [InlineData("data/nul.txt")]
        [InlineData("attachments/com1.blob")]
        [InlineData("a./b")]
        [InlineData("data/x.")]
        [InlineData("data/school.pnlic")]
        [InlineData("data/request.pnreq")]
        [InlineData("data/install.pnds")]
        public void Unsafe_or_forbidden_entry_names_are_refused(string name)
        {
            Assert.Throws<ArgumentException>(() => BackupArchive.ValidateName(name));
        }

        [Fact]
        public void Existing_archive_and_existing_staging_are_never_overwritten()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var arc = work.File("a.pnbk");
                var sources = MakeSources(src.Path);
                BackupArchive.Create(arc, sources, Pass, 1, Fast);
                Assert.Throws<IOException>(() => BackupArchive.Create(arc, sources, Pass, 1, Fast));
                Directory.CreateDirectory(work.File("stage"));
                Assert.Throws<IOException>(() => BackupArchive.RestoreToStaging(arc, Pass, work.File("stage"), null));
            }
        }

        [Fact]
        public void Kdf_parameters_from_archive_are_bounds_checked()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var arc = work.File("a.pnbk");
                BackupArchive.Create(arc, MakeSources(src.Path), Pass, 1, Fast);
                var raw = File.ReadAllBytes(arc);
                Bytes.WriteUInt32BE(raw, 21, 2u * 1024 * 1024); // 2 GiB memory request
                File.WriteAllBytes(work.File("b.pnbk"), raw);
                Assert.Throws<MalformedDataException>(() => BackupArchive.RestoreToStaging(work.File("b.pnbk"), Pass, work.File("s"), null));
            }
        }

        [Fact]
        public void Key_entries_round_trip_in_memory_and_never_reach_disk()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var key = CryptoRandom.Bytes(32);
                var sources = MakeSources(src.Path);
                var arc = work.File("k.pnbk");
                using (var keyEntry = BackupEntrySource.FromKeyMaterial("keys/lmk", key))
                {
                    sources.Add(keyEntry);
                    BackupArchive.Create(arc, sources, Pass, 1, Fast);
                }
                Assert.True(Find(File.ReadAllBytes(arc), key) < 0, "raw key visible in archive");
                Assert.DoesNotContain(Directory.GetFiles(src.Path), f => Find(File.ReadAllBytes(f), key) >= 0);

                var stage = work.File("stage");
                var info = BackupArchive.RestoreToStaging(arc, Pass, stage, null);
                using (info)
                {
                    Assert.Equal(new[] { "keys/lmk" }, info.KeyEntryNames.ToArray());
                    Assert.Equal(Bytes.ToHex(key), Bytes.ToHex(info.GetKeyEntry("keys/lmk")));
                    Assert.False(Directory.Exists(Path.Combine(stage, "keys")), "key entry written to staging");
                    foreach (var f in Directory.EnumerateFiles(work.Path, "*", SearchOption.AllDirectories))
                        Assert.True(Find(File.ReadAllBytes(f), key) < 0, "plaintext key found on disk: " + Path.GetFileName(f));
                }
                Assert.Throws<ObjectDisposedException>(() => info.GetKeyEntry("keys/lmk"));
            }
        }

        [Fact]
        public void Key_entries_cannot_come_from_files_and_file_entries_cannot_use_keys_prefix()
        {
            Assert.Throws<ArgumentException>(() => new BackupEntrySource("keys/lmk", "lmk.bin"));
            Assert.Throws<ArgumentException>(() => BackupEntrySource.FromKeyMaterial("data/x.bin", new byte[32]));
            Assert.Throws<ArgumentException>(() => BackupEntrySource.FromKeyMaterial("keys/big", new byte[BackupArchive.MaxKeyEntryBytes + 1]));
            Assert.Throws<ArgumentException>(() => BackupEntrySource.FromKeyMaterial("keys/empty", new byte[0]));
        }

        [Fact]
        public void Source_held_open_for_writing_is_refused_and_leaves_nothing()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var sources = MakeSources(src.Path);
                var arc = work.File("w.pnbk");
                using (new FileStream(sources[0].FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                    Assert.ThrowsAny<IOException>(() => BackupArchive.Create(arc, sources, Pass, 1, Fast));
                Assert.Empty(Directory.GetFiles(work.Path));
            }
        }

        [Fact]
        public void Sources_cannot_be_opened_for_writing_while_the_backup_is_written()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var sources = MakeSources(src.Path);
                var arc = work.File("m.pnbk");
                // Max-bound KDF keeps Create busy while every source is already open with FileShare.Read.
                var slow = new Argon2idParameters(Argon2idParameters.MaxMemoryKiB, Argon2idParameters.MaxIterations, 1);
                var create = System.Threading.Tasks.Task.Run(() => BackupArchive.Create(arc, sources, Pass, 1, slow));
                Exception writerError = null;
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (writerError == null && !create.IsCompleted && DateTime.UtcNow < deadline)
                {
                    try { using (new FileStream(sources[0].FilePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } System.Threading.Thread.Sleep(5); }
                    catch (IOException ex) { writerError = ex; }
                }
                create.Wait();
                Assert.NotNull(writerError);
                using (var info = BackupArchive.RestoreToStaging(arc, Pass, work.File("stage"), null))
                    Assert.Equal(sources.Count, info.Entries.Count);
            }
        }

        [Fact]
        public void Unrelated_partial_files_are_never_deleted_and_archive_cannot_be_its_own_source()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                var sources = MakeSources(src.Path);
                var arc = work.File("p.pnbk");
                File.WriteAllText(arc + ".partial", "user file");
                BackupArchive.Create(arc, sources, Pass, 1, Fast);
                Assert.Equal("user file", File.ReadAllText(arc + ".partial"));
                Assert.Equal(2, Directory.GetFiles(work.Path).Length);

                var self = src.File("self.pnbk");
                var withSelf = new List<BackupEntrySource>(sources) { new BackupEntrySource("data/self.bin", self) };
                Assert.Throws<ArgumentException>(() => BackupArchive.Create(self, withSelf, Pass, 1, Fast));
                Assert.Throws<DirectoryNotFoundException>(() => BackupArchive.Create(work.File(Path.Combine("missing", "x.pnbk")), sources, Pass, 1, Fast));
            }
        }

        [Fact]
        public void Conflicting_entry_names_are_refused()
        {
            using (var src = new TempDir("bk-src"))
            using (var work = new TempDir("bk-work"))
            {
                File.WriteAllBytes(src.File("a"), new byte[] { 1 });
                File.WriteAllBytes(src.File("b"), new byte[] { 2 });
                var conflict = new[] { new BackupEntrySource("data", src.File("a")), new BackupEntrySource("data/x.bin", src.File("b")) };
                Assert.Throws<ArgumentException>(() => BackupArchive.Create(work.File("c.pnbk"), conflict, Pass, 1, Fast));
                var dup = new[] { new BackupEntrySource("data/x.bin", src.File("a")), new BackupEntrySource("data/x.bin", src.File("b")) };
                Assert.Throws<ArgumentException>(() => BackupArchive.Create(work.File("d.pnbk"), dup, Pass, 1, Fast));
                Assert.Empty(Directory.GetFiles(work.Path));
            }
        }

        [Fact]
        public void Restore_requires_existing_parent_and_missing_archive_creates_nothing()
        {
            using (var work = new TempDir("bk-work"))
            {
                Assert.Throws<DirectoryNotFoundException>(() => BackupArchive.RestoreToStaging(work.File("none.pnbk"), Pass, Path.Combine(work.File("missing"), "stage"), null));
                Assert.Throws<FileNotFoundException>(() => BackupArchive.RestoreToStaging(work.File("none.pnbk"), Pass, work.File("stage"), null));
                Assert.False(Directory.Exists(work.File("missing")) || Directory.Exists(work.File("stage")));
            }
        }

        [Fact]
        public void Key_only_archive_restores_with_empty_staging_and_format_v1()
        {
            // The PNBK v1 wire format is unchanged: key entries are ordinary encrypted entries;
            // only the reader decides to keep keys/... in memory.
            using (var work = new TempDir("bk-work"))
            {
                var key = CryptoRandom.Bytes(32);
                var arc = work.File("v1.pnbk");
                using (var keyEntry = BackupEntrySource.FromKeyMaterial("keys/lmk", key))
                    BackupArchive.Create(arc, new[] { keyEntry }, Pass, 1, Fast);
                Assert.Equal(1, BackupArchive.ReadHeaderInfo(arc).FormatVersion);
                using (var info = BackupArchive.RestoreToStaging(arc, Pass, work.File("s"), null))
                    Assert.Equal(Bytes.ToHex(key), Bytes.ToHex(info.GetKeyEntry("keys/lmk")));
                Assert.Empty(Directory.GetFileSystemEntries(work.File("s")));
            }
        }

        private static int Find(byte[] h, byte[] n)
        {
            for (int i = 0; i <= h.Length - n.Length; i++) { int j = 0; while (j < n.Length && h[i + j] == n[j]) j++; if (j == n.Length) return i; }
            return -1;
        }
    }
}
