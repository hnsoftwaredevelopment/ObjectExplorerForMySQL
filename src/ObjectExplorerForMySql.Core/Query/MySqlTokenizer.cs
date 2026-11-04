using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>Kind of a colored token in the MySQL Query Editor (US-0702).</summary>
public enum MySqlTokenKind
{
    /// <summary>Reserved word or keyword: SELECT, FROM, DELIMITER, ...</summary>
    Keyword,

    /// <summary>Comment: <c>-- </c>, <c>#</c> or <c>/* */</c>.</summary>
    Comment,

    /// <summary>String in single or double quotes.</summary>
    String,

    /// <summary>Number: 12, 1.5, 2e10, 0x1F.</summary>
    Number,

    /// <summary>Identifier in backticks.</summary>
    QuotedIdentifier,

    /// <summary>User or system variable: <c>@name</c>, <c>@@sql_mode</c>.</summary>
    Variable,
}

/// <summary>State at the end of a line: the construct that continues on the next line.</summary>
public enum MySqlLexState
{
    /// <summary>Nothing open.</summary>
    Normal,

    /// <summary>Inside <c>/* ... */</c>.</summary>
    BlockComment,

    /// <summary>Inside a string in single quotes.</summary>
    SingleQuoted,

    /// <summary>Inside a string in double quotes.</summary>
    DoubleQuoted,

    /// <summary>Inside a backtick identifier.</summary>
    Backtick,
}

/// <summary>A colored token: start and length within the line.</summary>
public readonly struct MySqlToken : IEquatable<MySqlToken>
{
    /// <summary>Initializes a new instance of the <see cref="MySqlToken"/> struct.</summary>
    public MySqlToken(int start, int length, MySqlTokenKind kind)
    {
        Start = start;
        Length = length;
        Kind = kind;
    }

    /// <summary>Gets the start within the line.</summary>
    public int Start { get; }

    /// <summary>Gets the length.</summary>
    public int Length { get; }

    /// <summary>Gets the kind.</summary>
    public MySqlTokenKind Kind { get; }

    /// <inheritdoc />
    public bool Equals(MySqlToken other) => Start == other.Start && Length == other.Length && Kind == other.Kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MySqlToken other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Start * 397) ^ (Length * 31) ^ (int)Kind;

    /// <inheritdoc />
    public override string ToString() => $"{Kind}({Start},{Length})";
}

/// <summary>
/// Splits one line of MySQL into colored tokens (US-0702). Works per line with a state that carries comments, strings
/// and backtick identifiers over line ends, so the editor only needs to re-read the lines that changed. Plain words,
/// operators and white space get no token and keep the default text color.
/// </summary>
public static class MySqlTokenizer
{
    /// <summary>Tokenizes <paramref name="line"/> that starts in <paramref name="state"/>.</summary>
    /// <returns>The tokens in order; <paramref name="endState"/> is the state at the end of the line.</returns>
    public static IReadOnlyList<MySqlToken> Tokenize(string line, MySqlLexState state, out MySqlLexState endState)
    {
        var tokens = new List<MySqlToken>();
        endState = Scan(line ?? string.Empty, state, tokens);
        return tokens;
    }

    /// <summary>Returns the state at the end of <paramref name="line"/> without building tokens (for long scripts).</summary>
    public static MySqlLexState EndState(string line, MySqlLexState state) => Scan(line ?? string.Empty, state, null);

    private static MySqlLexState Scan(string line, MySqlLexState state, List<MySqlToken>? tokens)
    {
        var i = 0;

        if (state != MySqlLexState.Normal)
        {
            var end = ScanOpen(line, 0, state, out var closed);
            tokens?.Add(new MySqlToken(0, end, KindOf(state)));
            if (!closed)
            {
                return state;
            }

            i = end;
        }

        while (i < line.Length)
        {
            var c = line[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Comments. "--" is a comment only when followed by white space or the line end (MySQL rule), so 5--3 stays.
            if (c == '#' || (c == '-' && At(line, i + 1) == '-' && (i + 2 >= line.Length || char.IsWhiteSpace(line[i + 2]))))
            {
                tokens?.Add(new MySqlToken(i, line.Length - i, MySqlTokenKind.Comment));
                break;
            }

            if (c == '/' && At(line, i + 1) == '*')
            {
                var end = ScanOpen(line, i + 2, MySqlLexState.BlockComment, out var closed);
                tokens?.Add(new MySqlToken(i, end - i, MySqlTokenKind.Comment));
                if (!closed)
                {
                    return MySqlLexState.BlockComment;
                }

                i = end;
                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                var open = c == '\'' ? MySqlLexState.SingleQuoted : c == '"' ? MySqlLexState.DoubleQuoted : MySqlLexState.Backtick;
                var end = ScanOpen(line, i + 1, open, out var closed);
                tokens?.Add(new MySqlToken(i, end - i, KindOf(open)));
                if (!closed)
                {
                    return open;
                }

                i = end;
                continue;
            }

            if (c == '@')
            {
                var end = i + 1;
                if (At(line, end) == '@')
                {
                    end++;
                }

                if (At(line, end) is '\'' or '"' or '`')
                {
                    // @'name' or @`name`: the quoted part is the variable name.
                    var quote = line[end];
                    var close = line.IndexOf(quote, end + 1);
                    end = close < 0 ? line.Length : close + 1;
                }
                else
                {
                    while (end < line.Length && (IsWordChar(line[end]) || line[end] == '.'))
                    {
                        end++;
                    }
                }

                if (end > i + 1)
                {
                    tokens?.Add(new MySqlToken(i, end - i, MySqlTokenKind.Variable));
                }

                i = end;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && char.IsDigit(At(line, i + 1))))
            {
                // Words that contain digits (t1) never get here: the word branch below reads them as a whole.
                var end = ScanNumber(line, i);
                if (end < line.Length && IsWordChar(line[end]))
                {
                    // 1abc is a valid MySQL identifier: no number.
                    while (end < line.Length && IsWordChar(line[end]))
                    {
                        end++;
                    }

                    i = end;
                    continue;
                }

                tokens?.Add(new MySqlToken(i, end - i, MySqlTokenKind.Number));
                i = end;
                continue;
            }

            if (IsWordChar(c))
            {
                var end = i + 1;
                while (end < line.Length && IsWordChar(line[end]))
                {
                    end++;
                }

                // A word after a dot is a column or table name (t.status), not a keyword.
                var afterDot = i > 0 && line[i - 1] == '.';
                if (!afterDot && MySqlKeywords.IsKeyword(line.Substring(i, end - i)))
                {
                    tokens?.Add(new MySqlToken(i, end - i, MySqlTokenKind.Keyword));
                }

                i = end;
                continue;
            }

            i++;
        }

        return MySqlLexState.Normal;
    }

    private static char At(string line, int index) => index < line.Length ? line[index] : '\0';

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static MySqlTokenKind KindOf(MySqlLexState state) => state switch
    {
        MySqlLexState.BlockComment => MySqlTokenKind.Comment,
        MySqlLexState.Backtick => MySqlTokenKind.QuotedIdentifier,
        _ => MySqlTokenKind.String,
    };

    // Returns the index after the closing characters, or the line length when the construct stays open.
    private static int ScanOpen(string line, int start, MySqlLexState state, out bool closed)
    {
        var i = start;
        while (i < line.Length)
        {
            var c = line[i];
            switch (state)
            {
                case MySqlLexState.BlockComment:
                    if (c == '*' && At(line, i + 1) == '/')
                    {
                        closed = true;
                        return i + 2;
                    }

                    break;

                case MySqlLexState.Backtick:
                    if (c == '`')
                    {
                        if (At(line, i + 1) == '`')
                        {
                            i += 2;
                            continue;
                        }

                        closed = true;
                        return i + 1;
                    }

                    break;

                default:
                    var quote = state == MySqlLexState.SingleQuoted ? '\'' : '"';
                    if (c == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (c == quote)
                    {
                        if (At(line, i + 1) == quote)
                        {
                            i += 2;
                            continue;
                        }

                        closed = true;
                        return i + 1;
                    }

                    break;
            }

            i++;
        }

        closed = false;
        return line.Length;
    }

    private static int ScanNumber(string line, int start)
    {
        var i = start;
        if (line[i] == '0' && At(line, i + 1) is 'x' or 'X' && IsHex(At(line, i + 2)))
        {
            i += 2;
            while (i < line.Length && IsHex(line[i]))
            {
                i++;
            }

            return i;
        }

        while (i < line.Length && char.IsDigit(line[i]))
        {
            i++;
        }

        if (At(line, i) == '.' && (i == start || char.IsDigit(At(line, i + 1)) || !IsWordChar(At(line, i + 1))))
        {
            i++;
            while (i < line.Length && char.IsDigit(line[i]))
            {
                i++;
            }
        }

        if (At(line, i) is 'e' or 'E')
        {
            var j = i + 1;
            if (At(line, j) is '+' or '-')
            {
                j++;
            }

            if (char.IsDigit(At(line, j)))
            {
                i = j;
                while (i < line.Length && char.IsDigit(line[i]))
                {
                    i++;
                }
            }
        }

        return i;
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
