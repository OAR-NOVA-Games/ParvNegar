using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace ParvNegar.Security
{
    /// <summary>Generic authentication failure. Deliberately carries no detail (no decryption oracle).</summary>
    public sealed class AuthenticationFailedException : CryptographicException
    {
        public AuthenticationFailedException() : base("Authentication failed: wrong key, wrong password or modified data.") { }
    }

    /// <summary>Structurally invalid or unsupported input (rejected before any decryption).</summary>
    public sealed class MalformedDataException : CryptographicException
    {
        public MalformedDataException(string reason) : base("Malformed or unsupported data: " + reason) { }
    }

    public static class CryptoRandom
    {
        /// <summary>OS CSPRNG (BCryptGenRandom on Windows) on both .NET Framework and .NET.</summary>
        public static byte[] Bytes(int count)
        {
            if (count <= 0 || count > 1 << 20) throw new ArgumentOutOfRangeException(nameof(count));
            var b = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }
    }

    public static class Bytes
    {
        /// <summary>Best-effort wipe. The GC may already have copied the data; see SECURITY-ARCHITECTURE.md.</summary>
        public static void Wipe(byte[] b) { if (b != null) Array.Clear(b, 0, b.Length); }

        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static void WriteUInt32BE(byte[] dst, int offset, uint v)
        {
            dst[offset] = (byte)(v >> 24); dst[offset + 1] = (byte)(v >> 16); dst[offset + 2] = (byte)(v >> 8); dst[offset + 3] = (byte)v;
        }

        public static void WriteUInt64BE(byte[] dst, int offset, ulong v)
        {
            WriteUInt32BE(dst, offset, (uint)(v >> 32)); WriteUInt32BE(dst, offset + 4, (uint)v);
        }

        public static uint ReadUInt32BE(byte[] src, int offset) =>
            ((uint)src[offset] << 24) | ((uint)src[offset + 1] << 16) | ((uint)src[offset + 2] << 8) | src[offset + 3];

        public static ulong ReadUInt64BE(byte[] src, int offset) => ((ulong)ReadUInt32BE(src, offset) << 32) | ReadUInt32BE(src, offset + 4);

        public static string ToHex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static byte[] FromHex(string hex)
        {
            if (hex == null || hex.Length % 2 != 0) throw new FormatException("Invalid hex.");
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        public static byte[] Sha256(byte[] data)
        {
            using (var s = SHA256.Create()) return s.ComputeHash(data);
        }
    }

    /// <summary>HKDF-SHA256 (RFC 5869) via BouncyCastle.</summary>
    public static class Hkdf
    {
        public static byte[] DeriveKey(byte[] ikm, byte[] salt, string info, int length = 32)
        {
            if (ikm == null || ikm.Length == 0) throw new ArgumentException("IKM required.", nameof(ikm));
            return DeriveKey(ikm, salt, Encoding.UTF8.GetBytes(info ?? throw new ArgumentNullException(nameof(info))), length);
        }

        public static byte[] DeriveKey(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            if (length < 16 || length > 255 * 32) throw new ArgumentOutOfRangeException(nameof(length));
            var g = new HkdfBytesGenerator(new Sha256Digest());
            g.Init(new HkdfParameters(ikm, salt, info));
            var okm = new byte[length];
            g.GenerateBytes(okm, 0, length);
            return okm;
        }
    }

    /// <summary>Domain-separation labels. Changing a label changes every derived key: version them.</summary>
    public static class KeyLabels
    {
        public const string Database = "ParvNegar/v1/key/database";
        public const string Attachments = "ParvNegar/v1/key/attachments";
        public const string Audit = "ParvNegar/v1/key/audit";
        public const string KekPasswordAndDevice = "ParvNegar/v1/kek/password+device";
        public const string KekRecovery = "ParvNegar/v1/kek/recovery";
        public const string BackupKek = "ParvNegar/v1/kek/backup";
        public const string DeviceSecretEntropy = "ParvNegar/v1/dpapi/device-secret";
    }

    /// <summary>Argon2id parameters with hard bounds checked on create and on read (DoS protection).</summary>
    public sealed class Argon2idParameters
    {
        public const int MinMemoryKiB = 19456;   // 19 MiB
        public const int MaxMemoryKiB = 65536;   // 64 MiB: must fit a 32-bit process on a 2 GB PC
        public const int MinIterations = 2;
        public const int MaxIterations = 10;
        public const int MinParallelism = 1;
        public const int MaxParallelism = 4;

        public static readonly Argon2idParameters Default = new Argon2idParameters(65536, 3, 1);

        public Argon2idParameters(int memoryKiB, int iterations, int parallelism)
        {
            if (memoryKiB < MinMemoryKiB || memoryKiB > MaxMemoryKiB) throw new MalformedDataException("Argon2id memory out of bounds");
            if (iterations < MinIterations || iterations > MaxIterations) throw new MalformedDataException("Argon2id iterations out of bounds");
            if (parallelism < MinParallelism || parallelism > MaxParallelism) throw new MalformedDataException("Argon2id parallelism out of bounds");
            MemoryKiB = memoryKiB; Iterations = iterations; Parallelism = parallelism;
        }

        public int MemoryKiB { get; }
        public int Iterations { get; }
        public int Parallelism { get; }
    }

    public static class Argon2id
    {
        public const int SaltLength = 16;

        /// <summary>Argon2id v1.3 (RFC 9106). Password bytes are UTF-8 of the exact string (no normalization).</summary>
        public static byte[] Derive(byte[] password, byte[] salt, Argon2idParameters p, int length = 32)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            if (salt == null || salt.Length != SaltLength) throw new MalformedDataException("salt length");
            if (p == null) throw new ArgumentNullException(nameof(p));
            var parameters = new Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Builder(Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Argon2id)
                .WithVersion(Org.BouncyCastle.Crypto.Parameters.Argon2Parameters.Version13)
                .WithMemoryAsKB(p.MemoryKiB).WithIterations(p.Iterations).WithParallelism(p.Parallelism)
                .WithSalt(salt).Build();
            var g = new Argon2BytesGenerator();
            g.Init(parameters);
            var output = new byte[length];
            g.GenerateBytes(password, output);
            return output;
        }
    }

    /// <summary>AES-256-GCM with a 96-bit nonce and 128-bit tag (BouncyCastle on all targets).</summary>
    public static class AesGcm256
    {
        public const int KeyLength = 32;
        public const int NonceLength = 12;
        public const int TagLength = 16;

        public static byte[] Encrypt(byte[] key, byte[] nonce, byte[] aad, byte[] plaintext) => Run(true, key, nonce, aad, plaintext);

        public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] aad, byte[] ciphertextAndTag)
        {
            if (ciphertextAndTag == null || ciphertextAndTag.Length < TagLength) throw new MalformedDataException("ciphertext too short");
            try { return Run(false, key, nonce, aad, ciphertextAndTag); }
            catch (InvalidCipherTextException) { throw new AuthenticationFailedException(); }
        }

        private static byte[] Run(bool encrypt, byte[] key, byte[] nonce, byte[] aad, byte[] input)
        {
            if (key == null || key.Length != KeyLength) throw new ArgumentException("AES-256 key required.", nameof(key));
            if (nonce == null || nonce.Length != NonceLength) throw new ArgumentException("96-bit nonce required.", nameof(nonce));
            var g = new GcmBlockCipher(new AesEngine());
            g.Init(encrypt, new AeadParameters(new KeyParameter(key), TagLength * 8, nonce, aad));
            var output = new byte[g.GetOutputSize(input.Length)];
            int n = g.ProcessBytes(input, 0, input.Length, output, 0);
            n += g.DoFinal(output, n);
            if (n != output.Length) Array.Resize(ref output, n);
            return output;
        }
    }
}
