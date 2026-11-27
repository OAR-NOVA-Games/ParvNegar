using System;
using System.Linq;
using System.Text;
using ParvNegar.Security;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Security.Tests
{
    public class KeyEnvelopeV2Tests
    {
        private static readonly Argon2idParameters Fast = new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 2, 1);
        private static readonly byte[] Pw = Encoding.UTF8.GetBytes("synthetic long password ۱۲۳۴");
        private readonly Guid _install = Guid.NewGuid(), _account = Guid.NewGuid();

        public KeyEnvelopeV2Tests() { TestEnvironment.AssertExpectedArchitecture(); }

        private EnvelopeBinding Binding(uint rev = 1, string user = "owner.one", Guid? env = null)
            => new EnvelopeBinding(_account, _install, env ?? Guid.NewGuid(), rev, EnvelopeBinding.HashUsername(user));

        [Fact]
        public void Round_trip_returns_the_authenticated_binding()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var b = Binding(7);
                var e = KeyEnvelopeV2.WrapWithPasswordAndDevice(lmk, Pw, dev, b, Fast);
                Assert.Equal(KeyEnvelopeV2.TotalLength, e.Length);
                using (var back = KeyEnvelopeV2.UnwrapWithPasswordAndDevice(e, Pw, dev, _install, out var got))
                {
                    Assert.Equal(Bytes.ToHex(lmk.DatabaseKey()), Bytes.ToHex(back.DatabaseKey()));
                    Assert.Equal(b.EnvelopeId, got.EnvelopeId);
                    Assert.Equal(7u, got.Revision);
                    Assert.Equal(b.UsernameHash, got.UsernameHash);
                    Assert.Equal(_account, got.AccountId);
                }
            }
        }

        [Fact]
        public void Password_only_device_only_and_cross_installation_unlocks_fail()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var e = KeyEnvelopeV2.WrapWithPasswordAndDevice(lmk, Pw, dev, Binding(), Fast);
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelopeV2.UnwrapWithPasswordAndDevice(e, Pw, CryptoRandom.Bytes(32), _install, out _)); // password without the device secret
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelopeV2.UnwrapWithPasswordAndDevice(e, Encoding.UTF8.GetBytes("wrong password entirely"), dev, _install, out _)); // device without the password
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelopeV2.UnwrapWithPasswordAndDevice(e, null, dev, _install, out _));
                Assert.Throws<MalformedDataException>(() => KeyEnvelopeV2.UnwrapWithPasswordAndDevice(e, Pw, dev, Guid.NewGuid(), out _));
            }
        }

        [Fact]
        public void Every_bound_field_is_authenticated()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var e = KeyEnvelopeV2.WrapWithPasswordAndDevice(lmk, Pw, dev, Binding(3), Fast);
                // accountId, envelopeId, revision, usernameHash (installId is checked before decryption)
                foreach (var pos in new[] { 34, 49, 66, 81, 85, 86, 117 })
                {
                    var t = (byte[])e.Clone(); t[pos] ^= 0x01;
                    if (pos >= 82 && pos <= 85 && Bytes.ReadUInt32BE(t, 82) == 0) continue;
                    Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => KeyEnvelopeV2.UnwrapWithPasswordAndDevice(t, Pw, dev, _install, out _));
                }
            }
        }

        [Fact]
        public void Recovery_envelope_round_trip_and_rejections()
        {
            var rk = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var envId = Guid.NewGuid();
                var e = KeyEnvelopeV2.WrapWithRecoveryKey(lmk, rk, _install, envId, 2);
                using (var back = KeyEnvelopeV2.UnwrapWithRecoveryKey(e, rk, _install, out var b))
                {
                    Assert.Equal(Bytes.ToHex(lmk.AuditKey()), Bytes.ToHex(back.AuditKey()));
                    Assert.Equal(envId, b.EnvelopeId);
                    Assert.Equal(Guid.Empty, b.AccountId);
                }
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelopeV2.UnwrapWithRecoveryKey(e, CryptoRandom.Bytes(32), _install, out _));
                var t = (byte[])e.Clone(); t[150] ^= 1;
                Assert.Throws<AuthenticationFailedException>(() => KeyEnvelopeV2.UnwrapWithRecoveryKey(t, rk, _install, out _));
                Assert.Throws<MalformedDataException>(() => KeyEnvelopeV2.UnwrapWithPasswordAndDevice(e, Pw, CryptoRandom.Bytes(32), _install, out _));
            }
        }

        [Fact]
        public void Version_1_envelopes_are_not_accepted_by_the_v2_reader()
        {
            var dev = CryptoRandom.Bytes(32);
            using (var lmk = LocalMasterKey.Generate())
            {
                var v1 = KeyEnvelope.WrapWithPasswordAndDevice(lmk, Pw, dev, _account, _install, Fast);
                Assert.Throws<MalformedDataException>(() => KeyEnvelopeV2.UnwrapWithPasswordAndDevice(v1, Pw, dev, _install, out _));
            }
        }
    }

    public class PasswordPolicyTests
    {
        public PasswordPolicyTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Theory]
        [InlineData("short pass 123", PasswordProblem.TooShort)]          // 14 code points
        [InlineData("password12345678", PasswordProblem.Common)]
        [InlineData("Password 1234 5678", PasswordProblem.Common)]
        [InlineData("aaaaaaaaaaaaaaaaaaaa", PasswordProblem.Repetitive)]
        [InlineData("abcabcabcabcabcabc", PasswordProblem.Repetitive)]
        [InlineData("123456789012345678", PasswordProblem.Repetitive)]
        [InlineData("qwertyuiopasdfgh", PasswordProblem.Common)]
        [InlineData("tab\tinside the password", PasswordProblem.InvalidCharacters)]
        [InlineData("owner.karimi-2026!!", PasswordProblem.ContainsUsername)]
        public void Rejections(string pw, PasswordProblem expected)
            => Assert.Equal(expected, PasswordPolicy.Check(pw, "owner.karimi", out _));

        [Fact]
        public void Length_counts_code_points_after_nfc_and_never_truncates()
        {
            var fourteenEmoji = string.Concat(Enumerable.Repeat("😀a", 7)); // 14 code points, 21 UTF-16 units
            Assert.Equal(PasswordProblem.TooShort, PasswordPolicy.Check(fourteenEmoji, null, out _));
            var exact128 = string.Concat(Enumerable.Range(0, 128).Select(i => (char)('a' + i % 26)));
            exact128 = "Z" + exact128.Substring(1);
            Assert.Equal(PasswordProblem.None, PasswordPolicy.Check(exact128, null, out var b128));
            Assert.Equal(128, b128.Length);
            Assert.Equal(PasswordProblem.TooLong, PasswordPolicy.Check(exact128 + "q", null, out _));
            // NFD input is composed: "é" as e + U+0301 counts as one character and equals the NFC form.
            var nfd = "Café au lait demain soir";
            var nfc = "Café au lait demain soir";
            Assert.Equal(PasswordProblem.None, PasswordPolicy.Check(nfd, null, out var a));
            Assert.Equal(PasswordProblem.None, PasswordPolicy.Check(nfc, null, out var c));
            Assert.Equal(a, c);
            Assert.Equal(PasswordPolicy.LoginBytes(nfd), PasswordPolicy.LoginBytes(nfc));
        }

        [Fact]
        public void Persian_and_mixed_script_passwords_are_accepted_without_letter_unification()
        {
            Assert.Equal(PasswordProblem.None, PasswordPolicy.Check("باغ سبز کنار رودخانه ۱۴۰۵", null, out var persian));
            Assert.Equal("باغ سبز کنار رودخانه ۱۴۰۵", Encoding.UTF8.GetString(persian));
            // Arabic Kaf (U+0643) and Persian Keheh (U+06A9) stay different: no search-style normalization.
            Assert.NotEqual(PasswordPolicy.LoginBytes("كتابخانه مدرسه شاهد"), PasswordPolicy.LoginBytes("کتابخانه مدرسه شاهد"));
            Assert.Equal(PasswordProblem.InvalidCharacters, PasswordPolicy.Check("valid start \ud800 and more text", null, out _));
            Assert.True(PasswordPolicy.BlocklistCount > 100);
        }
    }
}
