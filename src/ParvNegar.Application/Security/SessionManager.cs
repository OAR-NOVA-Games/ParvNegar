using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using ParvNegar.Licensing;
using ParvNegar.Security;
using ParvNegar.Storage;

namespace ParvNegar.Application.Security
{
    /// <summary>Authentication and session states (SESSION-SECURITY.md §2).</summary>
    public enum AppState
    {
        Unlicensed,
        LicensedNotInitialized,
        ProvisioningRequired,
        Locked,
        Authenticated,
        PasswordChangeRequired,
        RecoveryKeyConfirmationRequired,
        RecoveryRequired,
        InvalidInstallation,
    }

    public enum LockReason { Explicit, Inactivity, WindowsLock, SessionSwitch, Suspend, Logout, AccountChanged }

    public enum AuthResult
    {
        Success, InvalidCredentials, Throttled, AccountDisabled, Busy, LicenseInvalid, NotInitialized, DeviceUnavailable,
        PasswordPolicy, WrongState, InvalidCode, InvalidProvisioning, AlreadyInitialized, StaleArtifact, Failed,
    }

    public sealed class AuthOutcome
    {
        public AuthResult Result { get; internal set; }
        public TimeSpan RetryAfter { get; internal set; }
        public PasswordProblem PasswordProblem { get; internal set; }
        /// <summary>A newly generated recovery key, shown once (setup, recovery and rotation only).</summary>
        public string RecoveryKeyCode { get; internal set; }
        public bool Ok => Result == AuthResult.Success;
        internal static AuthOutcome Of(AuthResult r) => new AuthOutcome { Result = r };
    }

    public sealed class UnauthorizedOperationException : Exception
    {
        public UnauthorizedOperationException(string permission) : base("Not authorized: " + permission) { Permission = permission; }
        public string Permission { get; }
    }

    public sealed class SessionExpiredException : Exception
    {
        public SessionExpiredException() : base("The session is locked or has ended.") { }
    }

    /// <summary>Opaque handle of one authenticated session. Invalid after lock, logout or account change.</summary>
    public sealed class SessionToken
    {
        internal SessionToken(long generation, Guid accountId) { Generation = generation; AccountId = accountId; }
        internal long Generation { get; }
        public Guid AccountId { get; }
    }

    public sealed class SessionOptions
    {
        public string Edition { get; set; } = "legacy";
        public string Architecture { get; set; } = "x86";
        public string AppVersion { get; set; } = "0.2.0";
        public Argon2idParameters Kdf { get; set; } = Argon2idParameters.Default;
        public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// The single owner of security state in the process: license state, setup, login, the open encrypted
    /// database and the unlocked LMK. All protected work runs through <see cref="Run{T}"/>, which re-checks
    /// the session and the caller's authorization against the database inside the transaction, so a lock
    /// or a permission change can never be bypassed by a queued operation.
    /// </summary>
    public sealed partial class SessionManager : IDisposable
    {
        /// <summary>One Argon2id at a time per process: 64 MiB each must fit a 32-bit process on a 2 GB PC.</summary>
        private static readonly SemaphoreSlim KdfGate = new SemaphoreSlim(1, 1);

        private readonly object _gate = new object();
        private readonly InstallationPaths _paths;
        private readonly TrustStore _trust;
        private readonly IDeviceSignalSource _signals;
        private readonly IDeviceSecretProtector _dpapi;
        private readonly IClock _clock;
        private readonly SessionOptions _options;
        private readonly GuardKey _guard;
        private readonly LoginThrottle _throttle;
        private readonly PreUnlockJournal _journal;

        private LicenseCheck _license;
        private OwnerProvisioning _pendingProvisioning;
        private Session _session;
        private long _generation;
        private long _lastActivity;
        private AppState _state;

        public SessionManager(InstallationPaths paths, TrustStore trust, IDeviceSignalSource signals, IDeviceSecretProtector dpapi, IClock clock, SessionOptions options)
        {
            _paths = paths; _trust = trust; _signals = signals; _dpapi = dpapi; _clock = clock; _options = options ?? new SessionOptions();
            _guard = new GuardKey(paths, dpapi);
            _throttle = new LoginThrottle(paths, _guard, clock);
            _journal = new PreUnlockJournal(paths, _guard, clock);
            CleanupIncompleteSetups();
            Refresh();
        }

        public event EventHandler StateChanged;
        public event EventHandler<LockReason> Locked;

        public AppState State { get { lock (_gate) return _state; } }
        public LicenseCheck License => _license;
        public OwnerProvisioning PendingProvisioning => _pendingProvisioning;
        public InstallationPaths Paths => _paths;
        public TimeSpan InactivityTimeout => _options.InactivityTimeout;
        public bool HasSession { get { lock (_gate) return _session != null; } }

        /// <summary>Snapshot of the signed-in account (null when locked).</summary>
        public AccountRecord CurrentAccount { get { lock (_gate) return _session?.Account; } }
        public SessionToken Token { get { lock (_gate) return _session == null ? null : new SessionToken(_generation, _session.Account.Id); } }

        // ------------------------------------------------------------------ state

        public void Refresh()
        {
            lock (_gate)
            {
                _license = CheckInstalledLicense();
                var old = _state;
                _state = ComputeState();
                if (old != _state || _license != null) RaiseChanged();
            }
        }

        private AppState ComputeState()
        {
            if (_session != null)
            {
                if (_session.Account.MustChangePassword) return AppState.PasswordChangeRequired;
                if (_session.RecoveryKeyPendingConfirmation) return AppState.RecoveryKeyConfirmationRequired;
                return AppState.Authenticated;
            }
            if (_license == null || !_license.IsValid) return AppState.Unlicensed;
            if (Directory.Exists(_paths.InstallationDir) && !_paths.IsInitialized) return AppState.InvalidInstallation;
            if (!_paths.IsInitialized) return _pendingProvisioning != null ? AppState.ProvisioningRequired : AppState.LicensedNotInitialized;
            InstallationMarker marker;
            try { marker = InstallationMarker.Read(_paths.MarkerFile); }
            catch (Exception ex) when (ex is IOException || ex is LicenseFormatException || ex is FormatException) { return AppState.InvalidInstallation; }
            if (!File.Exists(_paths.Installed.Database)) return AppState.InvalidInstallation;
            if (LoadDeviceSecrets(marker.InstallId).Count == 0) return AppState.RecoveryRequired;
            return AppState.Locked;
        }

        private LicenseCheck CheckInstalledLicense()
        {
            if (!File.Exists(_paths.LicenseFile)) return null;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(_paths.LicenseFile); }
            catch (IOException) { return new LicenseCheck { Status = LicenseStatus.Malformed, Detail = "unreadable" }; }
            return LicenseVerifier.Verify(bytes, _trust, _options.Edition, _options.Architecture, DeviceBinding.Claims(_signals.Read()));
        }

        private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

        // ------------------------------------------------------------------ licensing

        /// <summary>Creates (or re-creates) the activation request. It contains no secrets and no school data.</summary>
        public byte[] GenerateActivationRequest(out ActivationRequest request)
        {
            var readings = _signals.Read();
            var claims = DeviceBinding.Claims(readings);
            request = ActivationRequest.Create(Guid.NewGuid(), _clock.UtcNow, _options.AppVersion, _options.Edition, _options.Architecture, Environment.OSVersion.Version, claims);
            var bytes = request.Serialize();
            AtomicFile.Write(_paths.PendingRequestFile, bytes);
            _journal.Append("activation.request", request.RequestId, "created");
            return bytes;
        }

        public ActivationRequest PendingRequest()
        {
            try { return File.Exists(_paths.PendingRequestFile) ? ActivationRequest.Parse(File.ReadAllBytes(_paths.PendingRequestFile)) : null; }
            catch (LicenseFormatException) { return null; }
        }

        /// <summary>Verifies and installs a license file. An invalid file never replaces a valid installed license.</summary>
        public LicenseCheck ImportLicense(byte[] file)
        {
            var check = LicenseVerifier.Verify(file, _trust, _options.Edition, _options.Architecture, DeviceBinding.Claims(_signals.Read()));
            _journal.Append(AuditKinds.LicenseImport, check.Claims?.LicenseId, check.Status.ToString());
            if (check.IsValid)
            {
                lock (_gate)
                {
                    if (_session != null) Audit(AuditKinds.LicenseImport, _session.Account.Id.ToString("D"), check.Claims.LicenseId, "valid", "revision " + check.Claims.LicenseRevision);
                    AtomicFile.Write(_paths.LicenseFile, file);
                }
                Refresh();
            }
            return check;
        }

        // ------------------------------------------------------------------ provisioning and setup

        public AuthOutcome LoadProvisioning(byte[] file)
        {
            lock (_gate)
            {
                if (_paths.IsInitialized) return AuthOutcome.Of(AuthResult.AlreadyInitialized);
                if (_license == null || !_license.IsValid) return AuthOutcome.Of(AuthResult.LicenseInvalid);
                OwnerProvisioning p;
                try { p = OwnerProvisioning.Verify(file, _trust, out var sig); if (p == null) return AuthOutcome.Of(AuthResult.InvalidProvisioning); }
                catch (Exception ex) when (ex is LicenseFormatException || ex is WrongProductException) { return AuthOutcome.Of(AuthResult.InvalidProvisioning); }
                var pending = PendingRequest();
                // Bound to this license and to the activation request generated on this computer.
                if (p.LicenseId != _license.Claims.LicenseId || p.RequestId != _license.Claims.RequestId || pending == null || pending.RequestId != p.RequestId)
                {
                    _journal.Append("provisioning.load", p.ProvisioningId, "stale-or-foreign");
                    return AuthOutcome.Of(AuthResult.StaleArtifact);
                }
                _pendingProvisioning = p;
                _state = ComputeState();
            }
            RaiseChanged();
            return AuthOutcome.Of(AuthResult.Success);
        }

        /// <summary>
        /// One-time, crash-safe first-run setup. Everything is built in a staging folder and published by a
        /// single folder rename; a failure deletes the staging folder and leaves no installation behind.
        /// </summary>
        public AuthOutcome CompleteSetup(string bootstrapCode, string newPassword)
        {
            OwnerProvisioning p;
            lock (_gate)
            {
                if (_paths.IsInitialized || Directory.Exists(_paths.InstallationDir)) return AuthOutcome.Of(AuthResult.AlreadyInitialized);
                p = _pendingProvisioning;
                if (p == null) return AuthOutcome.Of(AuthResult.WrongState);
                if (_license == null || !_license.IsValid) return AuthOutcome.Of(AuthResult.LicenseInvalid);
            }
            var wait = _throttle.RemainingWait("bootstrap");
            if (wait > TimeSpan.Zero) return new AuthOutcome { Result = AuthResult.Throttled, RetryAfter = wait };
            var secret = TypedCode.Decode(bootstrapCode, 20, TypedCode.BootstrapPurpose);
            if (secret == null) return AuthOutcome.Of(AuthResult.InvalidCode); // typing error: detected by the check value, not counted
            bool ok = p.VerifyBootstrapSecret(secret);
            Bytes.Wipe(secret);
            if (!ok)
            {
                _throttle.RecordFailure("bootstrap");
                _journal.Append(AuditKinds.BootstrapFailure, p.ProvisioningId, "wrong-code");
                return AuthOutcome.Of(AuthResult.InvalidCredentials);
            }
            var problem = PasswordPolicy.Check(newPassword, p.OwnerUsername, out var pw);
            if (problem != PasswordProblem.None) return new AuthOutcome { Result = AuthResult.PasswordPolicy, PasswordProblem = problem };
            if (!KdfGate.Wait(0)) { Bytes.Wipe(pw); return AuthOutcome.Of(AuthResult.Busy); }
            try
            {
                using (var setupLock = AcquireSetupLock())
                {
                    if (setupLock == null || _paths.IsInitialized || Directory.Exists(_paths.InstallationDir)) return AuthOutcome.Of(AuthResult.AlreadyInitialized);
                    var recoveryKey = CryptoRandom.Bytes(32);
                    try
                    {
                        var session = BuildInstallation(p, pw, recoveryKey);
                        _throttle.RecordSuccess("bootstrap");
                        if (File.Exists(_paths.PendingRequestFile)) File.Delete(_paths.PendingRequestFile); // provisioning is one-time
                        lock (_gate)
                        {
                            _pendingProvisioning = null;
                            StartSession(session);
                            _session.RecoveryKeyPendingConfirmation = true;
                            _state = ComputeState();
                        }
                        RaiseChanged();
                        return new AuthOutcome { Result = AuthResult.Success, RecoveryKeyCode = TypedCode.Encode(recoveryKey, TypedCode.RecoveryKeyPurpose) };
                    }
                    finally { Bytes.Wipe(recoveryKey); }
                }
            }
            finally { Bytes.Wipe(pw); KdfGate.Release(); }
        }

        private Session BuildInstallation(OwnerProvisioning p, byte[] pw, byte[] recoveryKey)
        {
            var staging = Path.Combine(_paths.Root, "installation.staging-" + Guid.NewGuid().ToString("N"));
            var layout = new InstallationLayout(staging);
            var installId = Guid.NewGuid();
            var now = _clock.UtcNow;
            SqliteConnection c = null;
            LocalMasterKey lmk = null;
            try
            {
                Directory.CreateDirectory(layout.AccountsDir);
                Directory.CreateDirectory(layout.RecoveryDir);
                Directory.CreateDirectory(layout.DataDir);
                var device = DeviceSecret.CreateFile(layout.DeviceSecretFile, installId, _dpapi);
                lmk = LocalMasterKey.Generate();
                var ownerId = Guid.NewGuid();
                var envId = Guid.NewGuid();
                var recId = Guid.NewGuid();
                File.WriteAllBytes(layout.AccountEnvelope(envId), KeyEnvelopeV2.WrapWithPasswordAndDevice(lmk, pw, device,
                    new EnvelopeBinding(ownerId, installId, envId, 1, EnvelopeBinding.HashUsername(p.OwnerUsername)), _options.Kdf));
                File.WriteAllBytes(layout.RecoveryEnvelope(recId), KeyEnvelopeV2.WrapWithRecoveryKey(lmk, recoveryKey, installId, recId, 1));
                Bytes.Wipe(device);

                c = EncryptedDatabase.Create(layout.Database, lmk.DatabaseKey());
                MigrationRunner.Migrate(c, MigrationRunner.Embedded());
                using (var tx = c.BeginTransaction(deferred: false))
                {
                    AccountStore.Exec(c, tx, "INSERT INTO installation_info(id, install_id, license_id, provisioning_id, created_utc, recovery_envelope_id, recovery_revision, recovery_confirmed, device_generation) VALUES (1,$i,$l,$p,$t,$r,1,0,1);",
                        ("$i", installId.ToString("D")), ("$l", p.LicenseId), ("$p", p.ProvisioningId), ("$t", AccountStore.Ts(now)), ("$r", recId.ToString("D")));
                    AccountStore.Insert(c, tx, new AccountRecord
                    {
                        Id = ownerId, Username = p.OwnerUsername, DisplayName = p.OwnerUsername, Role = AccountRole.Owner, Status = AccountStatus.Active,
                        EnvelopeId = envId, EnvelopeRevision = 1, MustChangePassword = false, AuthzVersion = 1, CreatedUtc = now,
                    }, null);
                    AuditLog.Initialize(c, tx);
                    var audit = new AuditLog(c, lmk.AuditKey(), _clock);
                    audit.Append(AuditKinds.Setup, ownerId.ToString("D"), p.ProvisioningId, "success", "license " + p.LicenseId, tx: tx);
                    audit.Append(AuditKinds.AccountCreate, null, ownerId.ToString("D"), "owner", "provisioned", tx: tx);
                    tx.Commit();
                }
                EncryptedDatabase.Checkpoint(c);
                c.Dispose(); c = null;
                SqliteConnection.ClearAllPools();
                File.WriteAllBytes(layout.Marker, new InstallationMarker { InstallId = installId, CreatedUtc = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc) }.Serialize());
                Directory.Move(staging, _paths.InstallationDir); // the single publishing step

                var live = _paths.Installed;
                var conn = EncryptedDatabase.Open(live.Database, lmk.DatabaseKey());
                var account = AccountStore.ById(conn, ownerId);
                var s = new Session(conn, lmk, account, installId, _clock);
                lmk = null; // owned by the session now
                return s;
            }
            catch
            {
                c?.Dispose();
                SqliteConnection.ClearAllPools();
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
            finally { lmk?.Dispose(); }
        }

        private FileStream AcquireSetupLock()
        {
            Directory.CreateDirectory(_paths.Root);
            try { return new FileStream(_paths.SetupLockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) { return null; }
        }

        private void CleanupIncompleteSetups()
        {
            using (var l = AcquireSetupLock())
            {
                if (l == null) return; // another process is setting up
                foreach (var d in _paths.StagingDirs())
                    try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // ------------------------------------------------------------------ device secrets

        /// <summary>The device secret, plus a pending replacement left by an interrupted device re-binding.</summary>
        private List<byte[]> LoadDeviceSecrets(Guid installId)
        {
            var list = new List<byte[]>();
            foreach (var f in new[] { _paths.Installed.DeviceSecretFile, _paths.Installed.DeviceSecretFile + ".new" })
            {
                if (!File.Exists(f)) continue;
                try { list.Add(DeviceSecret.LoadFile(f, installId, _dpapi)); }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException || ex is IOException) { }
            }
            return list;
        }

        // ------------------------------------------------------------------ helpers shared by the partial files

        private static string ThrottleKey(string canonicalUsername) => "u:" + Bytes.ToHex(EnvelopeBinding.HashUsername(canonicalUsername ?? "")).Substring(0, 24);

        private IEnumerable<(string path, EnvelopeBinding binding, byte[] bytes)> AccountEnvelopeCandidates(byte[] usernameHash)
        {
            var dir = _paths.Installed.AccountsDir;
            if (!Directory.Exists(dir)) yield break;
            var found = new List<(string, EnvelopeBinding, byte[])>();
            foreach (var f in Directory.GetFiles(dir, "*.pnke"))
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(f); } catch (IOException) { continue; }
                EnvelopeBinding b;
                try { b = KeyEnvelopeV2.PeekBinding(bytes, out var kind); if (kind != KeyEnvelopeKind.PasswordAndDevice) continue; }
                catch (MalformedDataException) { continue; }
                if (Bytes.FixedTimeEquals(b.UsernameHash, usernameHash)) found.Add((f, b, bytes));
            }
            foreach (var x in found.OrderByDescending(x => x.Item2.Revision).Take(3)) yield return x;
        }

        private void Audit(string kind, string actor, string subject, string outcome, string detail = null)
        {
            var s = _session;
            if (s == null) { _journal.Append(kind, subject, outcome); return; }
            s.Audit.Append(kind, actor, subject, outcome, detail);
        }

        public void Dispose()
        {
            lock (_gate) EndSession(LockReason.Logout, raise: false);
        }
    }

    internal sealed class Session : IDisposable
    {
        public Session(SqliteConnection c, LocalMasterKey lmk, AccountRecord account, Guid installId, IClock clock)
        {
            Connection = c; Lmk = lmk; Account = account; InstallId = installId;
            AuditKey = lmk.AuditKey();
            Audit = new AuditLog(c, AuditKey, clock);
            Cancellation = new CancellationTokenSource();
        }

        public SqliteConnection Connection { get; private set; }
        public LocalMasterKey Lmk { get; private set; }
        public AccountRecord Account { get; set; }
        public Guid InstallId { get; }
        public byte[] AuditKey { get; private set; }
        public AuditLog Audit { get; set; }
        public CancellationTokenSource Cancellation { get; }
        public bool RecoveryKeyPendingConfirmation { get; set; }
        public string PendingRecoveryCodeLastGroup { get; set; }

        public void Dispose()
        {
            Cancellation.Cancel();
            Connection?.Dispose();
            Connection = null;
            SqliteConnection.ClearAllPools();
            Lmk?.Dispose();
            Lmk = null;
            Bytes.Wipe(AuditKey);
            AuditKey = null;
        }
    }
}
