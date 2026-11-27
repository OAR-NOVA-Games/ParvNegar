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
    public sealed class AccountSummary
    {
        public Guid Id; public string Username; public string DisplayName; public AccountRole Role; public AccountStatus Status;
        public bool MustChangePassword; public bool HasPassword; public string[] Permissions;
    }

    public sealed partial class SessionManager
    {
        // ------------------------------------------------------------------ owner recovery

        /// <summary>
        /// Owner recovery with the installation's recovery key (RECOVERY-POLICY.md). Requires a valid license.
        /// Same device: the owner gets a new password and envelope; the old envelope is removed.
        /// Device secret unreadable (Windows reinstalled or data moved to replacement hardware): additionally a
        /// new device secret is created, every other account's envelope is removed and those accounts must be
        /// reset by the owner (device re-binding). In both cases the recovery key is rotated immediately and the
        /// new key is returned once. No account is ever created by recovery.
        /// </summary>
        public AuthOutcome RecoverOwner(string username, string recoveryCode, string newPassword)
        {
            lock (_gate)
            {
                if (_session != null) return AuthOutcome.Of(AuthResult.WrongState);
                if (_license == null || !_license.IsValid) return AuthOutcome.Of(AuthResult.LicenseInvalid);
                if (!_paths.IsInitialized) return AuthOutcome.Of(AuthResult.NotInitialized);
            }
            var canonical = Usernames.Canonicalize(username);
            if (canonical == null) return AuthOutcome.Of(AuthResult.InvalidCredentials);
            var problem = PasswordPolicy.Check(newPassword, canonical, out var pw);
            if (problem != PasswordProblem.None) return new AuthOutcome { Result = AuthResult.PasswordPolicy, PasswordProblem = problem };
            var wait = _throttle.RemainingWait("recovery");
            if (wait > TimeSpan.Zero) { Bytes.Wipe(pw); return new AuthOutcome { Result = AuthResult.Throttled, RetryAfter = wait }; }
            var rk = TypedCode.Decode(recoveryCode, 32, TypedCode.RecoveryKeyPurpose);
            if (rk == null) { Bytes.Wipe(pw); return AuthOutcome.Of(AuthResult.InvalidCode); }
            if (!KdfGate.Wait(0)) { Bytes.Wipe(pw); Bytes.Wipe(rk); return AuthOutcome.Of(AuthResult.Busy); }
            try
            {
                var marker = InstallationMarker.Read(_paths.MarkerFile);
                LocalMasterKey lmk = null;
                EnvelopeBinding binding = null;
                foreach (var f in Directory.Exists(_paths.Installed.RecoveryDir) ? Directory.GetFiles(_paths.Installed.RecoveryDir, "*.pnke") : new string[0])
                {
                    try { lmk = KeyEnvelopeV2.UnwrapWithRecoveryKey(File.ReadAllBytes(f), rk, marker.InstallId, out binding); break; }
                    catch (AuthenticationFailedException) { }
                    catch (MalformedDataException) { }
                    catch (IOException) { }
                }
                if (lmk == null) return RecoveryFailed("wrong-key");

                SqliteConnection c;
                try { c = EncryptedDatabase.Open(_paths.Installed.Database, lmk.DatabaseKey()); MigrationRunner.Migrate(c, MigrationRunner.Embedded()); }
                catch (Exception ex) when (ex is DatabaseKeyException || ex is SqliteException || ex is MigrationException) { lmk.Dispose(); return RecoveryFailed("database"); }
                var info = AccountStore.Installation(c);
                var owner = AccountStore.ByUsername(c, canonical);
                if (info == null || info.InstallId != marker.InstallId || info.RecoveryEnvelopeId != binding.EnvelopeId || info.RecoveryRevision != binding.Revision
                    || owner == null || !owner.IsActiveOwner)
                {
                    new AuditLog(c, lmk.AuditKey(), _clock).Append(AuditKinds.RecoveryFailure, null, owner?.Id.ToString("D"), "rejected",
                        info != null && info.RecoveryRevision != binding.Revision ? "stale recovery envelope" : "not an active owner");
                    c.Dispose(); lmk.Dispose();
                    return RecoveryFailed("rejected");
                }

                var session = new Session(c, lmk, owner, marker.InstallId, _clock);
                var devices = LoadDeviceSecrets(marker.InstallId);
                bool rebind = devices.Count == 0;
                byte[] device = rebind ? null : devices[0];
                string newCode;
                lock (_gate)
                {
                    StartSession(session);
                    ImportJournal();
                    if (rebind) device = RebindDevice(session, marker.InstallId, pw);
                    else ReplaceEnvelope(session, owner, pw, device, mustChange: false, AuditKinds.Recovery, owner.Id);
                    newCode = RotateRecoveryKeyCore(session, AuditKinds.RecoveryKeyRotate);
                    session.Account = AccountStore.ById(c, owner.Id);
                    session.RecoveryKeyPendingConfirmation = true;
                    _throttle.RecordSuccess("recovery");
                    _state = ComputeState();
                }
                foreach (var d in devices) Bytes.Wipe(d);
                Bytes.Wipe(device);
                RaiseChanged();
                return new AuthOutcome { Result = AuthResult.Success, RecoveryKeyCode = newCode };
            }
            finally { Bytes.Wipe(pw); Bytes.Wipe(rk); KdfGate.Release(); }
        }

        private AuthOutcome RecoveryFailed(string why)
        {
            _throttle.RecordFailure("recovery");
            _journal.Append(AuditKinds.RecoveryFailure, null, why);
            return AuthOutcome.Of(AuthResult.InvalidCredentials);
        }

        /// <summary>
        /// Device re-binding after the device secret became unreadable. Order: new device secret written as
        /// device.pnds.new and the owner's new envelope (both unused yet) → one DB transaction (owner moves to the
        /// new envelope; every other account loses its envelope and must be reset; device_generation + 1) →
        /// device.pnds.new replaces device.pnds → old envelopes deleted. A crash after the transaction is
        /// finished by the next sign-in, which also accepts the pending device secret.
        /// </summary>
        private byte[] RebindDevice(Session s, Guid installId, byte[] pw)
        {
            var layout = _paths.Installed;
            var pending = layout.DeviceSecretFile + ".new";
            if (File.Exists(pending)) File.Delete(pending);
            var device = DeviceSecret.CreateFile(pending, installId, _dpapi);
            var owner = AccountStore.ById(s.Connection, s.Account.Id);
            var newId = Guid.NewGuid();
            var newRev = owner.EnvelopeRevision + 1;
            File.WriteAllBytes(layout.AccountEnvelope(newId), KeyEnvelopeV2.WrapWithPasswordAndDevice(s.Lmk, pw, device,
                new EnvelopeBinding(owner.Id, installId, newId, (uint)newRev, EnvelopeBinding.HashUsername(owner.Username)), _options.Kdf));
            var others = AccountStore.All(s.Connection).Where(a => a.Id != owner.Id && a.EnvelopeId.HasValue).ToList();
            using (var tx = s.Connection.BeginTransaction(deferred: false))
            {
                if (!AccountStore.SetEnvelope(s.Connection, tx, owner.Id, owner.EnvelopeRevision, newId, newRev, false, _clock.UtcNow)) throw new InvalidOperationException("owner changed");
                foreach (var a in others) AccountStore.SetEnvelope(s.Connection, tx, a.Id, a.EnvelopeRevision, null, a.EnvelopeRevision + 1, true, _clock.UtcNow);
                AccountStore.Exec(s.Connection, tx, "UPDATE installation_info SET device_generation=device_generation+1 WHERE id=1;");
                s.Audit.Append(AuditKinds.DeviceRebind, owner.Id.ToString("D"), null, "success", others.Count + " other account(s) need a password reset", tx: tx);
                s.Audit.Append(AuditKinds.Recovery, owner.Id.ToString("D"), owner.Id.ToString("D"), "success", "with device re-binding", tx: tx);
                tx.Commit();
            }
            File.Copy(pending, layout.DeviceSecretFile, true);
            File.Delete(pending);
            if (owner.EnvelopeId.HasValue) try { File.Delete(layout.AccountEnvelope(owner.EnvelopeId.Value)); } catch (IOException) { }
            foreach (var a in others) try { File.Delete(layout.AccountEnvelope(a.EnvelopeId.Value)); } catch (IOException) { }
            return device;
        }

        // ------------------------------------------------------------------ account administration (owner only)

        public IReadOnlyList<AccountSummary> ListAccounts(SessionToken token) => Run(token, Permissions.AccountsManage, (c, tx, actor) =>
            (IReadOnlyList<AccountSummary>)AccountStore.All(c, tx).Select(a => new AccountSummary
            {
                Id = a.Id, Username = a.Username, DisplayName = a.DisplayName, Role = a.Role, Status = a.Status, MustChangePassword = a.MustChangePassword,
                HasPassword = a.EnvelopeId.HasValue, Permissions = AccountStore.Permissions(c, a.Id, tx).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            }).ToList());

        /// <summary>Creates a delegated administrator with a temporary password (must be changed at first sign-in) and no permissions.</summary>
        public AuthOutcome CreateDelegate(SessionToken token, string username, string displayName, string temporaryPassword, out Guid accountId)
        {
            accountId = Guid.Empty;
            var canonical = Usernames.Canonicalize(username);
            if (canonical == null) return AuthOutcome.Of(AuthResult.InvalidCredentials);
            var problem = PasswordPolicy.Check(temporaryPassword, canonical, out var pw);
            if (problem != PasswordProblem.None) return new AuthOutcome { Result = AuthResult.PasswordPolicy, PasswordProblem = problem };
            if (!KdfGate.Wait(0)) { Bytes.Wipe(pw); return AuthOutcome.Of(AuthResult.Busy); }
            try
            {
                var id = Guid.NewGuid();
                var envId = Guid.NewGuid();
                lock (_gate)
                {
                    var s = Demand(token, Permissions.AccountsManage);
                    if (AccountStore.ByUsername(s.Connection, canonical) != null) return AuthOutcome.Of(AuthResult.Failed);
                    var device = LoadDeviceSecrets(s.InstallId).FirstOrDefault();
                    if (device == null) return AuthOutcome.Of(AuthResult.DeviceUnavailable);
                    var path = _paths.Installed.AccountEnvelope(envId);
                    File.WriteAllBytes(path, KeyEnvelopeV2.WrapWithPasswordAndDevice(s.Lmk, pw, device, new EnvelopeBinding(id, s.InstallId, envId, 1, EnvelopeBinding.HashUsername(canonical)), _options.Kdf));
                    Bytes.Wipe(device);
                    try
                    {
                        Run(token, Permissions.AccountsManage, (c, tx, actor) =>
                        {
                            AccountStore.Insert(c, tx, new AccountRecord
                            {
                                Id = id, Username = canonical, DisplayName = string.IsNullOrWhiteSpace(displayName) ? canonical : displayName.Trim(), Role = AccountRole.Delegate,
                                Status = AccountStatus.Active, EnvelopeId = envId, EnvelopeRevision = 1, MustChangePassword = true, AuthzVersion = 1, CreatedUtc = _clock.UtcNow,
                            }, actor);
                            s.Audit.Append(AuditKinds.AccountCreate, actor.ToString("D"), id.ToString("D"), "delegate", "no permissions", tx: tx);
                            return 0;
                        });
                    }
                    catch { try { File.Delete(path); } catch (IOException) { } throw; }
                }
                accountId = id;
                return AuthOutcome.Of(AuthResult.Success);
            }
            finally { Bytes.Wipe(pw); KdfGate.Release(); }
        }

        /// <summary>Owner resets a delegate's password to a temporary one (must change at next sign-in). Owners use recovery instead.</summary>
        public AuthOutcome ResetDelegatePassword(SessionToken token, Guid accountId, string temporaryPassword)
        {
            if (!KdfGate.Wait(0)) return AuthOutcome.Of(AuthResult.Busy);
            byte[] pw = null;
            try
            {
                lock (_gate)
                {
                    var s = Demand(token, Permissions.DelegatePasswordReset);
                    var acc = AccountStore.ById(s.Connection, accountId);
                    if (acc == null || acc.Status == AccountStatus.Deleted || acc.Role != AccountRole.Delegate) throw new UnauthorizedOperationException(Permissions.DelegatePasswordReset);
                    var problem = PasswordPolicy.Check(temporaryPassword, acc.Username, out pw);
                    if (problem != PasswordProblem.None) return new AuthOutcome { Result = AuthResult.PasswordPolicy, PasswordProblem = problem };
                    var device = LoadDeviceSecrets(s.InstallId).FirstOrDefault();
                    if (device == null) return AuthOutcome.Of(AuthResult.DeviceUnavailable);
                    ReplaceEnvelope(s, acc, pw, device, mustChange: true, AuditKinds.PasswordReset, s.Account.Id);
                    Bytes.Wipe(device);
                }
                return AuthOutcome.Of(AuthResult.Success);
            }
            finally { Bytes.Wipe(pw); KdfGate.Release(); }
        }

        public void SetAccountStatus(SessionToken token, Guid accountId, AccountStatus status)
        {
            Run(token, Permissions.AccountsManage, (c, tx, actor) =>
            {
                var acc = AccountStore.ById(c, accountId, tx) ?? throw new UnauthorizedOperationException(Permissions.AccountsManage);
                if (acc.Status == AccountStatus.Deleted) throw new UnauthorizedOperationException(Permissions.AccountsManage);
                if (acc.IsActiveOwner && status != AccountStatus.Active && AccountStore.ActiveOwnerCount(c, tx) <= 1)
                    throw new LastOwnerException();
                AccountStore.SetStatusRole(c, tx, acc.Id, status, acc.Role, _clock.UtcNow);
                if (status == AccountStatus.Deleted)
                {
                    AccountStore.SetEnvelope(c, tx, acc.Id, acc.EnvelopeRevision, null, acc.EnvelopeRevision + 1, false, _clock.UtcNow);
                    AccountStore.Exec(c, tx, "DELETE FROM account_permission WHERE account_id=$a;", ("$a", acc.Id.ToString("D")));
                }
                _session.Audit.Append(status == AccountStatus.Active ? AuditKinds.AccountEnable : status == AccountStatus.Disabled ? AuditKinds.AccountDisable : AuditKinds.AccountDelete,
                    actor.ToString("D"), acc.Id.ToString("D"), "success", tx: tx);
                if (status == AccountStatus.Deleted && acc.EnvelopeId.HasValue)
                    try { File.Delete(_paths.Installed.AccountEnvelope(acc.EnvelopeId.Value)); } catch (IOException) { }
                return 0;
            });
        }

        public void SetRole(SessionToken token, Guid accountId, AccountRole role)
        {
            Run(token, Permissions.AccountsManage, (c, tx, actor) =>
            {
                var acc = AccountStore.ById(c, accountId, tx) ?? throw new UnauthorizedOperationException(Permissions.AccountsManage);
                if (acc.Status == AccountStatus.Deleted) throw new UnauthorizedOperationException(Permissions.AccountsManage);
                if (acc.IsActiveOwner && role != AccountRole.Owner && AccountStore.ActiveOwnerCount(c, tx) <= 1) throw new LastOwnerException();
                AccountStore.SetStatusRole(c, tx, acc.Id, acc.Status, role, _clock.UtcNow);
                if (role == AccountRole.Owner) AccountStore.Exec(c, tx, "DELETE FROM account_permission WHERE account_id=$a;", ("$a", acc.Id.ToString("D")));
                _session.Audit.Append(AuditKinds.PermissionChange, actor.ToString("D"), acc.Id.ToString("D"), "role " + role, tx: tx);
                return 0;
            });
        }

        public void SetPermissions(SessionToken token, Guid accountId, IEnumerable<string> permissions)
        {
            var set = permissions.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (set.Any(p => !Permissions.IsGrantable(p))) throw new UnauthorizedOperationException(string.Join(",", set.Where(p => !Permissions.IsGrantable(p))));
            Run(token, Permissions.PermissionsAssign, (c, tx, actor) =>
            {
                var acc = AccountStore.ById(c, accountId, tx) ?? throw new UnauthorizedOperationException(Permissions.PermissionsAssign);
                if (acc.Role != AccountRole.Delegate || acc.Status == AccountStatus.Deleted || acc.Id == actor) throw new UnauthorizedOperationException(Permissions.PermissionsAssign);
                AccountStore.ReplacePermissions(c, tx, acc.Id, set, actor, _clock.UtcNow);
                _session.Audit.Append(AuditKinds.PermissionChange, actor.ToString("D"), acc.Id.ToString("D"), "success", string.Join(",", set), tx: tx);
                return 0;
            });
        }

        public IReadOnlyList<AuditEvent> ReadAudit(SessionToken token, int limit)
        {
            lock (_gate) return Demand(token, Permissions.AuditView).Audit.Read(limit);
        }

        public AuditVerification VerifyAudit(SessionToken token)
        {
            lock (_gate) return Demand(token, Permissions.AuditView).Audit.Verify();
        }
    }

    public sealed class LastOwnerException : Exception
    {
        public LastOwnerException() : base("The last active owner cannot be disabled, deleted or demoted.") { }
    }
}
