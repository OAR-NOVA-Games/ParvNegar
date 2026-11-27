using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using ParvNegar.Security;

namespace ParvNegar.Licensing
{
    public enum TrustEnvironment { Production, Development, Test }

    /// <summary>
    /// Key status. Active and Retired keys both verify: a perpetual license signed before a key was
    /// retired stays valid. Retired keys must not sign anything new (issuer rule). Revoked keys (suspected
    /// compromise) verify nothing; licenses signed by them must be reissued under another key.
    /// </summary>
    public enum TrustStatus { Active, Retired, Revoked }

    public sealed class TrustedKey
    {
        public TrustedKey(string kid, byte[] spkiDer, TrustEnvironment environment, TrustStatus status)
        {
            if (!Base64Url.IsKid(kid)) throw new LicenseFormatException("invalid kid");
            Kid = kid;
            SpkiDer = (byte[])spkiDer.Clone();
            PublicKey = EcdsaP256.ParsePublicKey(spkiDer);
            Environment = environment;
            Status = status;
        }

        public string Kid { get; }
        public byte[] SpkiDer { get; }
        internal ECPublicKeyParameters PublicKey { get; }
        public TrustEnvironment Environment { get; }
        public TrustStatus Status { get; }
    }

    /// <summary>
    /// The pinned verification keys of one build profile. A production store accepts only production keys,
    /// so a development or test key can never be trusted by a production build. Artifacts never carry
    /// their own trust roots.
    /// </summary>
    public sealed class TrustStore
    {
        private readonly Dictionary<string, TrustedKey> _keys;

        public TrustStore(TrustEnvironment environment, IEnumerable<TrustedKey> keys)
        {
            Environment = environment;
            _keys = new Dictionary<string, TrustedKey>(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                if (k.Environment != environment) throw new LicenseFormatException("key " + k.Kid + " belongs to " + k.Environment + ", store is " + environment);
                if (environment == TrustEnvironment.Production && !k.Kid.StartsWith("pn-prod-", StringComparison.Ordinal)) throw new LicenseFormatException("production keys use the pn-prod- prefix");
                if (environment != TrustEnvironment.Production && k.Kid.StartsWith("pn-prod-", StringComparison.Ordinal)) throw new LicenseFormatException("pn-prod- keys belong to the production store only");
                _keys.Add(k.Kid, k);
            }
        }

        public TrustEnvironment Environment { get; }
        public IEnumerable<TrustedKey> Keys => _keys.Values;
        public int Count => _keys.Count;

        internal TrustedKey Find(string kid) => _keys.TryGetValue(kid, out var k) && k.Status != TrustStatus.Revoked ? k : null;

        /// <summary>Trust-anchor file: {"format":"PARVNEGAR-TRUST-ANCHORS-V1","environment":"...","keys":[{"kid","spki","status"}]}.</summary>
        public static TrustStore Load(byte[] json, TrustEnvironment expected)
        {
            var root = StrictJson.Parse(json);
            root.RequireExactKeys("format", "environment", "keys");
            if (root.Get("format").AsString("format") != "PARVNEGAR-TRUST-ANCHORS-V1") throw new LicenseFormatException("trust anchor format");
            var env = ParseEnvironment(root.Get("environment").AsString("environment"));
            if (env != expected) throw new LicenseFormatException("trust anchors are for " + env + ", expected " + expected);
            var keys = new List<TrustedKey>();
            foreach (var k in root.Get("keys").AsArray("keys"))
            {
                k.RequireExactKeys("kid", "spki", "status");
                keys.Add(new TrustedKey(k.Get("kid").AsString("kid"), Base64Url.Decode(k.Get("spki").AsString("spki")), env, ParseStatus(k.Get("status").AsString("status"))));
            }
            return new TrustStore(env, keys);
        }

        public static string EnvironmentName(TrustEnvironment e) => e == TrustEnvironment.Production ? "production" : e == TrustEnvironment.Development ? "development" : "test";

        private static TrustEnvironment ParseEnvironment(string s)
        {
            switch (s)
            {
                case "production": return TrustEnvironment.Production;
                case "development": return TrustEnvironment.Development;
                case "test": return TrustEnvironment.Test;
                default: throw new LicenseFormatException("unknown environment");
            }
        }

        private static TrustStatus ParseStatus(string s)
        {
            switch (s)
            {
                case "active": return TrustStatus.Active;
                case "retired": return TrustStatus.Retired;
                case "revoked": return TrustStatus.Revoked;
                default: throw new LicenseFormatException("unknown key status");
            }
        }
    }

    public enum SignatureCheck { Valid, UnknownKey, BadSignature }

    /// <summary>
    /// Signed artifact envelope (LICENSE-FORMAT.md §2):
    /// <c>{"algorithm":"ES256-P1363","kid":K,"payload":B64URL(canonical payload),"signature":B64URL(r‖s)}</c>.
    /// Signed bytes = UTF-8 domain prefix (one per artifact type, ending in "\n") ‖ payload bytes.
    /// ECDSA P-256 / SHA-256; signature exactly 64 bytes, r and s big-endian 32 bytes each, 1 ≤ r,s &lt; n,
    /// and low-S (s ≤ n/2) so each signature has one accepted form.
    /// </summary>
    public sealed class SignedEnvelope
    {
        public const string Algorithm = "ES256-P1363";
        public const int MaxEnvelopeBytes = 32 * 1024;

        public string Kid { get; private set; }
        public byte[] Payload { get; private set; }
        public byte[] Signature { get; private set; }

        public static SignedEnvelope Parse(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0 || fileBytes.Length > MaxEnvelopeBytes) throw new LicenseFormatException("envelope size");
            var root = StrictJson.Parse(fileBytes);
            root.RequireExactKeys("algorithm", "kid", "payload", "signature");
            if (root.Get("algorithm").AsString("algorithm") != Algorithm) throw new LicenseFormatException("unsupported algorithm");
            var kid = root.Get("kid").AsString("kid");
            if (!Base64Url.IsKid(kid)) throw new LicenseFormatException("invalid kid");
            var sig = Base64Url.Decode(root.Get("signature").AsString("signature"));
            if (sig.Length != EcdsaP256.SignatureLength) throw new LicenseFormatException("signature length");
            var payload = Base64Url.Decode(root.Get("payload").AsString("payload"));
            return new SignedEnvelope { Kid = kid, Payload = payload, Signature = sig };
        }

        public static byte[] Serialize(string kid, byte[] payload, byte[] signature) => StrictJson.Canonical(JsonValue.Obj(
            ("algorithm", JsonValue.Str(Algorithm)),
            ("kid", JsonValue.Str(kid)),
            ("payload", JsonValue.Str(Base64Url.Encode(payload))),
            ("signature", JsonValue.Str(Base64Url.Encode(signature)))));

        public SignatureCheck Verify(TrustStore trust, string domainPrefix)
        {
            var key = trust.Find(Kid);
            if (key == null) return SignatureCheck.UnknownKey;
            return EcdsaP256.Verify(key.PublicKey, SignedBytes(domainPrefix, Payload), Signature) ? SignatureCheck.Valid : SignatureCheck.BadSignature;
        }

        public static byte[] SignedBytes(string domainPrefix, byte[] payload)
        {
            var d = Encoding.UTF8.GetBytes(domainPrefix);
            var all = new byte[d.Length + payload.Length];
            Buffer.BlockCopy(d, 0, all, 0, d.Length);
            Buffer.BlockCopy(payload, 0, all, d.Length, payload.Length);
            return all;
        }
    }

    internal static class EcdsaP256
    {
        public const int SignatureLength = 64;
        internal static readonly X9ECParameters Curve = ECNamedCurveTable.GetByName("P-256");
        internal static readonly ECDomainParameters Domain = new ECDomainParameters(Curve.Curve, Curve.G, Curve.N, Curve.H, Curve.GetSeed());
        internal static readonly BigInteger HalfN = Curve.N.ShiftRight(1);

        public static ECPublicKeyParameters ParsePublicKey(byte[] spkiDer)
        {
            AsymmetricKeyParameter k;
            try { k = PublicKeyFactory.CreateKey(spkiDer); }
            catch (Exception) { throw new LicenseFormatException("invalid public key"); }
            if (!(k is ECPublicKeyParameters ec) || !ec.Parameters.Curve.Equals(Curve.Curve) || !ec.Parameters.G.Equals(Curve.G))
                throw new LicenseFormatException("public key is not P-256");
            return new ECPublicKeyParameters(ec.Q, Domain);
        }

        public static bool Verify(ECPublicKeyParameters key, byte[] message, byte[] sig)
        {
            if (sig == null || sig.Length != SignatureLength) return false;
            var r = new BigInteger(1, sig, 0, 32);
            var s = new BigInteger(1, sig, 32, 32);
            if (r.SignValue <= 0 || s.SignValue <= 0 || r.CompareTo(Curve.N) >= 0 || s.CompareTo(Curve.N) >= 0) return false;
            if (s.CompareTo(HalfN) > 0) return false; // high-S form is not accepted
            var signer = new ECDsaSigner();
            signer.Init(false, key);
            return signer.VerifySignature(Hash(message), r, s);
        }

        internal static byte[] Hash(byte[] message) => Bytes.Sha256(message);
    }

    public static class Base64Url
    {
        public static string Encode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Strict: alphabet A–Z a–z 0–9 - _, no padding, and the text must be the canonical encoding.</summary>
        public static byte[] Decode(string s)
        {
            if (s == null || s.Length == 0 || s.Length > 64 * 1024) throw new LicenseFormatException("base64url length");
            foreach (var c in s)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) throw new LicenseFormatException("base64url alphabet");
            if (s.Length % 4 == 1) throw new LicenseFormatException("base64url length");
            var b64 = s.Replace('-', '+').Replace('_', '/');
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            byte[] r;
            try { r = Convert.FromBase64String(b64); }
            catch (FormatException) { throw new LicenseFormatException("base64url"); }
            if (Encode(r) != s) throw new LicenseFormatException("non-canonical base64url");
            return r;
        }

        public static bool IsKid(string kid)
            => kid != null && kid.Length >= 3 && kid.Length <= 64 && kid.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');
    }
}
