using System.Globalization;
using System.Text;

namespace Origami.Core;

/// <summary>极简 JSON 解析器（只需覆盖本项目自己写出的 JSON 结构）。</summary>
public static class JsonLite
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    public sealed class Value
    {
        public JsonKind Kind { get; init; }
        public string Str { get; init; } = "";
        public double Num { get; init; }
        public bool Bool { get; init; }
        public List<Value>? Items { get; init; }
        public Dictionary<string, Value>? Map { get; init; }

        public string AsString() => Str;
        public double AsDouble() => Num;
        public bool TryGetValue(string k, out Value v)
        {
            v = default!;
            return Map is not null && Map.TryGetValue(k, out var got) && (v = got) is not null;
        }
    }

    public static Dictionary<string, Value> Parse(string text)
    {
        int i = 0;
        var v = ParseValue(text, ref i);
        return v.Map ?? throw new FormatException("JSON 根不是对象");
    }

    /// <summary>JSON 字符串转义（不转义非 ASCII，保持 UTF-8 原文）。</summary>
    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
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
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static Value ParseValue(string s, ref int i)
    {
        Ws(s, ref i);
        if (i >= s.Length) throw new FormatException("JSON 意外结束");
        char c = s[i];
        if (c == '{') return new Value { Kind = JsonKind.Object, Map = ParseObject(s, ref i) };
        if (c == '[') return new Value { Kind = JsonKind.Array, Items = ParseArray(s, ref i) };
        if (c == '"') return new Value { Kind = JsonKind.String, Str = ParseString(s, ref i) };
        if (s.AsSpan(i).StartsWith("true")) { i += 4; return new Value { Kind = JsonKind.Bool, Bool = true }; }
        if (s.AsSpan(i).StartsWith("false")) { i += 5; return new Value { Kind = JsonKind.Bool, Bool = false }; }
        if (s.AsSpan(i).StartsWith("null")) { i += 4; return new Value { Kind = JsonKind.Null }; }
        return new Value { Kind = JsonKind.Number, Num = ParseNumber(s, ref i) };
    }

    static Dictionary<string, Value> ParseObject(string s, ref int i)
    {
        var d = new Dictionary<string, Value>();
        i++;                                    // {
        Ws(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (i < s.Length)
        {
            Ws(s, ref i);
            string key = ParseString(s, ref i);
            Ws(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            d[key] = ParseValue(s, ref i);
            Ws(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; break; }
            break;
        }
        return d;
    }

    static List<Value> ParseArray(string s, ref int i)
    {
        var l = new List<Value>();
        i++;                                    // [
        Ws(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return l; }
        while (i < s.Length)
        {
            l.Add(ParseValue(s, ref i));
            Ws(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
            break;
        }
        return l;
    }

    static string ParseString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++;                                    // opening quote
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                if (s[i] == 'u')
                {
                    // 必须先校验剩余长度：截断的 \u 序列要报 FormatException（与其他畸形
                    // 输入一致），而不是让 Substring 抛 ArgumentOutOfRangeException 漏过
                    // 调用方的 catch (FormatException)。
                    if (i + 5 > s.Length)
                        throw new FormatException($"位置 {i} 处\\u 转义被截断");
                    sb.Append((char)ushort.Parse(s.AsSpan(i + 1, 4),
                                                  NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                }
                else
                {
                    sb.Append(s[i] switch
                    {
                        'n' => '\n', 't' => '\t', 'r' => '\r',
                        'b' => '\b', 'f' => '\f',
                        _ => s[i],
                    });
                }
            }
            else sb.Append(s[i]);
            i++;
        }
        if (i >= s.Length) throw new FormatException("字符串未闭合");
        i++;                                    // closing quote
        return sb.ToString();
    }

    static double ParseNumber(string s, ref int i)
    {
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' ||
                                s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) i++;
        return double.Parse(s.AsSpan(start, i - start), NumberStyles.Float,
                            CultureInfo.InvariantCulture);
    }
}

