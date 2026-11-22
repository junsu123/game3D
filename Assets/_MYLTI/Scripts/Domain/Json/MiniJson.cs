// MYLTI Challenge Studio — pure C# JSON reader/writer (no UnityEngine dependency).
// Parser returns: Dictionary<string,object>, List<object>, string, double, bool, null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mylti.Domain
{
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public static class MiniJson
    {
        // ------------------------------------------------------------------ PARSE
        public static object Parse(string json)
        {
            if (json == null) throw new JsonException("JSON is null");
            var p = new Parser(json);
            p.SkipWs();
            object value = p.ReadValue(0);
            p.SkipWs();
            if (!p.End) throw new JsonException("Unexpected trailing characters at " + p.Pos);
            return value;
        }

        public static bool TryParse(string json, out object value, out string error)
        {
            try { value = Parse(json); error = null; return true; }
            catch (Exception e) { value = null; error = e.Message; return false; }
        }

        private sealed class Parser
        {
            private readonly string s;
            public int Pos;
            public Parser(string text) { s = text; }
            public bool End => Pos >= s.Length;

            public void SkipWs()
            {
                while (Pos < s.Length && char.IsWhiteSpace(s[Pos])) Pos++;
            }

            public object ReadValue(int depth)
            {
                if (depth > 128) throw new JsonException("JSON nesting too deep");
                SkipWs();
                if (End) throw new JsonException("Unexpected end of JSON");
                char c = s[Pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw new JsonException("Unexpected character '" + c + "' at " + Pos);
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(s, Pos, word, 0, word.Length) != 0)
                    throw new JsonException("Expected '" + word + "' at " + Pos);
                Pos += word.Length;
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                Pos++; // {
                SkipWs();
                if (!End && s[Pos] == '}') { Pos++; return dict; }
                while (true)
                {
                    SkipWs();
                    if (End || s[Pos] != '"') throw new JsonException("Expected property name at " + Pos);
                    string key = ReadString();
                    SkipWs();
                    if (End || s[Pos] != ':') throw new JsonException("Expected ':' at " + Pos);
                    Pos++;
                    dict[key] = ReadValue(depth + 1);
                    SkipWs();
                    if (End) throw new JsonException("Unterminated object");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == '}') { Pos++; return dict; }
                    throw new JsonException("Expected ',' or '}' at " + Pos);
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                Pos++; // [
                SkipWs();
                if (!End && s[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    list.Add(ReadValue(depth + 1));
                    SkipWs();
                    if (End) throw new JsonException("Unterminated array");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == ']') { Pos++; return list; }
                    throw new JsonException("Expected ',' or ']' at " + Pos);
                }
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                Pos++; // opening quote
                while (true)
                {
                    if (End) throw new JsonException("Unterminated string");
                    char c = s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw new JsonException("Bad escape");
                    char e = s[Pos++];
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
                            if (Pos + 4 > s.Length) throw new JsonException("Bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            Pos += 4;
                            break;
                        default: throw new JsonException("Bad escape \\" + e);
                    }
                }
            }

            private double ReadNumber()
            {
                int start = Pos;
                while (Pos < s.Length && "+-0123456789.eE".IndexOf(s[Pos]) >= 0) Pos++;
                string num = s.Substring(start, Pos - start);
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new JsonException("Bad number '" + num + "'");
                return d;
            }
        }

        // ------------------------------------------------------------ ACCESSORS
        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;

        public static object Get(Dictionary<string, object> d, string key)
        {
            if (d == null || key == null) return null;
            return d.TryGetValue(key, out object v) ? v : null;
        }

        public static string GetString(Dictionary<string, object> d, string key, string fallback = null)
        {
            object v = Get(d, key);
            return v is string str ? str : fallback;
        }

        public static double GetDouble(Dictionary<string, object> d, string key, double fallback = 0)
        {
            object v = Get(d, key);
            if (v is double dd && !double.IsNaN(dd) && !double.IsInfinity(dd)) return dd;
            if (v is string str && double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
            return fallback;
        }

        public static double? GetNullableDouble(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v is double dd && !double.IsNaN(dd) && !double.IsInfinity(dd)) return dd;
            return null;
        }

        public static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            double v = GetDouble(d, key, double.NaN);
            if (double.IsNaN(v)) return fallback;
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)Math.Round(v);
        }

        public static bool GetBool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            object v = Get(d, key);
            return v is bool b ? b : fallback;
        }

        // --------------------------------------------------------------- WRITE
        public static string Escape(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Num(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Serialize plain object graphs (dictionaries, lists, primitives, IJsonWritable).</summary>
        public static string Serialize(object value, bool pretty = true)
        {
            var w = new JsonWriter(pretty);
            w.Value(value);
            return w.ToString();
        }
    }

    public interface IJsonWritable
    {
        void WriteJson(JsonWriter w);
    }

    /// <summary>Small streaming JSON writer with optional pretty printing.</summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly bool pretty;
        private readonly Stack<bool> firstInScope = new Stack<bool>();
        private bool pendingKey;

        public JsonWriter(bool pretty = true) { this.pretty = pretty; }

        public override string ToString() => sb.ToString();

        private void NewLine()
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', firstInScope.Count * 2);
        }

        private void BeforeValue()
        {
            if (pendingKey) { pendingKey = false; return; }
            if (firstInScope.Count == 0) return;
            bool first = firstInScope.Pop();
            if (!first) sb.Append(',');
            firstInScope.Push(false);
            NewLine();
        }

        public JsonWriter BeginObject() { BeforeValue(); sb.Append('{'); firstInScope.Push(true); return this; }

        public JsonWriter EndObject()
        {
            bool empty = firstInScope.Pop();
            if (!empty) NewLine();
            sb.Append('}');
            return this;
        }

        public JsonWriter BeginArray() { BeforeValue(); sb.Append('['); firstInScope.Push(true); return this; }

        public JsonWriter EndArray()
        {
            bool empty = firstInScope.Pop();
            if (!empty) NewLine();
            sb.Append(']');
            return this;
        }

        public JsonWriter Key(string key)
        {
            BeforeValue();
            sb.Append(MiniJson.Escape(key)).Append(pretty ? ": " : ":");
            pendingKey = true;
            return this;
        }

        public JsonWriter Raw(string token) { BeforeValue(); sb.Append(token); return this; }
        public JsonWriter Str(string s) => Raw(MiniJson.Escape(s));
        public JsonWriter Num(double d) => Raw(MiniJson.Num(d));
        public JsonWriter Num(double? d) => Raw(d.HasValue ? MiniJson.Num(d.Value) : "null");
        public JsonWriter Bool(bool b) => Raw(b ? "true" : "false");
        public JsonWriter Null() => Raw("null");

        public JsonWriter Prop(string key, string v) { Key(key); return Str(v); }
        public JsonWriter Prop(string key, double v) { Key(key); return Num(v); }
        public JsonWriter Prop(string key, double? v) { Key(key); return Num(v); }
        public JsonWriter Prop(string key, int v) { Key(key); return Num(v); }
        public JsonWriter Prop(string key, bool v) { Key(key); return Bool(v); }

        public JsonWriter PropStrings(string key, IEnumerable<string> values)
        {
            Key(key).BeginArray();
            if (values != null) foreach (var v in values) Str(v);
            return EndArray();
        }

        public JsonWriter Value(object value)
        {
            switch (value)
            {
                case null: return Null();
                case string s: return Str(s);
                case bool b: return Bool(b);
                case int i: return Num(i);
                case long l: return Num(l);
                case float f: return Num(f);
                case double d: return Num(d);
                case IJsonWritable jw: jw.WriteJson(this); return this;
                case IDictionary<string, object> dict:
                    BeginObject();
                    foreach (var kv in dict) { Key(kv.Key); Value(kv.Value); }
                    return EndObject();
                case IDictionary<string, double> dd:
                    BeginObject();
                    foreach (var kv in dd) { Key(kv.Key); Num(kv.Value); }
                    return EndObject();
                case System.Collections.IEnumerable seq:
                    BeginArray();
                    foreach (var item in seq) Value(item);
                    return EndArray();
                default:
                    return Str(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }
    }
}
