using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using ParvNegar.Security;

namespace ParvNegar.Storage
{
    public sealed class Migration
    {
        public Migration(int version, string name, string sql)
        {
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            Version = version;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Sql = sql ?? throw new ArgumentNullException(nameof(sql));
            // Checksum over the exact UTF-8 bytes with line endings normalized to LF, so a Git
            // autocrlf checkout produces the same value on every machine.
            Sha256 = Bytes.ToHex(Bytes.Sha256(Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n"))));
        }

        public int Version { get; }
        public string Name { get; }
        public string Sql { get; }
        public string Sha256 { get; }
    }

    public sealed class MigrationException : Exception
    {
        public MigrationException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Versioned, transactional migrations. Each migration runs in one BEGIN IMMEDIATE transaction
    /// together with its schema_migrations row and PRAGMA user_version update, so a failure leaves
    /// the database at the previous version. Applied migrations are verified by SHA-256; a changed
    /// or unknown (newer) migration stops startup.
    /// </summary>
    public static class MigrationRunner
    {
        public static IReadOnlyList<Migration> Embedded()
        {
            var asm = typeof(MigrationRunner).GetTypeInfo().Assembly;
            const string prefix = "ParvNegar.Storage.Migrations.";
            var list = new List<Migration>();
            foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal)))
            {
                var file = res.Substring(prefix.Length);
                var us = file.IndexOf('_');
                if (us != 4) throw new MigrationException("Migration name must be NNNN_name.sql: " + file);
                int v = int.Parse(file.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture);
                using (var s = asm.GetManifestResourceStream(res))
                using (var r = new StreamReader(s, new UTF8Encoding(false)))
                    list.Add(new Migration(v, file.Substring(5, file.Length - 9), r.ReadToEnd()));
            }
            return Validate(list);
        }

        public static IReadOnlyList<Migration> Validate(IEnumerable<Migration> migrations)
        {
            var ordered = migrations.OrderBy(m => m.Version).ToList();
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i].Version != i + 1) throw new MigrationException("Migrations must be numbered 1..N without gaps.");
            return ordered;
        }

        public static int CurrentVersion(SqliteConnection c)
        {
            EnsureTable(c);
            var v = EncryptedDatabase.Scalar(c, "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
            return int.Parse(v, CultureInfo.InvariantCulture);
        }

        /// <summary>Applies pending migrations. Returns the number applied.</summary>
        public static int Migrate(SqliteConnection c, IReadOnlyList<Migration> migrations)
        {
            migrations = Validate(migrations);
            EnsureTable(c);
            var applied = new Dictionary<int, string>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT version, sha256 FROM schema_migrations ORDER BY version;";
                using (var r = cmd.ExecuteReader()) while (r.Read()) applied[r.GetInt32(0)] = r.GetString(1);
            }

            int known = migrations.Count;
            foreach (var kv in applied)
            {
                if (kv.Key > known) throw new MigrationException("Database schema version " + kv.Key + " is newer than this application supports (" + known + ").");
                if (!string.Equals(migrations[kv.Key - 1].Sha256, kv.Value, StringComparison.Ordinal))
                    throw new MigrationException("Checksum mismatch for applied migration " + kv.Key + ".");
            }

            int count = 0;
            foreach (var m in migrations.Where(x => !applied.ContainsKey(x.Version)))
            {
                using (var tx = c.BeginTransaction(deferred: false))
                {
                    try
                    {
                        EncryptedDatabase.Exec(c, m.Sql, tx);
                        using (var cmd = c.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "INSERT INTO schema_migrations(version, name, sha256, applied_utc) VALUES ($v, $n, $s, $t);";
                            cmd.Parameters.AddWithValue("$v", m.Version);
                            cmd.Parameters.AddWithValue("$n", m.Name);
                            cmd.Parameters.AddWithValue("$s", m.Sha256);
                            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                            cmd.ExecuteNonQuery();
                        }
                        EncryptedDatabase.Exec(c, "PRAGMA user_version=" + m.Version.ToString(CultureInfo.InvariantCulture) + ";", tx);
                        var fk = EncryptedDatabase.Scalar(c, "SELECT count(*) FROM pragma_foreign_key_check;", tx);
                        if (fk != "0") throw new MigrationException("Foreign key check failed after migration " + m.Version + ".");
                        tx.Commit();
                        count++;
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        if (ex is MigrationException) throw;
                        throw new MigrationException("Migration " + m.Version + " (" + m.Name + ") failed and was rolled back.", ex);
                    }
                }
            }
            return count;
        }

        private static void EnsureTable(SqliteConnection c)
        {
            EncryptedDatabase.Exec(c,
                "CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY NOT NULL, name TEXT NOT NULL, sha256 TEXT NOT NULL, applied_utc TEXT NOT NULL) STRICT;");
        }
    }
}
