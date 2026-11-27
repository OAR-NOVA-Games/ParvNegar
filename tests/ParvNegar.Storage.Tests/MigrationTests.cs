using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using ParvNegar.Security;
using ParvNegar.Storage;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Storage.Tests
{
    public class MigrationTests
    {
        public MigrationTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Embedded_migrations_apply_once_and_are_idempotent()
        {
            using (var d = new TempDir("mig"))
            using (var c = EncryptedDatabase.Create(d.File("a.db"), CryptoRandom.Bytes(32)))
            {
                var all = MigrationRunner.Embedded();
                Assert.True(all.Count >= 1);
                Assert.Equal(all.Count, MigrationRunner.Migrate(c, all));
                Assert.Equal(0, MigrationRunner.Migrate(c, all));
                Assert.Equal(all.Count, MigrationRunner.CurrentVersion(c));
                Assert.Equal(all.Count.ToString(), EncryptedDatabase.Scalar(c, "PRAGMA user_version;"));
                foreach (var t in new[] { "school_profile", "academic_year", "classroom", "student", "enrollment" })
                    Assert.Equal("1", EncryptedDatabase.Scalar(c, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='" + t + "';"));
            }
        }

        [Fact]
        public void Failed_migration_rolls_back_completely()
        {
            using (var d = new TempDir("mig"))
            using (var c = EncryptedDatabase.Create(d.File("a.db"), CryptoRandom.Bytes(32)))
            {
                var list = MigrationRunner.Embedded().ToList();
                MigrationRunner.Migrate(c, list);
                int v = list.Count;
                list.Add(new Migration(v + 1, "broken", "CREATE TABLE partial_one(a INTEGER); INSERT INTO no_such_table VALUES (1);"));
                var ex = Assert.Throws<MigrationException>(() => MigrationRunner.Migrate(c, list));
                Assert.Contains("rolled back", ex.Message);
                Assert.Equal(v, MigrationRunner.CurrentVersion(c));
                Assert.Equal(v.ToString(), EncryptedDatabase.Scalar(c, "PRAGMA user_version;"));
                Assert.Equal("0", EncryptedDatabase.Scalar(c, "SELECT count(*) FROM sqlite_master WHERE name='partial_one';"));
            }
        }

        [Fact]
        public void Changed_applied_migration_is_detected()
        {
            using (var d = new TempDir("mig"))
            using (var c = EncryptedDatabase.Create(d.File("a.db"), CryptoRandom.Bytes(32)))
            {
                var list = MigrationRunner.Embedded().ToList();
                MigrationRunner.Migrate(c, list);
                list[0] = new Migration(1, list[0].Name, list[0].Sql + "\n-- edited after release\n");
                var ex = Assert.Throws<MigrationException>(() => MigrationRunner.Migrate(c, list));
                Assert.Contains("Checksum mismatch", ex.Message);
            }
        }

        [Fact]
        public void Database_newer_than_application_is_refused()
        {
            using (var d = new TempDir("mig"))
            using (var c = EncryptedDatabase.Create(d.File("a.db"), CryptoRandom.Bytes(32)))
            {
                var list = MigrationRunner.Embedded().ToList();
                var plus = list.Concat(new[] { new Migration(list.Count + 1, "future", "CREATE TABLE future_x(a INTEGER);") }).ToList();
                MigrationRunner.Migrate(c, plus);
                var ex = Assert.Throws<MigrationException>(() => MigrationRunner.Migrate(c, list));
                Assert.Contains("newer", ex.Message);
            }
        }

        [Fact]
        public void Checksum_ignores_crlf_vs_lf()
        {
            Assert.Equal(new Migration(1, "x", "A\r\nB\r\n").Sha256, new Migration(1, "x", "A\nB\n").Sha256);
        }

        [Fact]
        public void Gaps_in_numbering_are_rejected()
        {
            Assert.Throws<MigrationException>(() => MigrationRunner.Validate(new[] { new Migration(1, "a", "SELECT 1;"), new Migration(3, "c", "SELECT 1;") }));
        }

        [Fact]
        public void Schema_constraints_hold_foreign_keys_and_unique_enrollment()
        {
            using (var d = new TempDir("mig"))
            using (var c = EncryptedDatabase.Create(d.File("a.db"), CryptoRandom.Bytes(32)))
            {
                MigrationRunner.Migrate(c, MigrationRunner.Embedded());
                Fixtures.SeedMinimal(c, out var ids);
                // Second enrollment for the same student/school/year must fail.
                var dup = Assert.Throws<SqliteException>(() => Fixtures.InsertEnrollment(c, ids, System.Guid.NewGuid()));
                Assert.Equal(19, dup.SqliteErrorCode); // SQLITE_CONSTRAINT
                // Orphan reference must fail (foreign_keys=ON).
                var fk = Assert.Throws<SqliteException>(() => EncryptedDatabase.Exec(c,
                    "INSERT INTO classroom(id, academic_year_id, grade, code, name) VALUES ('" + System.Guid.NewGuid() + "', '" + System.Guid.NewGuid() + "', 1, 'X', 'x');"));
                Assert.Equal(19, fk.SqliteErrorCode);
                // National ID: digits only, exactly 10.
                Assert.Throws<SqliteException>(() => EncryptedDatabase.Exec(c,
                    "INSERT INTO student(id, full_name, full_name_key, national_id, is_target_group, created_utc, updated_utc) VALUES ('" + System.Guid.NewGuid() + "', 'n', 'n', '12345', 0, 't', 't');"));
            }
        }
    }
}
