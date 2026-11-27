using System.Text;

namespace ParvNegar.Domain
{
    /// <summary>
    /// Search-key normalization only (ADR-C12). Stored display text is never changed.
    /// Arabic Yeh/Kaf map to Persian, Persian/Arabic digits map to ASCII, ZWNJ and tatweel are removed.
    /// </summary>
    public static class PersianText
    {
        public static string SearchKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            bool lastSpace = false;
            foreach (char c in text)
            {
                char o = c;
                switch (c)
                {
                    case 'ي': o = 'ی'; break; // Arabic Yeh -> Persian Yeh
                    case 'ى': o = 'ی'; break; // Alef Maksura -> Persian Yeh
                    case 'ك': o = 'ک'; break; // Arabic Kaf -> Persian Keheh
                    case '‌': case 'ـ': continue; // ZWNJ, tatweel
                }
                int d = NationalId.DigitValue(o);
                if (d >= 0) o = (char)('0' + d);
                if (char.IsWhiteSpace(o)) { if (!lastSpace && sb.Length > 0) sb.Append(' '); lastSpace = true; continue; }
                lastSpace = false;
                sb.Append(o);
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>ASCII digits to Persian digits for display (e.g., academic-year labels).</summary>
        public static string ToPersianDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] >= '0' && chars[i] <= '9') chars[i] = (char)('۰' + (chars[i] - '0'));
            return new string(chars);
        }
    }
}
