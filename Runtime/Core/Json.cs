using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Helpwing
{
    /// <summary>
    /// A small JSON reader and writer, so the package depends on nothing.
    /// Objects parse to <c>Dictionary&lt;string, object&gt;</c>, arrays to <c>List&lt;object&gt;</c>, numbers to double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) return null;
            var reader = new Reader(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw new FormatException("Unexpected characters after the JSON value.");
            return value;
        }

        /// <summary>Parse, returning null instead of throwing on anything malformed.</summary>
        public static object TryParse(string text)
        {
            try
            {
                return Parse(text);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string Serialize(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        // -- reading fields tolerantly: a missing or mistyped field is its default ----------

        public static string Str(IDictionary<string, object> data, string key, string fallback = "")
        {
            if (data == null || !data.TryGetValue(key, out var value) || value == null) return fallback;
            return value as string ?? (value is double || value is bool ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback);
        }

        public static bool Bool(IDictionary<string, object> data, string key, bool fallback = false)
        {
            if (data == null || !data.TryGetValue(key, out var value)) return fallback;
            return value is bool flag ? flag : fallback;
        }

        public static long Long(IDictionary<string, object> data, string key, long fallback = 0)
        {
            if (data == null || !data.TryGetValue(key, out var value)) return fallback;
            return value is double number ? (long)number : fallback;
        }

        public static IDictionary<string, object> Obj(IDictionary<string, object> data, string key)
        {
            if (data == null || !data.TryGetValue(key, out var value)) return null;
            return value as IDictionary<string, object>;
        }

        public static IList<object> List(IDictionary<string, object> data, string key)
        {
            if (data == null || !data.TryGetValue(key, out var value)) return null;
            return value as IList<object>;
        }

        public static bool Has(IDictionary<string, object> data, string key)
        {
            return data != null && data.ContainsKey(key);
        }

        // -- writing -----------------------------------------------------------------------

        private static void Write(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    return;
                case string text:
                    WriteString(builder, text);
                    return;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    return;
                case double number:
                    builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float number:
                    builder.Append(((double)number).ToString("R", CultureInfo.InvariantCulture));
                    return;
                case int _:
                case long _:
                case short _:
                case uint _:
                case ulong _:
                case decimal _:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case IDictionary<string, object> map:
                    WriteObject(builder, map);
                    return;
                case IDictionary<string, string> strings:
                    var copy = new Dictionary<string, object>();
                    foreach (var pair in strings) copy[pair.Key] = pair.Value;
                    WriteObject(builder, copy);
                    return;
                case IEnumerable items:
                    builder.Append('[');
                    var first = true;
                    foreach (var item in items)
                    {
                        if (!first) builder.Append(',');
                        first = false;
                        Write(builder, item);
                    }
                    builder.Append(']');
                    return;
                default:
                    WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void WriteObject(StringBuilder builder, IDictionary<string, object> map)
        {
            builder.Append('{');
            var first = true;
            foreach (var pair in map)
            {
                if (!first) builder.Append(',');
                first = false;
                WriteString(builder, pair.Key);
                builder.Append(':');
                Write(builder, pair.Value);
            }
            builder.Append('}');
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class Reader
        {
            private readonly string text;
            private int index;

            public Reader(string text)
            {
                this.text = text;
            }

            public bool AtEnd => index >= text.Length;

            public void SkipWhitespace()
            {
                while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            }

            public object ReadValue()
            {
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");
                var c = text[index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                var map = new Dictionary<string, object>();
                index++;
                SkipWhitespace();
                if (Peek() == '}')
                {
                    index++;
                    return map;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw new FormatException("Expected a property name.");
                    var key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':') throw new FormatException("Expected ':'.");
                    index++;
                    SkipWhitespace();
                    map[key] = ReadValue();
                    SkipWhitespace();
                    var next = Peek();
                    index++;
                    if (next == '}') return map;
                    if (next != ',') throw new FormatException("Expected ',' or '}'.");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                index++;
                SkipWhitespace();
                if (Peek() == ']')
                {
                    index++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    var next = Peek();
                    index++;
                    if (next == ']') return list;
                    if (next != ',') throw new FormatException("Expected ',' or ']'.");
                }
            }

            private string ReadString()
            {
                index++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new FormatException("Unterminated string.");
                    var c = text[index++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd) throw new FormatException("Unterminated escape.");
                    var escaped = text[index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length) throw new FormatException("Bad unicode escape.");
                            builder.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default: throw new FormatException("Bad escape.");
                    }
                }
            }

            private double ReadNumber()
            {
                var start = index;
                while (index < text.Length && "+-0123456789.eE".IndexOf(text[index]) >= 0) index++;
                if (start == index) throw new FormatException("Unexpected character in JSON.");
                return double.Parse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(text, index, word, 0, word.Length) != 0) throw new FormatException("Unexpected token.");
                index += word.Length;
            }

            private char Peek()
            {
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");
                return text[index];
            }
        }
    }
}
