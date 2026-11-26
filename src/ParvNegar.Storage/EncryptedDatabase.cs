using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;
using ParvNegar.Security;

namespace ParvNegar.Storage
{
    public sealed class DatabaseKeyException : Exception
    {
        public DatabaseKeyException() : base("The database could not be opened with the supplied key.") { }
    }

    public sealed class StorageSecurityException : Exception
    {
        public StorageSecurityException(string message) : base(message) { }
    }

    /// <summary>
    /// Fixed SQLite3 Multiple Ciphers profile (ADR-P1-02, STORAGE-FORMAT.md §1). Every value is set
    /// explicitly so a library default change cannot silently change the on-disk format.
    /// </summary>
    public static class CipherProfile
    {
        public const string Cipher = "chacha20";
        public const int KdfIterations = 64007;
        public const int Legacy = 0;
        public const int KeyLength = 32;
    }

    /// <summary>
    /// Opens or creates the encrypted database. Guarantees:
    /// * the key never appears in the connection string, exceptions or logs;
    /// * a failed encryption setup never creates or opens a plaintext database;
    /// * pooling is off, so closing a connection releases the keyed handle (session lock).
    /// </summary>
    public static class EncryptedDatabase
    {
        private static readonly object InitLock = new object();
        private static bool _initialized;
        private static readonly byte[] PlainHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

        private static string _providerVersion;

        /// <summary>
        /// Loads the native provider once and proves it is SQLite3 Multiple Ciphers by calling
        /// sqlite3mc_version() on an in-memory database. Any other SQLite build is refused.
        /// </summary>
        public static string InitializeProvider()
        {
            lock (InitLock)
            {
                if (_initialized) return Verified();
                _initialized = true;
                SQLitePCL.Batteries_V2.Init();
                try
                {
                    using (var c = new SqliteConnection("Data Source=:memory:;Pooling=False"))
                    {
                        c.Open();
                        _providerVersion = Scalar(c, "SELECT sqlite3mc_version();");
                    }
                }
                catch (SqliteException ex)
                {
                    _providerVersion = null;
                    throw new StorageSecurityException("SQLite3 Multiple Ciphers is not loaded; refusing to continue without encryption (" + ex.SqliteErrorCode + ").");
                }
                return Verified();
            }
        }

        private static string Verified()
        {
            if (string.IsNullOrEmpty(_providerVersion) || !_providerVersion.StartsWith("SQLite3 Multiple Ciphers", StringComparison.Ordinal))
                throw new StorageSecurityException("SQLite3 Multiple Ciphers is not loaded; refusing to continue without encryption.");
            return _providerVersion;
        }

        /// <summary>Creates a new encrypted database. Fails if the file exists.</summary>
        public static SqliteConnection Create(string path, byte[] key)
        {
            ValidateKey(key);
            path = Path.GetFullPath(path);
            if (File.Exists(path)) throw new IOException("Database already exists; refusing to overwrite.");
            InitializeProvider();

            var staging = path + ".creating";
            DeleteIfExists(staging);
            try
            {
                using (var c = OpenRaw(staging, SqliteOpenMode.ReadWriteCreate))
                {
                    ApplyKey(c, key);
                    Exec(c, "PRAGMA journal_mode=DELETE;");
                    Exec(c, "CREATE TABLE parvnegar_format(id INTEGER PRIMARY KEY CHECK (id = 1), format INTEGER NOT NULL) STRICT;");
                    Exec(c, "INSERT INTO parvnegar_format(id, format) VALUES (1, 1);");
                }
                SqliteConnection.ClearAllPools();
                VerifyEncryptedHeader(staging);
                File.Move(staging, path);
            }
            catch
            {
                DeleteIfExists(staging);
                throw;
            }
            return Open(path, key);
        }

        /// <summary>Opens an existing encrypted database and applies the session pragmas.</summary>
        public static SqliteConnection Open(string path, byte[] key)
        {
            ValidateKey(key);
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Database not found.");
            VerifyEncryptedHeader(path);
            InitializeProvider();

            var c = OpenRaw(path, SqliteOpenMode.ReadWrite);
            try
            {
                ApplyKey(c, key);
                Exec(c, "PRAGMA journal_mode=WAL;");
                Exec(c, "PRAGMA synchronous=FULL;");
                Exec(c, "PRAGMA temp_store=MEMORY;");
                Exec(c, "PRAGMA foreign_keys=ON;");
                Exec(c, "PRAGMA secure_delete=ON;");
                Expect(c, "PRAGMA journal_mode;", "wal");
                Expect(c, "PRAGMA temp_store;", "2");
                Expect(c, "PRAGMA foreign_keys;", "1");
                return c;
            }
            catch
            {
                c.Dispose();
                throw;
            }
        }

        /// <summary>Changes the database key in place (LMK rotation). The connection must be open with the old key.</summary>
        public static void Rekey(SqliteConnection c, byte[] newKey)
        {
            ValidateKey(newKey);
            Exec(c, "PRAGMA journal_mode=DELETE;");
            var hex = Bytes.ToHex(newKey);
            try { Exec(c, "PRAGMA hexrekey='" + hex + "';"); }
            finally { hex = null; }
            Exec(c, "PRAGMA journal_mode=WAL;");
        }

        /// <summary>Flushes the WAL into the main file (before copying the file into a backup).</summary>
        public static void Checkpoint(SqliteConnection c) => Exec(c, "PRAGMA wal_checkpoint(TRUNCATE);");

        public static string ConnectionStringFor(string path, SqliteOpenMode mode) =>
            new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, Cache = SqliteCacheMode.Private }.ToString();

        private static SqliteConnection OpenRaw(string path, SqliteOpenMode mode)
        {
            var c = new SqliteConnection(ConnectionStringFor(path, mode));
            c.Open();
            return c;
        }

        private static void ApplyKey(SqliteConnection c, byte[] key)
        {
            InitializeProvider(); // proven SQLite3MC before any key is sent
            Expect(c, "PRAGMA cipher='" + CipherProfile.Cipher + "';", CipherProfile.Cipher);
            Expect(c, "PRAGMA kdf_iter=" + CipherProfile.KdfIterations.ToString(CultureInfo.InvariantCulture) + ";", CipherProfile.KdfIterations.ToString(CultureInfo.InvariantCulture));
            Expect(c, "PRAGMA legacy=" + CipherProfile.Legacy.ToString(CultureInfo.InvariantCulture) + ";", CipherProfile.Legacy.ToString(CultureInfo.InvariantCulture));

            var hex = Bytes.ToHex(key);
            try
            {
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA hexkey='" + hex + "';";
                    var r = Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                    if (!string.Equals(r, "ok", StringComparison.OrdinalIgnoreCase)) throw new StorageSecurityException("Encryption key was not accepted by the cipher layer.");
                    cmd.CommandText = string.Empty;
                }
            }
            finally { hex = null; }

            // Reading the schema forces page 1 decryption: a wrong key fails here, never later.
            try { Scalar(c, "SELECT count(*) FROM sqlite_master;"); }
            catch (SqliteException) { throw new DatabaseKeyException(); }
        }

        private static void VerifyEncryptedHeader(string path)
        {
            var head = new byte[16];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length < 512) throw new StorageSecurityException("Database file is too small to be a valid encrypted database.");
                if (fs.Read(head, 0, 16) != 16) throw new StorageSecurityException("Unreadable database header.");
            }
            if (Bytes.FixedTimeEquals(head, PlainHeader)) throw new StorageSecurityException("Database file is NOT encrypted; refusing to open it.");
        }

        private static void ValidateKey(byte[] key)
        {
            if (key == null || key.Length != CipherProfile.KeyLength) throw new ArgumentException("A 256-bit database key is required.", nameof(key));
        }

        internal static void Exec(SqliteConnection c, string sql, SqliteTransaction tx = null)
        {
            using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.Transaction = tx; cmd.ExecuteNonQuery(); }
        }

        internal static string Scalar(SqliteConnection c, string sql, SqliteTransaction tx = null)
        {
            using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.Transaction = tx; return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
        }

        private static void Expect(SqliteConnection c, string sql, string expected)
        {
            var v = Scalar(c, sql);
            if (!string.Equals(v, expected, StringComparison.OrdinalIgnoreCase))
                throw new StorageSecurityException("Unexpected storage configuration for " + sql.Split('=')[0].Trim() + " (got '" + v + "').");
        }

        private static void DeleteIfExists(string p)
        {
            foreach (var f in new[] { p, p + "-journal", p + "-wal", p + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
