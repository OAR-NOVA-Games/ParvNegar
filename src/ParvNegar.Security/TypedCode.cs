using System;
using System.Text;

namespace ParvNegar.Security
{
    /// <summary>
    /// Human-typable encoding for the Owner Recovery Key (32 bytes) and the provisioning bootstrap code
    /// (20 bytes): data ‖ 2-byte check value, Crockford base32 (0-9 A-Z without I L O U), groups of 5
    /// separated by '-'. Decoding ignores case, spaces and hyphens, maps O→0 and I/L→1, and accepts
    /// Persian and Arabic-Indic digits. The check value (first 2 bytes of SHA-256 over a purpose label and
    /// the data) detects a typing error with probability 1 − 1/65,536. It is not a secret and adds no
    /// security; wrong but well-formed codes are rejected by the cryptographic check that follows.
    /// </summary>
    public static class TypedCode
    {
        public const string RecoveryKeyPurpose = "ParvNegar/v1/code/recovery-key";
        public const string BootstrapPurpose = "ParvNegar/v1/code/bootstrap";
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string Encode(byte[] data, string purpose)
        {
            if (data == null || data.Length == 0 || data.Length > 64) throw new ArgumentException("1–64 bytes required.", nameof(data));
            var all = new byte[data.Length + 2];
            Buffer.BlockCopy(data, 0, all, 0, data.Length);
            var check = Check(data, purpose);
            all[data.Length] = check[0]; all[data.Length + 1] = check[1];
            var chars = Base32(all);
            Bytes.Wipe(all);
            var sb = new StringBuilder();
            for (int i = 0; i < chars.Length; i++)
            {
                if (i > 0 && i % 5 == 0) sb.Append('-');
                sb.Append(chars[i]);
            }
            return sb.ToString();
        }

        /// <summary>Returns the data bytes, or null when the text is malformed or the check value does not match.</summary>
        public static byte[] Decode(string text, int expectedLength, string purpose)
        {
            if (text == null) return null;
            int totalBits = (expectedLength + 2) * 8;
            int charCount = (totalBits + 4) / 5;
            var values = new int[charCount];
            int n = 0;
            foreach (var raw in text)
            {
                char c = Map(raw);
                if (c == ' ' || c == '-' || c == '‌' || c == '\t') continue;
                int v = Alphabet.IndexOf(c);
                if (v < 0 || n >= charCount) return null;
                values[n++] = v;
            }
            if (n != charCount) return null;
            var all = new byte[expectedLength + 2];
            int bitPos = 0;
            for (int i = 0; i < charCount; i++)
                for (int b = 4; b >= 0; b--, bitPos++)
                {
                    int bit = (values[i] >> b) & 1;
                    if (bitPos < totalBits) { if (bit == 1) all[bitPos / 8] |= (byte)(0x80 >> (bitPos % 8)); }
                    else if (bit != 0) return null; // padding bits must be zero (one text per value)
                }
            var data = new byte[expectedLength];
            Buffer.BlockCopy(all, 0, data, 0, expectedLength);
            var check = Check(data, purpose);
            bool ok = all[expectedLength] == check[0] && all[expectedLength + 1] == check[1];
            Bytes.Wipe(all);
            if (!ok) { Bytes.Wipe(data); return null; }
            return data;
        }

        private static char Map(char c)
        {
            if (c >= '۰' && c <= '۹') return (char)('0' + (c - '۰')); // Persian digits
            if (c >= '٠' && c <= '٩') return (char)('0' + (c - '٠')); // Arabic-Indic digits
            c = char.ToUpperInvariant(c);
            if (c == 'O') return '0';
            if (c == 'I' || c == 'L') return '1';
            return c;
        }

        private static byte[] Check(byte[] data, string purpose)
        {
            var label = Encoding.UTF8.GetBytes(purpose + "\n");
            var m = new byte[label.Length + data.Length];
            Buffer.BlockCopy(label, 0, m, 0, label.Length);
            Buffer.BlockCopy(data, 0, m, label.Length, data.Length);
            var h = Bytes.Sha256(m);
            Bytes.Wipe(m);
            return new[] { h[0], h[1] };
        }

        private static char[] Base32(byte[] data)
        {
            int totalBits = data.Length * 8;
            var chars = new char[(totalBits + 4) / 5];
            for (int i = 0; i < chars.Length; i++)
            {
                int v = 0;
                for (int b = 0; b < 5; b++)
                {
                    int bitPos = i * 5 + b;
                    int bit = bitPos < totalBits ? (data[bitPos / 8] >> (7 - bitPos % 8)) & 1 : 0;
                    v = (v << 1) | bit;
                }
                chars[i] = Alphabet[v];
            }
            return chars;
        }
    }
}
