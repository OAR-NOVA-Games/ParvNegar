using System;
using System.Collections.Generic;
using System.Linq;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using ParvNegar.Licensing;
using ParvNegar.Security;

namespace ParvNegar.Licensing.Tests
{
    /// <summary>Ephemeral test signing key, generated inside the test run and never written to disk.</summary>
    internal sealed class TestSigner
    {
        public TestSigner(string kid = "pn-test-unit", TrustEnvironment env = TrustEnvironment.Test)
        {
            var gen = new ECKeyPairGenerator();
            gen.Init(new ECKeyGenerationParameters(EcdsaP256.Domain, new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            Private = (ECPrivateKeyParameters)pair.Private;
            Spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded();
            Kid = kid;
            Environment = env;
        }

        public string Kid { get; }
        public TrustEnvironment Environment { get; }
        public ECPrivateKeyParameters Private { get; }
        public byte[] Spki { get; }

        public TrustStore Trust(TrustStatus status = TrustStatus.Active) => new TrustStore(Environment, new[] { new TrustedKey(Kid, Spki, Environment, status) });

        public byte[] SignRaw(string domain, byte[] payload, bool highS = false)
        {
            var signer = new Org.BouncyCastle.Crypto.Signers.ECDsaSigner(new Org.BouncyCastle.Crypto.Signers.HMacDsaKCalculator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest()));
            signer.Init(true, Private);
            var rs = signer.GenerateSignature(Bytes.Sha256(SignedEnvelope.SignedBytes(domain, payload)));
            var s = rs[1];
            if (s.CompareTo(EcdsaP256.HalfN) > 0) s = EcdsaP256.Curve.N.Subtract(s);
            if (highS) s = EcdsaP256.Curve.N.Subtract(s);
            var sig = new byte[64];
            Fixed(rs[0]).CopyTo(sig, 0);
            Fixed(s).CopyTo(sig, 32);
            return SignedEnvelope.Serialize(Kid, payload, sig);
        }

        public byte[] License(DeviceClaims device, Action<Dictionary<string, JsonValue>> edit = null, string kid = null)
        {
            var c = new LicenseClaims
            {
                Kid = kid ?? Kid, IssuedUtc = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc), LicenseId = "6f1b4c2a-6c1e-4b7f-9a52-0f3a1e2b9c11",
                LicenseRevision = 1, CustomerRef = "school-0001", RequestId = "1e0f2b3c-4d5e-4f60-8a1b-2c3d4e5f6071",
                Editions = new[] { "legacy", "modern" }, Architectures = new[] { "x64", "x86" }, Device = device,
            };
            var json = c.ToJson();
            if (edit != null)
            {
                var d = json.Members.ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);
                edit(d);
                json = JsonValue.Obj(d.Select(kv => new KeyValuePair<string, JsonValue>(kv.Key, kv.Value)));
            }
            return SignRaw(ProductLicensing.LicenseDomain, StrictJson.Canonical(json));
        }

        private static byte[] Fixed(BigInteger v)
        {
            var b = v.ToByteArrayUnsigned();
            var r = new byte[32];
            Buffer.BlockCopy(b, 0, r, 32 - b.Length, b.Length);
            return r;
        }
    }

    internal sealed class FakeSignals : IDeviceSignalSource
    {
        private readonly string _smbios, _disk, _guid;
        public FakeSignals(string smbios, string disk, string guid) { _smbios = smbios; _disk = disk; _guid = guid; }
        public IReadOnlyList<SignalReading> Read() => new[]
        {
            new SignalReading(DeviceSignal.Smbios, _smbios), new SignalReading(DeviceSignal.Disk, _disk), new SignalReading(DeviceSignal.MachineGuid, _guid),
        };

        public static readonly FakeSignals PcA = new FakeSignals("4C4C4544-0042-3510-8051-B4C04F4B3932", "S4EWNX0R123456A", "{6a1e7a43-3b9c-4a6e-9d8f-0c1b2a3d4e5f}");
        public DeviceClaims Claims() => DeviceBinding.Claims(Read());
    }
}
