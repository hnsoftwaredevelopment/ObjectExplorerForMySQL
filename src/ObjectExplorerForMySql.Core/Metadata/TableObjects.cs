using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ObjectExplorerForMySql.Core.Metadata;

/// <summary>Kind of a key.</summary>
public enum KeyType
{
    /// <summary>Primary key.</summary>
    Primary,

    /// <summary>Unique key.</summary>
    Unique,

    /// <summary>Foreign key.</summary>
    Foreign,
}

/// <summary>Primary, unique or foreign key of a table.</summary>
public sealed class KeyInfo
{
    /// <summary>Initializes a new instance of the <see cref="KeyInfo"/> class.</summary>
    public KeyInfo(string name, KeyType type, IReadOnlyList<string> columns, string? referencedSchema = null, string? referencedTable = null, IReadOnlyList<string>? referencedColumns = null)
    {
        Name = name;
        Type = type;
        Columns = columns;
        ReferencedSchema = referencedSchema;
        ReferencedTable = referencedTable;
        ReferencedColumns = referencedColumns ?? Array.Empty<string>();
    }

    /// <summary>Gets the constraint name (always "PRIMARY" for a primary key).</summary>
    public string Name { get; }

    /// <summary>Gets the key type.</summary>
    public KeyType Type { get; }

    /// <summary>Gets the key columns in order.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Gets the schema of the referenced table (foreign keys only).</summary>
    public string? ReferencedSchema { get; }

    /// <summary>Gets the referenced table (foreign keys only).</summary>
    public string? ReferencedTable { get; }

    /// <summary>Gets the referenced columns in order (foreign keys only).</summary>
    public IReadOnlyList<string> ReferencedColumns { get; }

    /// <summary>Gets the ON UPDATE rule of a foreign key, for example CASCADE; null for other keys (US-0402).</summary>
    public string? UpdateRule { get; init; }

    /// <summary>Gets the ON DELETE rule of a foreign key, for example RESTRICT; null for other keys.</summary>
    public string? DeleteRule { get; init; }

    /// <summary>
    /// Gets the label for a key of a table in <paramref name="tableSchema"/>: <c>PRIMARY (id)</c>,
    /// <c>uq_email (email)</c> or <c>fk_order_customer (customer_id) → customers (id)</c>.
    /// The referenced table gets a schema prefix only when it is in another schema.
    /// </summary>
    public string LabelFor(string tableSchema)
    {
        var label = $"{Name} ({string.Join(", ", Columns)})";
        if (Type != KeyType.Foreign || ReferencedTable is null)
        {
            return label;
        }

        var target = ReferencedSchema is null || string.Equals(ReferencedSchema, tableSchema, StringComparison.OrdinalIgnoreCase)
            ? ReferencedTable
            : $"{ReferencedSchema}.{ReferencedTable}";
        return $"{label} → {target} ({string.Join(", ", ReferencedColumns)})";
    }

    /// <summary>One row of the keys query: one column of one key.</summary>
    internal sealed record Row(string Name, string ConstraintType, string Column, int Position, string? ReferencedSchema, string? ReferencedTable, string? ReferencedColumn, string? UpdateRule = null, string? DeleteRule = null);

    /// <summary>Groups query rows into keys: primary key first, then unique keys, then foreign keys, each by name.</summary>
    internal static IReadOnlyList<KeyInfo> FromRows(IEnumerable<Row> rows) =>
        rows.GroupBy(r => (r.Name, r.ConstraintType))
            .Select(g =>
            {
                var ordered = g.OrderBy(r => r.Position).ToList();
                var type = g.Key.ConstraintType.ToUpperInvariant() switch
                {
                    "PRIMARY KEY" => KeyType.Primary,
                    "UNIQUE" => KeyType.Unique,
                    _ => KeyType.Foreign,
                };
                return new KeyInfo(
                    g.Key.Name,
                    type,
                    ordered.Select(r => r.Column).ToList(),
                    ordered[0].ReferencedSchema,
                    ordered[0].ReferencedTable,
                    type == KeyType.Foreign ? ordered.Select(r => r.ReferencedColumn ?? string.Empty).ToList() : null)
                {
                    UpdateRule = type == KeyType.Foreign ? ordered[0].UpdateRule : null,
                    DeleteRule = type == KeyType.Foreign ? ordered[0].DeleteRule : null,
                };
            })
            .OrderBy(k => k.Type)
            .ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Check constraint of a table (MySQL 8.0.16+, MariaDB 10.2+).</summary>
public sealed class CheckConstraintInfo
{
    /// <summary>Longest clause shown in the label.</summary>
    public const int MaximumClauseLength = 60;

    /// <summary>Initializes a new instance of the <see cref="CheckConstraintInfo"/> class.</summary>
    public CheckConstraintInfo(string name, string clause, bool enforced)
    {
        Name = name;
        Clause = clause ?? string.Empty;
        Enforced = enforced;
    }

    /// <summary>Gets the constraint name.</summary>
    public string Name { get; }

    /// <summary>Gets the check expression.</summary>
    public string Clause { get; }

    /// <summary>Gets a value indicating whether the server enforces the constraint.</summary>
    public bool Enforced { get; }

    /// <summary>Gets the label: <c>chk_price (`price` &gt;= 0)</c>, with ", not enforced" when disabled.</summary>
    public string Label
    {
        get
        {
            var clause = Clause.Trim();
            if (clause.Length > MaximumClauseLength)
            {
                clause = clause.Substring(0, MaximumClauseLength - 3) + "...";
            }

            return Enforced ? $"{Name} ({clause})" : $"{Name} ({clause}, not enforced)";
        }
    }
}

/// <summary>Index of a table.</summary>
public sealed class IndexInfo
{
    /// <summary>Initializes a new instance of the <see cref="IndexInfo"/> class.</summary>
    public IndexInfo(string name, bool isUnique, string indexType, IReadOnlyList<string> columns)
    {
        Name = name;
        IsUnique = isUnique;
        IndexType = indexType ?? string.Empty;
        Columns = columns;
    }

    /// <summary>Gets the index name ("PRIMARY" for the primary key).</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the index is unique.</summary>
    public bool IsUnique { get; }

    /// <summary>Gets the index type: BTREE, HASH, FULLTEXT or SPATIAL.</summary>
    public string IndexType { get; }

    /// <summary>Gets the indexed columns in order; a prefix length is shown as <c>name(10)</c>, an expression as <c>(expression)</c>.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Gets a value indicating whether the optimizer uses the index (MySQL: visible; MariaDB: not ignored).</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>Gets the index comment, or null.</summary>
    public string? Comment { get; init; }

    /// <summary>Gets a value indicating whether this is the primary key index.</summary>
    public bool IsPrimary => string.Equals(Name, "PRIMARY", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets a value indicating whether this is a FULLTEXT index.</summary>
    public bool IsFullText => string.Equals(IndexType, "FULLTEXT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the label: <c>ix_date (non-unique: order_date, customer_id)</c>, <c>ft_body (fulltext: body)</c>.</summary>
    public string Label
    {
        get
        {
            var kind = IndexType.ToUpperInvariant() switch
            {
                "FULLTEXT" => "fulltext",
                "SPATIAL" => "spatial",
                _ => IsUnique ? "unique" : "non-unique",
            };
            return $"{Name} ({kind}: {string.Join(", ", Columns)})";
        }
    }

    /// <summary>One row of the index query: one column of one index.</summary>
    internal sealed record Row(string Name, bool NonUnique, string? Column, int Position, string IndexType, long? SubPart, bool IsVisible = true, string? Comment = null);

    /// <summary>Groups query rows into indexes: PRIMARY first, then by name.</summary>
    internal static IReadOnlyList<IndexInfo> FromRows(IEnumerable<Row> rows) =>
        rows.GroupBy(r => r.Name)
            .Select(g =>
            {
                var ordered = g.OrderBy(r => r.Position).ToList();
                var columns = ordered
                    .Select(r => r.Column is null
                        ? "(expression)"
                        : r.SubPart is { } length ? string.Format(CultureInfo.InvariantCulture, "{0}({1})", r.Column, length) : r.Column)
                    .ToList();
                return new IndexInfo(g.Key, !ordered[0].NonUnique, ordered[0].IndexType, columns)
                {
                    IsVisible = ordered[0].IsVisible,
                    Comment = string.IsNullOrEmpty(ordered[0].Comment) ? null : ordered[0].Comment,
                };
            })
            .OrderBy(i => i.IsPrimary ? 0 : 1)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Trigger on a table.</summary>
public sealed class TriggerInfo
{
    /// <summary>Initializes a new instance of the <see cref="TriggerInfo"/> class.</summary>
    public TriggerInfo(string name, string timing, string manipulation)
    {
        Name = name;
        Timing = timing ?? string.Empty;
        Manipulation = manipulation ?? string.Empty;
    }

    /// <summary>Gets the trigger name.</summary>
    public string Name { get; }

    /// <summary>Gets BEFORE or AFTER.</summary>
    public string Timing { get; }

    /// <summary>Gets INSERT, UPDATE or DELETE.</summary>
    public string Manipulation { get; }

    /// <summary>Gets the definer, for example root@localhost (US-0402).</summary>
    public string? Definer { get; init; }

    /// <summary>Gets the creation time.</summary>
    public DateTime? Created { get; init; }

    /// <summary>Gets the order among triggers with the same timing and event (1-based), or 0 when unknown.</summary>
    public int ActionOrder { get; init; }

    /// <summary>Gets the label: <c>trg_orders_bi (BEFORE INSERT)</c>.</summary>
    public string Label => $"{Name} ({Timing.ToUpperInvariant()} {Manipulation.ToUpperInvariant()})";
}

/// <summary>Features that depend on the server type and version (ADR-008: detected at runtime).</summary>
public sealed class ServerCapabilities
{
    private ServerCapabilities(bool isMariaDb, Version version)
    {
        IsMariaDb = isMariaDb;
        Version = version;
    }

    /// <summary>Gets a value indicating whether the server is MariaDB.</summary>
    public bool IsMariaDb { get; }

    /// <summary>Gets the numeric server version.</summary>
    public Version Version { get; }

    /// <summary>Gets a value indicating whether the server knows CHECK constraints: MySQL 8.0.16+ or MariaDB 10.2+.</summary>
    public bool SupportsCheckConstraints => IsMariaDb ? Version >= new Version(10, 2) : Version >= new Version(8, 0, 16);

    /// <summary>Gets a value indicating whether the server knows <c>ALTER TABLE ... RENAME COLUMN</c>: MySQL 8.0+ or MariaDB 10.5.2+.</summary>
    public bool SupportsRenameColumn => IsMariaDb ? Version >= new Version(10, 5, 2) : Version >= new Version(8, 0);

    /// <summary>Parses a version string such as "8.0.36", "8.4.2-log" or "10.11.6-MariaDB-0ubuntu".</summary>
    public static ServerCapabilities FromVersion(string? serverVersion)
    {
        var text = (serverVersion ?? string.Empty).Trim();
        var end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        var numeric = text.Substring(0, end).TrimEnd('.');
        if (!System.Version.TryParse(numeric.IndexOf('.') >= 0 ? numeric : numeric + ".0", out var version))
        {
            version = new Version(0, 0);
        }

        return new ServerCapabilities(text.IndexOf("MariaDB", StringComparison.OrdinalIgnoreCase) >= 0, version);
    }
}
