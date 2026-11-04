using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// Words colored as keywords in the MySQL Query Editor (US-0702): the reserved words of MySQL 8.0 and 8.4, plus
/// frequent non-reserved keywords and the client command DELIMITER. Built-in function names (COUNT, NOW, CONCAT) are
/// not in the list; they keep the text color, as in MySQL Workbench.
/// </summary>
public static class MySqlKeywords
{
    private static readonly HashSet<string> _keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Reserved words (MySQL 8.0 / 8.4 reference manual, "Keywords and Reserved Words").
        "ACCESSIBLE", "ADD", "ALL", "ALTER", "ANALYZE", "AND", "AS", "ASC", "ASENSITIVE", "BEFORE", "BETWEEN", "BIGINT",
        "BINARY", "BLOB", "BOTH", "BY", "CALL", "CASCADE", "CASE", "CHANGE", "CHAR", "CHARACTER", "CHECK", "COLLATE",
        "COLUMN", "CONDITION", "CONSTRAINT", "CONTINUE", "CONVERT", "CREATE", "CROSS", "CUBE", "CUME_DIST", "CURRENT_DATE",
        "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR", "DATABASE", "DATABASES", "DAY_HOUR",
        "DAY_MICROSECOND", "DAY_MINUTE", "DAY_SECOND", "DEC", "DECIMAL", "DECLARE", "DEFAULT", "DELAYED", "DELETE",
        "DENSE_RANK", "DESC", "DESCRIBE", "DETERMINISTIC", "DISTINCT", "DISTINCTROW", "DIV", "DOUBLE", "DROP", "DUAL",
        "EACH", "ELSE", "ELSEIF", "EMPTY", "ENCLOSED", "ESCAPED", "EXCEPT", "EXISTS", "EXIT", "EXPLAIN", "FALSE", "FETCH",
        "FIRST_VALUE", "FLOAT", "FLOAT4", "FLOAT8", "FOR", "FORCE", "FOREIGN", "FROM", "FULLTEXT", "FUNCTION", "GENERATED",
        "GET", "GRANT", "GROUP", "GROUPING", "GROUPS", "HAVING", "HIGH_PRIORITY", "HOUR_MICROSECOND", "HOUR_MINUTE",
        "HOUR_SECOND", "IF", "IGNORE", "IN", "INDEX", "INFILE", "INNER", "INOUT", "INSENSITIVE", "INSERT", "INT", "INT1",
        "INT2", "INT3", "INT4", "INT8", "INTEGER", "INTERSECT", "INTERVAL", "INTO", "IO_AFTER_GTIDS", "IO_BEFORE_GTIDS",
        "IS", "ITERATE", "JOIN", "JSON_TABLE", "KEY", "KEYS", "KILL", "LAG", "LAST_VALUE", "LATERAL", "LEAD", "LEADING",
        "LEAVE", "LEFT", "LIKE", "LIMIT", "LINEAR", "LINES", "LOAD", "LOCALTIME", "LOCALTIMESTAMP", "LOCK", "LONG",
        "LONGBLOB", "LONGTEXT", "LOOP", "LOW_PRIORITY", "MANUAL", "MASTER_BIND", "MASTER_SSL_VERIFY_SERVER_CERT", "MATCH",
        "MAXVALUE", "MEDIUMBLOB", "MEDIUMINT", "MEDIUMTEXT", "MIDDLEINT", "MINUTE_MICROSECOND", "MINUTE_SECOND", "MOD",
        "MODIFIES", "NATURAL", "NOT", "NO_WRITE_TO_BINLOG", "NTH_VALUE", "NTILE", "NULL", "NUMERIC", "OF", "ON",
        "OPTIMIZE", "OPTIMIZER_COSTS", "OPTION", "OPTIONALLY", "OR", "ORDER", "OUT", "OUTER", "OUTFILE", "OVER",
        "PARALLEL", "PARTITION", "PERCENT_RANK", "PRECISION", "PRIMARY", "PROCEDURE", "PURGE", "QUALIFY", "RANGE",
        "RANK", "READ", "READS", "READ_WRITE", "REAL", "RECURSIVE", "REFERENCES", "REGEXP", "RELEASE", "RENAME",
        "REPEAT", "REPLACE", "REQUIRE", "RESIGNAL", "RESTRICT", "RETURN", "REVOKE", "RIGHT", "RLIKE", "ROW", "ROWS",
        "ROW_NUMBER", "SCHEMA", "SCHEMAS", "SECOND_MICROSECOND", "SELECT", "SENSITIVE", "SEPARATOR", "SET", "SHOW",
        "SIGNAL", "SMALLINT", "SPATIAL", "SPECIFIC", "SQL", "SQLEXCEPTION", "SQLSTATE", "SQLWARNING", "SQL_BIG_RESULT",
        "SQL_CALC_FOUND_ROWS", "SQL_SMALL_RESULT", "SSL", "STARTING", "STORED", "STRAIGHT_JOIN", "SYSTEM", "TABLE",
        "TERMINATED", "THEN", "TINYBLOB", "TINYINT", "TINYTEXT", "TO", "TRAILING", "TRIGGER", "TRUE", "UNDO", "UNION",
        "UNIQUE", "UNLOCK", "UNSIGNED", "UPDATE", "USAGE", "USE", "USING", "UTC_DATE", "UTC_TIME", "UTC_TIMESTAMP",
        "VALUES", "VARBINARY", "VARCHAR", "VARCHARACTER", "VARYING", "VIRTUAL", "WHEN", "WHERE", "WHILE", "WINDOW",
        "WITH", "WRITE", "XOR", "YEAR_MONTH", "ZEROFILL",

        // Frequent non-reserved keywords: DDL, data types, routines, transactions, events and table options.
        "AFTER", "ALGORITHM", "AUTO_INCREMENT", "AVG_ROW_LENGTH", "BEGIN", "BIT", "BOOL", "BOOLEAN", "BTREE", "CHARSET",
        "CHECKSUM", "CLOSE", "COLUMNS", "COMMENT", "COMMIT", "COMMITTED", "COMPACT", "COMPRESSED", "CONTAINS", "DATA",
        "DATE", "DATETIME", "DEALLOCATE", "DEFINER", "DELIMITER", "DISABLE", "DO", "DYNAMIC", "ENABLE", "END", "ENGINE",
        "ENGINES", "ENUM", "EVENT", "EVENTS", "EVERY", "EXECUTE", "FIELDS", "FIXED", "FLUSH", "FULL", "GEOMETRY",
        "GLOBAL", "HANDLER", "HASH", "INVOKER", "ISOLATION", "JSON", "LANGUAGE", "LEVEL", "LOCAL", "MERGE", "MODIFY",
        "NAMES", "NEXT", "NO", "NONE", "OPEN", "PASSWORD", "PREPARE", "PROCESSLIST", "QUICK", "REDUNDANT", "REPEATABLE",
        "RETURNS", "ROLE", "ROLLBACK", "ROW_FORMAT", "SAVEPOINT", "SCHEDULE", "SECURITY", "SERIAL", "SERIALIZABLE",
        "SESSION", "SHARE", "SIGNED", "SQL_NO_CACHE", "START", "STARTS", "STATUS", "TABLES", "TEMPORARY", "TEMPTABLE",
        "TEXT", "TIME", "TIMESTAMP", "TRANSACTION", "TRUNCATE", "TYPE", "UNCOMMITTED", "UNDEFINED", "UNKNOWN", "USER",
        "VARIABLES", "VIEW", "WARNINGS", "WORK", "YEAR", "COMPLETION", "ENDS", "PRESERVE", "AT", "WITHOUT", "VALIDATION",
        "ACTION", "MEMORY", "MYISAM", "INNODB", "PERSIST", "PERSIST_ONLY", "INSTANT", "INPLACE", "COPY", "SHARED",
        "EXCLUSIVE", "VISIBLE", "INVISIBLE", "SRID", "HISTOGRAM", "BUCKETS", "SKIP", "LOCKED", "NOWAIT", "OFFSET",
        "DUPLICATE", "SQL_SECURITY",
    };

    /// <summary>Returns true when <paramref name="word"/> is colored as a keyword (case-insensitive).</summary>
    public static bool IsKeyword(string word) => !string.IsNullOrEmpty(word) && _keywords.Contains(word);

    /// <summary>Gets all keywords, for the completion list (US-1401).</summary>
    public static IEnumerable<string> All => _keywords;
}
