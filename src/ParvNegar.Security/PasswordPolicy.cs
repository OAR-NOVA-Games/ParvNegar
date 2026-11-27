using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ParvNegar.Security
{
    public enum PasswordProblem
    {
        None,
        TooShort,
        TooLong,
        InvalidCharacters,
        Common,
        ContainsUsername,
        Repetitive,
    }

    /// <summary>
    /// Owner-approved password policy (PASSWORD-POLICY.md):
    /// <list type="bullet">
    /// <item>The password string is normalized to Unicode NFC and nothing else: no case folding, no
    ///   Persian/Arabic letter unification, no trimming. The student-name search normalization is never used.</item>
    /// <item>Length is counted in Unicode scalar values (code points) after NFC: 15 to 128.</item>
    /// <item>Unpaired surrogates and control characters (category Cc, including tab and newline) are rejected;
    ///   spaces and every other printable character, in any script, are allowed. Paste is allowed.</item>
    /// <item>There are no composition rules. The password is rejected only when it is a known common password,
    ///   is built from the username, or is trivially repetitive.</item>
    /// <item>Bytes for the KDF: UTF-8 of the NFC string. Nothing is truncated: longer input is rejected, never shortened.</item>
    /// </list>
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinLength = 15;
        public const int MaxLength = 128;

        private static readonly Lazy<HashSet<string>> Blocklist = new Lazy<HashSet<string>>(LoadBlocklist);

        public static int BlocklistCount => Blocklist.Value.Count;

        /// <summary>Normalizes and validates; on success returns the UTF-8 bytes to use (caller wipes them).</summary>
        public static PasswordProblem Check(string password, string canonicalUsername, out byte[] utf8)
        {
            utf8 = null;
            if (password == null) return PasswordProblem.TooShort;
            string nfc;
            try { nfc = password.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return PasswordProblem.InvalidCharacters; } // invalid UTF-16 (unpaired surrogate)
            int count = 0;
            for (int i = 0; i < nfc.Length; i++)
            {
                char c = nfc[i];
                if (char.IsHighSurrogate(c)) { if (i + 1 >= nfc.Length || !char.IsLowSurrogate(nfc[i + 1])) return PasswordProblem.InvalidCharacters; i++; }
                else if (char.IsLowSurrogate(c)) return PasswordProblem.InvalidCharacters;
                else if (char.GetUnicodeCategory(c) == UnicodeCategory.Control) return PasswordProblem.InvalidCharacters;
                count++;
            }
            if (count < MinLength) return PasswordProblem.TooShort;
            if (count > MaxLength) return PasswordProblem.TooLong;

            var lower = nfc.ToLowerInvariant();
            if (Blocklist.Value.Contains(lower) || Blocklist.Value.Contains(StripSeparators(lower))) return PasswordProblem.Common;
            if (!string.IsNullOrEmpty(canonicalUsername) && StripSeparators(lower).Replace(StripSeparators(canonicalUsername), "").Length < 8) return PasswordProblem.ContainsUsername;
            if (IsRepetitive(lower)) return PasswordProblem.Repetitive;
            utf8 = Encoding.UTF8.GetBytes(nfc);
            return PasswordProblem.None;
        }

        /// <summary>UTF-8 of the NFC form, for verifying a login attempt (no policy checks: old passwords keep working).</summary>
        public static byte[] LoginBytes(string password)
        {
            if (string.IsNullOrEmpty(password)) return null;
            try { return Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)); }
            catch (ArgumentException) { return null; }
        }

        private static string StripSeparators(string s) => new string(s.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_' && c != '.').ToArray());

        /// <summary>
        /// Rejects passwords with at most 3 distinct characters, and passwords that are a short unit repeated
        /// (e.g. "abcabcabcabcabc"). Also rejects passwords that are only a run of consecutive characters
        /// ("123456789012345", "abcdefghijklmnop", "qwertyuiopasdfgh").
        /// </summary>
        private static bool IsRepetitive(string s)
        {
            var cps = new List<int>();
            for (int i = 0; i < s.Length; i++) { cps.Add(char.ConvertToUtf32(s, i)); if (char.IsHighSurrogate(s[i])) i++; }
            if (cps.Distinct().Count() <= 3) return true;
            for (int unit = 1; unit <= cps.Count / 3; unit++)
            {
                bool rep = true;
                for (int i = unit; i < cps.Count && rep; i++) rep = cps[i] == cps[i % unit];
                if (rep) return true;
            }
            int seq = 1;
            for (int i = 1; i < cps.Count; i++)
            {
                int d = cps[i] - cps[i - 1];
                if (d == 1 || d == -1 || (cps[i - 1] == '9' && cps[i] == '0') || (cps[i - 1] == '0' && cps[i] == '9')) seq++;
            }
            if (seq == cps.Count) return true;
            const string rows = "qwertyuiopasdfghjklzxcvbnm1234567890";
            return rows.Contains(s) || new string(rows.Reverse().ToArray()).Contains(s);
        }

        private static HashSet<string> LoadBlocklist()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var asm = typeof(PasswordPolicy).GetTypeInfo().Assembly;
            using (var st = asm.GetManifestResourceStream("ParvNegar.Security.common-passwords.txt"))
            {
                if (st == null) throw new InvalidOperationException("Embedded password blocklist missing.");
                using (var r = new StreamReader(st, new UTF8Encoding(false, true)))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                        var lower = line.Normalize(NormalizationForm.FormC).ToLowerInvariant();
                        set.Add(lower);
                        set.Add(StripSeparators(lower));
                    }
                }
            }
            return set;
        }
    }
}
