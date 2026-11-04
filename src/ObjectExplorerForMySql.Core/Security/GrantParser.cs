using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ObjectExplorerForMySql.Core.Security;

/// <summary>Level of a line of <c>SHOW GRANTS</c> (US-1001).</summary>
public enum GrantLevel
{
    /// <summary><c>ON *.*</c>.</summary>
    Global,

    /// <summary><c>ON `db`.*</c>.</summary>
    Database,

    /// <summary><c>ON `db`.`table`</c>, also with column privileges.</summary>
    Table,

    /// <summary><c>ON PROCEDURE `db`.`p`</c> or <c>ON FUNCTION `db`.`f`</c>.</summary>
    Routine,

    /// <summary><c>GRANT PROXY ON ...</c>.</summary>
    Proxy,

    /// <summary>A role granted to the account: <c>GRANT `role`@`%` TO ...</c> (no ON).</summary>
    Role,
}

/// <summary>One parsed line of <c>SHOW GRANTS</c>.</summary>
public sealed class GrantEntry
{
    /// <summary>Initializes a new instance of the <see cref="GrantEntry"/> class.</summary>
    public GrantEntry(bool isRevoke, IReadOnlyList<string> privileges, GrantLevel level, string? database, string? objectName, bool withGrantOption, IReadOnlyList<string> roles)
    {
        IsRevoke = isRevoke;
        Privileges = privileges;
        Level = level;
        Database = database;
        ObjectName = objectName;
        WithGrantOption = withGrantOption;
        Roles = roles;
    }

    /// <summary>Gets a value indicating whether this is a partial revoke (<c>REVOKE ... FROM</c>, MySQL 8 with partial_revokes).</summary>
    public bool IsRevoke { get; }

    /// <summary>Gets the privileges in upper case, without column lists; <c>ALL PRIVILEGES</c> becomes <c>ALL</c>.</summary>
    public IReadOnlyList<string> Privileges { get; }

    /// <summary>Gets the level.</summary>
    public GrantLevel Level { get; }

    /// <summary>Gets the database of a database, table or routine grant (unquoted).</summary>
    public string? Database { get; }

    /// <summary>Gets the table or routine name (unquoted).</summary>
    public string? ObjectName { get; }

    /// <summary>Gets a value indicating whether the line ends with WITH GRANT OPTION (or has ADMIN OPTION for a role).</summary>
    public bool WithGrantOption { get; }

    /// <summary>Gets the granted role names (without host) of a <see cref="GrantLevel.Role"/> line.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>Returns true when the line contains <paramref name="privilege"/> or ALL.</summary>
    public bool Has(string privilege) =>
        Privileges.Any(p => p == "ALL" || string.Equals(p, privilege, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Parses the output of <c>SHOW GRANTS</c> of MySQL 5.7, 8.x and MariaDB (US-1001). Names may be quoted with backticks,
/// single or double quotes; privileges may have column lists; lists may be written with or without spaces.
/// </summary>
public static class GrantParser
{
    /// <summary>Parses all lines; lines that cannot be read are skipped and counted in <paramref name="unknown"/>.</summary>
    public static IReadOnlyList<GrantEntry> ParseAll(IEnumerable<string> lines, out int unknown)
    {
        var result = new List<GrantEntry>();
        unknown = 0;
        foreach (var line in lines ?? throw new ArgumentNullException(nameof(lines)))
        {
            if (Parse(line) is { } entry)
            {
                result.Add(entry);
            }
            else
            {
                unknown++;
            }
        }

        return result;
    }

    /// <summary>Parses one line, or returns null when it is not a GRANT or REVOKE statement that can be read.</summary>
    public static GrantEntry? Parse(string? line)
    {
        var tokens = Tokenize(line ?? string.Empty);
        if (tokens.Count < 3)
        {
            return null;
        }

        var isRevoke = Is(tokens[0], "REVOKE");
        if (!isRevoke && !Is(tokens[0], "GRANT"))
        {
            return null;
        }

        var target = isRevoke ? "FROM" : "TO";
        var on = IndexOf(tokens, "ON", 1);
        var to = IndexOf(tokens, target, 1);
        if (to < 0)
        {
            return null;
        }

        var withGrantOption = ContainsSequence(tokens, to, "WITH", "GRANT", "OPTION") || ContainsSequence(tokens, to, "WITH", "ADMIN", "OPTION");

        // GRANT `r1`@`%`, `r2`@`%` TO ...: roles, no ON before TO.
        if (on < 0 || on > to)
        {
            var roles = SplitList(tokens, 1, to).Select(RoleName).Where(r => r.Length > 0).ToList();
            return roles.Count == 0 ? null : new GrantEntry(isRevoke, [], GrantLevel.Role, null, null, withGrantOption, roles);
        }

        var privileges = SplitList(tokens, 1, on).Select(PrivilegeName).Where(p => p.Length > 0).ToList();
        if (privileges.Count == 0)
        {
            return null;
        }

        if (privileges.Count == 1 && privileges[0] == "PROXY")
        {
            return new GrantEntry(isRevoke, privileges, GrantLevel.Proxy, null, null, withGrantOption, []);
        }

        var objectStart = on + 1;
        var isRoutine = false;
        if (objectStart < to && (Is(tokens[objectStart], "PROCEDURE") || Is(tokens[objectStart], "FUNCTION") || Is(tokens[objectStart], "TABLE")))
        {
            isRoutine = !Is(tokens[objectStart], "TABLE");
            objectStart++;
        }

        if (objectStart >= to)
        {
            return null;
        }

        // The object is one token: *.*, `db`.*, `db`.`t` (the tokenizer keeps quoted names and dots together).
        var parts = SplitQualified(tokens[objectStart]);
        if (parts.Count == 0 || parts.Count > 2)
        {
            return null;
        }

        if (parts.Count == 1)
        {
            // ON * (the current database) or ON `t`: not used by SHOW GRANTS; read as global only for *.
            return parts[0] == "*" ? new GrantEntry(isRevoke, privileges, GrantLevel.Global, null, null, withGrantOption, []) : null;
        }

        if (parts[0] == "*" && parts[1] == "*")
        {
            return new GrantEntry(isRevoke, privileges, GrantLevel.Global, null, null, withGrantOption, []);
        }

        if (parts[1] == "*")
        {
            return new GrantEntry(isRevoke, privileges, GrantLevel.Database, parts[0], null, withGrantOption, []);
        }

        return new GrantEntry(isRevoke, privileges, isRoutine ? GrantLevel.Routine : GrantLevel.Table, parts[0], parts[1], withGrantOption, []);
    }

    private static bool Is(string token, string word) => string.Equals(token, word, StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<string> tokens, string word, int start)
    {
        for (var i = start; i < tokens.Count; i++)
        {
            if (Is(tokens[i], word))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool ContainsSequence(IReadOnlyList<string> tokens, int start, params string[] words)
    {
        for (var i = start; i + words.Length <= tokens.Count; i++)
        {
            if (words.Select((w, j) => Is(tokens[i + j], w)).All(match => match))
            {
                return true;
            }
        }

        return false;
    }

    // Groups the tokens between start and end by the comma tokens.
    private static IEnumerable<string> SplitList(IReadOnlyList<string> tokens, int start, int end)
    {
        var current = new List<string>();
        for (var i = start; i < end; i++)
        {
            if (tokens[i] == ",")
            {
                yield return string.Join(" ", current);
                current.Clear();
            }
            else
            {
                current.Add(tokens[i]);
            }
        }

        if (current.Count > 0)
        {
            yield return string.Join(" ", current);
        }
    }

    // "SELECT (`a`, `b`)" -> SELECT; "ALL PRIVILEGES" -> ALL; "CREATE USER" stays.
    private static string PrivilegeName(string item)
    {
        var paren = item.IndexOf('(');
        var name = (paren >= 0 ? item.Substring(0, paren) : item).Trim().ToUpperInvariant();
        return name == "ALL PRIVILEGES" ? "ALL" : name;
    }

    // `r1`@`%` -> r1; `role` -> role (MariaDB roles have no host).
    private static string RoleName(string item)
    {
        var parts = SplitQualified(item.Replace(" ", string.Empty), '@');
        return parts.Count > 0 ? parts[0] : string.Empty;
    }

    // Splits `a`.`b` (or 'a'@'b') on the separator outside quotes and removes the quotes.
    private static List<string> SplitQualified(string text, char separator = '.')
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } q)
            {
                if (c == q)
                {
                    if (i + 1 < text.Length && text[i + 1] == q)
                    {
                        current.Append(c);
                        i++;
                    }
                    else
                    {
                        quote = null;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '`' or '\'' or '"')
            {
                quote = c;
            }
            else if (c == separator)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    // Words, commas and parenthesized groups; quoted names stay together with what is attached to them (`db`.`t`, 'u'@'h').
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var depth = 0;

        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        foreach (var c in line.Trim().TrimEnd(';'))
        {
            if (quote is { } q)
            {
                current.Append(c);
                if (c == q)
                {
                    quote = null;
                }
            }
            else if (c is '`' or '\'' or '"')
            {
                quote = c;
                current.Append(c);
            }
            else if (c == '(')
            {
                depth++;
                current.Append(c);
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
                current.Append(c);
            }
            else if (depth > 0)
            {
                current.Append(c);
            }
            else if (c == ',')
            {
                Flush();
                tokens.Add(",");
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return tokens;
    }
}
