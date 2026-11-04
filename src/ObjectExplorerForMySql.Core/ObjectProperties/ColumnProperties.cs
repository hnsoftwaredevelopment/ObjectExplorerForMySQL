using System;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.ObjectProperties;

/// <summary>Properties of a table or view column (US-0401).</summary>
public sealed class ColumnProperties : ServerObjectPropertiesBase
{
    private readonly ColumnInfo _column;
    private readonly TableInfo _table;

    /// <summary>Initializes a new instance of the <see cref="ColumnProperties"/> class.</summary>
    /// <param name="column">The column.</param>
    /// <param name="table">The table or view of the column.</param>
    /// <param name="connection">The server connection, or null when unknown.</param>
    public ColumnProperties(ColumnInfo column, TableInfo table, ConnectionDefinition? connection)
        : base(connection)
    {
        _column = column ?? throw new ArgumentNullException(nameof(column));
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    // General

    /// <summary>Gets a value indicating whether the column accepts NULL.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropNullable")]
    public bool Nullable => _column.IsNullable;

    /// <summary>Gets a value indicating whether the column is part of the primary key.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropPrimaryKey")]
    public bool PrimaryKey => _column.IsPrimaryKey;

    /// <summary>Gets a value indicating whether the column is part of a foreign key.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropForeignKey")]
    public bool ForeignKey => _column.IsForeignKey;

    /// <summary>Gets the default value; empty when there is none.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropDefault")]
    public string Default => _column.Default ?? string.Empty;

    /// <summary>Gets a value indicating whether the column has AUTO_INCREMENT.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropAutoIncrement")]
    public bool AutoIncrement => _column.IsAutoIncrement;

    /// <summary>Gets a value indicating whether the column is generated.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropIsGenerated")]
    public bool IsGenerated => _column.IsGenerated;

    /// <summary>Gets the expression of a generated column.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropGenerationExpression")]
    public string GenerationExpression => _column.GenerationExpression ?? string.Empty;

    // Data type

    /// <summary>Gets the full MySQL type, for example <c>decimal(10,2) unsigned</c>.</summary>
    [LocalizedCategory(PropertyCategories.DataType)]
    [LocalizedDisplayName("PropDataType")]
    public string DataType => _column.ColumnType;

    /// <summary>Gets the maximum length in characters; empty for non-character types.</summary>
    [LocalizedCategory(PropertyCategories.DataType)]
    [LocalizedDisplayName("PropLength")]
    public long? Length => _column.CharacterMaximumLength;

    /// <summary>Gets the numeric precision; empty for non-numeric types.</summary>
    [LocalizedCategory(PropertyCategories.DataType)]
    [LocalizedDisplayName("PropPrecision")]
    public long? Precision => _column.NumericPrecision;

    /// <summary>Gets the numeric scale; empty for non-numeric types.</summary>
    [LocalizedCategory(PropertyCategories.DataType)]
    [LocalizedDisplayName("PropScale")]
    public long? Scale => _column.NumericScale;

    /// <summary>Gets a value indicating whether the numeric type is UNSIGNED.</summary>
    [LocalizedCategory(PropertyCategories.DataType)]
    [LocalizedDisplayName("PropUnsigned")]
    public bool Unsigned => _column.IsUnsigned;

    /// <summary>Gets the character set; empty for non-character types.</summary>
    [LocalizedCategory(PropertyCategories.DataType)]
    [LocalizedDisplayName("PropCharacterSet")]
    public string CharacterSet => _column.CharacterSet ?? string.Empty;

    /// <summary>Gets the collation; empty for non-character types.</summary>
    [LocalizedCategory(PropertyCategories.DataType)]
    [LocalizedDisplayName("PropCollation")]
    public string Collation => _column.Collation ?? string.Empty;

    // Description

    /// <summary>Gets the column name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name => _column.Name;

    /// <summary>Gets the table or view the column belongs to.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropParent")]
    public string Parent => _table.Name;

    /// <summary>Gets the type of the parent: Table or View.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropParentType")]
    public string ParentType => PropertyTexts.Get(_table.IsView ? "TypeView" : "TypeTable");

    /// <summary>Gets the database (schema).</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropDatabase")]
    public string Database => _table.Database;

    /// <summary>Gets the column comment.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropComment")]
    public string Comment => _column.Comment ?? string.Empty;

    /// <summary>Gets the 1-based position in the table.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropOrdinalPosition")]
    public int OrdinalPosition => _column.OrdinalPosition;

    /// <inheritdoc />
    protected override string TypeKey => "TypeColumn";

    /// <inheritdoc />
    public override string GetObjectName() => _column.Name;
}
