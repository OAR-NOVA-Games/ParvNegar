using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;
using ParvNegar.Security;
using ParvNegar.Storage;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Storage.Tests
{
    public class EncryptedDatabaseTests
    {
        private const string Canary = "PARVNEGAR_CANARY_SYNTHETIC_7f3a";
        private const string PersianCanary = "پرونگار-نمونه-آزمایشی";

        public EncryptedDatabaseTests() { TestEnvironment.AssertExpectedArchitecture(); }

        private static void InsertCanaries(SqliteConnection c, int rows, SqliteTransaction tx = null)
        {
            EncryptedDatabase.Exec(c, "CREATE TABLE IF NOT EXISTS t(id INTEGER PRIMARY KEY, v TEXT NOT NULL, b BLOB);", tx);
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO t(v, b) VALUES ($v, randomblob(200));";
                var p = cmd.Parameters.Add("$v", SqliteType.Text);
                for (int i = 0; i < rows; i++) { p.Value = Canary + " " + PersianCanary + " " + i; cmd.ExecuteNonQuery(); }
            }
        }

        private static List<string> ScanForPlaintext(string dir)
        {
            var hits = new List<string>();
            var patterns = new[] { Encoding.UTF8.GetBytes(Canary), Encoding.Unicode.GetBytes(Canary), Encoding.UTF8.GetBytes(PersianCanary), Encoding.ASCII.GetBytes("SQLite format 3") };
            foreach (var f in Directory.GetFiles(dir))
            {
                byte[] b;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                { b = new byte[fs.Length]; ChunkedAead.ReadFull(fs, b); }
                foreach (var p in patterns) if (IndexOf(b, p) >= 0) hits.Add(Path.GetFileName(f) + ":" + Encoding.UTF8.GetString(p));
            }
            return hits;
        }

        private static int IndexOf(byte[] h, byte[] n)
        {
            for (int i = 0; i <= h.Length - n.Length; i++) { int j = 0; while (j < n.Length && h[i + j] == n[j]) j++; if (j == n.Length) return i; }
            return -1;
        }

        [Fact]
        public void Create_open_with_correct_key_reads_data()
        {
            using (var d = new TempDir("db"))
            {
                var key = CryptoRandom.Bytes(32);
                using (var c = EncryptedDatabase.Create(d.File("a.db"), key)) InsertCanaries(c, 10);
                using (var c = EncryptedDatabase.Open(d.File("a.db"), key))
                {
                    Assert.Equal("10", EncryptedDatabase.Scalar(c, "SELECT count(*) FROM t;"));
                    Assert.Equal("chacha20", EncryptedDatabase.Scalar(c, "PRAGMA cipher;"));
                    Assert.Equal("wal", EncryptedDatabase.Scalar(c, "PRAGMA journal_mode;"));
                    Assert.Equal("2", EncryptedDatabase.Scalar(c, "PRAGMA temp_store;"));
                    Assert.Equal("1", EncryptedDatabase.Scalar(c, "PRAGMA foreign_keys;"));
                }
            }
        }

        [Fact]
        public void Wrong_key_is_rejected_and_file_is_unchanged()
        {
            using (var d = new TempDir("db"))
            {
                var key = CryptoRandom.Bytes(32);
                using (var c = EncryptedDatabase.Create(d.File("a.db"), key)) InsertCanaries(c, 3);
                SqliteConnection.ClearAllPools();
                var before = Bytes.ToHex(Bytes.Sha256(File.ReadAllBytes(d.File("a.db"))));
                Assert.Throws<DatabaseKeyException>(() => EncryptedDatabase.Open(d.File("a.db"), CryptoRandom.Bytes(32)));
                Assert.Equal(before, Bytes.ToHex(Bytes.Sha256(File.ReadAllBytes(d.File("a.db")))));
            }
        }

        [Fact]
        public void Missing_or_short_key_is_refused_before_any_file_access()
        {
            using (var d = new TempDir("db"))
            {
                Assert.Throws<ArgumentException>(() => EncryptedDatabase.Create(d.File("a.db"), null));
                Assert.Throws<ArgumentException>(() => EncryptedDatabase.Create(d.File("a.db"), new byte[16]));
                Assert.False(File.Exists(d.File("a.db")));
                Assert.Empty(Directory.GetFiles(d.Path));
            }
        }

        [Fact]
        public void Opening_without_a_key_through_plain_sqlite_fails()
        {
            using (var d = new TempDir("db"))
            {
                using (var c = EncryptedDatabase.Create(d.File("a.db"), CryptoRandom.Bytes(32))) InsertCanaries(c, 3);
                using (var c = new SqliteConnection(EncryptedDatabase.ConnectionStringFor(d.File("a.db"), SqliteOpenMode.ReadOnly)))
                {
                    c.Open();
                    Assert.Throws<SqliteException>(() => EncryptedDatabase.Scalar(c, "SELECT count(*) FROM sqlite_master;"));
                }
            }
        }

        [Fact]
        public void Plaintext_database_is_never_opened()
        {
            using (var d = new TempDir("db"))
            {
                var path = d.File("plain.db");
                EncryptedDatabase.InitializeProvider();
                using (var c = new SqliteConnection(EncryptedDatabase.ConnectionStringFor(path, SqliteOpenMode.ReadWriteCreate)))
                { c.Open(); EncryptedDatabase.Exec(c, "CREATE TABLE x(a); INSERT INTO x VALUES (1);"); }
                SqliteConnection.ClearAllPools();
                Assert.Throws<StorageSecurityException>(() => EncryptedDatabase.Open(path, CryptoRandom.Bytes(32)));
            }
        }

        [Fact]
        public void Create_refuses_to_overwrite_and_leaves_no_staging_file()
        {
            using (var d = new TempDir("db"))
            {
                var key = CryptoRandom.Bytes(32);
                using (EncryptedDatabase.Create(d.File("a.db"), key)) { }
                Assert.Throws<IOException>(() => EncryptedDatabase.Create(d.File("a.db"), key));
                Assert.False(File.Exists(d.File("a.db.creating")));
            }
        }

        [Fact]
        public void Header_is_not_the_plain_sqlite_header()
        {
            using (var d = new TempDir("db"))
            {
                using (EncryptedDatabase.Create(d.File("a.db"), CryptoRandom.Bytes(32))) { }
                var head = File.ReadAllBytes(d.File("a.db")).Take(16).ToArray();
                Assert.NotEqual("SQLite format 3\0", Encoding.ASCII.GetString(head));
            }
        }

        [Fact]
        public void Db_wal_shm_and_journal_contain_no_plaintext()
        {
            using (var d = new TempDir("db"))
            {
                var key = CryptoRandom.Bytes(32);
                using (var c = EncryptedDatabase.Create(d.File("a.db"), key))
                {
                    using (var tx = c.BeginTransaction()) { InsertCanaries(c, 300, tx); tx.Commit(); }
                    Assert.True(new FileInfo(d.File("a.db-wal")).Length > 0, "WAL must hold uncheckpointed pages during the scan");
                    Assert.Empty(ScanForPlaintext(d.Path));
                    EncryptedDatabase.Checkpoint(c);
                    EncryptedDatabase.Exec(c, "PRAGMA journal_mode=DELETE;");
                    using (var tx = c.BeginTransaction())
                    {
                        EncryptedDatabase.Exec(c, "UPDATE t SET v = v || ' J';", tx);
                        EncryptedDatabase.Exec(c, "INSERT INTO t(v, b) SELECT v, b FROM t;", tx);
                        Assert.True(File.Exists(d.File("a.db-journal")), "rollback journal must exist during the scan");
                        Assert.Empty(ScanForPlaintext(d.Path));
                        tx.Commit();
                    }
                }
                Assert.Empty(ScanForPlaintext(d.Path));
            }
        }

        [Fact]
        public void Large_sort_creates_no_temporary_files_with_temp_store_memory()
        {
            using (var d = new TempDir("db"))
            {
                var key = CryptoRandom.Bytes(32);
                var tmp = Path.GetTempPath();
                using (var c = EncryptedDatabase.Create(d.File("a.db"), key))
                {
                    using (var tx = c.BeginTransaction()) { InsertCanaries(c, 20000, tx); tx.Commit(); }
                    EncryptedDatabase.Exec(c, "PRAGMA cache_size=-256;"); // 256 KiB cache forces spill for the sort

                    int Created(string store)
                    {
                        EncryptedDatabase.Exec(c, "PRAGMA temp_store=" + store + ";");
                        var created = new List<string>();
                        using (var w = new FileSystemWatcher(tmp, "etilqs_*") { IncludeSubdirectories = false, EnableRaisingEvents = true })
                        {
                            w.Created += (s, e) => { lock (created) created.Add(e.Name); };
                            using (var cmd = c.CreateCommand())
                            {
                                cmd.CommandText = "SELECT v, b FROM t ORDER BY b, v;";
                                using (var r = cmd.ExecuteReader()) while (r.Read()) { }
                            }
                            Thread.Sleep(300);
                        }
                        lock (created) return created.Count;
                    }

                    int fileMode = Created("FILE");
                    int memoryMode = Created("MEMORY");
                    Assert.True(fileMode > 0, "control: with temp_store=FILE the sort must spill to a temp file, otherwise the test proves nothing");
                    Assert.Equal(0, memoryMode);
                    Assert.Equal("2", EncryptedDatabase.Scalar(c, "PRAGMA temp_store;"));
                }
            }
        }

        [Fact]
        public void Tampered_encrypted_page_is_detected()
        {
            using (var d = new TempDir("db"))
            {
                var key = CryptoRandom.Bytes(32);
                using (var c = EncryptedDatabase.Create(d.File("a.db"), key))
                {
                    InsertCanaries(c, 200);
                    EncryptedDatabase.Checkpoint(c);
                }
                SqliteConnection.ClearAllPools();
                var bytes = File.ReadAllBytes(d.File("a.db"));
                Assert.True(bytes.Length >= 4096 * 4);
                bytes[4096 * 3 + 100] ^= 0x01; // inside page 4
                File.WriteAllBytes(d.File("a.db"), bytes);
                using (var c = EncryptedDatabase.Open(d.File("a.db"), key))
                {
                    var ex = Assert.Throws<SqliteException>(() =>
                    {
                        using (var cmd = c.CreateCommand())
                        {
                            cmd.CommandText = "SELECT v, b FROM t;";
                            using (var r = cmd.ExecuteReader()) while (r.Read()) { }
                        }
                    });
                    Assert.True(ex.SqliteErrorCode == 11 || ex.SqliteErrorCode == 26, "expected SQLITE_CORRUPT or SQLITE_NOTADB, got " + ex.SqliteErrorCode);
                }
            }
        }

        [Fact]
        public void Interrupted_transaction_is_rolled_back_from_a_crash_image()
        {
            using (var d = new TempDir("db"))
            using (var crash = new TempDir("crash"))
            {
                var key = CryptoRandom.Bytes(32);
                using (var c = EncryptedDatabase.Create(d.File("a.db"), key)) { InsertCanaries(c, 5); EncryptedDatabase.Checkpoint(c); }
                using (var c = EncryptedDatabase.Open(d.File("a.db"), key))
                using (var tx = c.BeginTransaction())
                {
                    InsertCanaries(c, 500, tx);
                    // Copy the files while the transaction is uncommitted: this is the on-disk state after a power loss.
                    // The -shm index is rebuilt on open and is byte-range locked, so it is not part of the crash image.
                    foreach (var f in Directory.GetFiles(d.Path).Where(x => !x.EndsWith("-shm", StringComparison.Ordinal)))
                        using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var dst = File.Create(Path.Combine(crash.Path, Path.GetFileName(f)))) src.CopyTo(dst);
                    tx.Rollback();
                }
                using (var c = EncryptedDatabase.Open(crash.File("a.db"), key))
                {
                    Assert.Equal("5", EncryptedDatabase.Scalar(c, "SELECT count(*) FROM t;"));
                    Assert.Equal("ok", EncryptedDatabase.Scalar(c, "PRAGMA integrity_check;"));
                }
            }
        }

        [Fact]
        public void Closing_releases_the_keyed_handle_no_pooling()
        {
            using (var d = new TempDir("db"))
            {
                var key = CryptoRandom.Bytes(32);
                var cs = EncryptedDatabase.ConnectionStringFor(d.File("a.db"), SqliteOpenMode.ReadWrite);
                Assert.Contains("Pooling=False", cs, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("password", cs, StringComparison.OrdinalIgnoreCase);
                using (var c = EncryptedDatabase.Create(d.File("a.db"), key)) InsertCanaries(c, 3);
                // No pooled handle may keep the file open after dispose: the session-lock path relies on this.
                File.Move(d.File("a.db"), d.File("b.db"));
                Assert.True(File.Exists(d.File("b.db")));
            }
        }

        [Fact]
        public void Rekey_changes_the_key()
        {
            using (var d = new TempDir("db"))
            {
                var k1 = CryptoRandom.Bytes(32); var k2 = CryptoRandom.Bytes(32);
                using (var c = EncryptedDatabase.Create(d.File("a.db"), k1)) { InsertCanaries(c, 20); EncryptedDatabase.Rekey(c, k2); }
                Assert.Throws<DatabaseKeyException>(() => EncryptedDatabase.Open(d.File("a.db"), k1));
                using (var c = EncryptedDatabase.Open(d.File("a.db"), k2)) Assert.Equal("20", EncryptedDatabase.Scalar(c, "SELECT count(*) FROM t;"));
            }
        }

        [Fact]
        public void Loaded_native_sqlite3mc_matches_process_architecture()
        {
            EncryptedDatabase.InitializeProvider();
            var mod = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .FirstOrDefault(m => m.ModuleName.Equals("sqlite3mc.dll", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(mod);
            var b = File.ReadAllBytes(mod.FileName);
            int pe = BitConverter.ToInt32(b, 0x3c);
            ushort machine = BitConverter.ToUInt16(b, pe + 4);
            Assert.Equal(Environment.Is64BitProcess ? (ushort)0x8664 : (ushort)0x14c, machine);
        }
    }
}
