using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using ParvNegar.Security;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Security.Tests
{
    /// <summary>Published test vectors; any mismatch means the primitive or its wiring is wrong.</summary>
    public class KnownAnswerTests
    {
        public KnownAnswerTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Hkdf_sha256_rfc5869_case1()
        {
            var ikm = Enumerable.Repeat((byte)0x0b, 22).ToArray();
            var salt = Bytes.FromHex("000102030405060708090a0b0c");
            var info = Bytes.FromHex("f0f1f2f3f4f5f6f7f8f9");
            var okm = Hkdf.DeriveKey(ikm, salt, info, 42);
            Assert.Equal("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865", Bytes.ToHex(okm));
        }

        [Fact]
        public void Aes256Gcm_mcgrew_viega_case16()
        {
            var k = Bytes.FromHex("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308");
            var iv = Bytes.FromHex("cafebabefacedbaddecaf888");
            var p = Bytes.FromHex("d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39");
            var a = Bytes.FromHex("feedfacedeadbeeffeedfacedeadbeefabaddad2");
            var ct = AesGcm256.Encrypt(k, iv, a, p);
            Assert.Equal("522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f662" + "76fc6ece0f4e1768cddf8853bb2d551b", Bytes.ToHex(ct));
            Assert.Equal(Bytes.ToHex(p), Bytes.ToHex(AesGcm256.Decrypt(k, iv, a, ct)));
        }

        [Fact]
        public void Aes256Gcm_rejects_ciphertext_tag_nonce_and_aad_tamper()
        {
            var k = CryptoRandom.Bytes(32); var n = CryptoRandom.Bytes(12); var aad = Encoding.ASCII.GetBytes("aad");
            var ct = AesGcm256.Encrypt(k, n, aad, Encoding.UTF8.GetBytes("پرونگار synthetic"));
            for (int i = 0; i < ct.Length; i++)
            {
                var t = (byte[])ct.Clone(); t[i] ^= 0x01;
                Assert.Throws<AuthenticationFailedException>(() => AesGcm256.Decrypt(k, n, aad, t));
            }
            Assert.Throws<AuthenticationFailedException>(() => AesGcm256.Decrypt(k, n, Encoding.ASCII.GetBytes("aaD"), ct));
            var n2 = (byte[])n.Clone(); n2[11] ^= 1;
            Assert.Throws<AuthenticationFailedException>(() => AesGcm256.Decrypt(k, n2, aad, ct));
            Assert.Throws<MalformedDataException>(() => AesGcm256.Decrypt(k, n, aad, new byte[15]));
        }

        [Fact]
        public void Argon2id_rfc9106_section_5_3()
        {
            var p = new Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Builder(Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Argon2id)
                .WithVersion(Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Version13).WithIterations(3).WithMemoryAsKB(32).WithParallelism(4)
                .WithSalt(Enumerable.Repeat((byte)2, 16).ToArray()).WithSecret(Enumerable.Repeat((byte)3, 8).ToArray())
                .WithAdditional(Enumerable.Repeat((byte)4, 12).ToArray()).Build();
            var g = new Argon2BytesGenerator(); g.Init(p);
            var tag = new byte[32];
            g.GenerateBytes(Enumerable.Repeat((byte)1, 32).ToArray(), tag);
            Assert.Equal("0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659", Bytes.ToHex(tag));
        }

        [Fact]
        public void Argon2id_wrapper_is_deterministic_and_salt_sensitive()
        {
            var p = new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 2, 1);
            var pw = Encoding.UTF8.GetBytes("synthetic-پسورد");
            var s1 = Enumerable.Repeat((byte)7, 16).ToArray();
            var a = Argon2id.Derive(pw, s1, p); var b = Argon2id.Derive(pw, s1, p);
            Assert.Equal(Bytes.ToHex(a), Bytes.ToHex(b));
            var s2 = (byte[])s1.Clone(); s2[0] ^= 1;
            Assert.NotEqual(Bytes.ToHex(a), Bytes.ToHex(Argon2id.Derive(pw, s2, p)));
        }

        [Theory]
        [InlineData(19455, 3, 1)]
        [InlineData(65537, 3, 1)]
        [InlineData(65536, 1, 1)]
        [InlineData(65536, 11, 1)]
        [InlineData(65536, 3, 0)]
        [InlineData(65536, 3, 5)]
        public void Argon2id_parameter_bounds_are_enforced(int m, int t, int p)
        {
            Assert.Throws<MalformedDataException>(() => new Argon2idParameters(m, t, p));
        }

        [Fact]
        public void Key_labels_are_distinct_and_derived_keys_differ()
        {
            using (var lmk = LocalMasterKey.Generate())
            {
                var keys = new[] { lmk.DatabaseKey(), lmk.AttachmentKey(), lmk.AuditKey() }.Select(Bytes.ToHex).ToList();
                Assert.Equal(3, keys.Distinct().Count());
                Assert.Equal(keys[0], Bytes.ToHex(lmk.DatabaseKey()));
            }
        }

        [Fact]
        public void Derived_database_key_is_stable_for_a_fixed_master_key()
        {
            // Cross-edition contract: the same LMK must give the same database key on net48 and net10, x86 and x64.
            // Expected values computed independently with Python hmac/hashlib (RFC 5869, zero salt).
            using (var lmk = LocalMasterKey.FromBytes(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()))
            {
                Assert.Equal("f347720d9a6780d35d4f62ee8011c7cd85cdea16894b3c6ef590a26af0c8ae9c", Bytes.ToHex(lmk.DatabaseKey()));
                Assert.Equal("ce9ea79fcfa94b9b84796787fe96dc0499d4959f80eda81da7294d9dcfa6593f", Bytes.ToHex(lmk.AttachmentKey()));
                Assert.Equal("bf02c2041e4a99ad84b14f9521b02903011203b3181785504701f9be524808bc", Bytes.ToHex(lmk.AuditKey()));
            }
        }

        [Fact]
        public void Disposed_master_key_cannot_derive()
        {
            var lmk = LocalMasterKey.Generate();
            lmk.Dispose();
            Assert.Throws<System.ObjectDisposedException>(() => lmk.DatabaseKey());
        }

        [Fact]
        public void Random_bytes_are_not_repeated()
        {
            var set = Enumerable.Range(0, 2000).Select(_ => Bytes.ToHex(CryptoRandom.Bytes(12))).ToList();
            Assert.Equal(set.Count, set.Distinct().Count());
        }
    }
}
