using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>A JSON object that keeps the order of its properties, as EXPLAIN lists the steps in order.</summary>
internal sealed class PlanJsonObject : List<KeyValuePair<string, object?>>
{
    /// <summary>Returns the value of <paramref name="name"/>, or null.</summary>
    public object? this[string name]
    {
        get
        {
            foreach (var pair in this)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }

            return null;
        }
    }
}

/// <summary>
/// Small JSON reader for the output of <c>EXPLAIN FORMAT=JSON</c> (US-1301). Objects become <see cref="PlanJsonObject"/>,
/// arrays <see cref="List{T}"/>, numbers stay text (MySQL writes costs as text as well), true/false become bool.
/// Core has no JSON library, and adding one to the VSIX brings binding redirects; this reader is enough for EXPLAIN.
/// </summary>
internal sealed class PlanJson
{
    private readonly string _text;
    private int _position;

    private PlanJson(string text) => _text = text;

    /// <summary>Parses <paramref name="text"/>; throws <see cref="FormatException"/> on invalid JSON.</summary>
    public static object? Parse(string text)
    {
        var reader = new PlanJson(text ?? throw new ArgumentNullException(nameof(text)));
        var value = reader.ReadValue();
        reader.SkipWhiteSpace();
        if (reader._position != text.Length)
        {
            throw reader.Error("end of text");
        }

        return value;
    }

    private object? ReadValue()
    {
        SkipWhiteSpace();
        if (_position >= _text.Length)
        {
            throw Error("a value");
        }

        switch (_text[_position])
        {
            case '{':
                return ReadObject();
            case '[':
                return ReadArray();
            case '"':
                return ReadString();
            case 't':
                return ReadWord("true", true);
            case 'f':
                return ReadWord("false", false);
            case 'n':
                return ReadWord("null", null);
            default:
                return ReadNumber();
        }
    }

    private PlanJsonObject ReadObject()
    {
        var result = new PlanJsonObject();
        _position++;
        SkipWhiteSpace();
        if (TryRead('}'))
        {
            return result;
        }

        do
        {
            SkipWhiteSpace();
            if (_position >= _text.Length || _text[_position] != '"')
            {
                throw Error("a property name");
            }

            var name = ReadString();
            SkipWhiteSpace();
            Expect(':');
            result.Add(new KeyValuePair<string, object?>(name, ReadValue()));
            SkipWhiteSpace();
        }
        while (TryRead(','));

        Expect('}');
        return result;
    }

    private List<object?> ReadArray()
    {
        var result = new List<object?>();
        _position++;
        SkipWhiteSpace();
        if (TryRead(']'))
        {
            return result;
        }

        do
        {
            result.Add(ReadValue());
            SkipWhiteSpace();
        }
        while (TryRead(','));

        Expect(']');
        return result;
    }

    private string ReadString()
    {
        _position++;
        var builder = new StringBuilder();
        while (_position < _text.Length)
        {
            var c = _text[_position++];
            if (c == '"')
            {
                return builder.ToString();
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (_position >= _text.Length)
            {
                break;
            }

            var escape = _text[_position++];
            switch (escape)
            {
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'u':
                    if (_position + 4 > _text.Length
                        || !int.TryParse(_text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    {
                        throw Error("four hex digits");
                    }

                    builder.Append((char)code);
                    _position += 4;
                    break;
                default: builder.Append(escape); break;
            }
        }

        throw Error("the end of a string");
    }

    private string ReadNumber()
    {
        var start = _position;
        while (_position < _text.Length && "+-0123456789.eE".IndexOf(_text[_position]) >= 0)
        {
            _position++;
        }

        if (_position == start)
        {
            throw Error("a value");
        }

        return _text.Substring(start, _position - start);
    }

    private object? ReadWord(string word, object? value)
    {
        if (string.CompareOrdinal(_text, _position, word, 0, word.Length) != 0)
        {
            throw Error(word);
        }

        _position += word.Length;
        return value;
    }

    private void SkipWhiteSpace()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
        {
            _position++;
        }
    }

    private bool TryRead(char c)
    {
        if (_position < _text.Length && _text[_position] == c)
        {
            _position++;
            return true;
        }

        return false;
    }

    private void Expect(char c)
    {
        if (!TryRead(c))
        {
            throw Error("'" + c + "'");
        }
    }

    private FormatException Error(string expected) =>
        new(string.Format(CultureInfo.InvariantCulture, "Invalid JSON at position {0}: expected {1}.", _position, expected));
}
