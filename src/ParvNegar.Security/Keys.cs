using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ParvNegar.Security
{
    /// <summary>
    /// Local Master Key: 256 random bits per data set. Never stored unwrapped. All data keys are
    /// derived from it with HKDF-SHA256 and versioned labels (SECURITY-ARCHITECTURE.md §2).
    /// </summary>
    public sealed class LocalMasterKey : IDisposable
    {
        public const int Length = 32;
        private byte[] _key;

        private LocalMasterKey(byte[] key) { _key = key; }

        public static LocalMasterKey Generate() => new LocalMasterKey(CryptoRandom.Bytes(Length));

        /// <summary>Takes ownership of a copy of the bytes; the caller should wipe its own copy.</summary>
        public static LocalMasterKey FromBytes(byte[] key)
        {
            if (key == null || key.Length != Length) throw new MalformedDataException("master key length");
            return new LocalMasterKey((byte[])key.Clone());
        }

        public byte[] DeriveKey(string label)
        {
            if (_key == null) throw new ObjectDisposedException(nameof(LocalMasterKey));
            return Hkdf.DeriveKey(_key, null, label, 32);
        }

        public byte[] DatabaseKey() => DeriveKey(KeyLabels.Database);
        public byte[] AttachmentKey() => DeriveKey(KeyLabels.Attachments);
        public byte[] AuditKey() => DeriveKey(KeyLabels.Audit);

        /// <summary>Copy for wrapping. Caller must wipe it.</summary>
        internal byte[] ExportForWrap()
        {
            if (_key == null) throw new ObjectDisposedException(nameof(LocalMasterKey));
            return (byte[])_key.Clone();
        }

        public void Dispose() { Bytes.Wipe(_key); _key = null; }
    }

    public enum KeyEnvelopeKind : byte
    {
        PasswordAndDevice = 1,
        RecoveryKey = 2,
    }

    /// <summary>
    /// Authenticated LMK wrap (STORAGE-FORMAT.md §3). Layout (126 bytes, integers big-endian):
    ///   "PNKE" | version:u8=1 | kind:u8 | memKiB:u32 | iterations:u32 | parallelism:u32 |
    ///   salt[16] | accountId[16] | installId[16] | nonce[12] | AES-256-GCM(LMK)[32+16]
    /// AAD = all bytes before the nonce. Each envelope uses a fresh random salt, so each KEK
    /// encrypts exactly one message; the nonce is random as defence in depth.
    /// Kind 1 (normal unlock): KEK = HKDF(Argon2id(password, salt) || deviceSecret, installId, label).
    ///   Requires BOTH the password and the DPAPI-protected device secret.
    /// Kind 2 (owner recovery key, policy pending O-03): KEK = HKDF(recoveryKey, salt, label); params zero.
    /// </summary>
    public static class KeyEnvelope
    {
        public const int Version = 1;
        public const int TotalLength = 126;
        private const int AadLength = 66;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PNKE");

        public static byte[] WrapWithPasswordAndDevice(LocalMasterKey lmk, byte[] passwordUtf8, byte[] deviceSecret, Guid accountId, Guid installId, Argon2idParameters p)
        {
            if (deviceSecret == null || deviceSecret.Length != DeviceSecret.Length) throw new ArgumentException("Device secret required.", nameof(deviceSecret));
            if (passwordUtf8 == null || passwordUtf8.Length == 0) throw new ArgumentException("Password required.", nameof(passwordUtf8));
            p = p ?? Argon2idParameters.Default;
            var salt = CryptoRandom.Bytes(Argon2id.SaltLength);
            var kek = PasswordDeviceKek(passwordUtf8, deviceSecret, salt, installId, p);
            try { return Seal(lmk, KeyEnvelopeKind.PasswordAndDevice, p.MemoryKiB, p.Iterations, p.Parallelism, salt, accountId, installId, kek); }
            finally { Bytes.Wipe(kek); }
        }

        public static byte[] WrapWithRecoveryKey(LocalMasterKey lmk, byte[] recoveryKey, Guid installId)
        {
            if (recoveryKey == null || recoveryKey.Length != 32) throw new ArgumentException("256-bit recovery key required.", nameof(recoveryKey));
            var salt = CryptoRandom.Bytes(Argon2id.SaltLength);
            var kek = Hkdf.DeriveKey(recoveryKey, salt, KeyLabels.KekRecovery, 32);
            try { return Seal(lmk, KeyEnvelopeKind.RecoveryKey, 0, 0, 0, salt, Guid.Empty, installId, kek); }
            finally { Bytes.Wipe(kek); }
        }

        public static LocalMasterKey UnwrapWithPasswordAndDevice(byte[] envelope, byte[] passwordUtf8, byte[] deviceSecret, Guid expectedInstallId)
        {
            var h = Parse(envelope, expectedInstallId);
            if (h.Kind != KeyEnvelopeKind.PasswordAndDevice) throw new MalformedDataException("envelope kind");
            if (deviceSecret == null || deviceSecret.Length != DeviceSecret.Length || passwordUtf8 == null || passwordUtf8.Length == 0)
                throw new AuthenticationFailedException();
            var p = new Argon2idParameters(h.MemoryKiB, h.Iterations, h.Parallelism); // bounds-checked before any KDF work
            var kek = PasswordDeviceKek(passwordUtf8, deviceSecret, h.Salt, expectedInstallId, p);
            try { return Open(envelope, kek); }
            finally { Bytes.Wipe(kek); }
        }

        public static LocalMasterKey UnwrapWithRecoveryKey(byte[] envelope, byte[] recoveryKey, Guid expectedInstallId)
        {
            var h = Parse(envelope, expectedInstallId);
            if (h.Kind != KeyEnvelopeKind.RecoveryKey) throw new MalformedDataException("envelope kind");
            if (h.MemoryKiB != 0 || h.Iterations != 0 || h.Parallelism != 0) throw new MalformedDataException("recovery envelope parameters");
            if (recoveryKey == null || recoveryKey.Length != 32) throw new AuthenticationFailedException();
            var kek = Hkdf.DeriveKey(recoveryKey, h.Salt, KeyLabels.KekRecovery, 32);
            try { return Open(envelope, kek); }
            finally { Bytes.Wipe(kek); }
        }

        public static Guid ReadAccountId(byte[] envelope) => Parse(envelope, null).AccountId;

        private static byte[] PasswordDeviceKek(byte[] password, byte[] deviceSecret, byte[] salt, Guid installId, Argon2idParameters p)
        {
            var pk = Argon2id.Derive(password, salt, p, 32);
            var ikm = new byte[pk.Length + deviceSecret.Length];
            Buffer.BlockCopy(pk, 0, ikm, 0, pk.Length);
            Buffer.BlockCopy(deviceSecret, 0, ikm, pk.Length, deviceSecret.Length);
            try { return Hkdf.DeriveKey(ikm, installId.ToByteArray(), KeyLabels.KekPasswordAndDevice, 32); }
            finally { Bytes.Wipe(pk); Bytes.Wipe(ikm); }
        }

        private static byte[] Seal(LocalMasterKey lmk, KeyEnvelopeKind kind, int m, int t, int par, byte[] salt, Guid accountId, Guid installId, byte[] kek)
        {
            var e = new byte[TotalLength];
            Buffer.BlockCopy(Magic, 0, e, 0, 4);
            e[4] = Version;
            e[5] = (byte)kind;
            Bytes.WriteUInt32BE(e, 6, (uint)m);
            Bytes.WriteUInt32BE(e, 10, (uint)t);
            Bytes.WriteUInt32BE(e, 14, (uint)par);
            Buffer.BlockCopy(salt, 0, e, 18, 16);
            Buffer.BlockCopy(accountId.ToByteArray(), 0, e, 34, 16);
            Buffer.BlockCopy(installId.ToByteArray(), 0, e, 50, 16);
            var nonce = CryptoRandom.Bytes(AesGcm256.NonceLength);
            Buffer.BlockCopy(nonce, 0, e, AadLength, 12);
            var aad = new byte[AadLength];
            Buffer.BlockCopy(e, 0, aad, 0, AadLength);
            var plain = lmk.ExportForWrap();
            try
            {
                var ct = AesGcm256.Encrypt(kek, nonce, aad, plain);
                Buffer.BlockCopy(ct, 0, e, AadLength + 12, ct.Length);
            }
            finally { Bytes.Wipe(plain); }
            return e;
        }

        private static LocalMasterKey Open(byte[] e, byte[] kek)
        {
            var aad = new byte[AadLength];
            Buffer.BlockCopy(e, 0, aad, 0, AadLength);
            var nonce = new byte[12];
            Buffer.BlockCopy(e, AadLength, nonce, 0, 12);
            var ct = new byte[48];
            Buffer.BlockCopy(e, AadLength + 12, ct, 0, 48);
            var plain = AesGcm256.Decrypt(kek, nonce, aad, ct);
            try { return LocalMasterKey.FromBytes(plain); }
            finally { Bytes.Wipe(plain); }
        }

        private struct Header
        {
            public KeyEnvelopeKind Kind; public int MemoryKiB, Iterations, Parallelism; public byte[] Salt; public Guid AccountId;
        }

        private static Header Parse(byte[] e, Guid? expectedInstallId)
        {
            if (e == null || e.Length != TotalLength) throw new MalformedDataException("envelope length");
            for (int i = 0; i < 4; i++) if (e[i] != Magic[i]) throw new MalformedDataException("envelope magic");
            if (e[4] != Version) throw new MalformedDataException("envelope version " + e[4]);
            var kind = (KeyEnvelopeKind)e[5];
            if (kind != KeyEnvelopeKind.PasswordAndDevice && kind != KeyEnvelopeKind.RecoveryKey) throw new MalformedDataException("envelope kind");
            uint m = Bytes.ReadUInt32BE(e, 6), t = Bytes.ReadUInt32BE(e, 10), p = Bytes.ReadUInt32BE(e, 14);
            if (m > int.MaxValue || t > int.MaxValue || p > int.MaxValue) throw new MalformedDataException("envelope parameters");
            var salt = new byte[16]; Buffer.BlockCopy(e, 18, salt, 0, 16);
            var acc = new byte[16]; Buffer.BlockCopy(e, 34, acc, 0, 16);
            var inst = new byte[16]; Buffer.BlockCopy(e, 50, inst, 0, 16);
            if (expectedInstallId.HasValue && new Guid(inst) != expectedInstallId.Value) throw new MalformedDataException("envelope belongs to another installation");
            return new Header { Kind = kind, MemoryKiB = (int)m, Iterations = (int)t, Parallelism = (int)p, Salt = salt, AccountId = new Guid(acc) };
        }
    }

    /// <summary>Protects the 256-bit device secret with an OS mechanism.</summary>
    public interface IDeviceSecretProtector
    {
        byte[] Protect(byte[] secret, byte[] entropy);
        byte[] Unprotect(byte[] blob, byte[] entropy);
    }

    /// <summary>
    /// DPAPI LocalMachine scope (ADR-P1-04). Binds the secret to this Windows installation for all
    /// Windows accounts on the licensed PC. It does NOT stop other processes on the same machine
    /// from unprotecting it; normal unlock still needs the password factor (KeyEnvelope kind 1).
    /// </summary>
    public sealed class DpapiLocalMachineProtector : IDeviceSecretProtector
    {
        public byte[] Protect(byte[] secret, byte[] entropy) => ProtectedData.Protect(secret, entropy, DataProtectionScope.LocalMachine);

        public byte[] Unprotect(byte[] blob, byte[] entropy)
        {
            try { return ProtectedData.Unprotect(blob, entropy, DataProtectionScope.LocalMachine); }
            catch (CryptographicException) { throw new AuthenticationFailedException(); }
        }
    }

    /// <summary>Device secret file: "PNDS" | version:u8=1 | installId[16] | dpapiBlobLength:u32be | dpapiBlob.</summary>
    public static class DeviceSecret
    {
        public const int Length = 32;
        private const int MaxBlobLength = 4096;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PNDS");

        public static byte[] CreateFile(string path, Guid installId, IDeviceSecretProtector protector)
        {
            if (File.Exists(path)) throw new IOException("Device secret already exists; refusing to overwrite.");
            var secret = CryptoRandom.Bytes(Length);
            var blob = protector.Protect(secret, Entropy(installId));
            var data = new byte[4 + 1 + 16 + 4 + blob.Length];
            Buffer.BlockCopy(Magic, 0, data, 0, 4);
            data[4] = 1;
            Buffer.BlockCopy(installId.ToByteArray(), 0, data, 5, 16);
            Bytes.WriteUInt32BE(data, 21, (uint)blob.Length);
            Buffer.BlockCopy(blob, 0, data, 25, blob.Length);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, path);
            return secret;
        }

        public static byte[] LoadFile(string path, Guid expectedInstallId, IDeviceSecretProtector protector)
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 25 || data.Length > 25 + MaxBlobLength) throw new MalformedDataException("device secret file length");
            for (int i = 0; i < 4; i++) if (data[i] != Magic[i]) throw new MalformedDataException("device secret magic");
            if (data[4] != 1) throw new MalformedDataException("device secret version");
            var inst = new byte[16]; Buffer.BlockCopy(data, 5, inst, 0, 16);
            if (new Guid(inst) != expectedInstallId) throw new MalformedDataException("device secret belongs to another installation");
            uint len = Bytes.ReadUInt32BE(data, 21);
            if (len != data.Length - 25) throw new MalformedDataException("device secret blob length");
            var blob = new byte[len]; Buffer.BlockCopy(data, 25, blob, 0, (int)len);
            var secret = protector.Unprotect(blob, Entropy(expectedInstallId));
            if (secret.Length != Length) { Bytes.Wipe(secret); throw new MalformedDataException("device secret length"); }
            return secret;
        }

        private static byte[] Entropy(Guid installId)
        {
            var label = Encoding.UTF8.GetBytes(KeyLabels.DeviceSecretEntropy);
            var e = new byte[label.Length + 16];
            Buffer.BlockCopy(label, 0, e, 0, label.Length);
            Buffer.BlockCopy(installId.ToByteArray(), 0, e, label.Length, 16);
            return e;
        }
    }
}
