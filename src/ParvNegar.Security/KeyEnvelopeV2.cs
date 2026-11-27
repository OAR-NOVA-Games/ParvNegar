using System;
using System.Text;

namespace ParvNegar.Security
{
    /// <summary>What an account envelope is cryptographically bound to (authenticated as AES-GCM AAD).</summary>
    public sealed class EnvelopeBinding
    {
        public EnvelopeBinding(Guid accountId, Guid installId, Guid envelopeId, uint revision, byte[] usernameHash)
        {
            if (usernameHash == null || usernameHash.Length != 32) throw new ArgumentException("32-byte username hash required.", nameof(usernameHash));
            if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision), "Revisions start at 1.");
            AccountId = accountId; InstallId = installId; EnvelopeId = envelopeId; Revision = revision; UsernameHash = (byte[])usernameHash.Clone();
        }

        public Guid AccountId { get; }
        public Guid InstallId { get; }
        public Guid EnvelopeId { get; }
        public uint Revision { get; }
        public byte[] UsernameHash { get; }

        /// <summary>SHA-256("ParvNegar/v1/username\n" ‖ canonical username). Recovery envelopes use 32 zero bytes.</summary>
        public static byte[] HashUsername(string canonicalUsername)
            => Bytes.Sha256(Encoding.UTF8.GetBytes("ParvNegar/v1/username\n" + canonicalUsername));
    }

    /// <summary>
    /// LMK key envelope PNKE v2 (STORAGE-FORMAT.md §3.2, 178 bytes, integers big-endian):
    /// <code>
    ///   0  4 "PNKE"      4 1 version = 2   5 1 kind (1 password+device, 2 recovery key)
    ///   6  4 memoryKiB  10 4 iterations   14 4 parallelism   (kind 2: all 0)
    ///  18 16 salt       34 16 accountId   50 16 installId    (Guid.ToByteArray layout; kind 2: accountId 0)
    ///  66 16 envelopeId 82 4 revision     86 32 usernameHash (kind 2: zero)
    /// 118 12 nonce     130 48 AES-256-GCM(LMK) || tag        AAD = bytes 0..117
    /// </code>
    /// The KEK derivations are those of v1. v2 adds the envelope identity, its revision and the username to
    /// the authenticated data, so a swapped, stale or transplanted envelope can be recognised after unlock
    /// by comparing the authenticated binding with the account record in the encrypted database.
    /// </summary>
    public static class KeyEnvelopeV2
    {
        public const int Version = 2;
        public const int TotalLength = 178;
        private const int AadLength = 118;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PNKE");

        public static byte[] WrapWithPasswordAndDevice(LocalMasterKey lmk, byte[] passwordUtf8, byte[] deviceSecret, EnvelopeBinding binding, Argon2idParameters p)
        {
            if (deviceSecret == null || deviceSecret.Length != DeviceSecret.Length) throw new ArgumentException("Device secret required.", nameof(deviceSecret));
            if (passwordUtf8 == null || passwordUtf8.Length == 0) throw new ArgumentException("Password required.", nameof(passwordUtf8));
            if (binding.AccountId == Guid.Empty) throw new ArgumentException("Account id required.", nameof(binding));
            p = p ?? Argon2idParameters.Default;
            var salt = CryptoRandom.Bytes(Argon2id.SaltLength);
            var kek = PasswordDeviceKek(passwordUtf8, deviceSecret, salt, binding.InstallId, p);
            try { return Seal(lmk, KeyEnvelopeKind.PasswordAndDevice, p.MemoryKiB, p.Iterations, p.Parallelism, salt, binding, kek); }
            finally { Bytes.Wipe(kek); }
        }

        public static byte[] WrapWithRecoveryKey(LocalMasterKey lmk, byte[] recoveryKey, Guid installId, Guid envelopeId, uint revision)
        {
            if (recoveryKey == null || recoveryKey.Length != 32) throw new ArgumentException("256-bit recovery key required.", nameof(recoveryKey));
            var salt = CryptoRandom.Bytes(Argon2id.SaltLength);
            var kek = Hkdf.DeriveKey(recoveryKey, salt, KeyLabels.KekRecovery, 32);
            try { return Seal(lmk, KeyEnvelopeKind.RecoveryKey, 0, 0, 0, salt, new EnvelopeBinding(Guid.Empty, installId, envelopeId, revision, new byte[32]), kek); }
            finally { Bytes.Wipe(kek); }
        }

        public static LocalMasterKey UnwrapWithPasswordAndDevice(byte[] envelope, byte[] passwordUtf8, byte[] deviceSecret, Guid expectedInstallId, out EnvelopeBinding binding)
        {
            var h = Parse(envelope, expectedInstallId);
            if (h.Kind != KeyEnvelopeKind.PasswordAndDevice) throw new MalformedDataException("envelope kind");
            if (deviceSecret == null || deviceSecret.Length != DeviceSecret.Length || passwordUtf8 == null || passwordUtf8.Length == 0)
                throw new AuthenticationFailedException();
            var p = new Argon2idParameters(h.MemoryKiB, h.Iterations, h.Parallelism); // bounds-checked before any KDF work
            var kek = PasswordDeviceKek(passwordUtf8, deviceSecret, h.Salt, expectedInstallId, p);
            try { var lmk = Open(envelope, kek); binding = h.Binding; return lmk; }
            finally { Bytes.Wipe(kek); }
        }

        public static LocalMasterKey UnwrapWithRecoveryKey(byte[] envelope, byte[] recoveryKey, Guid expectedInstallId, out EnvelopeBinding binding)
        {
            var h = Parse(envelope, expectedInstallId);
            if (h.Kind != KeyEnvelopeKind.RecoveryKey) throw new MalformedDataException("envelope kind");
            if (h.MemoryKiB != 0 || h.Iterations != 0 || h.Parallelism != 0 || h.Binding.AccountId != Guid.Empty) throw new MalformedDataException("recovery envelope fields");
            if (recoveryKey == null || recoveryKey.Length != 32) throw new AuthenticationFailedException();
            var kek = Hkdf.DeriveKey(recoveryKey, h.Salt, KeyLabels.KekRecovery, 32);
            try { var lmk = Open(envelope, kek); binding = h.Binding; return lmk; }
            finally { Bytes.Wipe(kek); }
        }

        /// <summary>Reads the binding WITHOUT authenticating it (for locating envelopes only; never for decisions).</summary>
        public static EnvelopeBinding PeekBinding(byte[] envelope, out KeyEnvelopeKind kind)
        {
            var h = Parse(envelope, null);
            kind = h.Kind;
            return h.Binding;
        }

        private static byte[] PasswordDeviceKek(byte[] password, byte[] deviceSecret, byte[] salt, Guid installId, Argon2idParameters p)
        {
            var pk = Argon2id.Derive(password, salt, p, 32);
            var ikm = new byte[pk.Length + deviceSecret.Length];
            Buffer.BlockCopy(pk, 0, ikm, 0, pk.Length);
            Buffer.BlockCopy(deviceSecret, 0, ikm, pk.Length, deviceSecret.Length);
            try { return Hkdf.DeriveKey(ikm, installId.ToByteArray(), KeyLabels.KekPasswordAndDevice, 32); }
            finally { Bytes.Wipe(pk); Bytes.Wipe(ikm); }
        }

        private static byte[] Seal(LocalMasterKey lmk, KeyEnvelopeKind kind, int m, int t, int par, byte[] salt, EnvelopeBinding b, byte[] kek)
        {
            var e = new byte[TotalLength];
            Buffer.BlockCopy(Magic, 0, e, 0, 4);
            e[4] = Version;
            e[5] = (byte)kind;
            Bytes.WriteUInt32BE(e, 6, (uint)m);
            Bytes.WriteUInt32BE(e, 10, (uint)t);
            Bytes.WriteUInt32BE(e, 14, (uint)par);
            Buffer.BlockCopy(salt, 0, e, 18, 16);
            Buffer.BlockCopy(b.AccountId.ToByteArray(), 0, e, 34, 16);
            Buffer.BlockCopy(b.InstallId.ToByteArray(), 0, e, 50, 16);
            Buffer.BlockCopy(b.EnvelopeId.ToByteArray(), 0, e, 66, 16);
            Bytes.WriteUInt32BE(e, 82, b.Revision);
            Buffer.BlockCopy(b.UsernameHash, 0, e, 86, 32);
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
            public KeyEnvelopeKind Kind; public int MemoryKiB, Iterations, Parallelism; public byte[] Salt; public EnvelopeBinding Binding;
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
            var salt = Slice(e, 18, 16);
            var inst = new Guid(Slice(e, 50, 16));
            if (expectedInstallId.HasValue && inst != expectedInstallId.Value) throw new MalformedDataException("envelope belongs to another installation");
            uint rev = Bytes.ReadUInt32BE(e, 82);
            if (rev == 0) throw new MalformedDataException("envelope revision");
            var binding = new EnvelopeBinding(new Guid(Slice(e, 34, 16)), inst, new Guid(Slice(e, 66, 16)), rev, Slice(e, 86, 32));
            return new Header { Kind = kind, MemoryKiB = (int)m, Iterations = (int)t, Parallelism = (int)p, Salt = salt, Binding = binding };
        }

        private static byte[] Slice(byte[] b, int o, int n) { var r = new byte[n]; Buffer.BlockCopy(b, o, r, 0, n); return r; }
    }
}
