using System;
using System.Linq;
using System.Text;
using ParvNegar.Licensing;
using ParvNegar.Security;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Licensing.Tests
{
    public class StrictJsonTests
    {
        public StrictJsonTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Theory]
        [InlineData("{\"a\":1,\"a\":2}", "duplicate key")]
        [InlineData("{\"a\":1.5}", "only integers")]
        [InlineData("{\"a\":1e3}", "only integers")]
        [InlineData("{\"a\":01}", "leading zero")]
        [InlineData("{\"a\":9007199254740992}", "safe range")]
        [InlineData("{\"a\":-0}", "negative zero")]
        [InlineData("{\"a\":\"\\ud800\"}", "unpaired surrogate")]
        [InlineData("{\"a\":1} x", "trailing data")]
        [InlineData("{\"a\":\"x\ny\"}", "control character")]
        [InlineData("[1,]", "unexpected")]
        public void Ambiguous_or_unsupported_json_is_rejected(string text, string reason)
        {
            var ex = Assert.Throws<LicenseFormatException>(() => StrictJson.Parse(Encoding.UTF8.GetBytes(text)));
            Assert.Contains(reason, ex.Reason);
        }

        [Fact]
        public void Bom_invalid_utf8_and_deep_nesting_are_rejected()
        {
            Assert.Throws<LicenseFormatException>(() => StrictJson.Parse(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' }));
            Assert.Throws<LicenseFormatException>(() => StrictJson.Parse(new byte[] { (byte)'"', 0xC3, (byte)'"' }));
            Assert.Throws<LicenseFormatException>(() => StrictJson.Parse(Encoding.ASCII.GetBytes(new string('[', 40) + new string(']', 40))));
        }

        [Fact]
        public void Canonical_form_is_unique_and_enforced()
        {
            var v = StrictJson.Parse(Encoding.UTF8.GetBytes("{ \"b\" : [1, \"x\\u0041\"], \"a\": null, \"ä\": \"\\u0001\" }"));
            var c = StrictJson.Canonical(v);
            Assert.Equal("{\"a\":null,\"b\":[1,\"xA\"],\"ä\":\"\\u0001\"}", Encoding.UTF8.GetString(c));
            Assert.Equal(JsonKind.Object, StrictJson.ParseCanonical(c).Kind);
            Assert.Throws<LicenseFormatException>(() => StrictJson.ParseCanonical(Encoding.UTF8.GetBytes("{\"b\":1,\"a\":2}")));
            Assert.Throws<LicenseFormatException>(() => StrictJson.ParseCanonical(Encoding.UTF8.GetBytes("{\"a\":\"\\u0041\"}")));
            Assert.Throws<LicenseFormatException>(() => StrictJson.ParseCanonical(Encoding.UTF8.GetBytes("{\"a\": 1}")));
        }

        [Theory]
        [InlineData("YWJj", true)]
        [InlineData("YWJj=", false)]
        [InlineData("YW+j", false)]
        [InlineData("YWI", true)]
        [InlineData("YWJ", false)] // non-canonical trailing bits
        public void Base64url_is_strict(string s, bool ok)
        {
            if (ok) Assert.NotEmpty(Base64Url.Decode(s));
            else Assert.Throws<LicenseFormatException>(() => Base64Url.Decode(s));
        }
    }

    public class LicenseVerificationTests
    {
        private static readonly TestSigner Signer = new TestSigner();
        private static readonly DeviceClaims Device = FakeSignals.PcA.Claims();

        public LicenseVerificationTests() { TestEnvironment.AssertExpectedArchitecture(); }

        private static LicenseCheck Check(byte[] lic, TrustStore trust = null, DeviceClaims current = null, string edition = "legacy", string arch = "x86")
            => LicenseVerifier.Verify(lic, trust ?? Signer.Trust(), edition, arch, current ?? Device);

        [Fact]
        public void Valid_license_verifies_for_every_edition_and_architecture()
        {
            var lic = Signer.License(Device);
            foreach (var e in ProductLicensing.Editions)
                foreach (var a in ProductLicensing.Architectures)
                {
                    var r = Check(lic, edition: e, arch: a);
                    Assert.Equal(LicenseStatus.Valid, r.Status);
                    Assert.Equal(3, r.MatchedSignals);
                    Assert.Equal("school-0001", r.Claims.CustomerRef);
                }
        }

        [Fact]
        public void Edition_and_architecture_limits_are_enforced()
        {
            var lic = Signer.License(Device, d => { d["editions"] = JsonValue.Arr(new[] { JsonValue.Str("modern") }); d["architectures"] = JsonValue.Arr(new[] { JsonValue.Str("x64") }); });
            Assert.Equal(LicenseStatus.Valid, Check(lic, edition: "modern", arch: "x64").Status);
            Assert.Equal(LicenseStatus.WrongEdition, Check(lic, edition: "legacy", arch: "x64").Status);
            Assert.Equal(LicenseStatus.WrongEdition, Check(lic, edition: "modern", arch: "x86").Status);
        }

        [Fact]
        public void Wrong_product_is_rejected()
            => Assert.Equal(LicenseStatus.WrongProduct, Check(Signer.License(Device, d => d["product_id"] = JsonValue.Str("yarizan"))).Status);

        [Fact]
        public void Unsupported_format_policy_and_binding_versions_are_rejected()
        {
            Assert.Equal(LicenseStatus.UnsupportedFormat, Check(Signer.License(Device, d => d["format"] = JsonValue.Str("PARVNEGAR-LICENSE-V2"))).Status);
            Assert.NotEqual(LicenseStatus.Valid, Check(Signer.License(Device, d => d["policy"] = JsonValue.Str("subscription"))).Status);
            Assert.NotEqual(LicenseStatus.Valid, Check(Signer.License(Device, d => d["expires_at"] = JsonValue.Str("2030-01-01T00:00:00Z"))).Status);
            Assert.NotEqual(LicenseStatus.Valid, Check(Signer.License(Device, d => d["unknown"] = JsonValue.Int(1))).Status);
            Assert.NotEqual(LicenseStatus.Valid, Check(Signer.License(Device, d => d.Remove("customer_ref"))).Status);
            Assert.NotEqual(LicenseStatus.Valid, Check(Signer.License(Device, d => d["license_revision"] = JsonValue.Int(0))).Status);
        }

        [Fact]
        public void Invalid_signature_tampered_claims_and_high_s_are_rejected()
        {
            var lic = Signer.License(Device);
            var env = StrictJson.Parse(lic);
            var payload = Base64Url.Decode(env.Get("payload").AsString("p"));
            var sig = Base64Url.Decode(env.Get("signature").AsString("s"));

            var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload).Replace("school-0001", "school-0002"));
            Assert.Equal(LicenseStatus.InvalidSignature, Check(SignedEnvelope.Serialize(Signer.Kid, tampered, sig)).Status);

            var flipped = (byte[])sig.Clone(); flipped[10] ^= 1;
            Assert.Equal(LicenseStatus.InvalidSignature, Check(SignedEnvelope.Serialize(Signer.Kid, payload, flipped)).Status);

            var other = new TestSigner();
            Assert.Equal(LicenseStatus.InvalidSignature, Check(other.License(Device)).Status); // same kid, different key

            Assert.Equal(LicenseStatus.InvalidSignature, Check(Signer.SignRaw(ProductLicensing.LicenseDomain, payload, highS: true)).Status);
            Assert.Equal(LicenseStatus.InvalidSignature, Check(Signer.SignRaw(ProductLicensing.ProvisioningDomain, payload)).Status); // cross-domain
        }

        [Fact]
        public void Unknown_revoked_and_environment_mismatched_keys_are_rejected()
        {
            var lic = Signer.License(Device);
            Assert.Equal(LicenseStatus.UnknownKey, Check(lic, trust: new TestSigner("pn-test-other").Trust()).Status);
            Assert.Equal(LicenseStatus.UnknownKey, Check(lic, trust: Signer.Trust(TrustStatus.Revoked)).Status);
            Assert.Equal(LicenseStatus.Valid, Check(lic, trust: Signer.Trust(TrustStatus.Retired)).Status); // perpetual licenses outlive key retirement
            Assert.Throws<LicenseFormatException>(() => new TrustStore(TrustEnvironment.Production, new[] { new TrustedKey("pn-test-unit", Signer.Spki, TrustEnvironment.Test, TrustStatus.Active) }));
            Assert.Throws<LicenseFormatException>(() => new TrustStore(TrustEnvironment.Production, new[] { new TrustedKey("pn-dev-x", Signer.Spki, TrustEnvironment.Production, TrustStatus.Active) }));
            Assert.Throws<LicenseFormatException>(() => new TrustStore(TrustEnvironment.Development, new[] { new TrustedKey("pn-prod-2027-01", Signer.Spki, TrustEnvironment.Development, TrustStatus.Active) }));
            var kidMismatch = Signer.License(Device, kid: "pn-test-other");
            Assert.Equal(LicenseStatus.Malformed, Check(kidMismatch).Status);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"algorithm\":\"ES256\",\"kid\":\"pn-test-unit\",\"payload\":\"e30\",\"signature\":\"AA\"}")]
        [InlineData("{\"algorithm\":\"ES256-P1363\",\"kid\":\"pn-test-unit\",\"payload\":\"e30\",\"signature\":\"AA\"}")]
        [InlineData("{\"algorithm\":\"ES256-P1363\",\"algorithm\":\"ES256-P1363\",\"kid\":\"pn-test-unit\",\"payload\":\"e30\",\"signature\":\"AA\"}")]
        public void Malformed_files_are_rejected(string text)
        {
            var r = Check(Encoding.UTF8.GetBytes(text));
            Assert.Equal(LicenseStatus.Malformed, r.Status);
        }

        [Fact]
        public void Duplicate_keys_inside_a_signed_payload_are_rejected_even_with_a_valid_signature()
        {
            var payload = Encoding.UTF8.GetBytes("{\"format\":\"PARVNEGAR-LICENSE-V1\",\"format\":\"PARVNEGAR-LICENSE-V1\"}");
            Assert.NotEqual(LicenseStatus.Valid, Check(Signer.SignRaw(ProductLicensing.LicenseDomain, payload)).Status);
        }

        [Fact]
        public void One_changed_signal_is_tolerated_two_are_not()
        {
            var lic = Signer.License(Device);
            var newDisk = new FakeSignals("4C4C4544-0042-3510-8051-B4C04F4B3932", "WD-NEWDISK-998877", "{6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f}").Claims();
            var reinstall = new FakeSignals("4C4C4544-0042-3510-8051-B4C04F4B3932", "S4EWNX0R123456A", "{11111111-2222-4333-8444-555555555555}").Claims();
            var mainboard = new FakeSignals("8D2A1F00-1111-2222-3333-444455556666", "S4EWNX0R123456A", "{6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f}").Claims();
            Assert.Equal(LicenseStatus.Valid, Check(lic, current: newDisk).Status);
            Assert.Equal(LicenseStatus.Valid, Check(lic, current: reinstall).Status);
            Assert.Equal(LicenseStatus.Valid, Check(lic, current: mainboard).Status);
            var newPc = new FakeSignals("8D2A1F00-1111-2222-3333-444455556666", "WD-NEWDISK-998877", "{6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f}").Claims();
            var r = Check(lic, current: newPc);
            Assert.Equal(LicenseStatus.DeviceMismatch, r.Status);
            Assert.Equal(1, r.MatchedSignals);
        }

        [Fact]
        public void Unavailable_current_signal_never_lowers_the_threshold()
        {
            var lic = Signer.License(Device);
            var noDisk = new FakeSignals("4C4C4544-0042-3510-8051-B4C04F4B3932", null, "{6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f}").Claims();
            Assert.Equal(LicenseStatus.Valid, Check(lic, current: noDisk).Status); // two real matches
            var noDiskNewGuid = new FakeSignals("4C4C4544-0042-3510-8051-B4C04F4B3932", null, "{11111111-2222-4333-8444-555555555555}").Claims();
            Assert.Equal(LicenseStatus.InsufficientDeviceIdentity, Check(lic, current: new FakeSignals(null, null, "{6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f}").Claims()).Status);
            Assert.Equal(LicenseStatus.DeviceMismatch, Check(lic, current: noDiskNewGuid).Status);
        }

        [Theory]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
        [InlineData("03000200-0400-0500-0006-000700080009")]
        [InlineData("To be filled by O.E.M.")]
        [InlineData("")]
        [InlineData("   ")]
        public void Placeholder_smbios_values_are_not_usable(string raw)
        {
            var r = new SignalReading(DeviceSignal.Smbios, raw);
            Assert.NotEqual(SignalQuality.Good, r.Quality);
        }

        [Theory]
        [InlineData("0000000000000000")]
        [InlineData("XXXXXXXXXXXX")]
        [InlineData("Default string")]
        [InlineData("None")]
        [InlineData("ab")]
        public void Placeholder_disk_values_are_not_usable(string raw)
            => Assert.NotEqual(SignalQuality.Good, new SignalReading(DeviceSignal.Disk, raw).Quality);

        [Fact]
        public void Normalization_is_deterministic_across_case_braces_and_whitespace()
        {
            var a = DeviceBinding.Claims(new FakeSignals("4c4c4544-0042-3510-8051-b4c04f4b3932", "  S4EWNX0R123456A \0", "6A1E7A43-3B9C-4A6E-9D8F-0C1B2A3D4E5F").Read());
            var b = FakeSignals.PcA.Claims();
            Assert.Equal(3, DeviceBinding.CountMatches(a, b));
            Assert.DoesNotContain("S4EWNX0R123456A", a.Disk, StringComparison.OrdinalIgnoreCase); // digest, not raw value
        }

        [Fact]
        public void Request_generation_refuses_insufficient_identity()
        {
            var weak = new FakeSignals("00000000-0000-0000-0000-000000000000", "Default string", "{6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f}").Claims();
            Assert.Equal(1, weak.UsableCount);
            Assert.Throws<InsufficientDeviceIdentityException>(() => ActivationRequest.Create(Guid.NewGuid(), DateTime.UtcNow, "0.2.0", "legacy", "x86", new Version(6, 1, 7601), weak));
        }

        [Fact]
        public void Activation_request_round_trips_and_contains_no_raw_identifiers()
        {
            var r = ActivationRequest.Create(Guid.NewGuid(), new DateTime(2026, 11, 27, 9, 30, 15, 123, DateTimeKind.Utc), "0.2.0", "modern", "x64", new Version(10, 0, 22631), Device);
            var bytes = r.Serialize();
            var text = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain("S4EWNX0R", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("4c4c4544", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("6a1e7a43", text, StringComparison.OrdinalIgnoreCase);
            var back = ActivationRequest.Parse(bytes);
            Assert.Equal(r.RequestId, back.RequestId);
            Assert.Equal("2026-11-27T09:30:15Z", text.Split(new[] { "\"created_at\":\"" }, StringSplitOptions.None)[1].Substring(0, 20));
            Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", back.ConfirmationCode());
        }

        [Fact]
        public void Perpetual_license_ignores_the_clock_and_issued_at()
        {
            // The verifier takes no clock. Licenses "issued" far in the past or future verify identically.
            foreach (var issued in new[] { "1999-01-01T00:00:00Z", "2026-11-27T08:00:00Z", "2099-12-31T23:59:59Z" })
                Assert.Equal(LicenseStatus.Valid, Check(Signer.License(Device, d => d["issued_at"] = JsonValue.Str(issued))).Status);
            Assert.DoesNotContain(typeof(LicenseVerifier).GetMethods().SelectMany(m => m.GetParameters()), p => p.ParameterType == typeof(DateTime));
        }

        [Fact]
        public void Provisioning_commitment_verifies_only_the_right_bootstrap_code()
        {
            var secret = CryptoRandom.Bytes(20);
            var code = TypedCode.Encode(secret, TypedCode.BootstrapPurpose);
            var prov = new OwnerProvisioning
            {
                Kid = Signer.Kid, IssuedUtc = DateTime.UtcNow, ProvisioningId = "2a3b4c5d-6e7f-4a81-9b2c-3d4e5f607182", LicenseId = "6f1b4c2a-6c1e-4b7f-9a52-0f3a1e2b9c11",
                RequestId = "1e0f2b3c-4d5e-4f60-8a1b-2c3d4e5f6071", OwnerUsername = "owner.karimi", Commitment = OwnerProvisioning.ComputeCommitment(secret, "2a3b4c5d-6e7f-4a81-9b2c-3d4e5f607182", "owner.karimi"),
            };
            prov.IssuedUtc = new DateTime(prov.IssuedUtc.Ticks - prov.IssuedUtc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            var file = Signer.SignRaw(ProductLicensing.ProvisioningDomain, StrictJson.Canonical(prov.ToJson()));
            Assert.DoesNotContain(code, Encoding.UTF8.GetString(file));
            var back = OwnerProvisioning.Verify(file, Signer.Trust(), out var check);
            Assert.Equal(SignatureCheck.Valid, check);
            Assert.True(back.VerifyBootstrapSecret(TypedCode.Decode(code.ToLowerInvariant().Replace("-", " "), 20, TypedCode.BootstrapPurpose)));
            Assert.False(back.VerifyBootstrapSecret(CryptoRandom.Bytes(20)));
            Assert.Null(OwnerProvisioning.Verify(file, new TestSigner().Trust(), out var bad));
            Assert.Equal(SignatureCheck.BadSignature, bad);
        }
    }

    public class TypedCodeTests
    {
        public TypedCodeTests() { TestEnvironment.AssertExpectedArchitecture(); }

        [Fact]
        public void Recovery_key_code_round_trips_with_tolerant_input_and_detects_typos()
        {
            var key = CryptoRandom.Bytes(32);
            var code = TypedCode.Encode(key, TypedCode.RecoveryKeyPurpose);
            Assert.Matches("^([0-9A-HJKMNP-TV-Z]{5}-){10}[0-9A-HJKMNP-TV-Z]{5}$", code); // 34 bytes = 272 bits = 55 characters
            Assert.Equal(key, TypedCode.Decode(code, 32, TypedCode.RecoveryKeyPurpose));
            Assert.Equal(key, TypedCode.Decode(" " + code.ToLowerInvariant().Replace("-", "  ") + " ", 32, TypedCode.RecoveryKeyPurpose));
            var persianDigits = new string(code.Select(c => c >= '0' && c <= '9' ? (char)('\u06F0' + (c - '0')) : c).ToArray());
            Assert.Equal(key, TypedCode.Decode(persianDigits, 32, TypedCode.RecoveryKeyPurpose));
            Assert.Null(TypedCode.Decode(code, 32, TypedCode.BootstrapPurpose)); // purpose-bound
            int detected = 0, total = 0;
            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] == '-') continue;
                var typo = code.Substring(0, i) + (code[i] == 'X' ? 'Y' : 'X') + code.Substring(i + 1);
                total++;
                if (TypedCode.Decode(typo, 32, TypedCode.RecoveryKeyPurpose) == null) detected++;
            }
            Assert.True(detected >= total - 1, detected + "/" + total + " single-character typos detected");
            Assert.Null(TypedCode.Decode(code.Substring(0, code.Length - 1), 32, TypedCode.RecoveryKeyPurpose));
            Assert.Null(TypedCode.Decode(code + "0", 32, TypedCode.RecoveryKeyPurpose));
        }
    }

#if HAS_ISSUER
    public class IssuerTests
    {
        public IssuerTests() { TestEnvironment.AssertExpectedArchitecture(); }

        private static readonly byte[] Pass = Encoding.UTF8.GetBytes("synthetic issuer passphrase for tests");
        private static readonly Argon2idParameters Fast = new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 2, 1);

        [Fact]
        public void Signing_key_file_round_trips_and_rejects_wrong_passphrase_and_tampering()
        {
            var (file, spki) = ParvNegar.Issuer.SigningKeyFile.Generate("pn-test-issuer", TrustEnvironment.Test, Pass, Fast);
            using (var k = ParvNegar.Issuer.SigningKeyFile.Open(file, Pass)) Assert.Equal(spki, k.Spki);
            Assert.ThrowsAny<Exception>(() => ParvNegar.Issuer.SigningKeyFile.Open(file, Encoding.UTF8.GetBytes("wrong passphrase for the key file")));
            var text = Encoding.UTF8.GetString(file).Replace("pn-test-issuer", "pn-test-issuex");
            Assert.ThrowsAny<Exception>(() => ParvNegar.Issuer.SigningKeyFile.Open(Encoding.UTF8.GetBytes(text), Pass));
            Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(file));
        }

        [Fact]
        public void Production_prefix_rules_and_short_passphrases_are_enforced()
        {
            Assert.Throws<ArgumentException>(() => ParvNegar.Issuer.SigningKeyFile.Generate("pn-prod-2027-01", TrustEnvironment.Test, Pass, Fast));
            Assert.Throws<ArgumentException>(() => ParvNegar.Issuer.SigningKeyFile.Generate("pn-dev-x", TrustEnvironment.Production, Pass, Fast));
            Assert.Throws<ArgumentException>(() => ParvNegar.Issuer.SigningKeyFile.Generate("pn-test-x", TrustEnvironment.Test, Encoding.UTF8.GetBytes("short"), Fast));
        }

        [Fact]
        public void Issued_license_and_provisioning_verify_with_exported_trust_anchors()
        {
            var (file, spki) = ParvNegar.Issuer.SigningKeyFile.Generate("pn-test-issuer", TrustEnvironment.Test, Pass, Fast);
            var trust = TrustStore.Load(ParvNegar.Issuer.SigningKeyFile.TrustAnchors(TrustEnvironment.Test, ("pn-test-issuer", spki, "active")), TrustEnvironment.Test);
            var device = FakeSignals.PcA.Claims();
            var req = ActivationRequest.Create(Guid.NewGuid(), DateTime.UtcNow, "0.2.0", "legacy", "x86", new Version(6, 3, 9600), device);
            using (var key = ParvNegar.Issuer.SigningKeyFile.Open(file, Pass))
            {
                var lic = ParvNegar.Issuer.Issuance.IssueLicense(key, ActivationRequest.Parse(req.Serialize()), "school-0001", new[] { "modern", "legacy" }, new[] { "x86", "x64" }, Guid.NewGuid(), 1, DateTime.UtcNow, out var claims);
                Assert.Equal(LicenseStatus.Valid, LicenseVerifier.Verify(lic, trust, "legacy", "x86", device).Status);
                var record = ParvNegar.Issuer.Issuance.Record("license", claims);
                Assert.DoesNotContain("S4EWNX0R", record, StringComparison.OrdinalIgnoreCase);

                var prov = ParvNegar.Issuer.Issuance.IssueProvisioning(key, claims, "Owner.Karimi", DateTime.UtcNow, out var code, out var p);
                Assert.Equal("owner.karimi", p.OwnerUsername);
                var back = OwnerProvisioning.Verify(prov, trust, out var check);
                Assert.Equal(SignatureCheck.Valid, check);
                Assert.True(back.VerifyBootstrapSecret(TypedCode.Decode(code, 20, TypedCode.BootstrapPurpose)));
                Assert.DoesNotContain(code, Encoding.UTF8.GetString(prov));
                Assert.DoesNotContain(code, ParvNegar.Issuer.Issuance.Record("owner-provisioning", claims, p));

                Assert.Throws<LicenseFormatException>(() => ParvNegar.Issuer.Issuance.IssueLicense(key, req, "bad ref with spaces", new[] { "legacy" }, new[] { "x86" }, Guid.NewGuid(), 1, DateTime.UtcNow, out _));
            }
        }
    }
#endif
}
