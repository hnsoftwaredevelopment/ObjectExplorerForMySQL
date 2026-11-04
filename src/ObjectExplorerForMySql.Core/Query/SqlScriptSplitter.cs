using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>One statement of a script; <see cref="Line"/> is the 0-based line of its first character in the script.</summary>
public sealed record SqlStatement(string Text, int Line);

/// <summary>
/// Splits a script into statements as the mysql command line client does (US-0703): on the current delimiter, outside
/// strings, backtick names and comments, with <c>DELIMITER</c> lines that change the delimiter. Comments before a
/// statement are left out, so a statement starts at its first code; <c>/*! ... */</c> and <c>/*+ ... */</c> count as code,
/// because MySQL runs them.
/// </summary>
public static class SqlScriptSplitter
{
    private enum State
    {
        Normal,
        LineComment,
        BlockComment,
        SingleQuoted,
        DoubleQuoted,
        Backtick,
    }

    /// <summary>Splits <paramref name="script"/>; statements without code are dropped.</summary>
    public static IReadOnlyList<SqlStatement> Split(string script)
    {
        var result = new List<SqlStatement>();
        if (string.IsNullOrEmpty(script))
        {
            return result;
        }

        var delimiter = ";";
        var state = State.Normal;
        var line = 0;
        var start = -1;
        var startLine = 0;
        var lineHasCode = false;
        var i = 0;

        void Finish(int end)
        {
            if (start >= 0)
            {
                var text = script.Substring(start, end - start).TrimEnd();
                if (text.Length > 0)
                {
                    result.Add(new SqlStatement(text, startLine));
                }
            }

            start = -1;
        }

        void Begin()
        {
            if (start < 0)
            {
                start = i;
                startLine = line;
            }

            lineHasCode = true;
        }

        while (i < script.Length)
        {
            var c = script[i];
            if (c == '\n')
            {
                line++;
                lineHasCode = false;
                if (state == State.LineComment)
                {
                    state = State.Normal;
                }

                i++;
                continue;
            }

            switch (state)
            {
                case State.Normal:
                    if (char.IsWhiteSpace(c))
                    {
                        i++;
                        continue;
                    }

                    // DELIMITER is a client command: only at the start of a line and between statements.
                    if (start < 0 && !lineHasCode && IsWordAt(script, i, "DELIMITER"))
                    {
                        var lineEnd = script.IndexOf('\n', i);
                        if (lineEnd < 0)
                        {
                            lineEnd = script.Length;
                        }

                        var argument = script.Substring(i + "DELIMITER".Length, lineEnd - i - "DELIMITER".Length).Trim();
                        var space = argument.IndexOfAny([' ', '\t']);
                        if (space > 0)
                        {
                            argument = argument.Substring(0, space);
                        }

                        if (argument.Length > 0)
                        {
                            delimiter = argument;
                        }

                        i = lineEnd;
                        continue;
                    }

                    if (string.CompareOrdinal(script, i, delimiter, 0, delimiter.Length) == 0)
                    {
                        Finish(i);
                        i += delimiter.Length;
                        lineHasCode = true;
                        continue;
                    }

                    if (c == '#' || (c == '-' && At(script, i + 1) == '-' && (i + 2 >= script.Length || char.IsWhiteSpace(script[i + 2]))))
                    {
                        state = State.LineComment;
                        i++;
                        continue;
                    }

                    if (c == '/' && At(script, i + 1) == '*' && At(script, i + 2) != '!' && At(script, i + 2) != '+')
                    {
                        state = State.BlockComment;
                        i += 2;
                        continue;
                    }

                    Begin();
                    state = c switch
                    {
                        '\'' => State.SingleQuoted,
                        '"' => State.DoubleQuoted,
                        '`' => State.Backtick,
                        _ => State.Normal,
                    };
                    i++;
                    continue;

                case State.LineComment:
                    i++;
                    continue;

                case State.BlockComment:
                    if (c == '*' && At(script, i + 1) == '/')
                    {
                        state = State.Normal;
                        i += 2;
                        continue;
                    }

                    i++;
                    continue;

                case State.Backtick:
                    if (c == '`')
                    {
                        if (At(script, i + 1) == '`')
                        {
                            i += 2;
                            continue;
                        }

                        state = State.Normal;
                    }

                    i++;
                    continue;

                default:
                    var quote = state == State.SingleQuoted ? '\'' : '"';
                    if (c == '\\')
                    {
                        // Keep the line count right when the escaped character is a line break.
                        if (At(script, i + 1) == '\n')
                        {
                            line++;
                        }

                        i += 2;
                        continue;
                    }

                    if (c == quote)
                    {
                        if (At(script, i + 1) == quote)
                        {
                            i += 2;
                            continue;
                        }

                        state = State.Normal;
                    }

                    i++;
                    continue;
            }
        }

        Finish(script.Length);
        return result;
    }

    private static char At(string text, int index) => index < text.Length ? text[index] : '\0';

    private static bool IsWordAt(string text, int index, string word) =>
        string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
        && (index + word.Length >= text.Length || char.IsWhiteSpace(text[index + word.Length]));
}
