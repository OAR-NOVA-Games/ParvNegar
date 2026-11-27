using System;
using System.IO;
using System.Linq;
using ParvNegar.Licensing;

namespace ParvNegar.DevTool
{
    /// <summary>
    /// Cross-edition licensing checks. Raw hardware identifiers are never printed: only signal quality and
    /// the first 12 hex digits of each claim digest, which must be identical in every edition and architecture
    /// (x86 processes read the 64-bit registry view and the same firmware/disk data).
    /// </summary>
    internal static class LicenseInterop
    {
        private static string Edition => Program.Edition.StartsWith("legacy", StringComparison.Ordinal) ? "legacy" : "modern";
        private static string Arch => Environment.Is64BitProcess ? "x64" : "x86";

        public static int Signals()
        {
            var readings = new WindowsDeviceSignals().Read();
            var claims = DeviceBinding.Claims(readings);
            Console.WriteLine("SIGNALS\t" + Program.Edition + "\t" + DeviceBinding.Describe(readings));
            Console.WriteLine("CLAIMS\t" + Program.Edition + "\t" + string.Join(" ", new[] { claims.Smbios, claims.Disk, claims.MachineGuid }.Select(c => c == null ? "none" : c.Substring(0, 12))));
            Console.WriteLine("USABLE\t" + claims.UsableCount);
            return claims.UsableCount >= DeviceBinding.RequiredMatches ? 0 : 1;
        }

        public static int Request(string outFile)
        {
            var claims = DeviceBinding.Claims(new WindowsDeviceSignals().Read());
            var r = ActivationRequest.Create(Guid.NewGuid(), DateTime.UtcNow, "0.2.0", Edition, Arch, Environment.OSVersion.Version, claims);
            File.WriteAllBytes(outFile, r.Serialize());
            Console.WriteLine("REQUEST\t" + Program.Edition + "\t" + r.RequestId + "\t" + r.ConfirmationCode());
            return 0;
        }

        public static int Verify(string licenseFile, string anchorsFile)
        {
            var anchors = File.ReadAllBytes(anchorsFile);
            var envName = StrictJson.Parse(anchors).Get("environment").AsString("environment");
            var env = envName == "production" ? TrustEnvironment.Production : envName == "development" ? TrustEnvironment.Development : TrustEnvironment.Test;
            var check = LicenseVerifier.Verify(File.ReadAllBytes(licenseFile), TrustStore.Load(anchors, env), Edition, Arch, DeviceBinding.Claims(new WindowsDeviceSignals().Read()));
            Console.WriteLine("LICENSE\t" + Program.Edition + "\t" + check.Status + "\tmatched=" + check.MatchedSignals + (check.Detail != null ? "\t" + check.Detail : ""));
            return check.IsValid ? 0 : 1;
        }
    }
}
