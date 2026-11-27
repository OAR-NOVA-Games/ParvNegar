using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ParvNegar.Application.Security
{
    public enum AccountRole { Owner = 1, Delegate = 2 }
    public enum AccountStatus { Active = 1, Disabled = 2, Deleted = 3 }

    public sealed class AccountRecord
    {
        public Guid Id; public string Username; public string DisplayName; public AccountRole Role; public AccountStatus Status;
        public Guid? EnvelopeId; public long EnvelopeRevision; public bool MustChangePassword; public long AuthzVersion;
        public DateTime CreatedUtc; public DateTime UpdatedUtc;
        public bool IsActiveOwner => Role == AccountRole.Owner && Status == AccountStatus.Active;
    }

    public sealed class InstallationInfo
    {
        public Guid InstallId; public Guid LicenseId; public Guid ProvisioningId; public DateTime CreatedUtc;
        public Guid RecoveryEnvelopeId; public long RecoveryRevision; public bool RecoveryConfirmed; public long DeviceGeneration;
    }

    /// <summary>Thin ADO.NET access to the authoritative account tables.</summary>
    internal static class AccountStore
    {
        internal static string Ts(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        private static DateTime ParseTs(string s) => DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        private const string Columns = "id, username, display_name, role, status, envelope_id, envelope_revision, must_change_password, authz_version, created_utc, updated_utc";

        public static AccountRecord ById(SqliteConnection c, Guid id, SqliteTransaction tx = null) => One(c, tx, "SELECT " + Columns + " FROM account WHERE id=$p;", id.ToString("D"));
        public static AccountRecord ByUsername(SqliteConnection c, string username, SqliteTransaction tx = null) => One(c, tx, "SELECT " + Columns + " FROM account WHERE username=$p;", username);

        public static List<AccountRecord> All(SqliteConnection c, SqliteTransaction tx = null)
        {
            var list = new List<AccountRecord>();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT " + Columns + " FROM account WHERE status <> 3 ORDER BY role, username;";
                using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(Map(r));
            }
            return list;
        }

        public static int ActiveOwnerCount(SqliteConnection c, SqliteTransaction tx)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT count(*) FROM account WHERE role=1 AND status=1;";
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public static HashSet<string> Permissions(SqliteConnection c, Guid accountId, SqliteTransaction tx = null)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT permission FROM account_permission WHERE account_id=$a;";
                cmd.Parameters.AddWithValue("$a", accountId.ToString("D"));
                using (var r = cmd.ExecuteReader()) while (r.Read()) set.Add(r.GetString(0));
            }
            return set;
        }

        public static void Insert(SqliteConnection c, SqliteTransaction tx, AccountRecord a, Guid? createdBy)
        {
            Exec(c, tx, "INSERT INTO account(id, username, display_name, role, status, envelope_id, envelope_revision, must_change_password, authz_version, created_utc, updated_utc, created_by) " +
                        "VALUES ($id,$u,$d,$r,$s,$e,$rev,$m,$z,$c,$c,$by);",
                ("$id", a.Id.ToString("D")), ("$u", a.Username), ("$d", a.DisplayName), ("$r", (int)a.Role), ("$s", (int)a.Status),
                ("$e", (object)a.EnvelopeId?.ToString("D") ?? DBNull.Value), ("$rev", a.EnvelopeRevision), ("$m", a.MustChangePassword ? 1 : 0),
                ("$z", a.AuthzVersion), ("$c", Ts(a.CreatedUtc)), ("$by", (object)createdBy?.ToString("D") ?? DBNull.Value));
        }

        /// <summary>Replaces the account's envelope reference. Fails (returns false) when the expected current revision no longer matches.</summary>
        public static bool SetEnvelope(SqliteConnection c, SqliteTransaction tx, Guid accountId, long expectedRevision, Guid? envelopeId, long newRevision, bool mustChange, DateTime now)
            => Exec(c, tx, "UPDATE account SET envelope_id=$e, envelope_revision=$r, must_change_password=$m, updated_utc=$t WHERE id=$id AND envelope_revision=$old;",
                ("$e", (object)envelopeId?.ToString("D") ?? DBNull.Value), ("$r", newRevision), ("$m", mustChange ? 1 : 0), ("$t", Ts(now)), ("$id", accountId.ToString("D")), ("$old", expectedRevision)) == 1;

        public static void SetStatusRole(SqliteConnection c, SqliteTransaction tx, Guid accountId, AccountStatus status, AccountRole role, DateTime now)
            => Exec(c, tx, "UPDATE account SET status=$s, role=$r, authz_version=authz_version+1, updated_utc=$t WHERE id=$id;",
                ("$s", (int)status), ("$r", (int)role), ("$t", Ts(now)), ("$id", accountId.ToString("D")));

        public static void ReplacePermissions(SqliteConnection c, SqliteTransaction tx, Guid accountId, IEnumerable<string> permissions, Guid grantedBy, DateTime now)
        {
            Exec(c, tx, "DELETE FROM account_permission WHERE account_id=$a;", ("$a", accountId.ToString("D")));
            foreach (var p in permissions)
                Exec(c, tx, "INSERT INTO account_permission(account_id, permission, granted_utc, granted_by) VALUES ($a,$p,$t,$g);",
                    ("$a", accountId.ToString("D")), ("$p", p), ("$t", Ts(now)), ("$g", grantedBy.ToString("D")));
            Exec(c, tx, "UPDATE account SET authz_version=authz_version+1, updated_utc=$t WHERE id=$a;", ("$a", accountId.ToString("D")), ("$t", Ts(now)));
        }

        public static InstallationInfo Installation(SqliteConnection c, SqliteTransaction tx = null)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT install_id, license_id, provisioning_id, created_utc, recovery_envelope_id, recovery_revision, recovery_confirmed, device_generation FROM installation_info WHERE id=1;";
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new InstallationInfo
                    {
                        InstallId = Guid.ParseExact(r.GetString(0), "D"), LicenseId = Guid.ParseExact(r.GetString(1), "D"), ProvisioningId = Guid.ParseExact(r.GetString(2), "D"),
                        CreatedUtc = ParseTs(r.GetString(3)), RecoveryEnvelopeId = Guid.ParseExact(r.GetString(4), "D"), RecoveryRevision = r.GetInt64(5),
                        RecoveryConfirmed = r.GetInt64(6) == 1, DeviceGeneration = r.GetInt64(7),
                    };
                }
            }
        }

        public static int Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] p)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                foreach (var kv in p) cmd.Parameters.AddWithValue(kv.Item1, kv.Item2 ?? DBNull.Value);
                return cmd.ExecuteNonQuery();
            }
        }

        private static AccountRecord One(SqliteConnection c, SqliteTransaction tx, string sql, string p)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$p", p);
                using (var r = cmd.ExecuteReader()) return r.Read() ? Map(r) : null;
            }
        }

        private static AccountRecord Map(SqliteDataReader r) => new AccountRecord
        {
            Id = Guid.ParseExact(r.GetString(0), "D"), Username = r.GetString(1), DisplayName = r.GetString(2), Role = (AccountRole)r.GetInt32(3),
            Status = (AccountStatus)r.GetInt32(4), EnvelopeId = r.IsDBNull(5) ? (Guid?)null : Guid.ParseExact(r.GetString(5), "D"),
            EnvelopeRevision = r.GetInt64(6), MustChangePassword = r.GetInt64(7) == 1, AuthzVersion = r.GetInt64(8),
            CreatedUtc = ParseTs(r.GetString(9)), UpdatedUtc = ParseTs(r.GetString(10)),
        };
    }
}
