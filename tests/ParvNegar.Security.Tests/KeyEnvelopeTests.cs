using System;
using System.IO;
using System.Linq;
using System.Text;
using ParvNegar.Security;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Security.Tests
{
    public class KeyEnvelopeTests
    {
        private static readonly Argon2idParameters Fast = new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 2, 1);
        private static readonly byte[] Pw = Encoding.UTF8.GetBytes("synthetic-رمز-1");
        private readonly Guid _install = Guid.NewGuid();
        private readonly Guid _account = Guid.NewGuid();

        public KeyEnvelopeTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Password_and_device_roundtrip_returns_same_master_key()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                Assert.Equal(KeyEnvelope.TotalLength, env.Length);
                Assert.Equal(_account, KeyEnvelope.ReadAccountId(env));
                using (var back = KeyEnvelope.UnwrapWithPasswordAndDevice(env, Pw, dev, _install))
                    Assert.Equal(Bytes.ToHex(lmk.DatabaseKey()), Bytes.ToHex(back.DatabaseKey()));
            }
        }

        [Fact]
        public void Device_factor_alone_never_unlocks()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(env, new byte[0], dev, _install));
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(env, Encoding.UTF8.GetBytes("wrong"), dev, _install));
            }
        }

        [Fact]
        public void Password_alone_never_unlocks_without_the_device_secret()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(env, Pw, CryptoRandom.Bytes(32), _install));
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(env, Pw, null, _install));
            }
        }

        [Fact]
        public void Envelope_from_another_installation_is_refused()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                Assert.Throws<MalformedDataException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(env, Pw, dev, Guid.NewGuid()));
            }
        }

        [Fact]
        public void Every_byte_of_the_envelope_is_authenticated_or_validated()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                for (int i = 0; i < env.Length; i++)
                {
                    if (i >= 6 && i < 18) continue; // KDF parameter bytes: covered by the bounds test below (huge values would be slow)
                    var t = (byte[])env.Clone(); t[i] ^= 0x01;
                    Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(t, Pw, dev, _install));
                }
            }
        }

        [Fact]
        public void Kdf_parameters_are_bounds_checked_and_authenticated()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                var huge = (byte[])env.Clone(); Bytes.WriteUInt32BE(huge, 6, 4u * 1024 * 1024); // 4 GiB request
                Assert.Throws<MalformedDataException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(huge, Pw, dev, _install));
                var changed = (byte[])env.Clone(); Bytes.WriteUInt32BE(changed, 10, 3); // in bounds but not what was sealed
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(changed, Pw, dev, _install));
            }
        }

        [Fact]
        public void Malformed_envelopes_are_rejected()
        {
            var dev = CryptoRandom.Bytes(32);
            Assert.Throws<MalformedDataException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(new byte[10], Pw, dev, _install));
            Assert.Throws<MalformedDataException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(null, Pw, dev, _install));
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                var v2 = (byte[])env.Clone(); v2[4] = 2;
                Assert.Throws<MalformedDataException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(v2, Pw, dev, _install));
                var rec = KeyEnvelope.WrapWithRecoveryKey(lmk, CryptoRandom.Bytes(32), _install);
                Assert.Throws<MalformedDataException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(rec, Pw, dev, _install));
            }
        }

        [Fact]
        public void Password_change_rewraps_and_old_wrap_still_opens_until_deleted()
        {
            // Documents the lifecycle limit: deleting an old wrap does not revoke key material already extracted.
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var oldEnv = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                var newPw = Encoding.UTF8.GetBytes("synthetic-new-2");
                byte[] newEnv;
                using (var opened = KeyEnvelope.UnwrapWithPasswordAndDevice(oldEnv, Pw, dev, _install))
                    newEnv = KeyEnvelope.WrapWithPasswordAndDevice(opened, newPw, dev, _account, _install, Fast);
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelope.UnwrapWithPasswordAndDevice(newEnv, Pw, dev, _install));
                using (var n = KeyEnvelope.UnwrapWithPasswordAndDevice(newEnv, newPw, dev, _install))
                    Assert.Equal(Bytes.ToHex(lmk.AuditKey()), Bytes.ToHex(n.AuditKey()));
                using (var o = KeyEnvelope.UnwrapWithPasswordAndDevice(oldEnv, Pw, dev, _install))
                    Assert.Equal(Bytes.ToHex(lmk.AuditKey()), Bytes.ToHex(o.AuditKey()));
            }
        }

        [Fact]
        public void Two_wraps_of_the_same_key_differ_in_salt_and_nonce()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var a = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                var b = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                Assert.NotEqual(Bytes.ToHex(a.Skip(18).Take(16).ToArray()), Bytes.ToHex(b.Skip(18).Take(16).ToArray()));
                Assert.NotEqual(Bytes.ToHex(a.Skip(66).Take(12).ToArray()), Bytes.ToHex(b.Skip(66).Take(12).ToArray()));
            }
        }

        [Fact]
        public void Recovery_key_envelope_roundtrip_and_wrong_key()
        {
            var rk = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var env = KeyEnvelope.WrapWithRecoveryKey(lmk, rk, _install);
                using (var back = KeyEnvelope.UnwrapWithRecoveryKey(env, rk, _install))
                    Assert.Equal(Bytes.ToHex(lmk.DatabaseKey()), Bytes.ToHex(back.DatabaseKey()));
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelope.UnwrapWithRecoveryKey(env, CryptoRandom.Bytes(32), _install));
            }
        }

        [Fact]
        public void Dpapi_device_secret_file_roundtrip_and_tamper()
        {
            using (var d = new TempDir("devsecret"))
            {
                var p = d.File("device.secret");
                var protector = new DpapiLocalMachineProtector();
                var secret = DeviceSecret.CreateFile(p, _install, protector);
                Assert.Equal(Bytes.ToHex(secret), Bytes.ToHex(DeviceSecret.LoadFile(p, _install, protector)));
                Assert.Throws<IOException>(() => DeviceSecret.CreateFile(p, _install, protector));
                Assert.Throws<MalformedDataException>(() => DeviceSecret.LoadFile(p, Guid.NewGuid(), protector));
                var bytes = File.ReadAllBytes(p);
                bytes[bytes.Length - 5] ^= 0x01;
                File.WriteAllBytes(p, bytes);
                Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => DeviceSecret.LoadFile(p, _install, protector));
            }
        }

        [Fact]
        public void Device_secret_file_contains_no_plain_secret()
        {
            using (var d = new TempDir("devsecret2"))
            {
                var p = d.File("device.secret");
                var secret = DeviceSecret.CreateFile(p, _install, new DpapiLocalMachineProtector());
                var hex = Bytes.ToHex(File.ReadAllBytes(p));
                Assert.DoesNotContain(Bytes.ToHex(secret), hex);
            }
        }
    }
}
