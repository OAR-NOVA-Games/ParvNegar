using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using ParvNegar.Security;
using ParvNegar.Storage;

namespace ParvNegar.Application.Security
{
    public static class AuditKinds
    {
        public const string LicenseImport = "license.import";
        public const string LicenseCheck = "license.check";
        public const string Setup = "installation.setup";
        public const string LoginSuccess = "auth.login";
        public const string LoginFailure = "auth.login_failed";
        public const string LoginThrottled = "auth.throttled";
        public const string Logout = "session.logout";
        public const string SessionLock = "session.lock";
        public const string PasswordChange = "account.password_change";
        public const string PasswordReset = "account.password_reset";
        public const string AccountCreate = "account.create";
        public const string AccountDisable = "account.disable";
        public const string AccountEnable = "account.enable";
        public const string AccountDelete = "account.delete";
        public const string PermissionChange = "account.permissions";
        public const string Recovery = "recovery.owner";
        public const string RecoveryFailure = "recovery.failed";
        public const string RecoveryKeyRotate = "recovery.key_rotated";
        public const string RecoveryKeyConfirmed = "recovery.key_confirmed";
        public const string DeviceRebind = "installation.device_rebind";
        public const string SecurityFailure = "security.failure";
        public const string AuditVerify = "audit.verify";
        public const string BootstrapFailure = "provisioning.bootstrap_failed";
    }

    public sealed class AuditEvent
    {
        public long Seq; public DateTime Utc; public string Kind; public string ActorId; public string Subject; public string Outcome; public string Detail; public string Source;
    }

    public sealed class AuditVerification
    {
        public bool Intact; public long CheckedEvents; public long FirstBadSeq; public string Problem;
    }

    /// <summary>
    /// Ordered HMAC chain (AUDIT-LOG-FORMAT.md): mac_n = HMAC-SHA256(auditKey, mac_{n-1} ‖ canonical(event_n)),
    /// starting from the anchor (32 zero bytes until the first pruning). The log keeps at most
    /// <see cref="MaxEvents"/> events; pruning moves the anchor to the mac of the last removed event.
    /// Never stores passwords, recovery keys, raw hardware identifiers, national IDs, phone numbers,
    /// document contents or student records: callers pass identifiers and outcome codes only.
    /// The chain detects modification, insertion, deletion and reordering inside the kept window; it cannot
    /// detect a rollback of the whole database file or deletion of the whole log by someone with the key.
    /// </summary>
    public sealed class AuditLog
    {
        public const int MaxEvents = 50000;
        public const int MaxField = 200;
        private readonly SqliteConnection _c;
        private readonly byte[] _key;
        private readonly IClock _clock;
        private readonly int _maxEvents;

        public AuditLog(SqliteConnection c, byte[] auditKey, IClock clock, int maxEvents = MaxEvents) { _c = c; _key = auditKey; _clock = clock; _maxEvents = maxEvents; }

        public static void Initialize(SqliteConnection c, SqliteTransaction tx)
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO audit_state(id, head_seq, head_mac, anchor_seq, anchor_mac) VALUES (1, 0, zeroblob(32), 0, zeroblob(32));";
                cmd.ExecuteNonQuery();
            }
        }

        public void Append(string kind, string actorId, string subject, string outcome, string detail = null, string source = "session", DateTime? utc = null, SqliteTransaction tx = null)
        {
            bool own = tx == null;
            var t = tx ?? _c.BeginTransaction(deferred: false);
            try
            {
                long headSeq; byte[] headMac;
                ReadState(t, out headSeq, out headMac, out _, out _);
                var e = new AuditEvent
                {
                    Seq = headSeq + 1, Utc = utc ?? _clock.UtcNow, Kind = Trim(kind), ActorId = Trim(actorId), Subject = Trim(subject),
                    Outcome = Trim(outcome), Detail = Trim(detail), Source = source,
                };
                var mac = Mac(headMac, e);
                using (var cmd = _c.CreateCommand())
                {
                    cmd.Transaction = t;
                    cmd.CommandText = "INSERT INTO audit_event(seq, utc, kind, actor_id, subject, outcome, detail, source, mac) VALUES ($s,$u,$k,$a,$j,$o,$d,$src,$m);" +
                                      "UPDATE audit_state SET head_seq=$s, head_mac=$m WHERE id=1;";
                    cmd.Parameters.AddWithValue("$s", e.Seq);
                    cmd.Parameters.AddWithValue("$u", Ts(e.Utc));
                    cmd.Parameters.AddWithValue("$k", e.Kind);
                    cmd.Parameters.AddWithValue("$a", (object)e.ActorId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$j", (object)e.Subject ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$o", e.Outcome);
                    cmd.Parameters.AddWithValue("$d", (object)e.Detail ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$src", e.Source);
                    cmd.Parameters.AddWithValue("$m", mac);
                    cmd.ExecuteNonQuery();
                }
                Prune(t);
                if (own) t.Commit();
            }
            catch { if (own) t.Rollback(); throw; }
            finally { if (own) t.Dispose(); }
        }

        public AuditVerification Verify()
        {
            ReadState(null, out long headSeq, out byte[] headMac, out long anchorSeq, out byte[] prev);
            long expected = anchorSeq + 1, count = 0;
            foreach (var e in Read(null))
            {
                if (e.Seq != expected) return Bad(count, e.Seq, "sequence gap or reordering");
                var mac = Mac(prev, e);
                if (!Bytes.FixedTimeEquals(mac, StoredMac(e.Seq))) return Bad(count, e.Seq, "event modified");
                prev = mac; expected++; count++;
            }
            if (expected - 1 != headSeq || !Bytes.FixedTimeEquals(prev, headMac)) return Bad(count, expected, "head does not match (events removed at the end)");
            return new AuditVerification { Intact = true, CheckedEvents = count };
        }

        public IReadOnlyList<AuditEvent> Read(int? limit)
        {
            var list = new List<AuditEvent>();
            using (var cmd = _c.CreateCommand())
            {
                cmd.CommandText = "SELECT seq, utc, kind, actor_id, subject, outcome, detail, source FROM audit_event ORDER BY seq" + (limit.HasValue ? " DESC LIMIT " + limit.Value.ToString(CultureInfo.InvariantCulture) : "") + ";";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new AuditEvent
                        {
                            Seq = r.GetInt64(0), Utc = DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                            Kind = r.GetString(2), ActorId = r.IsDBNull(3) ? null : r.GetString(3), Subject = r.IsDBNull(4) ? null : r.GetString(4),
                            Outcome = r.GetString(5), Detail = r.IsDBNull(6) ? null : r.GetString(6), Source = r.GetString(7),
                        });
            }
            return list;
        }

        private byte[] StoredMac(long seq)
        {
            using (var cmd = _c.CreateCommand())
            {
                cmd.CommandText = "SELECT mac FROM audit_event WHERE seq=$s;";
                cmd.Parameters.AddWithValue("$s", seq);
                return (byte[])cmd.ExecuteScalar();
            }
        }

        private void Prune(SqliteTransaction t)
        {
            ReadState(t, out long headSeq, out _, out long anchorSeq, out _);
            if (headSeq - anchorSeq <= _maxEvents) return;
            long newAnchor = headSeq - _maxEvents;
            using (var cmd = _c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = "UPDATE audit_state SET anchor_seq=$a, anchor_mac=(SELECT mac FROM audit_event WHERE seq=$a) WHERE id=1; DELETE FROM audit_event WHERE seq <= $a;";
                cmd.Parameters.AddWithValue("$a", newAnchor);
                cmd.ExecuteNonQuery();
            }
        }

        private void ReadState(SqliteTransaction t, out long headSeq, out byte[] headMac, out long anchorSeq, out byte[] anchorMac)
        {
            using (var cmd = _c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = "SELECT head_seq, head_mac, anchor_seq, anchor_mac FROM audit_state WHERE id=1;";
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) throw new InvalidOperationException("audit state missing");
                    headSeq = r.GetInt64(0); headMac = (byte[])r[1]; anchorSeq = r.GetInt64(2); anchorMac = (byte[])r[3];
                }
            }
        }

        private byte[] Mac(byte[] prev, AuditEvent e)
        {
            var body = Encoding.UTF8.GetBytes(string.Join("\u001f", new[]
            {
                e.Seq.ToString(CultureInfo.InvariantCulture), Ts(e.Utc), e.Kind, e.ActorId ?? "\u0000", e.Subject ?? "\u0000", e.Outcome, e.Detail ?? "\u0000", e.Source,
            }));
            var m = new byte[prev.Length + body.Length];
            Buffer.BlockCopy(prev, 0, m, 0, prev.Length);
            Buffer.BlockCopy(body, 0, m, prev.Length, body.Length);
            using (var h = new System.Security.Cryptography.HMACSHA256(_key)) return h.ComputeHash(m);
        }

        private static string Ts(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        private static string Trim(string s) => s == null ? null : (s.Length > MaxField ? s.Substring(0, MaxField) : s);
        private static AuditVerification Bad(long n, long seq, string p) => new AuditVerification { Intact = false, CheckedEvents = n, FirstBadSeq = seq, Problem = p };
    }
}
