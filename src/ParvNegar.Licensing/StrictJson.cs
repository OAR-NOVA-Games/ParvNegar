using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ParvNegar.Licensing
{
    public sealed class LicenseFormatException : Exception
    {
        public LicenseFormatException(string reason) : base("Malformed licensing data: " + reason) { Reason = reason; }
        public string Reason { get; }
    }

    public enum JsonKind { Object, Array, String, Integer, True, False, Null }

    /// <summary>
    /// Minimal JSON value for licensing artifacts. Only the subset the formats need: objects, arrays,
    /// strings, integers in the IEEE-754 safe range (|n| ≤ 2^53−1), booleans and null.
    /// Fractions, exponents, duplicate keys, a BOM and trailing data are rejected.
    /// </summary>
    public sealed class JsonValue
    {
        public const long MaxSafeInteger = 9007199254740991L;

        private JsonValue(JsonKind kind) { Kind = kind; }

        public JsonKind Kind { get; }
        public string String { get; private set; }
        public long Integer { get; private set; }
        public IReadOnlyList<JsonValue> Items { get; private set; }
        /// <summary>Object members in document order; keys are unique.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members { get; private set; }

        public static JsonValue Str(string s) => new JsonValue(JsonKind.String) { String = s ?? throw new ArgumentNullException(nameof(s)) };
        public static JsonValue Int(long n)
        {
            if (n > MaxSafeInteger || n < -MaxSafeInteger) throw new ArgumentOutOfRangeException(nameof(n));
            return new JsonValue(JsonKind.Integer) { Integer = n };
        }
        public static JsonValue Bool(bool b) => new JsonValue(b ? JsonKind.True : JsonKind.False);
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);
        public static JsonValue Arr(IEnumerable<JsonValue> items) => new JsonValue(JsonKind.Array) { Items = items.ToList() };
        public static JsonValue Obj(IEnumerable<KeyValuePair<string, JsonValue>> members)
        {
            var list = members.ToList();
            if (list.Select(m => m.Key).Distinct(StringComparer.Ordinal).Count() != list.Count) throw new ArgumentException("Duplicate key.");
            return new JsonValue(JsonKind.Object) { Members = list };
        }
        public static JsonValue Obj(params (string Key, JsonValue Value)[] members) => Obj(members.Select(m => new KeyValuePair<string, JsonValue>(m.Key, m.Value)));

        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object) throw new LicenseFormatException("object expected");
            foreach (var m in Members) if (string.Equals(m.Key, key, StringComparison.Ordinal)) return m.Value;
            throw new LicenseFormatException("missing field " + key);
        }

        /// <summary>Requires exactly these keys (no missing, no unknown).</summary>
        public void RequireExactKeys(params string[] keys)
        {
            if (Kind != JsonKind.Object) throw new LicenseFormatException("object expected");
            var have = new HashSet<string>(Members.Select(m => m.Key), StringComparer.Ordinal);
            foreach (var k in keys) if (!have.Remove(k)) throw new LicenseFormatException("missing field " + k);
            if (have.Count > 0) throw new LicenseFormatException("unknown field " + have.OrderBy(x => x, StringComparer.Ordinal).First());
        }

        public string AsString(string what)
        {
            if (Kind != JsonKind.String) throw new LicenseFormatException(what + ": string expected");
            return String;
        }

        public long AsInteger(string what)
        {
            if (Kind != JsonKind.Integer) throw new LicenseFormatException(what + ": integer expected");
            return Integer;
        }

        public IReadOnlyList<JsonValue> AsArray(string what)
        {
            if (Kind != JsonKind.Array) throw new LicenseFormatException(what + ": array expected");
            return Items;
        }
    }

    /// <summary>
    /// Strict parser and canonical writer. Canonical form: UTF-8 without BOM, no insignificant whitespace,
    /// object keys sorted by ordinal UTF-16 code units, strings escaped only where JSON requires
    /// (<c>"</c>, <c>\</c>, U+0000–U+001F as \uXXXX lower-case hex except \b \f \n \r \t), integers in
    /// plain decimal. Signed payloads must equal the canonical serialization of their parsed value, so
    /// each payload has exactly one accepted byte representation.
    /// </summary>
    public static class StrictJson
    {
        public const int MaxBytes = 64 * 1024;
        public const int MaxDepth = 16;
        private static readonly UTF8Encoding Utf8Strict = new UTF8Encoding(false, true);

        public static JsonValue Parse(byte[] utf8)
        {
            if (utf8 == null) throw new LicenseFormatException("no data");
            if (utf8.Length == 0 || utf8.Length > MaxBytes) throw new LicenseFormatException("size");
            if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF) throw new LicenseFormatException("byte order mark");
            string text;
            try { text = Utf8Strict.GetString(utf8); }
            catch (DecoderFallbackException) { throw new LicenseFormatException("invalid UTF-8"); }
            var p = new Parser(text);
            p.Ws();
            var v = p.Value(0);
            p.Ws();
            if (p.Pos != text.Length) throw new LicenseFormatException("trailing data");
            return v;
        }

        public static byte[] Canonical(JsonValue v)
        {
            var sb = new StringBuilder();
            Write(sb, v);
            return Utf8Strict.GetBytes(sb.ToString());
        }

        /// <summary>Parses and requires the bytes to be in canonical form.</summary>
        public static JsonValue ParseCanonical(byte[] utf8)
        {
            var v = Parse(utf8);
            var c = Canonical(v);
            if (!ParvNegar.Security.Bytes.FixedTimeEquals(c, utf8)) throw new LicenseFormatException("payload is not in canonical form");
            return v;
        }

        private static void Write(StringBuilder sb, JsonValue v)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.True: sb.Append("true"); break;
                case JsonKind.False: sb.Append("false"); break;
                case JsonKind.Integer: sb.Append(v.Integer.ToString(CultureInfo.InvariantCulture)); break;
                case JsonKind.String: WriteString(sb, v.String); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < v.Items.Count; i++) { if (i > 0) sb.Append(','); Write(sb, v.Items[i]); }
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    sb.Append('{');
                    bool first = true;
                    foreach (var m in v.Members.OrderBy(m => m.Key, StringComparer.Ordinal))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, m.Key);
                        sb.Append(':');
                        Write(sb, m.Value);
                    }
                    sb.Append('}');
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; }

            public void Ws() { while (Pos < _s.Length && (_s[Pos] == ' ' || _s[Pos] == '\t' || _s[Pos] == '\n' || _s[Pos] == '\r')) Pos++; }

            private char Peek() => Pos < _s.Length ? _s[Pos] : '\0';

            private void Expect(string lit)
            {
                if (string.CompareOrdinal(_s, Pos, lit, 0, lit.Length) != 0) throw new LicenseFormatException("unexpected token at " + Pos);
                Pos += lit.Length;
            }

            public JsonValue Value(int depth)
            {
                if (depth > MaxDepth) throw new LicenseFormatException("nesting too deep");
                char c = Peek();
                switch (c)
                {
                    case '{': return Object(depth);
                    case '[': return Array(depth);
                    case '"': return JsonValue.Str(String());
                    case 't': Expect("true"); return JsonValue.Bool(true);
                    case 'f': Expect("false"); return JsonValue.Bool(false);
                    case 'n': Expect("null"); return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return Number();
                        throw new LicenseFormatException("unexpected character at " + Pos);
                }
            }

            private JsonValue Object(int depth)
            {
                Pos++;
                var members = new List<KeyValuePair<string, JsonValue>>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                Ws();
                if (Peek() == '}') { Pos++; return JsonValue.Obj(members); }
                while (true)
                {
                    Ws();
                    if (Peek() != '"') throw new LicenseFormatException("key expected at " + Pos);
                    var key = String();
                    if (!keys.Add(key)) throw new LicenseFormatException("duplicate key " + key);
                    Ws();
                    Expect(":");
                    Ws();
                    members.Add(new KeyValuePair<string, JsonValue>(key, Value(depth + 1)));
                    Ws();
                    if (Peek() == ',') { Pos++; continue; }
                    if (Peek() == '}') { Pos++; return JsonValue.Obj(members); }
                    throw new LicenseFormatException("',' or '}' expected at " + Pos);
                }
            }

            private JsonValue Array(int depth)
            {
                Pos++;
                var items = new List<JsonValue>();
                Ws();
                if (Peek() == ']') { Pos++; return JsonValue.Arr(items); }
                while (true)
                {
                    Ws();
                    items.Add(Value(depth + 1));
                    Ws();
                    if (Peek() == ',') { Pos++; continue; }
                    if (Peek() == ']') { Pos++; return JsonValue.Arr(items); }
                    throw new LicenseFormatException("',' or ']' expected at " + Pos);
                }
            }

            private string String()
            {
                Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= _s.Length) throw new LicenseFormatException("unterminated string");
                    char c = _s[Pos++];
                    if (c == '"') break;
                    if (c < 0x20) throw new LicenseFormatException("control character in string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (Pos >= _s.Length) throw new LicenseFormatException("bad escape");
                    char e = _s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > _s.Length) throw new LicenseFormatException("bad unicode escape");
                            if (!int.TryParse(_s.Substring(Pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp)) throw new LicenseFormatException("bad unicode escape");
                            sb.Append((char)cp);
                            Pos += 4;
                            break;
                        default: throw new LicenseFormatException("bad escape");
                    }
                }
                var str = sb.ToString();
                for (int i = 0; i < str.Length; i++)
                {
                    if (char.IsHighSurrogate(str[i])) { if (i + 1 >= str.Length || !char.IsLowSurrogate(str[i + 1])) throw new LicenseFormatException("unpaired surrogate"); i++; }
                    else if (char.IsLowSurrogate(str[i])) throw new LicenseFormatException("unpaired surrogate");
                }
                return str;
            }

            private JsonValue Number()
            {
                int start = Pos;
                if (Peek() == '-') Pos++;
                if (Peek() == '0') { Pos++; if (Peek() >= '0' && Peek() <= '9') throw new LicenseFormatException("leading zero"); }
                else
                {
                    if (!(Peek() >= '1' && Peek() <= '9')) throw new LicenseFormatException("digit expected");
                    while (Peek() >= '0' && Peek() <= '9') Pos++;
                }
                if (Peek() == '.' || Peek() == 'e' || Peek() == 'E') throw new LicenseFormatException("only integers are allowed");
                var text = _s.Substring(start, Pos - start);
                if (text == "-0") throw new LicenseFormatException("negative zero");
                if (text.Length > 17 || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n) || n > JsonValue.MaxSafeInteger || n < -JsonValue.MaxSafeInteger)
                    throw new LicenseFormatException("integer out of safe range");
                return JsonValue.Int(n);
            }
        }
    }
}
