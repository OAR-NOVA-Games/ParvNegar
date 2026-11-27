using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ParvNegar.Security;

namespace ParvNegar.Licensing
{
    public static class ProductLicensing
    {
        public const string ProductId = "parvnegar";
        public static readonly string[] Editions = { "legacy", "modern" };
        public static readonly string[] Architectures = { "x64", "x86" };
        public const string LicenseDomain = "ParvNegar-License-V1\n";
        public const string ProvisioningDomain = "ParvNegar-Owner-Provisioning-V1\n";
    }

    internal static class Field
    {
        public static string Uuid(JsonValue v, string name)
        {
            var s = v.AsString(name);
            if (s.Length != 36 || !Guid.TryParseExact(s, "D", out var g) || g == Guid.Empty || g.ToString("D") != s) throw new LicenseFormatException(name + ": lowercase UUID expected");
            return s;
        }

        public static DateTime Timestamp(JsonValue v, string name)
        {
            var s = v.AsString(name);
            if (s.Length != 20 || !DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                throw new LicenseFormatException(name + ": UTC timestamp yyyy-MM-ddTHH:mm:ssZ expected");
            return DateTime.SpecifyKind(t, DateTimeKind.Utc);
        }

        public static string FormatTimestamp(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        public static string Exact(JsonValue v, string name, string expected)
        {
            var s = v.AsString(name);
            if (!string.Equals(s, expected, StringComparison.Ordinal)) throw new LicenseFormatException(name + ": unsupported value");
            return s;
        }

        public static string Token(JsonValue v, string name, int min, int max, Func<char, bool> allowed)
        {
            var s = v.AsString(name);
            if (s.Length < min || s.Length > max || !s.All(allowed)) throw new LicenseFormatException(name + ": invalid value");
            return s;
        }

        public static string[] SortedSet(JsonValue v, string name, string[] universe)
        {
            var items = v.AsArray(name).Select(x => x.AsString(name)).ToArray();
            if (items.Length == 0) throw new LicenseFormatException(name + ": empty");
            for (int i = 0; i < items.Length; i++)
            {
                if (Array.IndexOf(universe, items[i]) < 0) throw new LicenseFormatException(name + ": unknown value");
                if (i > 0 && string.CompareOrdinal(items[i - 1], items[i]) >= 0) throw new LicenseFormatException(name + ": must be sorted and unique");
            }
            return items;
        }

        public static bool CustomerChar(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
    }

    // ------------------------------------------------------------------------------------------------
    // Activation request (.pnreq) — unsigned, canonical JSON, contains no secrets and no school data.
    // ------------------------------------------------------------------------------------------------
    public sealed class ActivationRequest
    {
        public const string Format = "PARVNEGAR-ACTIVATION-REQUEST-V1";

        public string RequestId { get; private set; }
        public DateTime CreatedUtc { get; private set; }
        public string AppVersion { get; private set; }
        public string Edition { get; private set; }
        public string Architecture { get; private set; }
        public string OsVersion { get; private set; }
        public DeviceClaims Claims { get; private set; }

        public static ActivationRequest Create(Guid requestId, DateTime nowUtc, string appVersion, string edition, string architecture, Version os, DeviceClaims claims)
        {
            if (claims.UsableCount < DeviceBinding.RequiredMatches) throw new InsufficientDeviceIdentityException(claims.UsableCount);
            var r = new ActivationRequest
            {
                RequestId = requestId.ToString("D"), CreatedUtc = DateTime.SpecifyKind(new DateTime(nowUtc.Ticks - nowUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
                AppVersion = appVersion, Edition = edition, Architecture = architecture,
                OsVersion = os.Major + "." + os.Minor + "." + Math.Max(0, os.Build), Claims = claims,
            };
            return Parse(r.Serialize()); // validates every field exactly as a reader would
        }

        public byte[] Serialize() => StrictJson.Canonical(JsonValue.Obj(
            ("app_version", JsonValue.Str(AppVersion)),
            ("architecture", JsonValue.Str(Architecture)),
            ("created_at", JsonValue.Str(Field.FormatTimestamp(CreatedUtc))),
            ("device_binding", JsonValue.Obj(("claims", Claims.ToJson()), ("version", JsonValue.Int(DeviceBinding.Version)))),
            ("edition", JsonValue.Str(Edition)),
            ("format", JsonValue.Str(Format)),
            ("os_version", JsonValue.Str(OsVersion)),
            ("product_id", JsonValue.Str(ProductLicensing.ProductId)),
            ("request_id", JsonValue.Str(RequestId))));

        public static ActivationRequest Parse(byte[] bytes)
        {
            var v = StrictJson.ParseCanonical(bytes);
            v.RequireExactKeys("app_version", "architecture", "created_at", "device_binding", "edition", "format", "os_version", "product_id", "request_id");
            Field.Exact(v.Get("format"), "format", Format);
            Field.Exact(v.Get("product_id"), "product_id", ProductLicensing.ProductId);
            var db = v.Get("device_binding");
            db.RequireExactKeys("claims", "version");
            if (db.Get("version").AsInteger("device_binding.version") != DeviceBinding.Version) throw new LicenseFormatException("unsupported device binding version");
            var claims = DeviceClaims.FromJson(db.Get("claims"));
            if (claims.UsableCount < DeviceBinding.RequiredMatches) throw new LicenseFormatException("request has fewer than two device claims");
            var edition = v.Get("edition").AsString("edition");
            var arch = v.Get("architecture").AsString("architecture");
            if (Array.IndexOf(ProductLicensing.Editions, edition) < 0 || Array.IndexOf(ProductLicensing.Architectures, arch) < 0) throw new LicenseFormatException("edition or architecture");
            return new ActivationRequest
            {
                RequestId = Field.Uuid(v.Get("request_id"), "request_id"),
                CreatedUtc = Field.Timestamp(v.Get("created_at"), "created_at"),
                AppVersion = Field.Token(v.Get("app_version"), "app_version", 1, 32, c => (c >= '0' && c <= '9') || c == '.' || (c >= 'a' && c <= 'z') || c == '-'),
                Edition = edition, Architecture = arch,
                OsVersion = Field.Token(v.Get("os_version"), "os_version", 3, 32, c => (c >= '0' && c <= '9') || c == '.'),
                Claims = claims,
            };
        }

        /// <summary>Short code the customer can read over the phone to confirm the file (first 16 hex of SHA-256).</summary>
        public string ConfirmationCode()
        {
            var h = Bytes.ToHex(Bytes.Sha256(Serialize())).Substring(0, 16).ToUpperInvariant();
            return h.Substring(0, 4) + "-" + h.Substring(4, 4) + "-" + h.Substring(8, 4) + "-" + h.Substring(12, 4);
        }
    }

    public sealed class InsufficientDeviceIdentityException : Exception
    {
        public InsufficientDeviceIdentityException(int usable) : base("Only " + usable + " usable device signal(s); at least " + DeviceBinding.RequiredMatches + " are required.") { Usable = usable; }
        public int Usable { get; }
    }

    // ------------------------------------------------------------------------------------------------
    // License (.pnlic) — signed, perpetual, device bound.
    // ------------------------------------------------------------------------------------------------
    public sealed class LicenseClaims
    {
        public const string Format = "PARVNEGAR-LICENSE-V1";

        public string Kid { get; internal set; }
        public DateTime IssuedUtc { get; internal set; }
        public string LicenseId { get; internal set; }
        public long LicenseRevision { get; internal set; }
        public string CustomerRef { get; internal set; }
        public string RequestId { get; internal set; }
        public string[] Editions { get; internal set; }
        public string[] Architectures { get; internal set; }
        public DeviceClaims Device { get; internal set; }

        internal JsonValue ToJson() => JsonValue.Obj(
            ("architectures", JsonValue.Arr(Architectures.Select(JsonValue.Str))),
            ("customer_ref", JsonValue.Str(CustomerRef)),
            ("device_binding", JsonValue.Obj(("claims", Device.ToJson()), ("match", JsonValue.Str(DeviceBinding.MatchPolicy)), ("version", JsonValue.Int(DeviceBinding.Version)))),
            ("editions", JsonValue.Arr(Editions.Select(JsonValue.Str))),
            ("expires_at", JsonValue.Null),
            ("format", JsonValue.Str(Format)),
            ("issued_at", JsonValue.Str(Field.FormatTimestamp(IssuedUtc))),
            ("kid", JsonValue.Str(Kid)),
            ("license_id", JsonValue.Str(LicenseId)),
            ("license_revision", JsonValue.Int(LicenseRevision)),
            ("policy", JsonValue.Str("perpetual")),
            ("product_id", JsonValue.Str(ProductLicensing.ProductId)),
            ("request_id", JsonValue.Str(RequestId)));

        internal static LicenseClaims FromPayload(byte[] payload)
        {
            var v = StrictJson.ParseCanonical(payload);
            v.RequireExactKeys("architectures", "customer_ref", "device_binding", "editions", "expires_at", "format", "issued_at", "kid", "license_id", "license_revision", "policy", "product_id", "request_id");
            Field.Exact(v.Get("format"), "format", Format);
            if (v.Get("product_id").AsString("product_id") != ProductLicensing.ProductId) throw new WrongProductException();
            Field.Exact(v.Get("policy"), "policy", "perpetual");
            if (v.Get("expires_at").Kind != JsonKind.Null) throw new LicenseFormatException("expires_at must be null for a perpetual license");
            var db = v.Get("device_binding");
            db.RequireExactKeys("claims", "match", "version");
            if (db.Get("version").AsInteger("device_binding.version") != DeviceBinding.Version) throw new LicenseFormatException("unsupported device binding version");
            Field.Exact(db.Get("match"), "device_binding.match", DeviceBinding.MatchPolicy);
            var claims = DeviceClaims.FromJson(db.Get("claims"));
            if (claims.UsableCount < DeviceBinding.RequiredMatches) throw new LicenseFormatException("license has fewer than two device claims");
            long rev = v.Get("license_revision").AsInteger("license_revision");
            if (rev < 1) throw new LicenseFormatException("license_revision must be ≥ 1");
            var kid = v.Get("kid").AsString("kid");
            if (!Base64Url.IsKid(kid)) throw new LicenseFormatException("kid");
            return new LicenseClaims
            {
                Kid = kid,
                IssuedUtc = Field.Timestamp(v.Get("issued_at"), "issued_at"),
                LicenseId = Field.Uuid(v.Get("license_id"), "license_id"),
                LicenseRevision = rev,
                CustomerRef = Field.Token(v.Get("customer_ref"), "customer_ref", 1, 64, Field.CustomerChar),
                RequestId = Field.Uuid(v.Get("request_id"), "request_id"),
                Editions = Field.SortedSet(v.Get("editions"), "editions", ProductLicensing.Editions),
                Architectures = Field.SortedSet(v.Get("architectures"), "architectures", ProductLicensing.Architectures),
                Device = claims,
            };
        }
    }

    public sealed class WrongProductException : Exception
    {
        public WrongProductException() : base("The artifact belongs to another product.") { }
    }

    public enum LicenseStatus
    {
        Valid,
        Malformed,
        UnsupportedFormat,
        WrongProduct,
        UnknownKey,
        InvalidSignature,
        WrongEdition,
        DeviceMismatch,
        InsufficientDeviceIdentity,
    }

    public sealed class LicenseCheck
    {
        public LicenseStatus Status { get; internal set; }
        public LicenseClaims Claims { get; internal set; }
        public int MatchedSignals { get; internal set; }
        public string Detail { get; internal set; }
        public bool IsValid => Status == LicenseStatus.Valid;
    }

    /// <summary>
    /// Offline license verification. Order: envelope → trusted key → signature over the exact payload →
    /// canonical payload → product → edition/architecture → device binding. No clock is consulted:
    /// the license is perpetual and <c>issued_at</c> is informational only.
    /// </summary>
    public static class LicenseVerifier
    {
        public static LicenseCheck Verify(byte[] fileBytes, TrustStore trust, string edition, string architecture, DeviceClaims current)
        {
            SignedEnvelope env;
            try { env = SignedEnvelope.Parse(fileBytes); }
            catch (LicenseFormatException ex) { return Fail(LicenseStatus.Malformed, ex.Reason); }

            var sig = env.Verify(trust, ProductLicensing.LicenseDomain);
            if (sig == SignatureCheck.UnknownKey) return Fail(LicenseStatus.UnknownKey, "kid " + env.Kid + " is not trusted by this build");
            if (sig == SignatureCheck.BadSignature) return Fail(LicenseStatus.InvalidSignature, "signature does not verify");

            LicenseClaims claims;
            try { claims = LicenseClaims.FromPayload(env.Payload); }
            catch (WrongProductException) { return Fail(LicenseStatus.WrongProduct, "product_id"); }
            catch (LicenseFormatException ex) { return Fail(ex.Reason.StartsWith("format", StringComparison.Ordinal) || ex.Reason.Contains("unsupported") ? LicenseStatus.UnsupportedFormat : LicenseStatus.Malformed, ex.Reason); }
            if (!string.Equals(claims.Kid, env.Kid, StringComparison.Ordinal)) return Fail(LicenseStatus.Malformed, "payload kid differs from envelope kid");

            if (Array.IndexOf(claims.Editions, edition) < 0 || Array.IndexOf(claims.Architectures, architecture) < 0)
                return new LicenseCheck { Status = LicenseStatus.WrongEdition, Claims = claims, Detail = edition + "/" + architecture };
            if (current == null || current.UsableCount < DeviceBinding.RequiredMatches)
                return new LicenseCheck { Status = LicenseStatus.InsufficientDeviceIdentity, Claims = claims, Detail = "current device identity is incomplete" };
            int matched = DeviceBinding.CountMatches(claims.Device, current);
            if (matched < DeviceBinding.RequiredMatches)
                return new LicenseCheck { Status = LicenseStatus.DeviceMismatch, Claims = claims, MatchedSignals = matched, Detail = matched + " of 3 signals match" };
            return new LicenseCheck { Status = LicenseStatus.Valid, Claims = claims, MatchedSignals = matched };
        }

        private static LicenseCheck Fail(LicenseStatus s, string detail) => new LicenseCheck { Status = s, Detail = detail };
    }

    // ------------------------------------------------------------------------------------------------
    // Owner provisioning (.pnprov) — signed; carries a commitment to the separately delivered
    // bootstrap secret, never the secret itself.
    // ------------------------------------------------------------------------------------------------
    public sealed class OwnerProvisioning
    {
        public const string Format = "PARVNEGAR-OWNER-PROVISIONING-V1";
        public const string CommitmentLabel = "ParvNegar/v1/provisioning-commitment\n";

        public string Kid { get; internal set; }
        public DateTime IssuedUtc { get; internal set; }
        public string ProvisioningId { get; internal set; }
        public string LicenseId { get; internal set; }
        public string RequestId { get; internal set; }
        public string OwnerUsername { get; internal set; }
        public string Commitment { get; internal set; }

        internal JsonValue ToJson() => JsonValue.Obj(
            ("bootstrap", JsonValue.Obj(("algorithm", JsonValue.Str("HMAC-SHA256")), ("commitment", JsonValue.Str(Commitment)))),
            ("format", JsonValue.Str(Format)),
            ("issued_at", JsonValue.Str(Field.FormatTimestamp(IssuedUtc))),
            ("kid", JsonValue.Str(Kid)),
            ("license_id", JsonValue.Str(LicenseId)),
            ("owner_username", JsonValue.Str(OwnerUsername)),
            ("product_id", JsonValue.Str(ProductLicensing.ProductId)),
            ("provisioning_id", JsonValue.Str(ProvisioningId)),
            ("request_id", JsonValue.Str(RequestId)));

        /// <summary>HMAC-SHA256(key = bootstrap secret, message = label ‖ provisioning_id ‖ "\n" ‖ owner_username).</summary>
        public static string ComputeCommitment(byte[] bootstrapSecret, string provisioningId, string ownerUsername)
        {
            using (var h = new System.Security.Cryptography.HMACSHA256(bootstrapSecret))
                return Bytes.ToHex(h.ComputeHash(Encoding.UTF8.GetBytes(CommitmentLabel + provisioningId + "\n" + ownerUsername)));
        }

        public bool VerifyBootstrapSecret(byte[] bootstrapSecret)
        {
            var c = ComputeCommitment(bootstrapSecret, ProvisioningId, OwnerUsername);
            return Bytes.FixedTimeEquals(Encoding.ASCII.GetBytes(c), Encoding.ASCII.GetBytes(Commitment));
        }

        public static OwnerProvisioning Verify(byte[] fileBytes, TrustStore trust, out SignatureCheck check)
        {
            var env = SignedEnvelope.Parse(fileBytes);
            check = env.Verify(trust, ProductLicensing.ProvisioningDomain);
            if (check != SignatureCheck.Valid) return null;
            var v = StrictJson.ParseCanonical(env.Payload);
            v.RequireExactKeys("bootstrap", "format", "issued_at", "kid", "license_id", "owner_username", "product_id", "provisioning_id", "request_id");
            Field.Exact(v.Get("format"), "format", Format);
            if (v.Get("product_id").AsString("product_id") != ProductLicensing.ProductId) throw new WrongProductException();
            var b = v.Get("bootstrap");
            b.RequireExactKeys("algorithm", "commitment");
            Field.Exact(b.Get("algorithm"), "bootstrap.algorithm", "HMAC-SHA256");
            var p = new OwnerProvisioning
            {
                Kid = v.Get("kid").AsString("kid"),
                IssuedUtc = Field.Timestamp(v.Get("issued_at"), "issued_at"),
                ProvisioningId = Field.Uuid(v.Get("provisioning_id"), "provisioning_id"),
                LicenseId = Field.Uuid(v.Get("license_id"), "license_id"),
                RequestId = Field.Uuid(v.Get("request_id"), "request_id"),
                OwnerUsername = Field.Token(v.Get("owner_username"), "owner_username", 3, 32, Usernames.IsAllowedChar),
                Commitment = Field.Token(b.Get("commitment"), "bootstrap.commitment", 64, 64, c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')),
            };
            if (p.Kid != env.Kid) throw new LicenseFormatException("payload kid differs from envelope kid");
            if (!Usernames.IsCanonical(p.OwnerUsername)) throw new LicenseFormatException("owner_username is not canonical");
            return p;
        }
    }

    /// <summary>Canonical usernames: 3–32 characters a–z 0–9 . _ -, starting with a letter (case-folded on input).</summary>
    public static class Usernames
    {
        public static bool IsAllowedChar(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';

        public static string Canonicalize(string input)
        {
            if (input == null) return null;
            var s = input.Trim().ToLowerInvariant();
            return IsCanonical(s) ? s : null;
        }

        public static bool IsCanonical(string s)
            => s != null && s.Length >= 3 && s.Length <= 32 && s[0] >= 'a' && s[0] <= 'z' && s.All(IsAllowedChar);
    }
}
