using System;
using System.Collections.Generic;
using System.Globalization;

namespace ObjectExplorerForMySql.Core.Metadata;

/// <summary>One base table of a database.</summary>
public sealed class TableInfo
{
    /// <summary>Initializes a new instance of the <see cref="TableInfo"/> class.</summary>
    public TableInfo(string database, string name, string? engine, string? comment, bool isView = false)
    {
        IsView = isView;
        Database = database;
        Name = name;
        Engine = engine;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment;
    }

    /// <summary>Gets the database (schema) of the table.</summary>
    public string Database { get; }

    /// <summary>Gets the table name.</summary>
    public string Name { get; }

    /// <summary>Gets the storage engine, for example InnoDB.</summary>
    public string? Engine { get; }

    /// <summary>Gets the table comment, or null.</summary>
    public string? Comment { get; }

    /// <summary>Gets a value indicating whether this is a view (including the system views of information_schema).</summary>
    public bool IsView { get; }

    /// <summary>Gets the row format, for example Dynamic (tables, US-0402).</summary>
    public string? RowFormat { get; init; }

    /// <summary>Gets the estimated number of rows (InnoDB: an estimate; MySQL caches it, see information_schema_stats_expiry).</summary>
    public long? EstimatedRows { get; init; }

    /// <summary>Gets the size of the data in bytes.</summary>
    public long? DataLength { get; init; }

    /// <summary>Gets the size of the indexes in bytes.</summary>
    public long? IndexLength { get; init; }

    /// <summary>Gets the next AUTO_INCREMENT value, or null.</summary>
    public long? AutoIncrement { get; init; }

    /// <summary>Gets the creation time.</summary>
    public DateTime? Created { get; init; }

    /// <summary>Gets the time of the last data change, or null (InnoDB keeps it in memory only).</summary>
    public DateTime? Updated { get; init; }

    /// <summary>Gets the default collation of the table.</summary>
    public string? Collation { get; init; }

    /// <summary>Gets a value indicating whether the view is updatable (views; null for system views).</summary>
    public bool? IsUpdatable { get; init; }

    /// <summary>Gets the security type of the view: DEFINER or INVOKER.</summary>
    public string? SecurityType { get; init; }

    /// <summary>Gets the definer of the view, for example root@localhost.</summary>
    public string? Definer { get; init; }

    /// <summary>Gets the check option of the view: NONE, LOCAL or CASCADED.</summary>
    public string? CheckOption { get; init; }
}

/// <summary>One column of a table or view.</summary>
public sealed class ColumnInfo
{
    /// <summary>Initializes a new instance of the <see cref="ColumnInfo"/> class.</summary>
    public ColumnInfo(string name, string columnType, bool isNullable, bool isPrimaryKey, bool isForeignKey, string extra)
    {
        Name = name;
        ColumnType = columnType;
        IsNullable = isNullable;
        IsPrimaryKey = isPrimaryKey;
        IsForeignKey = isForeignKey;
        Extra = extra ?? string.Empty;
    }

    /// <summary>Gets the column name.</summary>
    public string Name { get; }

    /// <summary>Gets the full MySQL type, for example <c>varchar(128)</c> or <c>int unsigned</c>.</summary>
    public string ColumnType { get; }

    /// <summary>Gets a value indicating whether the column accepts NULL.</summary>
    public bool IsNullable { get; }

    /// <summary>Gets a value indicating whether the column is part of the primary key.</summary>
    public bool IsPrimaryKey { get; }

    /// <summary>Gets a value indicating whether the column is part of a foreign key.</summary>
    public bool IsForeignKey { get; }

    /// <summary>Gets the EXTRA value, for example <c>auto_increment</c> or <c>VIRTUAL GENERATED</c>.</summary>
    public string Extra { get; }

    /// <summary>
    /// Gets a value indicating whether the column is generated (virtual or stored). EXTRA is <c>VIRTUAL GENERATED</c> or
    /// <c>STORED GENERATED</c> (MariaDB also <c>PERSISTENT GENERATED</c>). <c>DEFAULT_GENERATED</c> only means the
    /// default is an expression such as CURRENT_TIMESTAMP; such a column is not generated.
    /// </summary>
    public bool IsGenerated =>
        Extra.IndexOf("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase) >= 0
        || Extra.IndexOf("STORED GENERATED", StringComparison.OrdinalIgnoreCase) >= 0
        || Extra.IndexOf("PERSISTENT GENERATED", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Gets a value indicating whether the column has AUTO_INCREMENT.</summary>
    public bool IsAutoIncrement => Extra.IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Gets a value indicating whether the numeric type is UNSIGNED.</summary>
    public bool IsUnsigned => ColumnType.IndexOf("unsigned", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Gets the base type without length or options, for example <c>varchar</c> (US-0401).</summary>
    public string? DataType { get; init; }

    /// <summary>Gets the default value as INFORMATION_SCHEMA reports it, or null when there is none (or it is NULL).</summary>
    public string? Default { get; init; }

    /// <summary>Gets the maximum length in characters of a character type, or null.</summary>
    public long? CharacterMaximumLength { get; init; }

    /// <summary>Gets the precision of a numeric type, or null.</summary>
    public long? NumericPrecision { get; init; }

    /// <summary>Gets the scale of a numeric type, or null.</summary>
    public long? NumericScale { get; init; }

    /// <summary>Gets the character set of a character type, or null.</summary>
    public string? CharacterSet { get; init; }

    /// <summary>Gets the collation of a character type, or null.</summary>
    public string? Collation { get; init; }

    /// <summary>Gets the column comment, or null.</summary>
    public string? Comment { get; init; }

    /// <summary>Gets the 1-based position of the column in the table, or 0 when unknown.</summary>
    public int OrdinalPosition { get; init; }

    /// <summary>Gets the expression of a generated column, or null.</summary>
    public string? GenerationExpression { get; init; }

    /// <summary>
    /// Gets the tree label in SQL Server Object Explorer style:
    /// <c>name (PK, FK, type, not null)</c>, for example <c>id (PK, int unsigned, not null)</c>
    /// or <c>comment (text, null)</c>.
    /// </summary>
    public string Label => ColumnLabelFormatter.Format(this);
}

/// <summary>Builds the tree label of a column.</summary>
public static class ColumnLabelFormatter
{
    /// <summary>Longest type text shown in the label; longer types (large ENUM or SET lists) are shortened.</summary>
    public const int MaximumTypeLength = 60;

    /// <summary>Formats <paramref name="column"/>; see <see cref="ColumnInfo.Label"/>.</summary>
    public static string Format(ColumnInfo column)
    {
        if (column is null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        var parts = new List<string>();
        if (column.IsPrimaryKey)
        {
            parts.Add("PK");
        }

        if (column.IsForeignKey)
        {
            parts.Add("FK");
        }

        parts.Add(ShortenType(column.ColumnType));
        parts.Add(column.IsNullable ? "null" : "not null");

        return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", column.Name, string.Join(", ", parts));
    }

    private static string ShortenType(string columnType)
    {
        var type = (columnType ?? string.Empty).Trim();
        return type.Length <= MaximumTypeLength ? type : type.Substring(0, MaximumTypeLength - 3) + "...";
    }
}
