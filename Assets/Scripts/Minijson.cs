using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DecisionTreeViz
{
    /// <summary>
    /// Tiny JSON reader. Returns Dictionary&lt;string, object&gt;, List&lt;object&gt;, double, string, bool or null.
    /// Used instead of JsonUtility because JsonUtility cannot deserialize a recursive tree
    /// (depth limit + no null child support).
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON text is empty.");
            return new Reader(json).ReadRoot();
        }

        private sealed class Reader
        {
            private readonly string s;
            private int i;

            public Reader(string text)
            {
                s = text;
                i = 0;
            }

            public object ReadRoot()
            {
                object value = ReadValue();
                SkipWhitespace();
                if (i != s.Length) throw Fail("Unexpected characters after JSON value");
                return value;
            }

            private object ReadValue()
            {
                SkipWhitespace();
                if (i >= s.Length) throw Fail("Unexpected end of JSON");

                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                return ReadNumber();
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>();
                i++; // '{'
                SkipWhitespace();
                if (Peek() == '}') { i++; return result; }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Fail("Expected string key");
                    string key = ReadString();

                    SkipWhitespace();
                    if (Peek() != ':') throw Fail("Expected ':'");
                    i++;

                    result[key] = ReadValue();

                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',') { i++; continue; }
                    if (c == '}') { i++; return result; }
                    throw Fail("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                i++; // '['
                SkipWhitespace();
                if (Peek() == ']') { i++; return result; }

                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',') { i++; continue; }
                    if (c == ']') { i++; return result; }
                    throw Fail("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    if (i >= s.Length) break;
                    char e = s[i++];
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
                            if (i + 4 > s.Length) throw Fail("Bad \\u escape");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw Fail("Bad escape character");
                    }
                }
                throw Fail("Unterminated string");
            }

            private double ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (start == i) throw Fail("Unexpected character '" + s[i] + "'");

                string token = s.Substring(start, i - start);
                double value;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw Fail("Invalid number '" + token + "'");
                return value;
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                    throw Fail("Expected '" + word + "'");
                i += word.Length;
            }

            private void SkipWhitespace()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            private char Peek()
            {
                return i < s.Length ? s[i] : '\0';
            }

            private Exception Fail(string message)
            {
                return new FormatException("JSON parse error at position " + i + ": " + message);
            }
        }
    }
}