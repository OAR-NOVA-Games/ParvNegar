using System;
using System.IO;
using System.Linq;
using System.Text;
using ParvNegar.Application.Security;
using ParvNegar.Licensing;
using ParvNegar.Security;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Application.Tests
{
    public class LicensingAndSetupTests
    {
        public LicensingAndSetupTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Missing_and_invalid_licenses_keep_the_app_unlicensed()
        {
            using (var h = new Harness())
            {
                Assert.Equal(AppState.Unlicensed, h.Sm.State);
                Assert.Null(h.Sm.License);
                var bad = h.Sm.ImportLicense(Encoding.UTF8.GetBytes("{}"));
                Assert.Equal(LicenseStatus.Malformed, bad.Status);
                Assert.False(File.Exists(h.Paths.LicenseFile));
                Assert.Equal(AuthResult.LicenseInvalid, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
            }
        }

        [Fact]
        public void Valid_license_then_provisioning_then_setup_reaches_authenticated()
        {
            using (var h = new Harness())
            {
                h.License();
                Assert.Equal(AppState.LicensedNotInitialized, h.Sm.State);
                var prov = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out var code);
                Assert.True(h.Sm.LoadProvisioning(prov).Ok);
                Assert.Equal(AppState.ProvisioningRequired, h.Sm.State);
                var r = h.Sm.CompleteSetup(code, Harness.OwnerPassword);
                Assert.True(r.Ok);
                Assert.Equal(AppState.RecoveryKeyConfirmationRequired, h.Sm.State);
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.ListAccounts(h.Sm.Token)); // nothing protected before confirmation
                Assert.False(h.Sm.ConfirmRecoveryKeySaved("WRONG", r.RecoveryKeyCode));
                Assert.True(h.Sm.ConfirmRecoveryKeySaved(r.RecoveryKeyCode.Split('-').Last().ToLowerInvariant(), r.RecoveryKeyCode));
                Assert.Equal(AppState.Authenticated, h.Sm.State);
                Assert.True(h.Paths.IsInitialized);
                Assert.False(File.Exists(h.Paths.PendingRequestFile));
                Assert.Empty(h.Paths.StagingDirs());
            }
        }

        [Fact]
        public void Wrong_bootstrap_code_creates_nothing_and_is_throttled()
        {
            using (var h = new Harness())
            {
                h.License();
                var prov = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out var code);
                h.Sm.LoadProvisioning(prov);
                Assert.Equal(AuthResult.InvalidCode, h.Sm.CompleteSetup("ABCD-EFGH", Harness.OwnerPassword).Result); // typo: detected, not counted
                var wrong = TypedCode.Encode(CryptoRandom.Bytes(20), TypedCode.BootstrapPurpose);
                for (int i = 0; i < LoginThrottle.FreeAttempts; i++) Assert.Equal(AuthResult.InvalidCredentials, h.Sm.CompleteSetup(wrong, Harness.OwnerPassword).Result);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.CompleteSetup(wrong, Harness.OwnerPassword).Result);
                Assert.Equal(AuthResult.Throttled, h.Sm.CompleteSetup(code, Harness.OwnerPassword).Result); // even the right code waits
                Assert.False(Directory.Exists(h.Paths.InstallationDir));
                h.Clock.Advance(TimeSpan.FromMinutes(1));
                Assert.Equal(AuthResult.PasswordPolicy, h.Sm.CompleteSetup(code, "too short").Result);
                Assert.True(h.Sm.CompleteSetup(code, Harness.OwnerPassword).Ok);
            }
        }

        [Fact]
        public void Provisioning_replay_stale_request_and_foreign_license_are_rejected()
        {
            using (var h = new Harness())
            {
                h.Setup();
                var replay = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out _);
                h.Sm.Lock(LockReason.Explicit);
                Assert.Equal(AuthResult.AlreadyInitialized, h.Sm.LoadProvisioning(replay).Result);
            }
            using (var h = new Harness())
            {
                h.License();
                var stale = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out _, requestIdOverride: Guid.NewGuid().ToString("D"));
                Assert.Equal(AuthResult.StaleArtifact, h.Sm.LoadProvisioning(stale).Result);
                h.Sm.GenerateActivationRequest(out _); // a newer request on this PC: the old provisioning no longer matches
                var old = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out _);
                Assert.Equal(AuthResult.StaleArtifact, h.Sm.LoadProvisioning(old).Result);
                Assert.Equal(AuthResult.InvalidProvisioning, h.Sm.LoadProvisioning(Encoding.UTF8.GetBytes("{}")).Result);
            }
        }

        [Fact]
        public void Interrupted_setup_leftovers_are_removed_and_a_broken_installation_is_never_recreated()
        {
            using (var h = new Harness())
            {
                h.License();
                var staging = Path.Combine(h.Paths.Root, "installation.staging-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(staging, "data"));
                File.WriteAllText(Path.Combine(staging, "data", "partial.db"), "x");
                h.Restart();
                Assert.False(Directory.Exists(staging));
                Assert.Equal(AppState.LicensedNotInitialized, h.Sm.State);

                Directory.CreateDirectory(h.Paths.InstallationDir); // installation folder without marker
                h.Sm.Refresh();
                Assert.Equal(AppState.InvalidInstallation, h.Sm.State);
                var prov = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out var code);
                h.Sm.LoadProvisioning(prov);
                Assert.NotEqual(AuthResult.Success, h.Sm.CompleteSetup(code, Harness.OwnerPassword).Result);
                Assert.Empty(Directory.GetFileSystemEntries(h.Paths.InstallationDir));
            }
        }

        [Fact]
        public void Concurrent_second_instance_cannot_initialize_the_same_installation()
        {
            using (var h = new Harness())
            {
                h.License();
                var second = h.NewManager();
                var prov = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out var code);
                Assert.True(h.Sm.LoadProvisioning(prov).Ok);
                Assert.True(second.LoadProvisioning(prov).Ok);
                Assert.True(h.Sm.CompleteSetup(code, Harness.OwnerPassword).Ok);
                Assert.Equal(AuthResult.AlreadyInitialized, second.CompleteSetup(code, Harness.OwnerPassword).Result);
                second.Dispose();
            }
        }

        [Fact]
        public void License_on_changed_hardware_tolerates_one_signal_and_rejects_two()
        {
            using (var h = new Harness())
            {
                h.Setup();
                h.Sm.Lock(LockReason.Logout);
                h.Signals.Disk = "WD-REPLACED-DISK-0099";
                h.Sm.Refresh();
                Assert.Equal(AppState.Locked, h.Sm.State);
                h.Signals.Smbios = "8D2A1F00-1111-2222-3333-444455556666";
                h.Sm.Refresh();
                Assert.Equal(AppState.Unlicensed, h.Sm.State);
                Assert.Equal(LicenseStatus.DeviceMismatch, h.Sm.License.Status);
                Assert.Equal(AuthResult.LicenseInvalid, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
            }
        }
    }

    public class AuthenticationTests
    {
        public AuthenticationTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Correct_incorrect_and_unknown_credentials()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AppState.Locked, h.Sm.State);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, Harness.OwnerPassword + "x").Result);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login("nobody.here", Harness.OwnerPassword).Result);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login("bad name!", Harness.OwnerPassword).Result);
                Assert.Equal(AuthResult.Success, h.Sm.Login(" OWNER.Test ", Harness.OwnerPassword).Result); // usernames are case-folded
                Assert.Equal(AppState.Authenticated, h.Sm.State);
            }
        }

        [Fact]
        public void Unicode_password_is_compared_after_nfc_only()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, Harness.OwnerPassword.Normalize(NormalizationForm.FormD)).Result);
                h.Sm.Lock(LockReason.Logout);
                var arabicKaf = Harness.OwnerPassword.Replace('ک', 'ك'); // Persian keheh -> Arabic kaf
                Assert.NotEqual(Harness.OwnerPassword, arabicKaf);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, arabicKaf).Result);
            }
        }

        [Fact]
        public void Device_secret_is_required_password_alone_is_not_enough()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                var device = h.Paths.Installed.DeviceSecretFile;
                var original = File.ReadAllBytes(device);
                var marker = InstallationMarker.Read(h.Paths.MarkerFile);
                File.Delete(device);
                DeviceSecret.CreateFile(device, marker.InstallId, h.Dpapi); // a different device secret, same install id
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
                File.WriteAllBytes(device, original);
                h.Dpapi.MachineKey = CryptoRandom.Bytes(32); // DPAPI of another Windows installation
                h.Sm.Refresh();
                Assert.Equal(AppState.RecoveryRequired, h.Sm.State);
                Assert.Equal(AuthResult.DeviceUnavailable, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
            }
        }

        [Fact]
        public void Stale_swapped_and_cross_installation_envelopes_are_rejected()
        {
            using (var h = new Harness().Setup())
            using (var other = new Harness().Setup())
            {
                var envFiles = Directory.GetFiles(h.Paths.Installed.AccountsDir);
                var oldEnvelope = File.ReadAllBytes(envFiles.Single());
                var newPw = "a different long passphrase 2026 new";
                Assert.True(h.Sm.ChangePassword(Harness.OwnerPassword, newPw).Ok);
                h.Sm.Lock(LockReason.Logout);

                // Stale: an attacker restores the previous envelope file next to the current one.
                File.WriteAllBytes(Path.Combine(h.Paths.Installed.AccountsDir, Guid.NewGuid().ToString("D") + ".pnke"), oldEnvelope);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
                h.Clock.Advance(TimeSpan.FromMinutes(20));
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, newPw).Result);
                Assert.Single(Directory.GetFiles(h.Paths.Installed.AccountsDir)); // orphan/stale file cleaned up
                h.Sm.Lock(LockReason.Logout);

                // Cross-installation: the other installation's owner envelope (same username and password).
                File.Copy(Directory.GetFiles(other.Paths.Installed.AccountsDir).Single(), Path.Combine(h.Paths.Installed.AccountsDir, Guid.NewGuid().ToString("D") + ".pnke"));
                h.Clock.Advance(TimeSpan.FromMinutes(20));
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
                h.Clock.Advance(TimeSpan.FromMinutes(20));
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, newPw).Result);
            }
        }

        [Fact]
        public void Swapped_envelope_of_another_account_does_not_grant_its_identity()
        {
            using (var h = new Harness().Setup())
            {
                var pw = "delegate initial password 2026";
                var id = h.AddDelegate("deputy.one", pw);
                var all = Directory.GetFiles(h.Paths.Installed.AccountsDir);
                h.Sm.Lock(LockReason.Logout);
                // Rename every envelope to another account's file name: lookup uses the authenticated binding, not the file name.
                var tmp = all.Select(f => f + ".tmp").ToArray();
                for (int i = 0; i < all.Length; i++) File.Move(all[i], tmp[i]);
                for (int i = 0; i < all.Length; i++) File.Move(tmp[i], all[(i + 1) % all.Length]);
                Assert.Equal(AuthResult.Success, h.Sm.Login("deputy.one", pw).Result);
                Assert.Equal(id, h.Sm.CurrentAccount.Id);
                Assert.Equal(AccountRole.Delegate, h.Sm.CurrentAccount.Role);
            }
        }

        [Fact]
        public void Delegate_must_change_the_initial_password_before_any_protected_access()
        {
            using (var h = new Harness().Setup())
            {
                var pw = "delegate initial password 2026";
                h.AddDelegate("deputy.one", pw, Permissions.StudentView);
                h.Sm.Lock(LockReason.Logout);
                Assert.True(h.Sm.Login("deputy.one", pw).Ok);
                Assert.Equal(AppState.PasswordChangeRequired, h.Sm.State);
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.Run(h.Sm.Token, Permissions.StudentView, (c, t, a) => 0));
                Assert.Equal(AuthResult.PasswordPolicy, h.Sm.ChangePassword(pw, "short").Result);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.ChangePassword("not the current password", "my own new long password 1405").Result);
                Assert.True(h.Sm.ChangePassword(pw, "my own new long password 1405").Ok);
                Assert.Equal(AppState.Authenticated, h.Sm.State);
                Assert.Equal(1, h.Sm.Run(h.Sm.Token, Permissions.StudentView, (c, t, a) => 1));
            }
        }

        [Fact]
        public void Throttling_is_progressive_survives_restarts_and_ignores_clock_rollback()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                for (int i = 0; i < LoginThrottle.FreeAttempts; i++) Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, "wrong password number " + i).Result);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, "wrong password again!!").Result);
                var t = h.Sm.Login(Harness.Owner, Harness.OwnerPassword);
                Assert.Equal(AuthResult.Throttled, t.Result);
                Assert.Equal(5, (int)Math.Ceiling(t.RetryAfter.TotalSeconds));

                h.Restart(); // new process: the persisted state still applies
                Assert.Equal(AuthResult.Throttled, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);

                h.Clock.UtcNow = h.Clock.UtcNow.AddDays(-1); // clock moved backwards: maximum delay, never a reset
                var back = h.Sm.Login(Harness.Owner, Harness.OwnerPassword);
                Assert.Equal(AuthResult.Throttled, back.Result);
                Assert.True(back.RetryAfter >= TimeSpan.FromSeconds(LoginThrottle.MaxDelaySeconds - 1));

                h.Clock.Advance(TimeSpan.FromSeconds(LoginThrottle.MaxDelaySeconds + 1));
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result); // no permanent lockout
            }
        }

        [Fact]
        public void Tampered_throttle_state_waits_instead_of_resetting()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, "wrong password number 1").Result);
                var bytes = File.ReadAllBytes(h.Paths.ThrottleFile);
                bytes[bytes.Length - 5] ^= 1;
                File.WriteAllBytes(h.Paths.ThrottleFile, bytes);
                Assert.Equal(AuthResult.Throttled, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
            }
        }

        [Fact]
        public void Interrupted_password_change_keeps_a_working_password()
        {
            using (var h = new Harness().Setup())
            {
                // Crash after writing the new envelope but before the database transaction: an orphan file.
                File.WriteAllBytes(Path.Combine(h.Paths.Installed.AccountsDir, Guid.NewGuid().ToString("D") + ".pnke"), new byte[KeyEnvelopeV2.TotalLength]);
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
                Assert.Single(Directory.GetFiles(h.Paths.Installed.AccountsDir));
            }
        }
    }

    public class AuthorizationTests
    {
        private const string DelegatePw = "delegate initial password 2026";
        private const string DelegateNewPw = "delegate chosen password 1405";

        public AuthorizationTests() { TestEnvironment.AssertExpectedArchitecture(); }

        private static Guid SignInDelegate(Harness h, params string[] perms)
        {
            var id = h.AddDelegate("deputy.one", DelegatePw, perms);
            h.Sm.Lock(LockReason.Logout);
            Assert.True(h.Sm.Login("deputy.one", DelegatePw).Ok);
            Assert.True(h.Sm.ChangePassword(DelegatePw, DelegateNewPw).Ok);
            return id;
        }

        [Fact]
        public void Delegate_permissions_are_denied_by_default_and_enforced_in_services()
        {
            using (var h = new Harness().Setup())
            {
                SignInDelegate(h);
                foreach (var p in Permissions.Grantable)
                    Assert.Throws<UnauthorizedOperationException>(() => h.Sm.Run(h.Sm.Token, p, (c, t, a) => 0));
                foreach (var p in Permissions.OwnerOnly.Concat(Permissions.Reserved))
                    Assert.Throws<UnauthorizedOperationException>(() => h.Sm.Run(h.Sm.Token, p, (c, t, a) => 0));
                Assert.Empty(h.Sm.EffectivePermissions());
            }
        }

        [Fact]
        public void Delegate_cannot_administer_accounts_or_escalate()
        {
            using (var h = new Harness().Setup())
            {
                var ownerId = h.Sm.CurrentAccount.Id;
                var me = SignInDelegate(h, Permissions.StudentView, Permissions.BackupCreate);
                var token = h.Sm.Token;
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.ListAccounts(token));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.SetPermissions(token, me, Permissions.Grantable));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.SetRole(token, me, AccountRole.Owner));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.SetAccountStatus(token, ownerId, AccountStatus.Disabled));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.ResetDelegatePassword(token, ownerId, "owner takeover password 2026"));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.RotateRecoveryKey(token));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.ReadAudit(token, 10));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.CreateDelegate(token, "sneaky.one", "x", "another long password 1405", out _));
                Assert.Equal(1, h.Sm.Run(token, Permissions.StudentView, (c, t, a) => 1));
                Assert.Equal(AccountRole.Delegate, h.Sm.CurrentAccount.Role);
            }
        }

        [Fact]
        public void Owner_cannot_grant_owner_only_or_reserved_permissions()
        {
            using (var h = new Harness().Setup())
            {
                var id = h.AddDelegate("deputy.one", DelegatePw);
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.SetPermissions(h.Sm.Token, id, new[] { Permissions.AuditView }));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.SetPermissions(h.Sm.Token, id, new[] { "report.view" }));
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.SetPermissions(h.Sm.Token, id, new[] { "anything.else" }));
            }
        }

        [Fact]
        public void Permission_and_status_changes_apply_to_the_live_session()
        {
            using (var h = new Harness().Setup())
            {
                var id = h.AddDelegate("deputy.one", DelegatePw, Permissions.StudentView);
                h.Sm.Lock(LockReason.Logout);
                Assert.True(h.Sm.Login("deputy.one", DelegatePw).Ok);
                Assert.True(h.Sm.ChangePassword(DelegatePw, DelegateNewPw).Ok);
                var token = h.Sm.Token;
                Assert.Equal(1, h.Sm.Run(token, Permissions.StudentView, (c, t, a) => 1));
                // The owner (on this PC: via the same database) removes the permission; the next call is refused.
                h.Sm.Run(token, null, (c, t, a) => { c.CreateCommand(); var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = "DELETE FROM account_permission;"; cmd.ExecuteNonQuery(); return 0; });
                Assert.Throws<UnauthorizedOperationException>(() => h.Sm.Run(token, Permissions.StudentView, (c, t, a) => 1));
                // Disabled while signed in: the session ends.
                h.Sm.Run(token, null, (c, t, a) => { var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = "UPDATE account SET status=2 WHERE id='" + id.ToString("D") + "';"; cmd.ExecuteNonQuery(); return 0; });
                Assert.Throws<SessionExpiredException>(() => h.Sm.Run(token, null, (c, t, a) => 0));
                Assert.False(h.Sm.HasSession);
                Assert.Equal(AuthResult.AccountDisabled, h.Sm.Login("deputy.one", DelegateNewPw).Result);
            }
        }

        [Fact]
        public void Last_active_owner_cannot_be_disabled_deleted_or_demoted()
        {
            using (var h = new Harness().Setup())
            {
                var me = h.Sm.CurrentAccount.Id;
                Assert.Throws<LastOwnerException>(() => h.Sm.SetAccountStatus(h.Sm.Token, me, AccountStatus.Disabled));
                Assert.Throws<LastOwnerException>(() => h.Sm.SetAccountStatus(h.Sm.Token, me, AccountStatus.Deleted));
                Assert.Throws<LastOwnerException>(() => h.Sm.SetRole(h.Sm.Token, me, AccountRole.Delegate));
                var second = h.AddDelegate("second.owner", DelegatePw);
                h.Sm.SetRole(h.Sm.Token, second, AccountRole.Owner);
                h.Sm.SetAccountStatus(h.Sm.Token, second, AccountStatus.Disabled); // allowed: another active owner remains
                Assert.Throws<LastOwnerException>(() => h.Sm.SetRole(h.Sm.Token, me, AccountRole.Delegate));
            }
        }

        [Fact]
        public void Deleted_account_envelope_is_removed_and_a_restored_copy_is_rejected()
        {
            using (var h = new Harness().Setup())
            {
                var id = h.AddDelegate("deputy.one", DelegatePw);
                var copies = Directory.GetFiles(h.Paths.Installed.AccountsDir).Select(File.ReadAllBytes).ToList();
                h.Sm.SetAccountStatus(h.Sm.Token, id, AccountStatus.Deleted);
                Assert.Single(Directory.GetFiles(h.Paths.Installed.AccountsDir));
                foreach (var c in copies) File.WriteAllBytes(Path.Combine(h.Paths.Installed.AccountsDir, Guid.NewGuid().ToString("D") + ".pnke"), c);
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login("deputy.one", DelegatePw).Result);
            }
        }

        [Fact]
        public void Lock_invalidates_tokens_and_queued_operations()
        {
            using (var h = new Harness().Setup())
            {
                var token = h.Sm.Token;
                Assert.Equal(1, h.Sm.Run(token, Permissions.StudentView, (c, t, a) => 1));
                h.Sm.Lock(LockReason.Explicit);
                Assert.Throws<SessionExpiredException>(() => h.Sm.Run(token, Permissions.StudentView, (c, t, a) => 1));
                Assert.True(h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Ok);
                Assert.Throws<SessionExpiredException>(() => h.Sm.Run(token, Permissions.StudentView, (c, t, a) => 1)); // old token stays dead
                // An operation that locks the session mid-way never commits.
                var t2 = h.Sm.Token;
                Assert.Throws<SessionExpiredException>(() => h.Sm.Run(t2, null, (c, t, a) =>
                {
                    var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = "UPDATE account SET display_name='changed';"; cmd.ExecuteNonQuery();
                    h.Sm.Lock(LockReason.WindowsLock);
                    return 0;
                }));
                Assert.True(h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Ok);
                Assert.DoesNotContain(h.Sm.ListAccounts(h.Sm.Token), a => a.DisplayName == "changed");
            }
        }
    }

    public class RecoveryTests
    {
        private const string NewOwnerPw = "recovered owner password 1405 ok";

        public RecoveryTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Correct_recovery_key_sets_a_new_password_rotates_the_key_and_kills_the_old_password()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                var r = h.Sm.RecoverOwner(Harness.Owner, h.RecoveryCode, NewOwnerPw);
                Assert.True(r.Ok);
                Assert.NotEqual(h.RecoveryCode, r.RecoveryKeyCode);
                Assert.Equal(AppState.RecoveryKeyConfirmationRequired, h.Sm.State);
                Assert.True(h.Sm.ConfirmRecoveryKeySaved(r.RecoveryKeyCode.Split('-').Last(), r.RecoveryKeyCode));
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result);
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, NewOwnerPw).Result);
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.RecoverOwner(Harness.Owner, h.RecoveryCode, NewOwnerPw + "x").Result); // old key is dead
                Assert.Single(Directory.GetFiles(h.Paths.Installed.RecoveryDir));
                Assert.Single(Directory.GetFiles(h.Paths.Installed.AccountsDir));
            }
        }

        [Fact]
        public void Wrong_tampered_and_mistyped_recovery_keys_are_rejected()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.RecoverOwner(Harness.Owner, TypedCode.Encode(CryptoRandom.Bytes(32), TypedCode.RecoveryKeyPurpose), NewOwnerPw).Result);
                var typo = h.RecoveryCode.Substring(0, 3) + (h.RecoveryCode[3] == 'A' ? 'B' : 'A') + h.RecoveryCode.Substring(4);
                Assert.Equal(AuthResult.InvalidCode, h.Sm.RecoverOwner(Harness.Owner, typo, NewOwnerPw).Result);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.RecoverOwner("nobody.else", h.RecoveryCode, NewOwnerPw).Result);
                var rec = Directory.GetFiles(h.Paths.Installed.RecoveryDir).Single();
                var bytes = File.ReadAllBytes(rec); bytes[140] ^= 1; File.WriteAllBytes(rec, bytes);
                h.Clock.Advance(TimeSpan.FromMinutes(20));
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.RecoverOwner(Harness.Owner, h.RecoveryCode, NewOwnerPw).Result);
                h.Clock.Advance(TimeSpan.FromMinutes(20));
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result); // normal password unaffected
            }
        }

        [Fact]
        public void Recovery_after_windows_reinstall_rebinds_the_device_and_requires_delegate_resets()
        {
            using (var h = new Harness().Setup())
            {
                var deputy = h.AddDelegate("deputy.one", "delegate initial password 2026", Permissions.StudentView);
                h.Sm.Lock(LockReason.Logout);
                h.Dpapi.MachineKey = CryptoRandom.Bytes(32); // Windows reinstalled: DPAPI LocalMachine secrets are gone
                h.Restart();
                Assert.Equal(AppState.RecoveryRequired, h.Sm.State);
                // The new DPAPI key cannot verify the old throttle file: treated like tampering (maximum wait, never a reset).
                Assert.Equal(AuthResult.Throttled, h.Sm.RecoverOwner(Harness.Owner, h.RecoveryCode, NewOwnerPw).Result);
                h.Clock.Advance(TimeSpan.FromSeconds(LoginThrottle.MaxDelaySeconds + 1));
                var r = h.Sm.RecoverOwner(Harness.Owner, h.RecoveryCode, NewOwnerPw);
                Assert.Equal(AuthResult.Success, r.Result);
                Assert.True(h.Sm.ConfirmRecoveryKeySaved(r.RecoveryKeyCode.Split('-').Last(), r.RecoveryKeyCode));
                var accounts = h.Sm.ListAccounts(h.Sm.Token);
                var d = accounts.Single(a => a.Id == deputy);
                Assert.False(d.HasPassword);
                Assert.True(d.MustChangePassword);
                Assert.Equal(new[] { Permissions.StudentView }, d.Permissions); // permissions are kept; only the password must be reset
                h.Sm.Lock(LockReason.Logout);
                h.Restart();
                Assert.Equal(AppState.Locked, h.Sm.State);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.Login("deputy.one", "delegate initial password 2026").Result);
                Assert.Equal(AuthResult.Success, h.Sm.Login(Harness.Owner, NewOwnerPw).Result);
                Assert.Equal(AuthResult.Success, h.Sm.ResetDelegatePassword(h.Sm.Token, deputy, "delegate reset password 1405").Result);
            }
        }

        [Fact]
        public void Recovery_key_rotation_is_owner_only_and_invalidates_the_previous_key()
        {
            using (var h = new Harness().Setup())
            {
                var r = h.Sm.RotateRecoveryKey(h.Sm.Token);
                Assert.True(r.Ok);
                Assert.Equal(AppState.RecoveryKeyConfirmationRequired, h.Sm.State);
                Assert.True(h.Sm.ConfirmRecoveryKeySaved(r.RecoveryKeyCode.Split('-').Last(), r.RecoveryKeyCode));
                h.Sm.Lock(LockReason.Logout);
                Assert.Equal(AuthResult.InvalidCredentials, h.Sm.RecoverOwner(Harness.Owner, h.RecoveryCode, NewOwnerPw).Result);
                h.Clock.Advance(TimeSpan.FromMinutes(20));
                Assert.True(h.Sm.RecoverOwner(Harness.Owner, r.RecoveryKeyCode, NewOwnerPw).Ok);
            }
        }

        [Fact]
        public void No_password_recovery_key_bootstrap_code_or_lmk_material_is_written_to_disk_in_plaintext()
        {
            using (var h = new Harness())
            {
                h.License();
                var prov = h.Issuer.Provisioning(h.LicenseFile, Harness.Owner, out var code);
                h.Sm.LoadProvisioning(prov);
                var r = h.Sm.CompleteSetup(code, Harness.OwnerPassword);
                var rk = TypedCode.Decode(r.RecoveryKeyCode, 32, TypedCode.RecoveryKeyPurpose);
                var boot = TypedCode.Decode(code, 20, TypedCode.BootstrapPurpose);
                var secrets = new[]
                {
                    rk, boot, Encoding.UTF8.GetBytes(Harness.OwnerPassword), Encoding.UTF8.GetBytes(r.RecoveryKeyCode), Encoding.UTF8.GetBytes(code),
                    Encoding.Unicode.GetBytes(Harness.OwnerPassword),
                };
                h.Sm.Lock(LockReason.Logout);
                foreach (var (path, bytes) in h.AllFiles())
                    foreach (var s in secrets)
                        Assert.False(Contains(bytes, s), "secret material found in " + Path.GetFileName(path));
                Assert.DoesNotContain(Directory.EnumerateFiles(h.Paths.Root, "*", SearchOption.AllDirectories), f => f.EndsWith(".tmp", StringComparison.Ordinal) || f.Contains("staging"));
            }
        }

        private static bool Contains(byte[] h, byte[] n)
        {
            for (int i = 0; i <= h.Length - n.Length; i++) { int j = 0; while (j < n.Length && h[i + j] == n[j]) j++; if (j == n.Length) return true; }
            return false;
        }
    }

    public class SessionTests
    {
        public SessionTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Inactivity_lock_happens_at_exactly_fifteen_minutes_of_monotonic_time()
        {
            using (var h = new Harness().Setup())
            {
                Assert.Equal(TimeSpan.FromMinutes(15), h.Sm.InactivityTimeout);
                h.Clock.MonotonicMilliseconds += 15 * 60 * 1000 - 1;
                Assert.False(h.Sm.CheckInactivity());
                h.Sm.Touch();
                h.Clock.MonotonicMilliseconds += 15 * 60 * 1000 - 1;
                h.Clock.UtcNow = h.Clock.UtcNow.AddHours(5); // wall clock changes are irrelevant
                Assert.False(h.Sm.CheckInactivity());
                h.Clock.MonotonicMilliseconds += 1;
                LockReason? reason = null;
                h.Sm.Locked += (s, r) => reason = r;
                Assert.True(h.Sm.CheckInactivity());
                Assert.Equal(LockReason.Inactivity, reason);
                Assert.Equal(AppState.Locked, h.Sm.State);
            }
        }

        [Fact]
        public void Lock_closes_the_database_and_disposes_the_session()
        {
            using (var h = new Harness().Setup())
            {
                var db = h.Paths.Installed.Database;
                Assert.Throws<IOException>(() => File.Move(db, db + ".moved")); // open while signed in
                h.Sm.Lock(LockReason.Suspend);
                Assert.False(h.Sm.HasSession);
                Assert.Null(h.Sm.CurrentAccount);
                Assert.Null(h.Sm.Token);
                File.Move(db, db + ".moved"); // no handle left: the connection is closed
                File.Move(db + ".moved", db);
                Assert.Empty(h.Sm.EffectivePermissions());
            }
        }

        [Fact]
        public void Concurrent_kdf_work_is_serialized()
        {
            using (var h = new Harness().Setup())
            {
                h.Sm.Lock(LockReason.Logout);
                var tasks = Enumerable.Range(0, 4).Select(_ => System.Threading.Tasks.Task.Run(() => h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Result)).ToArray();
                System.Threading.Tasks.Task.WaitAll(tasks);
                var results = tasks.Select(t => t.Result).ToList();
                Assert.Contains(AuthResult.Success, results);
                Assert.All(results, r => Assert.True(r == AuthResult.Success || r == AuthResult.Busy || r == AuthResult.WrongState, r.ToString()));
            }
        }
    }

    public class AuditTests
    {
        public AuditTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Audit_chain_records_security_events_and_verifies()
        {
            using (var h = new Harness().Setup())
            {
                h.AddDelegate("deputy.one", "delegate initial password 2026", Permissions.StudentView);
                h.Sm.Lock(LockReason.Logout);
                h.Sm.Login(Harness.Owner, "wrong password for the audit");
                Assert.True(h.Sm.Login(Harness.Owner, Harness.OwnerPassword).Ok);
                var v = h.Sm.VerifyAudit(h.Sm.Token);
                Assert.True(v.Intact, v.Problem);
                var events = h.Sm.ReadAudit(h.Sm.Token, 100);
                var kinds = events.Select(e => e.Kind).ToList();
                foreach (var k in new[] { AuditKinds.Setup, AuditKinds.AccountCreate, AuditKinds.PermissionChange, AuditKinds.Logout, AuditKinds.LoginFailure, AuditKinds.LoginSuccess, AuditKinds.RecoveryKeyConfirmed })
                    Assert.Contains(k, kinds);
                Assert.Contains(events, e => e.Source == "pre-unlock" && e.Kind == AuditKinds.LoginFailure);
                var all = string.Join("\n", events.Select(e => e.Kind + e.Subject + e.Outcome + e.Detail));
                Assert.DoesNotContain(Harness.OwnerPassword, all);
                Assert.DoesNotContain("wrong password for the audit", all);
                Assert.DoesNotContain(h.RecoveryCode, all);
                Assert.DoesNotContain("S4EWNX0R", all);
            }
        }

        [Fact]
        public void Modified_deleted_and_truncated_events_are_detected()
        {
            using (var h = new Harness().Setup())
            {
                for (int i = 0; i < 5; i++) h.Sm.Run(h.Sm.Token, null, (c, t, a) => 0);
                h.Sm.Lock(LockReason.Logout); h.Sm.Login(Harness.Owner, Harness.OwnerPassword);
                void Tamper(string sql) => h.Sm.Run(h.Sm.Token, null, (c, t, a) => { var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = sql; return cmd.ExecuteNonQuery(); });
                Assert.True(h.Sm.VerifyAudit(h.Sm.Token).Intact);
                Tamper("UPDATE audit_event SET outcome='forged' WHERE seq=2;");
                var v = h.Sm.VerifyAudit(h.Sm.Token);
                Assert.False(v.Intact);
                Assert.Equal(2, v.FirstBadSeq);
            }
            using (var h = new Harness().Setup())
            {
                h.Sm.Run(h.Sm.Token, null, (c, t, a) => { var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = "DELETE FROM audit_event WHERE seq=3;"; return cmd.ExecuteNonQuery(); });
                Assert.False(h.Sm.VerifyAudit(h.Sm.Token).Intact);
            }
            using (var h = new Harness().Setup())
            {
                h.Sm.Run(h.Sm.Token, null, (c, t, a) => { var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = "DELETE FROM audit_event WHERE seq=(SELECT max(seq) FROM audit_event);"; return cmd.ExecuteNonQuery(); });
                Assert.False(h.Sm.VerifyAudit(h.Sm.Token).Intact);
            }
        }

        [Fact]
        public void Log_growth_is_bounded_and_pruned_chain_still_verifies()
        {
            using (var dir = new TempDir("audit"))
            {
                var key = CryptoRandom.Bytes(32);
                using (var c = ParvNegar.Storage.EncryptedDatabase.Create(dir.File("a.db"), CryptoRandom.Bytes(32)))
                {
                    ParvNegar.Storage.MigrationRunner.Migrate(c, ParvNegar.Storage.MigrationRunner.Embedded());
                    using (var tx = c.BeginTransaction()) { AuditLog.Initialize(c, tx); tx.Commit(); }
                    var log = new AuditLog(c, key, new TestClock(), maxEvents: 20);
                    for (int i = 0; i < 75; i++) log.Append("test.event", null, "s" + i, "ok");
                    var events = log.Read(null);
                    Assert.Equal(20, events.Count);
                    Assert.Equal(56, events.First().Seq);
                    Assert.True(log.Verify().Intact);
                    Assert.False(new AuditLog(c, CryptoRandom.Bytes(32), new TestClock()).Verify().Intact); // wrong key
                }
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }
        }

        [Fact]
        public void Pre_unlock_journal_is_bounded_and_tampering_is_flagged()
        {
            using (var dir = new TempDir("journal"))
            {
                var paths = new InstallationPaths(dir.Path);
                var j = new PreUnlockJournal(paths, new GuardKey(paths, new FakeDpapi()), new TestClock());
                for (int i = 0; i < PreUnlockJournal.Capacity + 40; i++) j.Append("auth.login_failed", "u" + i, "InvalidCredentials");
                var all = j.ReadAll(out bool intact);
                Assert.True(intact);
                Assert.Equal(PreUnlockJournal.Capacity, all.Count);
                var lines = File.ReadAllLines(paths.JournalFile);
                lines[10] = lines[10].Replace("InvalidCredentials", "Success");
                File.WriteAllLines(paths.JournalFile, lines);
                j.ReadAll(out intact);
                Assert.False(intact);
            }
        }
    }
}
