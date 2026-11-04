using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.DragDrop;

/// <summary>Quoting of MySQL identifiers with backticks (US-0501).</summary>
public static class MySqlIdentifier
{
    // Reserved words of MySQL 8.0 and 8.4 (marked "(R)" in "Keywords and Reserved Words" of the reference manual).
    // Quoting a word that is not reserved is harmless; missing a reserved word breaks the SQL.
    private static readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCESSIBLE", "ADD", "ALL", "ALTER", "ANALYZE", "AND", "AS", "ASC", "ASENSITIVE", "BEFORE", "BETWEEN", "BIGINT",
        "BINARY", "BLOB", "BOTH", "BY", "CALL", "CASCADE", "CASE", "CHANGE", "CHAR", "CHARACTER", "CHECK", "COLLATE",
        "COLUMN", "CONDITION", "CONSTRAINT", "CONTINUE", "CONVERT", "CREATE", "CROSS", "CUBE", "CUME_DIST",
        "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR", "DATABASE", "DATABASES",
        "DAY_HOUR", "DAY_MICROSECOND", "DAY_MINUTE", "DAY_SECOND", "DEC", "DECIMAL", "DECLARE", "DEFAULT", "DELAYED",
        "DELETE", "DENSE_RANK", "DESC", "DESCRIBE", "DETERMINISTIC", "DISTINCT", "DISTINCTROW", "DIV", "DOUBLE", "DROP",
        "DUAL", "EACH", "ELSE", "ELSEIF", "EMPTY", "ENCLOSED", "ESCAPED", "EXCEPT", "EXISTS", "EXIT", "EXPLAIN", "FALSE",
        "FETCH", "FIRST_VALUE", "FLOAT", "FLOAT4", "FLOAT8", "FOR", "FORCE", "FOREIGN", "FROM", "FULLTEXT", "FUNCTION",
        "GENERATED", "GET", "GRANT", "GROUP", "GROUPING", "GROUPS", "HAVING", "HIGH_PRIORITY", "HOUR_MICROSECOND",
        "HOUR_MINUTE", "HOUR_SECOND", "IF", "IGNORE", "IN", "INDEX", "INFILE", "INNER", "INOUT", "INSENSITIVE", "INSERT",
        "INT", "INT1", "INT2", "INT3", "INT4", "INT8", "INTEGER", "INTERSECT", "INTERVAL", "INTO", "IO_AFTER_GTIDS",
        "IO_BEFORE_GTIDS", "IS", "ITERATE", "JOIN", "JSON_TABLE", "KEY", "KEYS", "KILL", "LAG", "LAST_VALUE", "LATERAL",
        "LEAD", "LEADING", "LEAVE", "LEFT", "LIKE", "LIMIT", "LINEAR", "LINES", "LOAD", "LOCALTIME", "LOCALTIMESTAMP",
        "LOCK", "LONG", "LONGBLOB", "LONGTEXT", "LOOP", "LOW_PRIORITY", "MASTER_BIND", "MASTER_SSL_VERIFY_SERVER_CERT",
        "MATCH", "MAXVALUE", "MEDIUMBLOB", "MEDIUMINT", "MEDIUMTEXT", "MIDDLEINT", "MINUTE_MICROSECOND", "MINUTE_SECOND",
        "MOD", "MODIFIES", "NATURAL", "NOT", "NO_WRITE_TO_BINLOG", "NTH_VALUE", "NTILE", "NULL", "NUMERIC", "OF", "ON",
        "OPTIMIZE", "OPTIMIZER_COSTS", "OPTION", "OPTIONALLY", "OR", "ORDER", "OUT", "OUTER", "OUTFILE", "OVER",
        "PARTITION", "PERCENT_RANK", "PRECISION", "PRIMARY", "PROCEDURE", "PURGE", "RANGE", "RANK", "READ", "READS",
        "READ_WRITE", "REAL", "RECURSIVE", "REFERENCES", "REGEXP", "RELEASE", "RENAME", "REPEAT", "REPLACE", "REQUIRE",
        "RESIGNAL", "RESTRICT", "RETURN", "REVOKE", "RIGHT", "RLIKE", "ROW", "ROWS", "ROW_NUMBER", "SCHEMA", "SCHEMAS",
        "SECOND_MICROSECOND", "SELECT", "SENSITIVE", "SEPARATOR", "SET", "SHOW", "SIGNAL", "SMALLINT", "SPATIAL",
        "SPECIFIC", "SQL", "SQLEXCEPTION", "SQLSTATE", "SQLWARNING", "SQL_BIG_RESULT", "SQL_CALC_FOUND_ROWS",
        "SQL_SMALL_RESULT", "SSL", "STARTING", "STORED", "STRAIGHT_JOIN", "SYSTEM", "TABLE", "TABLESAMPLE", "TERMINATED",
        "THEN", "TINYBLOB", "TINYINT", "TINYTEXT", "TO", "TRAILING", "TRIGGER", "TRUE", "UNDO", "UNION", "UNIQUE",
        "UNLOCK", "UNSIGNED", "UPDATE", "USAGE", "USE", "USING", "UTC_DATE", "UTC_TIME", "UTC_TIMESTAMP", "VALUES",
        "VARBINARY", "VARCHAR", "VARCHARACTER", "VARYING", "VIRTUAL", "WHEN", "WHERE", "WHILE", "WINDOW", "WITH",
        "WRITE", "XOR", "YEAR_MONTH", "ZEROFILL",
    };

    /// <summary>Returns true when <paramref name="word"/> is a reserved word (case-insensitive).</summary>
    public static bool IsReserved(string word) => word is not null && _reserved.Contains(word);

    /// <summary>
    /// Returns true when <paramref name="name"/> must be quoted: empty, a reserved word, a character other than
    /// letters, digits, <c>_</c> and <c>$</c> (non-ASCII letters are allowed unquoted), or starting with a digit
    /// (a name like <c>1e3</c> would read as a number).
    /// </summary>
    public static bool NeedsQuoting(string name)
    {
        if (string.IsNullOrEmpty(name) || IsReserved(name) || char.IsDigit(name[0]))
        {
            return true;
        }

        foreach (var c in name)
        {
            var allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '$' || c >= '\u0080';
            if (!allowed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Quotes with backticks; a backtick in the name is doubled: <c>a`b</c> becomes <c>`a``b`</c>.</summary>
    public static string Quote(string name) => "`" + (name ?? string.Empty).Replace("`", "``") + "`";

    /// <summary>Quotes only when <see cref="NeedsQuoting"/> says so.</summary>
    public static string QuoteIfNeeded(string name) => NeedsQuoting(name) ? Quote(name) : name;

    /// <summary>
    /// Removes the backticks from identifiers in a SQL text where MySQL does not need them (US-0602, CREATE script with
    /// "Only when needed"). Strings (<c>'...'</c>, <c>"..."</c>), comments (<c>-- </c>, <c>#</c>, <c>/* */</c> including
    /// version comments) are copied unchanged. A name keeps its backticks when it needs them, or when a letter, digit,
    /// <c>_</c> or <c>$</c> is directly before or after it (removing them would join two words).
    /// </summary>
    public static string UnquoteWhereSafe(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return sql ?? string.Empty;
        }

        var result = new System.Text.StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            int end;
            if (c is '\'' or '"')
            {
                end = SkipString(sql, i, c);
            }
            else if (c == '#' || (c == '-' && next == '-' && (i + 2 >= sql.Length || char.IsWhiteSpace(sql[i + 2]))))
            {
                end = sql.IndexOf('\n', i);
                end = end < 0 ? sql.Length : end;
            }
            else if (c == '/' && next == '*')
            {
                end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + 2;
            }
            else if (c == '`')
            {
                end = SkipQuotedIdentifier(sql, i);
                var name = sql.Substring(i + 1, Math.Max(0, end - i - 2)).Replace("``", "`");
                var closed = end - i >= 2 && sql[end - 1] == '`';
                var before = i > 0 ? sql[i - 1] : ' ';
                var after = end < sql.Length ? sql[end] : ' ';
                if (closed && !NeedsQuoting(name) && !IsWordChar(before) && !IsWordChar(after))
                {
                    result.Append(name);
                    i = end;
                    continue;
                }
            }
            else
            {
                result.Append(c);
                i++;
                continue;
            }

            result.Append(sql, i, end - i);
            i = end;
        }

        return result.ToString();
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    // Returns the index after the closing quote. Handles backslash escapes and doubled quotes; an unclosed string runs to the end.
    private static int SkipString(string sql, int start, char quote)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == '\\')
            {
                i += 2;
            }
            else if (sql[i] == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    i += 2;
                }
                else
                {
                    return i + 1;
                }
            }
            else
            {
                i++;
            }
        }

        return sql.Length;
    }

    // Returns the index after the closing backtick; a doubled backtick is part of the name.
    private static int SkipQuotedIdentifier(string sql, int start)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == '`')
            {
                if (i + 1 < sql.Length && sql[i + 1] == '`')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return sql.Length;
    }
}
