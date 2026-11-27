using System;
using System.Text;

namespace ParvNegar.Domain
{
    /// <summary>
    /// National ID kept as a string so leading zeros survive (SRS STU-03). Persian and Arabic-Indic
    /// digits are normalized to ASCII. Only the structure (exactly 10 digits) is enforced here.
    /// Check-digit validation, required-ness and the absent-ID exception are pending owner
    /// decision O-07 and are not enforced.
    /// </summary>
    public sealed class NationalId : IEquatable<NationalId>
    {
        public const int Length = 10;

        private NationalId(string digits) { Digits = digits; }

        public string Digits { get; }

        public static NationalId Parse(string input)
        {
            if (!TryParse(input, out var id)) throw new DomainException("national_id.invalid_structure", "National ID must contain exactly 10 digits.");
            return id;
        }

        public static bool TryParse(string input, out NationalId id)
        {
            id = null;
            if (input == null) return false;
            var sb = new StringBuilder(Length);
            foreach (char c in input)
            {
                if (c == ' ' || c == '-' || c == '‌') continue;
                int d = DigitValue(c);
                if (d < 0) return false;
                sb.Append((char)('0' + d));
            }
            if (sb.Length != Length) return false;
            id = new NationalId(sb.ToString());
            return true;
        }

        /// <summary>Maps ASCII, Persian (U+06F0..U+06F9) and Arabic-Indic (U+0660..U+0669) digits; -1 otherwise.</summary>
        public static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= '۰' && c <= '۹') return c - '۰';
            if (c >= '٠' && c <= '٩') return c - '٠';
            return -1;
        }

        public bool Equals(NationalId other) => other != null && string.Equals(Digits, other.Digits, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as NationalId);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Digits);

        /// <summary>Never print the value in logs; this returns a masked form.</summary>
        public override string ToString() => "NationalId(******" + Digits.Substring(Length - 2) + ")";
    }
}
