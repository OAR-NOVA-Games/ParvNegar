using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ParvNegar.Licensing;
using ParvNegar.Security;

namespace ParvNegar.Issuer
{
    /// <summary>
    /// ParvNegar publisher-only issuer (LICENSE-REISSUE-POLICY.md, SIGNING-KEY-CUSTODY.md).
    /// Exit codes: 0 success, 1 usage error, 2 verification or validation failure, 3 refused by policy.
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"ParvNegar.Issuer — PUBLISHER ONLY. Never ship this tool to customers.

  keygen --kid <kid> --environment development|test --out <key.pnsk> --trust-out <anchors.json>
         Production keys: only in the approved offline ceremony, with --environment production
         --ceremony-approved (SIGNING-KEY-CUSTODY.md). Never in CI or on a development PC.
  inspect-request <file.pnreq>
  issue --request <file.pnreq> --key <key.pnsk> --customer-ref <ref> --out <file.pnlic>
        [--editions legacy,modern] [--architectures x64,x86]
        [--license-id <uuid> --revision <n>]      (reissue: same license id, higher revision)
        [--records <dir>]
  issue-provisioning --license <file.pnlic> --key <key.pnsk> --owner-username <name> --out <file.pnprov> [--records <dir>]
        Prints the one-time bootstrap code ONCE. It is not stored anywhere; deliver it separately.
  verify <file> --trust <anchors.json> [--kind license|provisioning]
  inspect <file.pnlic|file.pnprov>                 (decodes claims without trusting them)

  Passphrase: PARVNEGAR_SIGNING_PASSPHRASE, --passphrase-file <file>, or an interactive prompt.";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) { Console.Error.WriteLine(Usage); return 1; }
                var a = Args.Parse(args.Skip(1).ToArray());
                switch (args[0])
                {
                    case "keygen": return KeyGen(a);
                    case "inspect-request": return InspectRequest(a.Positional(0));
                    case "issue": return Issue(a);
                    case "issue-provisioning": return IssueProvisioning(a);
                    case "verify": return Verify(a);
                    case "inspect": return Inspect(a.Positional(0));
                    default: Console.Error.WriteLine(Usage); return 1;
                }
            }
            catch (UsageException ex) { Console.Error.WriteLine("Usage error: " + ex.Message); return 1; }
            catch (LicenseFormatException ex) { Console.Error.WriteLine("Invalid input: " + ex.Reason); return 2; }
            catch (AuthenticationFailedException) { Console.Error.WriteLine("Wrong passphrase or modified key file."); return 2; }
            catch (PolicyException ex) { Console.Error.WriteLine("Refused: " + ex.Message); return 3; }
        }

        private static int KeyGen(Args a)
        {
            var kid = a.Required("--kid");
            var envName = a.Required("--environment");
            var env = envName == "development" ? TrustEnvironment.Development : envName == "test" ? TrustEnvironment.Test : envName == "production" ? TrustEnvironment.Production : throw new UsageException("environment");
            if (env == TrustEnvironment.Production && !a.Flag("--ceremony-approved"))
                throw new PolicyException("production key generation requires the approved offline ceremony (--ceremony-approved); see SIGNING-KEY-CUSTODY.md");
            if (env == TrustEnvironment.Production && Environment.GetEnvironmentVariable("CI") == "true")
                throw new PolicyException("production keys are never generated in CI");
            var outPath = a.Required("--out");
            var trustOut = a.Required("--trust-out");
            if (File.Exists(outPath) || File.Exists(trustOut)) throw new PolicyException("output exists; refusing to overwrite a key or trust file");
            var pass = Passphrase(a, confirm: true);
            try
            {
                var (file, spki) = SigningKeyFile.Generate(kid, env, pass, null);
                File.WriteAllBytes(outPath, file);
                File.WriteAllBytes(trustOut, SigningKeyFile.TrustAnchors(env, (kid, spki, "active")));
                Console.WriteLine("KEY\t" + kid + "\t" + TrustStore.EnvironmentName(env) + "\tspki_sha256=" + Bytes.ToHex(Bytes.Sha256(spki)));
                return 0;
            }
            finally { Bytes.Wipe(pass); }
        }

        private static int InspectRequest(string path)
        {
            var r = ActivationRequest.Parse(File.ReadAllBytes(path));
            Console.WriteLine("request_id\t" + r.RequestId);
            Console.WriteLine("confirmation\t" + r.ConfirmationCode());
            Console.WriteLine("created_at\t" + r.CreatedUtc.ToString("u"));
            Console.WriteLine("app\t" + r.AppVersion + "\t" + r.Edition + "\t" + r.Architecture + "\tos " + r.OsVersion);
            Console.WriteLine("device_claims\t" + r.Claims.UsableCount + " of 3 usable" + (r.Claims.UsableCount < 3 ? " (one signal missing: binding needs both remaining signals)" : ""));
            return 0;
        }

        private static int Issue(Args a)
        {
            var request = ActivationRequest.Parse(File.ReadAllBytes(a.Required("--request")));
            var customer = a.Required("--customer-ref");
            var outPath = a.Required("--out");
            if (File.Exists(outPath)) throw new PolicyException("output exists");
            var editions = (a.Optional("--editions") ?? "legacy,modern").Split(',');
            var archs = (a.Optional("--architectures") ?? "x64,x86").Split(',');
            var idText = a.Optional("--license-id");
            var revText = a.Optional("--revision");
            if ((idText == null) != (revText == null)) throw new UsageException("--license-id and --revision go together (reissue)");
            var licenseId = idText == null ? Guid.NewGuid() : Guid.ParseExact(idText, "D");
            long revision = revText == null ? 1 : long.Parse(revText, System.Globalization.CultureInfo.InvariantCulture);
            if (idText != null && revision < 2) throw new UsageException("a reissue needs revision 2 or higher");
            using (var key = OpenKey(a))
            {
                var file = Issuance.IssueLicense(key, request, customer, editions, archs, licenseId, revision, DateTime.UtcNow, out var claims);
                File.WriteAllBytes(outPath, file);
                AppendRecord(a, Issuance.Record(revision == 1 ? "license" : "license-reissue", claims));
                Console.WriteLine("ISSUED\t" + claims.LicenseId + "\trevision " + claims.LicenseRevision + "\t" + outPath);
            }
            return 0;
        }

        private static int IssueProvisioning(Args a)
        {
            var licBytes = File.ReadAllBytes(a.Required("--license"));
            var outPath = a.Required("--out");
            if (File.Exists(outPath)) throw new PolicyException("output exists");
            using (var key = OpenKey(a))
            {
                var trust = new TrustStore(key.Environment, new[] { new TrustedKey(key.Kid, key.Spki, key.Environment, TrustStatus.Active) });
                var env = SignedEnvelope.Parse(licBytes);
                if (env.Verify(trust, ProductLicensing.LicenseDomain) != SignatureCheck.Valid) throw new PolicyException("the license was not signed by this key");
                var claims = LicenseClaims.FromPayload(env.Payload);
                var file = Issuance.IssueProvisioning(key, claims, a.Required("--owner-username"), DateTime.UtcNow, out var code, out var prov);
                File.WriteAllBytes(outPath, file);
                AppendRecord(a, Issuance.Record("owner-provisioning", claims, prov));
                Console.WriteLine("PROVISIONING\t" + prov.ProvisioningId + "\towner " + prov.OwnerUsername + "\t" + outPath);
                Console.WriteLine("BOOTSTRAP CODE (shown once, deliver separately from the files):");
                Console.WriteLine(code);
            }
            return 0;
        }

        private static int Verify(Args a)
        {
            var path = a.Positional(0);
            var trustBytes = File.ReadAllBytes(a.Required("--trust"));
            var root = StrictJson.Parse(trustBytes);
            var envName = root.Get("environment").AsString("environment");
            var env = envName == "production" ? TrustEnvironment.Production : envName == "development" ? TrustEnvironment.Development : TrustEnvironment.Test;
            var trust = TrustStore.Load(trustBytes, env);
            var kind = a.Optional("--kind") ?? (path.EndsWith(".pnprov", StringComparison.OrdinalIgnoreCase) ? "provisioning" : "license");
            var envelope = SignedEnvelope.Parse(File.ReadAllBytes(path));
            var check = envelope.Verify(trust, kind == "provisioning" ? ProductLicensing.ProvisioningDomain : ProductLicensing.LicenseDomain);
            Console.WriteLine("SIGNATURE\t" + check + "\tkid " + envelope.Kid);
            if (check != SignatureCheck.Valid) return 2;
            if (kind == "license") LicenseClaims.FromPayload(envelope.Payload);
            else OwnerProvisioning.Verify(File.ReadAllBytes(path), trust, out _);
            Console.WriteLine("PAYLOAD\tvalid " + kind);
            return 0;
        }

        private static int Inspect(string path)
        {
            var env = SignedEnvelope.Parse(File.ReadAllBytes(path));
            Console.WriteLine("kid\t" + env.Kid + "\t(UNVERIFIED: use 'verify' with trust anchors)");
            Console.WriteLine(Encoding.UTF8.GetString(env.Payload));
            return 0;
        }

        private static SigningKey OpenKey(Args a)
        {
            var file = File.ReadAllBytes(a.Required("--key"));
            var pass = Passphrase(a, confirm: false);
            try { return SigningKeyFile.Open(file, pass); }
            finally { Bytes.Wipe(pass); }
        }

        private static byte[] Passphrase(Args a, bool confirm)
        {
            var fromEnv = Environment.GetEnvironmentVariable("PARVNEGAR_SIGNING_PASSPHRASE");
            if (!string.IsNullOrEmpty(fromEnv)) return Encoding.UTF8.GetBytes(fromEnv);
            var file = a.Optional("--passphrase-file");
            if (file != null) return Encoding.UTF8.GetBytes(File.ReadAllText(file).TrimEnd('\r', '\n'));
            if (Console.IsInputRedirected) throw new UsageException("no passphrase source");
            var p1 = ReadHidden("Signing-key passphrase: ");
            if (confirm && ReadHidden("Repeat passphrase: ") != p1) throw new UsageException("passphrases differ");
            return Encoding.UTF8.GetBytes(p1);
        }

        private static string ReadHidden(string prompt)
        {
            Console.Error.Write(prompt);
            var sb = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                sb.Append(k.KeyChar);
            }
            Console.Error.WriteLine();
            return sb.ToString();
        }

        private static void AppendRecord(Args a, string record)
        {
            var dir = a.Optional("--records");
            if (dir == null) return;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "issuance-records.jsonl"), record + "\n", new UTF8Encoding(false));
        }
    }

    internal sealed class UsageException : Exception { public UsageException(string m) : base(m) { } }
    internal sealed class PolicyException : Exception { public PolicyException(string m) : base(m) { } }

    internal sealed class Args
    {
        private readonly Dictionary<string, string> _named = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _positional = new List<string>();

        public static Args Parse(string[] args)
        {
            var r = new Args();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--ceremony-approved") { r._flags.Add(args[i]); continue; }
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    if (i + 1 >= args.Length) throw new UsageException(args[i] + " needs a value");
                    r._named[args[i]] = args[++i];
                }
                else r._positional.Add(args[i]);
            }
            return r;
        }

        public string Required(string name) => _named.TryGetValue(name, out var v) ? v : throw new UsageException(name + " is required");
        public string Optional(string name) => _named.TryGetValue(name, out var v) ? v : null;
        public bool Flag(string name) => _flags.Contains(name);
        public string Positional(int i) => i < _positional.Count ? _positional[i] : throw new UsageException("file argument missing");
    }
}
