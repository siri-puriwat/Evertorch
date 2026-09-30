using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Evertorch.Client.Editor
{
/// <summary>
///     A small JSON reader for an art delivery's manifest (Content Pipeline §6): objects become
///     <see cref="Dictionary{TKey,TValue}" /> of string to value, arrays <see cref="List{T}" /> of value, numbers
///     <see cref="double" />, and strings, booleans, and null themselves. <c>JsonUtility</c> reads no dictionary, and the
///     manifest's anchors and markers are dictionaries.
/// </summary>
public static class ManifestJson
{
    public static object? Parse(string text)
    {
        var reader = new Reader(text);
        object? value = reader.ReadValue();
        reader.SkipWhitespace();
        if (!reader.IsAtEnd)
        {
            throw reader.Error("text after the value");
        }

        return value;
    }

    private sealed class Reader
    {
        private readonly string m_text;
        private int m_position;

        public Reader(string text)
        {
            m_text = text;
        }

        public bool IsAtEnd => m_position >= m_text.Length;

        public FormatException Error(string what)
        {
            return new FormatException($"Manifest JSON: {what} at character {m_position}.");
        }

        public void SkipWhitespace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(m_text[m_position]))
            {
                m_position++;
            }
        }

        public object? ReadValue()
        {
            SkipWhitespace();
            if (IsAtEnd)
            {
                throw Error("a missing value");
            }

            char next = m_text[m_position];
            switch (next)
            {
                case '{':
                    return ReadObject();
                case '[':
                    return ReadArray();
                case '"':
                    return ReadString();
                case 't':
                    ReadWord("true");
                    return true;
                case 'f':
                    ReadWord("false");
                    return false;
                case 'n':
                    ReadWord("null");
                    return null;
                default:
                    return ReadNumber();
            }
        }

        private Dictionary<string, object?> ReadObject()
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            m_position++;
            SkipWhitespace();
            if (Peek('}'))
            {
                m_position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (!Peek('"'))
                {
                    throw Error("a key that is not a string");
                }

                string key = ReadString();
                if (result.ContainsKey(key))
                {
                    throw Error($"the repeated key '{key}'");
                }

                SkipWhitespace();
                Expect(':');
                result[key] = ReadValue();
                SkipWhitespace();
                if (Peek(','))
                {
                    m_position++;
                    continue;
                }

                Expect('}');
                return result;
            }
        }

        private List<object?> ReadArray()
        {
            var result = new List<object?>();
            m_position++;
            SkipWhitespace();
            if (Peek(']'))
            {
                m_position++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue());
                SkipWhitespace();
                if (Peek(','))
                {
                    m_position++;
                    continue;
                }

                Expect(']');
                return result;
            }
        }

        private string ReadString()
        {
            m_position++;
            var builder = new StringBuilder();
            while (true)
            {
                if (IsAtEnd)
                {
                    throw Error("an unterminated string");
                }

                char current = m_text[m_position++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current < ' ')
                {
                    throw Error("a control character in a string");
                }

                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }

                if (IsAtEnd)
                {
                    throw Error("an unterminated escape");
                }

                char escaped = m_text[m_position++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (m_position + 4 > m_text.Length
                            || !int.TryParse(
                                m_text.Substring(m_position, 4),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture,
                                out int code))
                        {
                            throw Error("a bad \\u escape");
                        }

                        builder.Append((char)code);
                        m_position += 4;
                        break;
                    default:
                        throw Error($"the unknown escape '\\{escaped}'");
                }
            }
        }

        private double ReadNumber()
        {
            int start = m_position;
            while (!IsAtEnd && "+-0123456789.eE".IndexOf(m_text[m_position]) >= 0)
            {
                m_position++;
            }

            string token = m_text.Substring(start, m_position - start);
            if (token.Length == 0
                || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                m_position = start;
                throw Error("an unexpected character");
            }

            return value;
        }

        private void ReadWord(string word)
        {
            if (string.CompareOrdinal(m_text, m_position, word, 0, word.Length) != 0)
            {
                throw Error("an unexpected word");
            }

            m_position += word.Length;
        }

        private bool Peek(char expected)
        {
            return !IsAtEnd && m_text[m_position] == expected;
        }

        private void Expect(char expected)
        {
            if (!Peek(expected))
            {
                throw Error($"a missing '{expected}'");
            }

            m_position++;
        }
    }
}
}
