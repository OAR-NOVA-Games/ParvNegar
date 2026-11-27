using System;
using System.IO;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using ParvNegar.Licensing;
using ParvNegar.Security;

namespace ParvNegar.Issuer
{
    /// <summary>A decrypted signing key, held only in memory for the duration of one command.</summary>
    internal sealed class SigningKey : IDisposable
    {
        public SigningKey(string kid, TrustEnvironment environment, ECPrivateKeyParameters key, byte[] spki)
        {
            Kid = kid; Environment = environment; Key = key; Spki = spki;
        }

        public string Kid { get; }
        public TrustEnvironment Environment { get; }
        public ECPrivateKeyParameters Key { get; private set; }
        public byte[] Spki { get; }

        public void Dispose() { Key = null; }
    }

    /// <summary>
    /// Encrypted signing-key file PNSK v1 (SIGNING-KEY-CUSTODY.md §3): canonical JSON
    /// {format, kid, environment, spki, kdf{m,t,p,salt}, nonce, ciphertext}. The PKCS#8 private key is
    /// encrypted with AES-256-GCM under HKDF(Argon2id(passphrase), info "ParvNegar/v1/kek/signing-key");
    /// AAD is the canonical JSON of every other field. Same primitives as the PNKE/PNBK formats.
    /// </summary>
    internal static class SigningKeyFile
    {
        public const string Format = "PARVNEGAR-SIGNING-KEY-V1";
        private const string KekLabel = "ParvNegar/v1/kek/signing-key";

        public static (byte[] file, byte[] spki) Generate(string kid, TrustEnvironment env, byte[] passphrase, Argon2idParameters kdf)
        {
            if (!Base64Url.IsKid(kid)) throw new ArgumentException("kid: 3–64 characters a-z 0-9 -");
            bool prodKid = kid.StartsWith("pn-prod-", StringComparison.Ordinal);
            if ((env == TrustEnvironment.Production) != prodKid) throw new ArgumentException("Production keys and only production keys use the pn-prod- prefix.");
            if (passphrase == null || passphrase.Length < 20) throw new ArgumentException("Signing-key passphrase must be at least 20 bytes.");
            var gen = new ECKeyPairGenerator();
            gen.Init(new ECKeyGenerationParameters(EcdsaP256.Domain, new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded();
            var pkcs8 = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
            try { return (Seal(kid, env, spki, pkcs8, passphrase, kdf ?? Argon2idParameters.Default), spki); }
            finally { Bytes.Wipe(pkcs8); }
        }

        private static byte[] Seal(string kid, TrustEnvironment env, byte[] spki, byte[] pkcs8, byte[] passphrase, Argon2idParameters kdf)
        {
            var salt = CryptoRandom.Bytes(16);
            var nonce = CryptoRandom.Bytes(12);
            var header = Header(kid, env, spki, kdf, salt, nonce);
            var kek = Kek(passphrase, salt, kdf);
            try
            {
                var ct = AesGcm256.Encrypt(kek, nonce, StrictJson.Canonical(header), pkcs8);
                var members = header.Members.ToList();
                members.Add(new System.Collections.Generic.KeyValuePair<string, JsonValue>("ciphertext", JsonValue.Str(Base64Url.Encode(ct))));
                return StrictJson.Canonical(JsonValue.Obj(members));
            }
            finally { Bytes.Wipe(kek); }
        }

        public static SigningKey Open(byte[] file, byte[] passphrase)
        {
            var v = StrictJson.ParseCanonical(file);
            v.RequireExactKeys("ciphertext", "environment", "format", "kdf", "kid", "nonce", "spki");
            if (v.Get("format").AsString("format") != Format) throw new LicenseFormatException("signing key format");
            var envName = v.Get("environment").AsString("environment");
            var env = envName == "production" ? TrustEnvironment.Production : envName == "development" ? TrustEnvironment.Development : envName == "test" ? TrustEnvironment.Test : throw new LicenseFormatException("environment");
            var kdfV = v.Get("kdf");
            kdfV.RequireExactKeys("m", "p", "salt", "t");
            var kdf = new Argon2idParameters((int)kdfV.Get("m").AsInteger("m"), (int)kdfV.Get("t").AsInteger("t"), (int)kdfV.Get("p").AsInteger("p"));
            var salt = Base64Url.Decode(kdfV.Get("salt").AsString("salt"));
            var nonce = Base64Url.Decode(v.Get("nonce").AsString("nonce"));
            var spki = Base64Url.Decode(v.Get("spki").AsString("spki"));
            var kid = v.Get("kid").AsString("kid");
            var header = Header(kid, env, spki, kdf, salt, nonce);
            var kek = Kek(passphrase, salt, kdf);
            byte[] pkcs8 = null;
            try
            {
                pkcs8 = AesGcm256.Decrypt(kek, nonce, StrictJson.Canonical(header), Base64Url.Decode(v.Get("ciphertext").AsString("ciphertext")));
                var priv = (ECPrivateKeyParameters)PrivateKeyFactory.CreateKey(pkcs8);
                var pub = EcdsaP256.ParsePublicKey(spki);
                if (!EcdsaP256.Domain.G.Multiply(priv.D).Normalize().Equals(pub.Q.Normalize())) throw new LicenseFormatException("private key does not match public key");
                return new SigningKey(kid, env, new ECPrivateKeyParameters(priv.D, EcdsaP256.Domain), spki);
            }
            finally { Bytes.Wipe(kek); Bytes.Wipe(pkcs8); }
        }

        private static JsonValue Header(string kid, TrustEnvironment env, byte[] spki, Argon2idParameters kdf, byte[] salt, byte[] nonce) => JsonValue.Obj(
            ("environment", JsonValue.Str(TrustStore.EnvironmentName(env))),
            ("format", JsonValue.Str(Format)),
            ("kdf", JsonValue.Obj(("m", JsonValue.Int(kdf.MemoryKiB)), ("p", JsonValue.Int(kdf.Parallelism)), ("salt", JsonValue.Str(Base64Url.Encode(salt))), ("t", JsonValue.Int(kdf.Iterations)))),
            ("kid", JsonValue.Str(kid)),
            ("nonce", JsonValue.Str(Base64Url.Encode(nonce))),
            ("spki", JsonValue.Str(Base64Url.Encode(spki))));

        private static byte[] Kek(byte[] passphrase, byte[] salt, Argon2idParameters p)
        {
            var k = Argon2id.Derive(passphrase, salt, p, 32);
            try { return Hkdf.DeriveKey(k, null, KekLabel, 32); }
            finally { Bytes.Wipe(k); }
        }

        public static byte[] TrustAnchors(TrustEnvironment env, params (string kid, byte[] spki, string status)[] keys) => StrictJson.Canonical(JsonValue.Obj(
            ("environment", JsonValue.Str(TrustStore.EnvironmentName(env))),
            ("format", JsonValue.Str("PARVNEGAR-TRUST-ANCHORS-V1")),
            ("keys", JsonValue.Arr(keys.Select(k => JsonValue.Obj(("kid", JsonValue.Str(k.kid)), ("spki", JsonValue.Str(Base64Url.Encode(k.spki))), ("status", JsonValue.Str(k.status))))))));
    }

    /// <summary>Deterministic (RFC 6979) low-S ECDSA P-256 signature in P1363 form (r‖s, 32 bytes each).</summary>
    internal static class Signer
    {
        public static byte[] Sign(ECPrivateKeyParameters key, byte[] message)
        {
            var signer = new Org.BouncyCastle.Crypto.Signers.ECDsaSigner(new Org.BouncyCastle.Crypto.Signers.HMacDsaKCalculator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest()));
            signer.Init(true, key);
            var rs = signer.GenerateSignature(Bytes.Sha256(message));
            var s = rs[1];
            if (s.CompareTo(EcdsaP256.HalfN) > 0) s = EcdsaP256.Curve.N.Subtract(s);
            var outp = new byte[64];
            Fixed32(rs[0]).CopyTo(outp, 0);
            Fixed32(s).CopyTo(outp, 32);
            return outp;
        }

        private static byte[] Fixed32(BigInteger v)
        {
            var b = v.ToByteArrayUnsigned();
            var r = new byte[32];
            Buffer.BlockCopy(b, 0, r, 32 - b.Length, b.Length);
            return r;
        }
    }

    /// <summary>Issues signed artifacts. Every artifact is verified with the public key immediately after signing.</summary>
    internal static class Issuance
    {
        public static byte[] IssueLicense(SigningKey key, ActivationRequest request, string customerRef, string[] editions, string[] architectures,
                                          Guid licenseId, long revision, DateTime nowUtc, out LicenseClaims claims)
        {
            claims = new LicenseClaims
            {
                Kid = key.Kid,
                IssuedUtc = Truncate(nowUtc),
                LicenseId = licenseId.ToString("D"),
                LicenseRevision = revision,
                CustomerRef = customerRef,
                RequestId = request.RequestId,
                Editions = editions.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Architectures = architectures.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Device = request.Claims,
            };
            var payload = StrictJson.Canonical(claims.ToJson());
            LicenseClaims.FromPayload(payload); // the issuer refuses to sign anything a reader would reject
            return SignAndCheck(key, ProductLicensing.LicenseDomain, payload);
        }

        public static byte[] IssueProvisioning(SigningKey key, LicenseClaims license, string ownerUsername, DateTime nowUtc, out string bootstrapCode, out OwnerProvisioning prov)
        {
            var canonical = Usernames.Canonicalize(ownerUsername) ?? throw new ArgumentException("Owner username: 3–32 characters a-z 0-9 . _ -, starting with a letter.");
            var secret = CryptoRandom.Bytes(20);
            try
            {
                var id = Guid.NewGuid().ToString("D");
                prov = new OwnerProvisioning
                {
                    Kid = key.Kid, IssuedUtc = Truncate(nowUtc), ProvisioningId = id, LicenseId = license.LicenseId, RequestId = license.RequestId,
                    OwnerUsername = canonical, Commitment = OwnerProvisioning.ComputeCommitment(secret, id, canonical),
                };
                bootstrapCode = TypedCode.Encode(secret, TypedCode.BootstrapPurpose);
                return SignAndCheck(key, ProductLicensing.ProvisioningDomain, StrictJson.Canonical(prov.ToJson()));
            }
            finally { Bytes.Wipe(secret); }
        }

        private static byte[] SignAndCheck(SigningKey key, string domain, byte[] payload)
        {
            var sig = Signer.Sign(key.Key, SignedEnvelope.SignedBytes(domain, payload));
            var file = SignedEnvelope.Serialize(key.Kid, payload, sig);
            var trust = new TrustStore(key.Environment, new[] { new TrustedKey(key.Kid, key.Spki, key.Environment, TrustStatus.Active) });
            if (SignedEnvelope.Parse(file).Verify(trust, domain) != SignatureCheck.Valid) throw new InvalidOperationException("Self-verification of the new artifact failed.");
            return file;
        }

        private static DateTime Truncate(DateTime t) { t = t.ToUniversalTime(); return new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc); }

        /// <summary>Privacy-minimized issuance record: no raw device data (only the already-hashed claims), no bootstrap secret.</summary>
        public static string Record(string kind, LicenseClaims c, OwnerProvisioning p = null)
        {
            var o = p == null
                ? JsonValue.Obj(("kind", JsonValue.Str(kind)), ("license_id", JsonValue.Str(c.LicenseId)), ("revision", JsonValue.Int(c.LicenseRevision)),
                                ("customer_ref", JsonValue.Str(c.CustomerRef)), ("request_id", JsonValue.Str(c.RequestId)), ("kid", JsonValue.Str(c.Kid)),
                                ("issued_at", JsonValue.Str(c.IssuedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))),
                                ("editions", JsonValue.Arr(c.Editions.Select(JsonValue.Str))), ("architectures", JsonValue.Arr(c.Architectures.Select(JsonValue.Str))),
                                ("device_claims", c.Device.ToJson()))
                : JsonValue.Obj(("kind", JsonValue.Str(kind)), ("provisioning_id", JsonValue.Str(p.ProvisioningId)), ("license_id", JsonValue.Str(p.LicenseId)),
                                ("owner_username", JsonValue.Str(p.OwnerUsername)), ("commitment_prefix", JsonValue.Str(p.Commitment.Substring(0, 8))), ("kid", JsonValue.Str(p.Kid)));
            return Encoding.UTF8.GetString(StrictJson.Canonical(o));
        }
    }
}
