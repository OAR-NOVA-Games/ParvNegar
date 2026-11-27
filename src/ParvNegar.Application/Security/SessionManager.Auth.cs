using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ParvNegar.Licensing;
using ParvNegar.Security;
using ParvNegar.Storage;

namespace ParvNegar.Application.Security
{
    public sealed partial class SessionManager
    {
        // ------------------------------------------------------------------ login

        /// <summary>
        /// Normal unlock: valid license + canonical username + password + this installation's device secret
        /// → authenticated PNKE v2 envelope → LMK → encrypted DB → active account whose envelope id and
        /// revision equal the envelope's authenticated binding. Anything else is a generic failure.
        /// </summary>
        public AuthOutcome Login(string username, string password)
        {
            lock (_gate)
            {
                if (_session != null) return AuthOutcome.Of(AuthResult.WrongState);
                if (_license == null || !_license.IsValid) return AuthOutcome.Of(AuthResult.LicenseInvalid);
                if (!_paths.IsInitialized) return AuthOutcome.Of(AuthResult.NotInitialized);
            }
            var canonical = Usernames.Canonicalize(username);
            var key = ThrottleKey(canonical ?? username ?? "");
            var wait = _throttle.RemainingWait(key);
            if (wait > TimeSpan.Zero)
            {
                _journal.Append(AuditKinds.LoginThrottled, key.Substring(2, 12), "wait " + (int)wait.TotalSeconds + "s");
                return new AuthOutcome { Result = AuthResult.Throttled, RetryAfter = wait };
            }
            if (!KdfGate.Wait(0)) return AuthOutcome.Of(AuthResult.Busy);
            var pw = PasswordPolicy.LoginBytes(password);
            try
            {
                var marker = InstallationMarker.Read(_paths.MarkerFile);
                var devices = LoadDeviceSecrets(marker.InstallId);
                if (devices.Count == 0) { Refresh(); return AuthOutcome.Of(AuthResult.DeviceUnavailable); }
                Session session = null;
                AuthResult failure = AuthResult.InvalidCredentials;
                var candidates = canonical == null ? new List<(string path, EnvelopeBinding binding, byte[] bytes)>() : AccountEnvelopeCandidates(EnvelopeBinding.HashUsername(canonical)).ToList();
                if (candidates.Count == 0 || pw == null)
                {
                    BurnKdf(); // same cost for unknown usernames
                }
                foreach (var cand in candidates)
                {
                    if (pw == null) break;
                    foreach (var device in devices)
                    {
                        LocalMasterKey lmk;
                        EnvelopeBinding binding;
                        try { lmk = KeyEnvelopeV2.UnwrapWithPasswordAndDevice(cand.bytes, pw, device, marker.InstallId, out binding); }
                        catch (AuthenticationFailedException) { continue; }
                        catch (MalformedDataException) { continue; }
                        var r = OpenAndValidate(lmk, binding, canonical, marker.InstallId, out session);
                        if (r == AuthResult.Success) { PromotePendingDevice(device); break; }
                        failure = r;
                    }
                    if (session != null) break;
                }
                foreach (var d in devices) Bytes.Wipe(d);

                if (session == null)
                {
                    if (failure != AuthResult.AccountDisabled) _throttle.RecordFailure(key);
                    _journal.Append(failure == AuthResult.AccountDisabled ? AuditKinds.LoginFailure : AuditKinds.LoginFailure, key.Substring(2, 12), failure.ToString());
                    return AuthOutcome.Of(failure == AuthResult.AccountDisabled ? AuthResult.AccountDisabled : AuthResult.InvalidCredentials);
                }
                _throttle.RecordSuccess(key);
                lock (_gate)
                {
                    StartSession(session);
                    ImportJournal();
                    Audit(AuditKinds.LoginSuccess, session.Account.Id.ToString("D"), session.Account.Id.ToString("D"), "success");
                    CleanupOrphanEnvelopes();
                    _state = ComputeState();
                }
                RaiseChanged();
                return AuthOutcome.Of(AuthResult.Success);
            }
            finally { Bytes.Wipe(pw); KdfGate.Release(); }
        }

        /// <summary>Checks the authoritative account record. Rejects swapped, stale, disabled, deleted and transplanted envelopes.</summary>
        private AuthResult OpenAndValidate(LocalMasterKey lmk, EnvelopeBinding binding, string canonical, Guid installId, out Session session)
        {
            session = null;
            SqliteConnection c = null;
            try
            {
                c = EncryptedDatabase.Open(_paths.Installed.Database, lmk.DatabaseKey());
                MigrationRunner.Migrate(c, MigrationRunner.Embedded());
                var info = AccountStore.Installation(c);
                var acc = AccountStore.ById(c, binding.AccountId);
                bool consistent = info != null && info.InstallId == installId && acc != null
                    && string.Equals(acc.Username, canonical, StringComparison.Ordinal)
                    && Bytes.FixedTimeEquals(EnvelopeBinding.HashUsername(acc.Username), binding.UsernameHash)
                    && acc.EnvelopeId == binding.EnvelopeId && acc.EnvelopeRevision == binding.Revision;
                if (!consistent || acc.Status == AccountStatus.Deleted)
                {
                    new AuditLog(c, lmk.AuditKey(), _clock).Append(AuditKinds.SecurityFailure, null, binding.AccountId.ToString("D"), "envelope-rejected",
                        acc == null ? "unknown account" : acc.EnvelopeRevision != binding.Revision ? "stale revision " + binding.Revision + " (current " + acc.EnvelopeRevision + ")" : "binding mismatch");
                    c.Dispose(); lmk.Dispose();
                    return AuthResult.InvalidCredentials;
                }
                if (acc.Status == AccountStatus.Disabled)
                {
                    new AuditLog(c, lmk.AuditKey(), _clock).Append(AuditKinds.LoginFailure, acc.Id.ToString("D"), acc.Id.ToString("D"), "disabled");
                    c.Dispose(); lmk.Dispose();
                    return AuthResult.AccountDisabled;
                }
                session = new Session(c, lmk, acc, installId, _clock) { RecoveryKeyPendingConfirmation = acc.Role == AccountRole.Owner && !info.RecoveryConfirmed };
                return AuthResult.Success;
            }
            catch (Exception ex) when (ex is DatabaseKeyException || ex is SqliteException || ex is MigrationException || ex is StorageSecurityException)
            {
                c?.Dispose(); lmk.Dispose();
                return AuthResult.Failed;
            }
        }

        private void BurnKdf()
        {
            var b = Argon2id.Derive(new byte[] { 0 }, new byte[16], _options.Kdf, 32);
            Bytes.Wipe(b);
        }

        private void StartSession(Session s)
        {
            _session = s;
            _generation++;
            _lastActivity = _clock.MonotonicMilliseconds;
        }

        private void ImportJournal()
        {
            var records = _journal.ReadAll(out bool intact);
            foreach (var r in records)
                _session.Audit.Append(r.Kind, null, r.Subject, r.Outcome, r.Verified ? "pre-unlock journal" : "pre-unlock journal (integrity NOT verified)", "pre-unlock", r.Utc == DateTime.MinValue ? (DateTime?)null : r.Utc);
            if (!intact) _session.Audit.Append(AuditKinds.SecurityFailure, null, "pre-unlock-journal", "chain-broken");
            _journal.Clear();
        }

        private void CleanupOrphanEnvelopes()
        {
            var referenced = new HashSet<Guid>(AccountStore.All(_session.Connection).Where(a => a.EnvelopeId.HasValue).Select(a => a.EnvelopeId.Value));
            foreach (var f in Directory.GetFiles(_paths.Installed.AccountsDir, "*.pnke"))
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(f), "D", out var id) && !referenced.Contains(id))
                    try { File.Delete(f); } catch (IOException) { }
            var info = AccountStore.Installation(_session.Connection);
            foreach (var f in Directory.GetFiles(_paths.Installed.RecoveryDir, "*.pnke"))
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(f), "D", out var id) && id != info.RecoveryEnvelopeId)
                    try { File.Delete(f); } catch (IOException) { }
        }

        private void PromotePendingDevice(byte[] usedDevice)
        {
            var pending = _paths.Installed.DeviceSecretFile + ".new";
            if (!File.Exists(pending)) return;
            try
            {
                var marker = InstallationMarker.Read(_paths.MarkerFile);
                var p = DeviceSecret.LoadFile(pending, marker.InstallId, _dpapi);
                bool usedPending = Bytes.FixedTimeEquals(p, usedDevice);
                Bytes.Wipe(p);
                if (usedPending) File.Copy(pending, _paths.Installed.DeviceSecretFile, true);
                File.Delete(pending);
            }
            catch (Exception ex) when (ex is IOException || ex is System.Security.Cryptography.CryptographicException) { }
        }

        // ------------------------------------------------------------------ lock, inactivity, logout

        /// <summary>Meaningful interaction inside the application (keyboard or mouse input in a ParvNegar window).</summary>
        public void Touch() { lock (_gate) if (_session != null) _lastActivity = _clock.MonotonicMilliseconds; }

        /// <summary>Called by a timer; locks once the inactivity timeout has elapsed (monotonic clock).</summary>
        public bool CheckInactivity()
        {
            lock (_gate)
            {
                if (_session == null) return false;
                if (_clock.MonotonicMilliseconds - _lastActivity < (long)_options.InactivityTimeout.TotalMilliseconds) return false;
            }
            Lock(LockReason.Inactivity);
            return true;
        }

        public void Lock(LockReason reason)
        {
            bool had;
            lock (_gate)
            {
                had = _session != null;
                EndSession(reason, raise: false);
                _state = ComputeState();
            }
            if (had) Locked?.Invoke(this, reason);
            RaiseChanged();
        }

        private void EndSession(LockReason reason, bool raise)
        {
            var s = _session;
            if (s == null) return;
            try { s.Audit.Append(reason == LockReason.Logout ? AuditKinds.Logout : AuditKinds.SessionLock, s.Account.Id.ToString("D"), null, reason.ToString()); }
            catch (Exception ex) when (ex is SqliteException || ex is InvalidOperationException) { }
            _generation++;          // invalidates every outstanding token
            _session = null;
            s.Dispose();            // closes the database, wipes the LMK and the audit key, cancels background work
        }

        // ------------------------------------------------------------------ protected operations and authorization

        /// <summary>
        /// Runs a protected operation for the session that issued <paramref name="token"/>. Authorization is
        /// evaluated against the database inside the same transaction and the transaction commits only if the
        /// session is still the same one. Locking waits for a running operation; queued operations fail.
        /// </summary>
        public T Run<T>(SessionToken token, string permission, Func<SqliteConnection, SqliteTransaction, Guid, T> work)
        {
            lock (_gate)
            {
                var s = Demand(token, permission);
                using (var tx = s.Connection.BeginTransaction(deferred: false))
                {
                    Demand(token, permission, tx);
                    var result = work(s.Connection, tx, s.Account.Id);
                    if (token.Generation != _generation || _session != s) throw new SessionExpiredException();
                    tx.Commit();
                    _lastActivity = _clock.MonotonicMilliseconds;
                    return result;
                }
            }
        }

        public bool IsAuthorized(SessionToken token, string permission)
        {
            lock (_gate)
            {
                try { Demand(token, permission); return true; }
                catch (UnauthorizedOperationException) { return false; }
                catch (SessionExpiredException) { return false; }
            }
        }

        public IReadOnlyCollection<string> EffectivePermissions()
        {
            lock (_gate)
            {
                if (_session == null) return new string[0];
                var acc = AccountStore.ById(_session.Connection, _session.Account.Id);
                if (acc == null || acc.Status != AccountStatus.Active) return new string[0];
                if (acc.Role == AccountRole.Owner) return Permissions.Grantable.Concat(Permissions.OwnerOnly).ToArray();
                return AccountStore.Permissions(_session.Connection, acc.Id).Where(Permissions.IsGrantable).ToArray();
            }
        }

        private Session Demand(SessionToken token, string permission, SqliteTransaction tx = null)
        {
            var s = _session;
            if (token == null || s == null || token.Generation != _generation || token.AccountId != s.Account.Id) throw new SessionExpiredException();
            var acc = AccountStore.ById(s.Connection, s.Account.Id, tx);
            if (acc == null || acc.Status != AccountStatus.Active || acc.EnvelopeId != s.Account.EnvelopeId)
            {
                // Disabled, deleted or reset while signed in: end the session.
                ThreadPoolLock(LockReason.AccountChanged);
                throw new SessionExpiredException();
            }
            s.Account = acc;
            if (permission == null) return s;
            if (acc.MustChangePassword || s.RecoveryKeyPendingConfirmation) throw new UnauthorizedOperationException(permission);
            if (acc.Role == AccountRole.Owner) return s;
            if (Permissions.IsOwnerOnly(permission) || !Permissions.IsGrantable(permission)) throw new UnauthorizedOperationException(permission);
            if (!AccountStore.Permissions(s.Connection, acc.Id, tx).Contains(permission)) throw new UnauthorizedOperationException(permission);
            return s;
        }

        private void ThreadPoolLock(LockReason reason)
        {
            EndSession(reason, raise: false);
            _state = ComputeState();
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { Locked?.Invoke(this, reason); RaiseChanged(); });
        }

        // ------------------------------------------------------------------ own password change

        public AuthOutcome ChangePassword(string currentPassword, string newPassword)
        {
            Session s;
            lock (_gate) { s = _session; if (s == null) return AuthOutcome.Of(AuthResult.WrongState); }
            var problem = PasswordPolicy.Check(newPassword, s.Account.Username, out var newPw);
            if (problem != PasswordProblem.None) return new AuthOutcome { Result = AuthResult.PasswordPolicy, PasswordProblem = problem };
            var key = ThrottleKey(s.Account.Username);
            var wait = _throttle.RemainingWait(key);
            if (wait > TimeSpan.Zero) { Bytes.Wipe(newPw); return new AuthOutcome { Result = AuthResult.Throttled, RetryAfter = wait }; }
            if (!KdfGate.Wait(0)) { Bytes.Wipe(newPw); return AuthOutcome.Of(AuthResult.Busy); }
            var cur = PasswordPolicy.LoginBytes(currentPassword);
            try
            {
                lock (_gate)
                {
                    if (_session != s) return AuthOutcome.Of(AuthResult.WrongState);
                    var acc = AccountStore.ById(s.Connection, s.Account.Id);
                    var devices = LoadDeviceSecrets(s.InstallId);
                    if (devices.Count == 0) return AuthOutcome.Of(AuthResult.DeviceUnavailable);
                    // Re-authenticate with the current password against the current envelope.
                    bool verified = false;
                    byte[] device = null;
                    if (cur != null && acc.EnvelopeId.HasValue && File.Exists(_paths.Installed.AccountEnvelope(acc.EnvelopeId.Value)))
                    {
                        var env = File.ReadAllBytes(_paths.Installed.AccountEnvelope(acc.EnvelopeId.Value));
                        foreach (var d in devices)
                        {
                            try { using (KeyEnvelopeV2.UnwrapWithPasswordAndDevice(env, cur, d, s.InstallId, out var b)) verified = b.Revision == acc.EnvelopeRevision; }
                            catch (AuthenticationFailedException) { continue; }
                            if (verified) { device = d; break; }
                        }
                    }
                    if (!verified)
                    {
                        _throttle.RecordFailure(key);
                        s.Audit.Append(AuditKinds.PasswordChange, acc.Id.ToString("D"), acc.Id.ToString("D"), "wrong-current-password");
                        return AuthOutcome.Of(AuthResult.InvalidCredentials);
                    }
                    _throttle.RecordSuccess(key);
                    ReplaceEnvelope(s, acc, newPw, device, mustChange: false, AuditKinds.PasswordChange, acc.Id);
                    foreach (var d in devices) Bytes.Wipe(d);
                    s.Account = AccountStore.ById(s.Connection, acc.Id);
                    _state = ComputeState();
                }
                RaiseChanged();
                return AuthOutcome.Of(AuthResult.Success);
            }
            finally { Bytes.Wipe(cur); Bytes.Wipe(newPw); KdfGate.Release(); }
        }

        /// <summary>
        /// Crash-safe envelope replacement: (1) write the new envelope file, (2) one DB transaction moves the
        /// account to the new envelope id and revision (optimistic check on the old revision) and appends the
        /// audit event, (3) delete the old file. A crash before (2) leaves an orphan that the next sign-in
        /// removes; a crash after (2) leaves a stale envelope that the revision check rejects.
        /// </summary>
        private void ReplaceEnvelope(Session s, AccountRecord acc, byte[] newPw, byte[] device, bool mustChange, string auditKind, Guid actor)
        {
            var newId = Guid.NewGuid();
            var newRev = acc.EnvelopeRevision + 1;
            var path = _paths.Installed.AccountEnvelope(newId);
            File.WriteAllBytes(path, KeyEnvelopeV2.WrapWithPasswordAndDevice(s.Lmk, newPw, device,
                new EnvelopeBinding(acc.Id, s.InstallId, newId, (uint)newRev, EnvelopeBinding.HashUsername(acc.Username)), _options.Kdf));
            try
            {
                using (var tx = s.Connection.BeginTransaction(deferred: false))
                {
                    if (!AccountStore.SetEnvelope(s.Connection, tx, acc.Id, acc.EnvelopeRevision, newId, newRev, mustChange, _clock.UtcNow))
                        throw new InvalidOperationException("Account changed concurrently.");
                    s.Audit.Append(auditKind, actor.ToString("D"), acc.Id.ToString("D"), "success", "revision " + newRev, tx: tx);
                    tx.Commit();
                }
            }
            catch { try { File.Delete(path); } catch (IOException) { } throw; }
            if (acc.EnvelopeId.HasValue) try { File.Delete(_paths.Installed.AccountEnvelope(acc.EnvelopeId.Value)); } catch (IOException) { }
        }

        // ------------------------------------------------------------------ recovery key confirmation and rotation

        /// <summary>The owner confirms the recovery key was stored by re-typing its last group.</summary>
        public bool ConfirmRecoveryKeySaved(string lastGroupTyped, string shownCode)
        {
            lock (_gate)
            {
                var s = _session;
                if (s == null || !s.RecoveryKeyPendingConfirmation || shownCode == null) return false;
                var expected = shownCode.Split('-').Last();
                if (!string.Equals((lastGroupTyped ?? "").Trim().ToUpperInvariant(), expected, StringComparison.Ordinal)) return false;
                using (var tx = s.Connection.BeginTransaction(deferred: false))
                {
                    AccountStore.Exec(s.Connection, tx, "UPDATE installation_info SET recovery_confirmed=1 WHERE id=1;");
                    s.Audit.Append(AuditKinds.RecoveryKeyConfirmed, s.Account.Id.ToString("D"), null, "confirmed", tx: tx);
                    tx.Commit();
                }
                s.RecoveryKeyPendingConfirmation = false;
                _state = ComputeState();
            }
            RaiseChanged();
            return true;
        }

        /// <summary>Owner-only: replaces the recovery key. The old recovery envelope stops working immediately.</summary>
        public AuthOutcome RotateRecoveryKey(SessionToken token)
        {
            string code;
            lock (_gate)
            {
                var s = Demand(token, null);
                if (s.Account.Role != AccountRole.Owner || s.Account.MustChangePassword) throw new UnauthorizedOperationException(Permissions.RecoveryManage);
                code = RotateRecoveryKeyCore(s, AuditKinds.RecoveryKeyRotate);
                s.RecoveryKeyPendingConfirmation = true;
                _state = ComputeState();
            }
            RaiseChanged();
            return new AuthOutcome { Result = AuthResult.Success, RecoveryKeyCode = code };
        }

        private string RotateRecoveryKeyCore(Session s, string auditKind)
        {
            var info = AccountStore.Installation(s.Connection);
            var rk = CryptoRandom.Bytes(32);
            try
            {
                var newId = Guid.NewGuid();
                var rev = info.RecoveryRevision + 1;
                var path = _paths.Installed.RecoveryEnvelope(newId);
                File.WriteAllBytes(path, KeyEnvelopeV2.WrapWithRecoveryKey(s.Lmk, rk, s.InstallId, newId, (uint)rev));
                try
                {
                    using (var tx = s.Connection.BeginTransaction(deferred: false))
                    {
                        AccountStore.Exec(s.Connection, tx, "UPDATE installation_info SET recovery_envelope_id=$e, recovery_revision=$r, recovery_confirmed=0 WHERE id=1 AND recovery_revision=$old;",
                            ("$e", newId.ToString("D")), ("$r", rev), ("$old", info.RecoveryRevision));
                        s.Audit.Append(auditKind, s.Account.Id.ToString("D"), null, "success", "recovery revision " + rev, tx: tx);
                        tx.Commit();
                    }
                }
                catch { try { File.Delete(path); } catch (IOException) { } throw; }
                try { File.Delete(_paths.Installed.RecoveryEnvelope(info.RecoveryEnvelopeId)); } catch (IOException) { }
                return TypedCode.Encode(rk, TypedCode.RecoveryKeyPurpose);
            }
            finally { Bytes.Wipe(rk); }
        }
    }
}
