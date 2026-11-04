using System;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.ObjectProperties;

/// <summary>Properties of a server (US-0402). Reads the node live, so a new connection shows its values on the next update.</summary>
public sealed class ServerProperties : ServerObjectPropertiesBase
{
    private readonly ServerNodeViewModel _server;

    /// <summary>Initializes a new instance of the <see cref="ServerProperties"/> class.</summary>
    public ServerProperties(ServerNodeViewModel server)
        : base((server ?? throw new ArgumentNullException(nameof(server))).Definition)
    {
        _server = server;
    }

    /// <summary>Gets the server version; empty when not connected.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropVersion")]
    public string Version => _server.Info?.Version ?? string.Empty;

    /// <summary>Gets the server character set.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCharacterSet")]
    public string CharacterSet => _server.Info?.CharacterSet ?? string.Empty;

    /// <summary>Gets the server collation.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCollation")]
    public string Collation => _server.Info?.Collation ?? string.Empty;

    /// <summary>Gets the time zone of the session.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropTimeZone")]
    public string TimeZone => _server.Info?.TimeZone ?? string.Empty;

    /// <summary>Gets the connection state.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropState")]
    public string State => PropertyTexts.Get("State" + _server.State);

    /// <summary>Gets the account the server authenticated, for example app@%.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropAuthenticatedAs")]
    public string AuthenticatedAs => _server.Info?.CurrentUser ?? string.Empty;

    /// <summary>Gets the configured SSL mode.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropSslMode")]
    public string SslMode => Connection?.SslMode.ToString() ?? string.Empty;

    /// <summary>Gets a value indicating whether the current connection uses SSL/TLS; empty when not connected.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropSslInUse")]
    public bool? SslInUse => _server.Info?.IsSslInUse;

    /// <summary>Gets the SSL cipher of the current connection.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropSslCipher")]
    public string SslCipher => _server.Info?.SslCipher ?? string.Empty;

    /// <summary>Gets the configured default database.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropDefaultDatabase")]
    public string DefaultDatabase => Connection?.DefaultDatabase ?? string.Empty;

    /// <summary>Gets a value indicating whether the account may read users (US-1001); empty when not connected.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropCanReadUsers")]
    public bool? CanReadUsers => _server.Privileges?.CanReadUsers;

    /// <summary>Gets a value indicating whether the account may create and drop users (US-1001); empty when not connected.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropCanManageUsers")]
    public bool? CanManageUsers => _server.Privileges?.CanManageUsers;

    /// <inheritdoc />
    protected override string TypeKey => "TypeServer";

    /// <inheritdoc />
    public override string GetObjectName() => _server.Label;
}

/// <summary>Properties of a database (US-0402).</summary>
public sealed class DatabaseProperties : ServerObjectPropertiesBase
{
    private readonly DatabaseInfo _database;

    /// <summary>Initializes a new instance of the <see cref="DatabaseProperties"/> class.</summary>
    /// <param name="database">The database.</param>
    /// <param name="tableCount">Number of tables when the Tables folder is loaded; otherwise null.</param>
    /// <param name="connection">The server connection, or null.</param>
    public DatabaseProperties(DatabaseInfo database, int? tableCount, ConnectionDefinition? connection)
        : base(connection)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        Tables = tableCount;
    }

    /// <summary>Gets the default character set.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCharacterSet")]
    public string CharacterSet => _database.CharacterSet ?? string.Empty;

    /// <summary>Gets the default collation.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCollation")]
    public string Collation => _database.Collation ?? string.Empty;

    /// <summary>Gets the number of tables; empty until the Tables folder was expanded.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropTableCount")]
    public int? Tables { get; }

    /// <summary>Gets a value indicating whether this is one of the server's own databases.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropSystemDatabase")]
    public bool SystemDatabase => _database.IsSystem;

    /// <summary>Gets the database name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name => _database.Name;

    /// <inheritdoc />
    protected override string TypeKey => "TypeDatabase";

    /// <inheritdoc />
    public override string GetObjectName() => _database.Name;
}

/// <summary>Properties of a base table (US-0402).</summary>
public sealed class TableProperties : ServerObjectPropertiesBase
{
    private readonly TableInfo _table;

    /// <summary>Initializes a new instance of the <see cref="TableProperties"/> class.</summary>
    public TableProperties(TableInfo table, ConnectionDefinition? connection)
        : base(connection)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    /// <summary>Gets the storage engine.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropEngine")]
    public string Engine => _table.Engine ?? string.Empty;

    /// <summary>Gets the row format.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropRowFormat")]
    public string RowFormat => _table.RowFormat ?? string.Empty;

    /// <summary>Gets the default collation.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCollation")]
    public string Collation => _table.Collation ?? string.Empty;

    /// <summary>Gets the next AUTO_INCREMENT value.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropAutoIncrementValue")]
    public long? AutoIncrement => _table.AutoIncrement;

    /// <summary>Gets the estimated number of rows.</summary>
    [LocalizedCategory(PropertyCategories.Storage)]
    [LocalizedDisplayName("PropEstimatedRows")]
    public long? EstimatedRows => _table.EstimatedRows;

    /// <summary>Gets the data size.</summary>
    [LocalizedCategory(PropertyCategories.Storage)]
    [LocalizedDisplayName("PropDataLength")]
    public string DataLength => PropertyFormat.Bytes(_table.DataLength);

    /// <summary>Gets the index size.</summary>
    [LocalizedCategory(PropertyCategories.Storage)]
    [LocalizedDisplayName("PropIndexLength")]
    public string IndexLength => PropertyFormat.Bytes(_table.IndexLength);

    /// <summary>Gets the creation time.</summary>
    [LocalizedCategory(PropertyCategories.Storage)]
    [LocalizedDisplayName("PropCreated")]
    public DateTime? Created => _table.Created;

    /// <summary>Gets the time of the last data change.</summary>
    [LocalizedCategory(PropertyCategories.Storage)]
    [LocalizedDisplayName("PropUpdated")]
    public DateTime? Updated => _table.Updated;

    /// <summary>Gets the table name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name => _table.Name;

    /// <summary>Gets the database.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropDatabase")]
    public string Database => _table.Database;

    /// <summary>Gets the table comment.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropComment")]
    public string Comment => _table.Comment ?? string.Empty;

    /// <inheritdoc />
    protected override string TypeKey => "TypeTable";

    /// <inheritdoc />
    public override string GetObjectName() => _table.Name;
}

/// <summary>Properties of a view (US-0402). System views have no view definition; those values stay empty.</summary>
public sealed class ViewProperties : ServerObjectPropertiesBase
{
    private readonly TableInfo _view;

    /// <summary>Initializes a new instance of the <see cref="ViewProperties"/> class.</summary>
    public ViewProperties(TableInfo view, ConnectionDefinition? connection)
        : base(connection)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    /// <summary>Gets a value indicating whether the view is updatable.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropIsUpdatable")]
    public bool? IsUpdatable => _view.IsUpdatable;

    /// <summary>Gets the security type: DEFINER or INVOKER.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropSecurityType")]
    public string SecurityType => _view.SecurityType ?? string.Empty;

    /// <summary>Gets the definer.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropDefiner")]
    public string Definer => _view.Definer ?? string.Empty;

    /// <summary>Gets the check option: NONE, LOCAL or CASCADED.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCheckOption")]
    public string CheckOption => _view.CheckOption ?? string.Empty;

    /// <summary>Gets the view name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name => _view.Name;

    /// <summary>Gets the database.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropDatabase")]
    public string Database => _view.Database;

    /// <inheritdoc />
    protected override string TypeKey => "TypeView";

    /// <inheritdoc />
    public override string GetObjectName() => _view.Name;
}

/// <summary>Base class for objects of a table (index, key, constraint, trigger): Name, Table and Database.</summary>
public abstract class TableObjectPropertiesBase : ServerObjectPropertiesBase
{
    /// <summary>Initializes a new instance of the <see cref="TableObjectPropertiesBase"/> class.</summary>
    protected TableObjectPropertiesBase(string name, TableInfo table, ConnectionDefinition? connection)
        : base(connection)
    {
        Name = name;
        TableInfo = table ?? throw new ArgumentNullException(nameof(table));
    }

    /// <summary>Gets the object name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name { get; }

    /// <summary>Gets the table.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropTable")]
    public string Table => TableInfo.Name;

    /// <summary>Gets the database.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropDatabase")]
    public string Database => TableInfo.Database;

    /// <summary>Gets the table metadata.</summary>
    protected TableInfo TableInfo { get; }

    /// <inheritdoc />
    public override string GetObjectName() => Name;
}

/// <summary>Properties of an index (US-0402).</summary>
public sealed class IndexProperties : TableObjectPropertiesBase
{
    private readonly IndexInfo _index;

    /// <summary>Initializes a new instance of the <see cref="IndexProperties"/> class.</summary>
    public IndexProperties(IndexInfo index, TableInfo table, ConnectionDefinition? connection)
        : base((index ?? throw new ArgumentNullException(nameof(index))).Name, table, connection)
    {
        _index = index;
    }

    /// <summary>Gets a value indicating whether the index is unique.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropUnique")]
    public bool Unique => _index.IsUnique;

    /// <summary>Gets the index type: BTREE, HASH, FULLTEXT or SPATIAL.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropIndexType")]
    public string IndexType => _index.IndexType;

    /// <summary>Gets a value indicating whether the optimizer uses the index.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropVisible")]
    public bool Visible => _index.IsVisible;

    /// <summary>Gets the indexed columns in order.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropColumns")]
    public string Columns => PropertyFormat.List(_index.Columns);

    /// <summary>Gets a value indicating whether this is the primary key index.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropPrimaryKey")]
    public bool PrimaryKey => _index.IsPrimary;

    /// <summary>Gets the index comment.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropComment")]
    public string Comment => _index.Comment ?? string.Empty;

    /// <inheritdoc />
    protected override string TypeKey => "TypeIndex";
}

/// <summary>Properties of a primary, unique or foreign key (US-0402).</summary>
public sealed class KeyProperties : TableObjectPropertiesBase
{
    private readonly KeyInfo _key;

    /// <summary>Initializes a new instance of the <see cref="KeyProperties"/> class.</summary>
    public KeyProperties(KeyInfo key, TableInfo table, ConnectionDefinition? connection)
        : base((key ?? throw new ArgumentNullException(nameof(key))).Name, table, connection)
    {
        _key = key;
    }

    /// <summary>Gets the key type.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropKeyType")]
    public string KeyType => PropertyTexts.Get(TypeKey);

    /// <summary>Gets the key columns in order.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropColumns")]
    public string Columns => PropertyFormat.List(_key.Columns);

    /// <summary>Gets the referenced table; with schema when it is in another database.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropReferencedTable")]
    public string ReferencedTable => _key.ReferencedTable is null
        ? string.Empty
        : _key.ReferencedSchema is null || string.Equals(_key.ReferencedSchema, TableInfo.Database, StringComparison.OrdinalIgnoreCase)
            ? _key.ReferencedTable
            : $"{_key.ReferencedSchema}.{_key.ReferencedTable}";

    /// <summary>Gets the referenced columns in order.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropReferencedColumns")]
    public string ReferencedColumns => PropertyFormat.List(_key.ReferencedColumns);

    /// <summary>Gets the ON UPDATE rule.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropOnUpdate")]
    public string OnUpdate => _key.UpdateRule ?? string.Empty;

    /// <summary>Gets the ON DELETE rule.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropOnDelete")]
    public string OnDelete => _key.DeleteRule ?? string.Empty;

    /// <inheritdoc />
    protected override string TypeKey => _key.Type switch
    {
        Metadata.KeyType.Primary => "TypePrimaryKey",
        Metadata.KeyType.Unique => "TypeUniqueKey",
        _ => "TypeForeignKey",
    };
}

/// <summary>Properties of a check constraint (US-0402).</summary>
public sealed class CheckConstraintProperties : TableObjectPropertiesBase
{
    private readonly CheckConstraintInfo _constraint;

    /// <summary>Initializes a new instance of the <see cref="CheckConstraintProperties"/> class.</summary>
    public CheckConstraintProperties(CheckConstraintInfo constraint, TableInfo table, ConnectionDefinition? connection)
        : base((constraint ?? throw new ArgumentNullException(nameof(constraint))).Name, table, connection)
    {
        _constraint = constraint;
    }

    /// <summary>Gets the check expression.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropExpression")]
    public string Expression => _constraint.Clause;

    /// <summary>Gets a value indicating whether the server enforces the constraint.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropEnforced")]
    public bool Enforced => _constraint.Enforced;

    /// <inheritdoc />
    protected override string TypeKey => "TypeCheckConstraint";
}

/// <summary>Properties of a trigger (US-0402).</summary>
public sealed class TriggerProperties : TableObjectPropertiesBase
{
    private readonly TriggerInfo _trigger;

    /// <summary>Initializes a new instance of the <see cref="TriggerProperties"/> class.</summary>
    public TriggerProperties(TriggerInfo trigger, TableInfo table, ConnectionDefinition? connection)
        : base((trigger ?? throw new ArgumentNullException(nameof(trigger))).Name, table, connection)
    {
        _trigger = trigger;
    }

    /// <summary>Gets BEFORE or AFTER.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropTiming")]
    public string Timing => _trigger.Timing.ToUpperInvariant();

    /// <summary>Gets INSERT, UPDATE or DELETE.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropEvent")]
    public string Event => _trigger.Manipulation.ToUpperInvariant();

    /// <summary>Gets the order among triggers with the same timing and event.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropActionOrder")]
    public int? ActionOrder => _trigger.ActionOrder > 0 ? _trigger.ActionOrder : null;

    /// <summary>Gets the definer.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropDefiner")]
    public string Definer => _trigger.Definer ?? string.Empty;

    /// <summary>Gets the creation time.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCreated")]
    public DateTime? Created => _trigger.Created;

    /// <inheritdoc />
    protected override string TypeKey => "TypeTrigger";
}
