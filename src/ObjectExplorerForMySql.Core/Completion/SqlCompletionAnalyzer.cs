using System;
using System.Collections.Generic;
using System.Linq;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Completion;

/// <summary>What the completion list should offer at the caret (US-1401).</summary>
public enum CompletionSituation
{
    /// <summary>No list: inside a string, a comment or after <c>@</c>.</summary>
    None,

    /// <summary>Keywords and object names.</summary>
    Names,

    /// <summary>After <c>qualifier.</c>: the columns of a table, or the objects of a database.</summary>
    Members,
}

/// <summary>A table in the FROM, JOIN, UPDATE or INTO part of the statement.</summary>
public sealed record TableReference(string? Database, string Name, string? Alias);

/// <summary>A routine call at the caret (US-1402).</summary>
/// <param name="Database">The database before the name, or null.</param>
/// <param name="Name">The routine name.</param>
/// <param name="ArgumentIndex">0-based index of the argument at the caret.</param>
/// <param name="OpenParen">Position of the opening parenthesis.</param>
public sealed record SignatureContext(string? Database, string Name, int ArgumentIndex, int OpenParen);

/// <summary>A name under the mouse (US-1402).</summary>
/// <param name="Qualifier">The names before its dots.</param>
/// <param name="Name">The name.</param>
/// <param name="Start">Start of the name in the text.</param>
/// <param name="End">End of the name in the text.</param>
/// <param name="Tables">The tables of the statement with their aliases.</param>
public sealed record SymbolContext(IReadOnlyList<string> Qualifier, string Name, int Start, int End, IReadOnlyList<TableReference> Tables);

/// <summary>The result of <see cref="SqlCompletionAnalyzer.Analyze"/>.</summary>
/// <param name="Situation">What to offer.</param>
/// <param name="WordStart">Start of the word being typed; the chosen name replaces the text from here to the caret.</param>
/// <param name="Qualifier">The names before the dot, for example ["sakila"] or ["f"]; empty for <see cref="CompletionSituation.Names"/>.</param>
/// <param name="Tables">The tables of the statement with their aliases.</param>
/// <param name="InBackticks">True when the word being typed starts with a backtick; the name is then always quoted.</param>
public sealed record SqlCompletionContext(
    CompletionSituation Situation,
    int WordStart,
    IReadOnlyList<string> Qualifier,
    IReadOnlyList<TableReference> Tables,
    bool InBackticks = false)
{
    /// <summary>No completion at this position.</summary>
    public static SqlCompletionContext Nothing(int caret) => new(CompletionSituation.None, caret, [], []);
}

/// <summary>
/// Reads the statement around the caret for the completion list (US-1401): the word being typed, the names before a
/// dot and the tables with their aliases. A light scan, not a parser; strings, comments and backtick names are
/// respected, statements end at <c>;</c>.
/// </summary>
public static class SqlCompletionAnalyzer
{
    // Words after which a table name or alias ends.
    private static readonly HashSet<string> ClauseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "ON", "USING", "SET", "VALUES", "VALUE", "SELECT", "GROUP", "ORDER", "LIMIT", "HAVING", "JOIN", "LEFT",
        "RIGHT", "INNER", "OUTER", "CROSS", "NATURAL", "STRAIGHT_JOIN", "UNION", "WINDOW", "FOR", "LOCK", "INTO",
        "PARTITION", "AS", "USE", "IGNORE", "FORCE", "EXCEPT", "INTERSECT", "RETURNING",
    };

    private enum Kind
    {
        Word,
        Quoted,
        String,
        Comment,
        Number,
        Symbol,
    }

    /// <summary>Analyzes <paramref name="text"/> (the whole document) at <paramref name="caret"/>.</summary>
    public static SqlCompletionContext Analyze(string text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Max(0, Math.Min(caret, text.Length));
        var tokens = Tokenize(text);

        // Inside a string or comment, or right after @ (a variable): no list.
        foreach (var token in tokens)
        {
            // An unclosed string or block comment runs to the end; a line comment ends before the line break.
            if (token.Kind is Kind.String or Kind.Comment && token.Start < caret && (caret < token.End || (caret == token.End && !token.Closed)))
            {
                return SqlCompletionContext.Nothing(caret);
            }
        }

        var wordStart = caret;
        var inBackticks = false;
        var current = tokens.FirstOrDefault(t => t.Start < caret && caret <= t.End && t.Kind is Kind.Word or Kind.Quoted or Kind.Number);
        if (current is not null)
        {
            if (current.Kind == Kind.Number || (current.Kind == Kind.Quoted && current.Closed && caret == current.End))
            {
                return SqlCompletionContext.Nothing(caret);
            }

            wordStart = current.Start;
            inBackticks = current.Kind == Kind.Quoted;
        }

        var before = tokens.LastOrDefault(t => t.End <= wordStart && t.Kind != Kind.Comment);
        if (before is { Kind: Kind.Symbol, Text: "@" } && before.End == wordStart)
        {
            return SqlCompletionContext.Nothing(caret);
        }

        // Statement around the caret.
        var start = tokens.FindLastIndex(t => t.Kind == Kind.Symbol && t.Text == ";" && t.End <= wordStart) + 1;
        var end = tokens.FindIndex(start, t => t.Kind == Kind.Symbol && t.Text == ";" && t.Start >= caret);
        var statement = tokens.GetRange(start, (end < 0 ? tokens.Count : end) - start).Where(t => t.Kind != Kind.Comment).ToList();
        var tables = Tables(statement.Where(t => t != current).ToList());

        // qualifier.word or db.table.word
        var qualifier = new List<string>();
        var index = statement.FindLastIndex(t => t.End <= wordStart);
        while (qualifier.Count < 2 && index >= 1
            && statement[index] is { Kind: Kind.Symbol, Text: "." } dot && dot.End == (qualifier.Count == 0 ? wordStart : statement[index + 1].Start)
            && statement[index - 1] is { Kind: Kind.Word or Kind.Quoted } name && name.End == dot.Start)
        {
            qualifier.Insert(0, name.Text);
            index -= 2;
        }

        return new SqlCompletionContext(qualifier.Count > 0 ? CompletionSituation.Members : CompletionSituation.Names, wordStart, qualifier, tables, inBackticks);
    }

    /// <summary>
    /// Returns the routine call around <paramref name="caret"/> for parameter info (US-1402): the name before the
    /// innermost open parenthesis and the number of commas before the caret. Null outside a call or in a string or comment.
    /// </summary>
    public static SignatureContext? SignatureAt(string text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Max(0, Math.Min(caret, text.Length));
        var tokens = Tokenize(text);
        if (InStringOrComment(tokens, caret))
        {
            return null;
        }

        var statement = Statement(tokens, caret, caret);
        var open = new Stack<(int Index, int Commas)>();
        for (var i = 0; i < statement.Count && statement[i].End <= caret; i++)
        {
            switch (statement[i])
            {
                case { Kind: Kind.Symbol, Text: "(" }:
                    open.Push((i, 0));
                    break;
                case { Kind: Kind.Symbol, Text: ")" } when open.Count > 0:
                    open.Pop();
                    break;
                case { Kind: Kind.Symbol, Text: "," } when open.Count > 0:
                    var top = open.Pop();
                    open.Push((top.Index, top.Commas + 1));
                    break;
            }
        }

        if (open.Count == 0)
        {
            return null;
        }

        var (paren, commas) = open.Peek();
        if (paren == 0 || statement[paren - 1].Kind is not (Kind.Word or Kind.Quoted))
        {
            return null;
        }

        var name = statement[paren - 1];
        string? database = null;
        if (paren >= 3 && statement[paren - 2] is { Kind: Kind.Symbol, Text: "." } && statement[paren - 3].Kind is Kind.Word or Kind.Quoted)
        {
            database = statement[paren - 3].Text;
        }

        // "SELECT (", "IN (", "VALUES (": a keyword before the parenthesis is no call.
        if (database is null && name.Kind == Kind.Word && MySqlKeywords.IsKeyword(name.Text))
        {
            return null;
        }

        return new SignatureContext(database, name.Text, commas, statement[paren].Start);
    }

    /// <summary>Returns the name at <paramref name="position"/> with the names before its dots, for quick info (US-1402).</summary>
    public static SymbolContext? SymbolAt(string text, int position)
    {
        text ??= string.Empty;
        var tokens = Tokenize(text);
        var symbol = tokens.FirstOrDefault(t => t.Kind is Kind.Word or Kind.Quoted && t.Start <= position && position < t.End);
        if (symbol is null)
        {
            return null;
        }

        var statement = Statement(tokens, symbol.Start, symbol.End);
        var index = statement.IndexOf(symbol);
        var qualifier = new List<string>();
        var next = symbol;
        while (qualifier.Count < 2 && index >= 2
            && statement[index - 1] is { Kind: Kind.Symbol, Text: "." } dot && dot.End == next.Start
            && statement[index - 2] is { Kind: Kind.Word or Kind.Quoted } name && name.End == dot.Start)
        {
            qualifier.Insert(0, name.Text);
            next = name;
            index -= 2;
        }

        return new SymbolContext(qualifier, symbol.Text, symbol.Start, symbol.End, Tables(statement.Where(t => t != symbol).ToList()));
    }

    private static bool InStringOrComment(List<Token> tokens, int caret) =>
        tokens.Any(t => t.Kind is Kind.String or Kind.Comment && t.Start < caret && (caret < t.End || (caret == t.End && !t.Closed)));

    // The tokens of the statement around [from, to], without comments.
    private static List<Token> Statement(List<Token> tokens, int from, int to)
    {
        var start = tokens.FindLastIndex(t => t.Kind == Kind.Symbol && t.Text == ";" && t.End <= from) + 1;
        var end = tokens.FindIndex(start, t => t.Kind == Kind.Symbol && t.Text == ";" && t.Start >= to);
        return tokens.GetRange(start, (end < 0 ? tokens.Count : end) - start).Where(t => t.Kind != Kind.Comment).ToList();
    }

    private static List<TableReference> Tables(List<Token> tokens)
    {
        var result = new List<TableReference>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != Kind.Word || tokens[i].Text.ToUpperInvariant() is not ("FROM" or "JOIN" or "UPDATE" or "INTO" or "STRAIGHT_JOIN"))
            {
                continue;
            }

            var isFrom = tokens[i].Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) || tokens[i].Text.Equals("UPDATE", StringComparison.OrdinalIgnoreCase);
            var position = i + 1;
            while (position < tokens.Count)
            {
                position = Reference(tokens, position, result);
                if (!isFrom || position >= tokens.Count || tokens[position] is not { Kind: Kind.Symbol, Text: "," })
                {
                    break;
                }

                position++;
            }

            i = Math.Max(i, position - 1);
        }

        return result;
    }

    // Reads "[db.]table [AS] [alias]" or "(subquery) [AS] alias"; returns the index after it.
    private static int Reference(List<Token> tokens, int i, List<TableReference> result)
    {
        if (i >= tokens.Count)
        {
            return i;
        }

        if (tokens[i] is { Kind: Kind.Symbol, Text: "(" })
        {
            var depth = 0;
            for (; i < tokens.Count; i++)
            {
                depth += tokens[i] is { Kind: Kind.Symbol, Text: "(" } ? 1 : tokens[i] is { Kind: Kind.Symbol, Text: ")" } ? -1 : 0;
                if (depth == 0)
                {
                    i++;
                    break;
                }
            }

            return Alias(tokens, i, out _);
        }

        if (!IsName(tokens[i]))
        {
            return i;
        }

        string? database = null;
        var name = tokens[i].Text;
        i++;
        if (i < tokens.Count && tokens[i] is { Kind: Kind.Symbol, Text: "." })
        {
            if (i + 1 >= tokens.Count || !IsName(tokens[i + 1]))
            {
                // "db." being typed: not a table yet.
                return i + 1;
            }

            database = name;
            name = tokens[i + 1].Text;
            i += 2;
        }

        i = Alias(tokens, i, out var alias);
        result.Add(new TableReference(database, name, alias));
        return i;
    }

    private static int Alias(List<Token> tokens, int i, out string? alias)
    {
        alias = null;
        if (i < tokens.Count && tokens[i] is { Kind: Kind.Word } asWord && asWord.Text.Equals("AS", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < tokens.Count && tokens[i + 1].Kind is Kind.Word or Kind.Quoted)
            {
                alias = tokens[i + 1].Text;
                return i + 2;
            }

            return i + 1;
        }

        if (i < tokens.Count && (tokens[i].Kind == Kind.Quoted
            || (tokens[i].Kind == Kind.Word && !ClauseWords.Contains(tokens[i].Text) && !MySqlKeywords.IsKeyword(tokens[i].Text))))
        {
            alias = tokens[i].Text;
            return i + 1;
        }

        return i;
    }

    private static bool IsName(Token token) =>
        token.Kind == Kind.Quoted || (token.Kind == Kind.Word && !ClauseWords.Contains(token.Text));

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var start = i;
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '#' || (c == '-' && i + 1 < text.Length && text[i + 1] == '-' && (i + 2 == text.Length || char.IsWhiteSpace(text[i + 2]))))
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                tokens.Add(new Token(Kind.Comment, start, i, string.Empty, Closed: false));
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? text.Length : close + 2;
                tokens.Add(new Token(Kind.Comment, start, i, string.Empty, close >= 0));
            }
            else if (c is '\'' or '"' or '`')
            {
                i++;
                var closed = false;
                var value = new System.Text.StringBuilder();
                while (i < text.Length)
                {
                    if (text[i] == c)
                    {
                        if (i + 1 < text.Length && text[i + 1] == c)
                        {
                            value.Append(c);
                            i += 2;
                            continue;
                        }

                        i++;
                        closed = true;
                        break;
                    }

                    if (text[i] == '\\' && c != '`' && i + 1 < text.Length)
                    {
                        i++;
                    }

                    value.Append(text[i]);
                    i++;
                }

                tokens.Add(new Token(c == '`' ? Kind.Quoted : Kind.String, start, i, value.ToString(), closed));
            }
            else if (char.IsLetter(c) || c is '_' or '$')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$'))
                {
                    i++;
                }

                tokens.Add(new Token(Kind.Word, start, i, text.Substring(start, i - start), true));
            }
            else if (char.IsDigit(c))
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new Token(Kind.Number, start, i, text.Substring(start, i - start), true));
            }
            else
            {
                i++;
                tokens.Add(new Token(Kind.Symbol, start, i, c.ToString(), true));
            }
        }

        return tokens;
    }

    private sealed record Token(Kind Kind, int Start, int End, string Text, bool Closed);
}
