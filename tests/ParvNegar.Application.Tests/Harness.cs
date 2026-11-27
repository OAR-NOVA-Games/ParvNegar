using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using ParvNegar.Application.Security;
using ParvNegar.Licensing;
using ParvNegar.Security;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Application.Tests
{
    internal sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc);
        public long MonotonicMilliseconds { get; set; } = 1000;
        public void Advance(TimeSpan t) { UtcNow += t; MonotonicMilliseconds += (long)t.TotalMilliseconds; }
    }

    /// <summary>Stand-in for DPAPI LocalMachine: AES-GCM under a per-"machine" key. Changing the machine key simulates a Windows reinstall.</summary>
    internal sealed class FakeDpapi : IDeviceSecretProtector
    {
        public byte[] MachineKey = CryptoRandom.Bytes(32);
        public byte[] Protect(byte[] secret, byte[] entropy)
        {
            var nonce = CryptoRandom.Bytes(12);
            return nonce.Concat(AesGcm256.Encrypt(MachineKey, nonce, entropy ?? new byte[0], secret)).ToArray();
        }
        public byte[] Unprotect(byte[] blob, byte[] entropy)
            => AesGcm256.Decrypt(MachineKey, blob.Take(12).ToArray(), entropy ?? new byte[0], blob.Skip(12).ToArray());
    }

    internal sealed class FakeSignals : IDeviceSignalSource
    {
        public string Smbios = "4C4C4544-0042-3510-8051-B4C04F4B3932", Disk = "S4EWNX0R123456A", Guid = "6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f";
        public IReadOnlyList<SignalReading> Read() => new[] { new SignalReading(DeviceSignal.Smbios, Smbios), new SignalReading(DeviceSignal.Disk, Disk), new SignalReading(DeviceSignal.MachineGuid, Guid) };
    }

    /// <summary>Ephemeral test issuer (keys generated in-process, never stored).</summary>
    internal sealed class TestIssuer
    {
        private readonly ECPrivateKeyParameters _key;
        public readonly TrustStore Trust;
        public const string Kid = "pn-test-app";

        public TestIssuer()
        {
            var gen = new ECKeyPairGenerator();
            gen.Init(new ECKeyGenerationParameters(EcdsaP256.Domain, new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            _key = (ECPrivateKeyParameters)pair.Private;
            Trust = new TrustStore(TrustEnvironment.Test, new[] { new TrustedKey(Kid, SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded(), TrustEnvironment.Test, TrustStatus.Active) });
        }

        public byte[] License(ActivationRequest r, Guid? licenseId = null)
        {
            var c = new LicenseClaims
            {
                Kid = Kid, IssuedUtc = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc), LicenseId = (licenseId ?? Guid.NewGuid()).ToString("D"), LicenseRevision = 1,
                CustomerRef = "school-0001", RequestId = r.RequestId, Editions = new[] { "legacy", "modern" }, Architectures = new[] { "x64", "x86" }, Device = r.Claims,
            };
            return Sign(ProductLicensing.LicenseDomain, StrictJson.Canonical(c.ToJson()));
        }

        public byte[] Provisioning(byte[] licenseFile, string owner, out string bootstrapCode, string requestIdOverride = null)
        {
            var lic = LicenseClaims.FromPayload(SignedEnvelope.Parse(licenseFile).Payload);
            var secret = CryptoRandom.Bytes(20);
            var id = Guid.NewGuid().ToString("D");
            var p = new OwnerProvisioning
            {
                Kid = Kid, IssuedUtc = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc), ProvisioningId = id, LicenseId = lic.LicenseId,
                RequestId = requestIdOverride ?? lic.RequestId, OwnerUsername = owner, Commitment = OwnerProvisioning.ComputeCommitment(secret, id, owner),
            };
            bootstrapCode = TypedCode.Encode(secret, TypedCode.BootstrapPurpose);
            return Sign(ProductLicensing.ProvisioningDomain, StrictJson.Canonical(p.ToJson()));
        }

        private byte[] Sign(string domain, byte[] payload)
        {
            var signer = new Org.BouncyCastle.Crypto.Signers.ECDsaSigner(new Org.BouncyCastle.Crypto.Signers.HMacDsaKCalculator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest()));
            signer.Init(true, _key);
            var rs = signer.GenerateSignature(Bytes.Sha256(SignedEnvelope.SignedBytes(domain, payload)));
            var s = rs[1].CompareTo(EcdsaP256.HalfN) > 0 ? EcdsaP256.Curve.N.Subtract(rs[1]) : rs[1];
            var sig = new byte[64];
            Fixed(rs[0]).CopyTo(sig, 0); Fixed(s).CopyTo(sig, 32);
            return SignedEnvelope.Serialize(Kid, payload, sig);
        }

        private static byte[] Fixed(BigInteger v) { var b = v.ToByteArrayUnsigned(); var r = new byte[32]; Buffer.BlockCopy(b, 0, r, 32 - b.Length, b.Length); return r; }
    }

    /// <summary>A complete synthetic installation in a temp folder.</summary>
    internal sealed class Harness : IDisposable
    {
        public const string OwnerPassword = "باغ سبز کنار رودخانه ۱۴۰۵ synthetic";
        public const string Owner = "owner.test";
        public static readonly Argon2idParameters Fast = new Argon2idParameters(Argon2idParameters.MinMemoryKiB, 2, 1);

        public readonly TempDir Dir = new TempDir("app");
        public readonly TestClock Clock = new TestClock();
        public readonly FakeDpapi Dpapi = new FakeDpapi();
        public readonly FakeSignals Signals = new FakeSignals();
        public readonly TestIssuer Issuer = new TestIssuer();
        public InstallationPaths Paths;
        public SessionManager Sm;
        public byte[] LicenseFile;
        public string RecoveryCode;

        public Harness() { Paths = new InstallationPaths(Dir.Path); Sm = NewManager(); }

        public SessionManager NewManager() => new SessionManager(Paths, Issuer.Trust, Signals, Dpapi, Clock,
            new SessionOptions { Edition = "legacy", Architecture = "x86", Kdf = Fast });

        public void Restart() { Sm.Dispose(); Sm = NewManager(); }

        public void License()
        {
            Sm.GenerateActivationRequest(out var req);
            LicenseFile = Issuer.License(req);
            Assert.True(Sm.ImportLicense(LicenseFile).IsValid);
        }

        public Harness Setup(bool confirmRecovery = true)
        {
            License();
            var prov = Issuer.Provisioning(LicenseFile, Owner, out var code);
            Assert.True(Sm.LoadProvisioning(prov).Ok);
            var r = Sm.CompleteSetup(code, OwnerPassword);
            Assert.Equal(AuthResult.Success, r.Result);
            RecoveryCode = r.RecoveryKeyCode;
            if (confirmRecovery) Assert.True(Sm.ConfirmRecoveryKeySaved(RecoveryCode.Split('-').Last(), RecoveryCode));
            return this;
        }

        public Guid AddDelegate(string name, string pw, params string[] permissions)
        {
            Assert.Equal(AuthResult.Success, Sm.CreateDelegate(Sm.Token, name, name, pw, out var id).Result);
            if (permissions.Length > 0) Sm.SetPermissions(Sm.Token, id, permissions);
            return id;
        }

        /// <summary>Every file under the installation root, as bytes (for secret-leak scans).</summary>
        public IEnumerable<(string path, byte[] bytes)> AllFiles()
            => Directory.EnumerateFiles(Paths.Root, "*", SearchOption.AllDirectories).Select(f => (f, File.ReadAllBytes(f)));

        public void Dispose() { Sm.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Dir.Dispose(); }
    }
}
