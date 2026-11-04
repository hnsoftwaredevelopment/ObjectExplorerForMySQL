using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;

namespace ObjectExplorerForMySql.Core.Metadata;

/// <summary>Information about a server, read when connecting.</summary>
public sealed class ServerInfo
{
    /// <summary>Initializes a new instance of the <see cref="ServerInfo"/> class.</summary>
    public ServerInfo(string version, string currentUser)
    {
        Version = version;
        CurrentUser = currentUser;
    }

    /// <summary>Gets the server version, for example "8.0.36".</summary>
    public string Version { get; }

    /// <summary>Gets the account the server authenticated, for example "app@%".</summary>
    public string CurrentUser { get; }

    /// <summary>Gets the server character set (<c>@@character_set_server</c>, US-0402).</summary>
    public string? CharacterSet { get; init; }

    /// <summary>Gets the server collation (<c>@@collation_server</c>).</summary>
    public string? Collation { get; init; }

    /// <summary>Gets the time zone of the session; SYSTEM is replaced by the system time zone, for example "SYSTEM (CET)".</summary>
    public string? TimeZone { get; init; }

    /// <summary>Gets the SSL cipher of the connection, or null when SSL is not used.</summary>
    public string? SslCipher { get; init; }

    /// <summary>Gets a value indicating whether the connection uses SSL/TLS.</summary>
    public bool IsSslInUse => !string.IsNullOrEmpty(SslCipher);

    /// <summary>Formats <c>@@time_zone</c>: "SYSTEM" becomes "SYSTEM (system time zone)".</summary>
    public static string? FormatTimeZone(string? timeZone, string? systemTimeZone) =>
        string.Equals(timeZone, "SYSTEM", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(systemTimeZone)
            ? $"SYSTEM ({systemTimeZone})"
            : timeZone;
}

/// <summary>One database (schema) on a server.</summary>
public sealed class DatabaseInfo
{
    /// <summary>Initializes a new instance of the <see cref="DatabaseInfo"/> class.</summary>
    public DatabaseInfo(string name, string? characterSet, string? collation)
    {
        Name = name;
        CharacterSet = characterSet;
        Collation = collation;
        IsSystem = SystemDatabases.IsSystem(name);
    }

    /// <summary>Gets the database name.</summary>
    public string Name { get; }

    /// <summary>Gets the default character set.</summary>
    public string? CharacterSet { get; }

    /// <summary>Gets the default collation.</summary>
    public string? Collation { get; }

    /// <summary>Gets a value indicating whether this is one of the server's own databases.</summary>
    public bool IsSystem { get; }
}

/// <summary>Names of the databases MySQL and MariaDB create for themselves.</summary>
public static class SystemDatabases
{
    private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        "mysql",
        "information_schema",
        "performance_schema",
        "sys",
    };

    /// <summary>Returns true for mysql, information_schema, performance_schema and sys (any case).</summary>
    public static bool IsSystem(string? name) => name is not null && _names.Contains(name);
}

/// <summary>Reads server metadata (ADR-003). Every call opens and closes its own pooled connection.</summary>
public interface IMetadataProvider
{
    /// <summary>Opens a connection to check the credentials and returns version and account.</summary>
    Task<ServerInfo> ConnectAsync(ConnectionDefinition definition, CancellationToken cancellationToken);

    /// <summary>Returns all databases the account can see, sorted by name.</summary>
    Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(ConnectionDefinition definition, CancellationToken cancellationToken);

    /// <summary>Returns the base tables of <paramref name="database"/>, sorted by name.</summary>
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken);

    /// <summary>Returns the columns of a table or view in ordinal order, with primary and foreign key flags.</summary>
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken);

    /// <summary>Returns the primary, unique and foreign keys of a table.</summary>
    Task<IReadOnlyList<KeyInfo>> GetKeysAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken);

    /// <summary>Returns the check constraints of a table. Only call when <see cref="ServerCapabilities.SupportsCheckConstraints"/> is true.</summary>
    Task<IReadOnlyList<CheckConstraintInfo>> GetCheckConstraintsAsync(ConnectionDefinition definition, string database, string table, bool isMariaDb, CancellationToken cancellationToken);

    /// <summary>Returns the indexes of a table. <paramref name="isMariaDb"/> selects the query for index visibility.</summary>
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ConnectionDefinition definition, string database, string table, bool isMariaDb, CancellationToken cancellationToken);

    /// <summary>Returns the triggers of a table.</summary>
    Task<IReadOnlyList<TriggerInfo>> GetTriggersAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken);

    /// <summary>Returns the views (and system views) of <paramref name="database"/>, sorted by name.</summary>
    Task<IReadOnlyList<TableInfo>> GetViewsAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken);

    /// <summary>Returns the stored procedures or functions of <paramref name="database"/>, sorted by name.</summary>
    Task<IReadOnlyList<RoutineInfo>> GetRoutinesAsync(ConnectionDefinition definition, string database, RoutineType type, CancellationToken cancellationToken);

    /// <summary>Returns the parameters of a routine in order (without the return value of a function).</summary>
    Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(ConnectionDefinition definition, RoutineInfo routine, CancellationToken cancellationToken);

    /// <summary>Returns the events of <paramref name="database"/>, sorted by name.</summary>
    Task<IReadOnlyList<EventInfo>> GetEventsAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the CREATE statement of an object with <c>SHOW CREATE ...</c> (US-0601), or null when the server returns
    /// no source (the account may not see the body of a routine it did not define).
    /// </summary>
    Task<string?> GetCreateStatementAsync(ConnectionDefinition definition, Scripting.CodeObject codeObject, CancellationToken cancellationToken);

    /// <summary>Returns the CREATE DATABASE statement (<c>SHOW CREATE DATABASE IF NOT EXISTS</c>, US-0603), or null.</summary>
    Task<string?> GetCreateDatabaseStatementAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken);

    /// <summary>Returns the foreign key dependencies between the tables of <paramref name="database"/> (US-0603).</summary>
    Task<IReadOnlyList<(string Table, string ReferencedTable)>> GetTableDependenciesAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all rows of a table (US-0603) and calls <paramref name="onRow"/> with the values of <paramref name="columns"/>
    /// per row. Zero dates arrive as <see cref="MySqlDateTime"/>; TIMESTAMP values are read in UTC (time zone +00:00).
    /// </summary>
    Task ReadRowsAsync(ConnectionDefinition definition, string database, string table, IReadOnlyList<string> columns, Action<object?[]> onRow, CancellationToken cancellationToken);

    /// <summary>Closes the pooled connections of <paramref name="definition"/>.</summary>
    void Disconnect(ConnectionDefinition definition);
}

/// <summary><see cref="IMetadataProvider"/> using MySqlConnector and INFORMATION_SCHEMA.</summary>
public sealed class MySqlMetadataProvider : IMetadataProvider
{
    internal const string DatabasesQuery =
        "SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM INFORMATION_SCHEMA.SCHEMATA ORDER BY SCHEMA_NAME";

    internal const string ServerInfoQuery =
        "SELECT CURRENT_USER(), @@character_set_server, @@collation_server, @@time_zone, @@system_time_zone";

    internal const string SslCipherQuery = "SHOW SESSION STATUS LIKE 'Ssl_cipher'";

    internal const string TablesQuery =
        "SELECT TABLE_NAME, ENGINE, TABLE_COMMENT, ROW_FORMAT, TABLE_ROWS, DATA_LENGTH, INDEX_LENGTH, " +
        "AUTO_INCREMENT, CREATE_TIME, UPDATE_TIME, TABLE_COLLATION FROM INFORMATION_SCHEMA.TABLES " +
        "WHERE TABLE_SCHEMA = @schema AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";

    internal const string ColumnsQuery =
        "SELECT c.COLUMN_NAME, c.COLUMN_TYPE, c.IS_NULLABLE, c.COLUMN_KEY, c.EXTRA, " +
        "c.DATA_TYPE, c.COLUMN_DEFAULT, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, " +
        "c.CHARACTER_SET_NAME, c.COLLATION_NAME, c.COLUMN_COMMENT, c.ORDINAL_POSITION, c.GENERATION_EXPRESSION, " +
        "EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k " +
        "WHERE k.TABLE_SCHEMA = c.TABLE_SCHEMA AND k.TABLE_NAME = c.TABLE_NAME AND k.COLUMN_NAME = c.COLUMN_NAME " +
        "AND k.REFERENCED_TABLE_NAME IS NOT NULL) AS IS_FOREIGN_KEY " +
        "FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table " +
        "ORDER BY c.ORDINAL_POSITION";

    internal const string KeysQuery =
        "SELECT tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE, k.COLUMN_NAME, k.ORDINAL_POSITION, " +
        "k.REFERENCED_TABLE_SCHEMA, k.REFERENCED_TABLE_NAME, k.REFERENCED_COLUMN_NAME, rc.UPDATE_RULE, rc.DELETE_RULE " +
        "FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc " +
        "JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k ON k.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA " +
        "AND k.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND k.TABLE_SCHEMA = tc.TABLE_SCHEMA AND k.TABLE_NAME = tc.TABLE_NAME " +
        "LEFT JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc ON rc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA " +
        "AND rc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND rc.TABLE_NAME = tc.TABLE_NAME " +
        "WHERE tc.TABLE_SCHEMA = @schema AND tc.TABLE_NAME = @table " +
        "AND tc.CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY') " +
        "ORDER BY tc.CONSTRAINT_NAME, k.ORDINAL_POSITION";

    internal const string CheckConstraintsQueryMySql =
        "SELECT tc.CONSTRAINT_NAME, cc.CHECK_CLAUSE, tc.ENFORCED " +
        "FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc " +
        "JOIN INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME " +
        "WHERE tc.TABLE_SCHEMA = @schema AND tc.TABLE_NAME = @table AND tc.CONSTRAINT_TYPE = 'CHECK' " +
        "ORDER BY tc.CONSTRAINT_NAME";

    internal const string CheckConstraintsQueryMariaDb =
        "SELECT CONSTRAINT_NAME, CHECK_CLAUSE, 'YES' " +
        "FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS " +
        "WHERE CONSTRAINT_SCHEMA = @schema AND TABLE_NAME = @table ORDER BY CONSTRAINT_NAME";

    // Visibility: MySQL 8.0 has IS_VISIBLE, MariaDB 10.6+ has IGNORED (ADR-008: detected at runtime).
    internal const string IndexesQueryMySql =
        "SELECT INDEX_NAME, NON_UNIQUE, COLUMN_NAME, SEQ_IN_INDEX, INDEX_TYPE, SUB_PART, IS_VISIBLE, INDEX_COMMENT " +
        "FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table " +
        "ORDER BY INDEX_NAME, SEQ_IN_INDEX";

    internal const string IndexesQueryMariaDb =
        "SELECT INDEX_NAME, NON_UNIQUE, COLUMN_NAME, SEQ_IN_INDEX, INDEX_TYPE, SUB_PART, " +
        "IF(IGNORED = 'YES', 'NO', 'YES'), INDEX_COMMENT " +
        "FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table " +
        "ORDER BY INDEX_NAME, SEQ_IN_INDEX";

    internal const string TriggersQuery =
        "SELECT TRIGGER_NAME, ACTION_TIMING, EVENT_MANIPULATION, DEFINER, CREATED, ACTION_ORDER FROM INFORMATION_SCHEMA.TRIGGERS " +
        "WHERE EVENT_OBJECT_SCHEMA = @schema AND EVENT_OBJECT_TABLE = @table " +
        "ORDER BY ACTION_TIMING, EVENT_MANIPULATION, ACTION_ORDER, TRIGGER_NAME";

    // System views (information_schema) have no row in VIEWS; the LEFT JOIN leaves their view properties empty.
    internal const string ViewsQuery =
        "SELECT t.TABLE_NAME, t.TABLE_COMMENT, v.IS_UPDATABLE, v.SECURITY_TYPE, v.DEFINER, v.CHECK_OPTION " +
        "FROM INFORMATION_SCHEMA.TABLES t " +
        "LEFT JOIN INFORMATION_SCHEMA.VIEWS v ON v.TABLE_SCHEMA = t.TABLE_SCHEMA AND v.TABLE_NAME = t.TABLE_NAME " +
        "WHERE t.TABLE_SCHEMA = @schema AND t.TABLE_TYPE IN ('VIEW', 'SYSTEM VIEW') ORDER BY t.TABLE_NAME";

    internal const string RoutinesQuery =
        "SELECT ROUTINE_NAME, DTD_IDENTIFIER, ROUTINE_COMMENT, DEFINER, SECURITY_TYPE, IS_DETERMINISTIC, " +
        "SQL_DATA_ACCESS, CREATED, LAST_ALTERED FROM INFORMATION_SCHEMA.ROUTINES " +
        "WHERE ROUTINE_SCHEMA = @schema AND ROUTINE_TYPE = @type ORDER BY ROUTINE_NAME";

    internal const string ParametersQuery =
        "SELECT PARAMETER_NAME, PARAMETER_MODE, DTD_IDENTIFIER, ORDINAL_POSITION FROM INFORMATION_SCHEMA.PARAMETERS " +
        "WHERE SPECIFIC_SCHEMA = @schema AND SPECIFIC_NAME = @name AND ROUTINE_TYPE = @type AND ORDINAL_POSITION > 0 " +
        "ORDER BY ORDINAL_POSITION";

    internal const string EventsQuery =
        "SELECT EVENT_NAME, STATUS, EXECUTE_AT, INTERVAL_VALUE, INTERVAL_FIELD, STARTS, ENDS, " +
        "ON_COMPLETION, DEFINER, EVENT_COMMENT, CREATED, LAST_ALTERED, LAST_EXECUTED " +
        "FROM INFORMATION_SCHEMA.EVENTS WHERE EVENT_SCHEMA = @schema ORDER BY EVENT_NAME";

    internal const string TableDependenciesQuery =
        "SELECT DISTINCT TABLE_NAME, REFERENCED_TABLE_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE " +
        "WHERE TABLE_SCHEMA = @schema AND REFERENCED_TABLE_SCHEMA = @schema AND REFERENCED_TABLE_NAME IS NOT NULL";

    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MySqlMetadataProvider"/> class.</summary>
    public MySqlMetadataProvider(IExtensionLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ServerInfo> ConnectAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        _logger.Info($"Connecting to '{definition.Label}' ({definition.Host}:{definition.Port} as '{definition.UserName}').");
        var stopwatch = Stopwatch.StartNew();

        using var connection = new MySqlConnection(ConnectionStringFactory.Build(definition));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        string currentUser;
        string? characterSet, collation, timeZone, systemTimeZone, sslCipher = null;
        using (var command = new MySqlCommand(ServerInfoQuery, connection))
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            currentUser = NullableString(reader, 0) ?? string.Empty;
            characterSet = NullableString(reader, 1);
            collation = NullableString(reader, 2);
            timeZone = NullableString(reader, 3);
            systemTimeZone = NullableString(reader, 4);
        }

        using (var command = new MySqlCommand(SslCipherQuery, connection))
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                sslCipher = NullableString(reader, 1);
            }
        }

        var info = new ServerInfo(connection.ServerVersion, currentUser)
        {
            CharacterSet = characterSet,
            Collation = collation,
            TimeZone = ServerInfo.FormatTimeZone(timeZone, systemTimeZone),
            SslCipher = sslCipher,
        };
        _logger.Info($"Connected to '{definition.Label}' in {stopwatch.ElapsedMilliseconds} ms. Server version {info.Version}, account {info.CurrentUser}.");
        if (!ProductInfo.IsSupportedServerVersion(info.Version))
        {
            _logger.Warning($"Server version {info.Version} of '{definition.Label}' is not supported (minimum MySQL 8.0 or MariaDB 10.6).");
        }

        return info;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            definition,
            DatabasesQuery,
            reader => new DatabaseInfo(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)),
            cancellationToken).ConfigureAwait(false);

        return rows.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            definition,
            TablesQuery,
            reader => new TableInfo(
                database,
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2))
            {
                RowFormat = NullableString(reader, 3),
                EstimatedRows = NullableInt64(reader, 4),
                DataLength = NullableInt64(reader, 5),
                IndexLength = NullableInt64(reader, 6),
                AutoIncrement = NullableInt64(reader, 7),
                Created = NullableDateTime(reader, 8),
                Updated = NullableDateTime(reader, 9),
                Collation = NullableString(reader, 10),
            },
            cancellationToken,
            ("@schema", database)).ConfigureAwait(false);

        return rows.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) =>
        await QueryAsync(
            definition,
            ColumnsQuery,
            reader => new ColumnInfo(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                string.Equals(reader.GetString(3), "PRI", StringComparison.OrdinalIgnoreCase),
                Convert.ToInt64(reader.GetValue(15), System.Globalization.CultureInfo.InvariantCulture) != 0,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4))
            {
                DataType = NullableString(reader, 5),
                Default = reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6), System.Globalization.CultureInfo.InvariantCulture),
                CharacterMaximumLength = NullableInt64(reader, 7),
                NumericPrecision = NullableInt64(reader, 8),
                NumericScale = NullableInt64(reader, 9),
                CharacterSet = NullableString(reader, 10),
                Collation = NullableString(reader, 11),
                Comment = NullableString(reader, 12),
                OrdinalPosition = (int)(NullableInt64(reader, 13) ?? 0),
                GenerationExpression = NullableString(reader, 14),
            },
            cancellationToken,
            ("@schema", database),
            ("@table", table)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeyInfo>> GetKeysAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            definition,
            KeysQuery,
            reader => new KeyInfo.Row(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Convert.ToInt32(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                NullableString(reader, 7),
                NullableString(reader, 8)),
            cancellationToken,
            ("@schema", database),
            ("@table", table)).ConfigureAwait(false);
        return KeyInfo.FromRows(rows);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CheckConstraintInfo>> GetCheckConstraintsAsync(ConnectionDefinition definition, string database, string table, bool isMariaDb, CancellationToken cancellationToken) =>
        await QueryAsync(
            definition,
            isMariaDb ? CheckConstraintsQueryMariaDb : CheckConstraintsQueryMySql,
            reader => new CheckConstraintInfo(
                reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                !string.Equals(Convert.ToString(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture), "NO", StringComparison.OrdinalIgnoreCase)),
            cancellationToken,
            ("@schema", database),
            ("@table", table)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ConnectionDefinition definition, string database, string table, bool isMariaDb, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            definition,
            isMariaDb ? IndexesQueryMariaDb : IndexesQueryMySql,
            reader => new IndexInfo.Row(
                reader.GetString(0),
                Convert.ToInt64(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture) != 0,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                Convert.ToInt32(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? null : Convert.ToInt64(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
                !string.Equals(NullableString(reader, 6), "NO", StringComparison.OrdinalIgnoreCase),
                NullableString(reader, 7)),
            cancellationToken,
            ("@schema", database),
            ("@table", table)).ConfigureAwait(false);
        return IndexInfo.FromRows(rows);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TriggerInfo>> GetTriggersAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) =>
        await QueryAsync(
            definition,
            TriggersQuery,
            reader => new TriggerInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            {
                Definer = NullableString(reader, 3),
                Created = NullableDateTime(reader, 4),
                ActionOrder = (int)(NullableInt64(reader, 5) ?? 0),
            },
            cancellationToken,
            ("@schema", database),
            ("@table", table)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TableInfo>> GetViewsAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            definition,
            ViewsQuery,
            reader => new TableInfo(database, reader.GetString(0), null, reader.IsDBNull(1) ? null : reader.GetString(1), isView: true)
            {
                IsUpdatable = NullableString(reader, 2) is { } updatable ? string.Equals(updatable, "YES", StringComparison.OrdinalIgnoreCase) : null,
                SecurityType = NullableString(reader, 3),
                Definer = NullableString(reader, 4),
                CheckOption = NullableString(reader, 5),
            },
            cancellationToken,
            ("@schema", database)).ConfigureAwait(false);
        return rows.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoutineInfo>> GetRoutinesAsync(ConnectionDefinition definition, string database, RoutineType type, CancellationToken cancellationToken)
    {
        var typeText = type == RoutineType.Function ? "FUNCTION" : "PROCEDURE";
        var rows = await QueryAsync(
            definition,
            RoutinesQuery,
            reader => new RoutineInfo(
                database,
                reader.GetString(0),
                type,
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2))
            {
                Definer = NullableString(reader, 3),
                SecurityType = NullableString(reader, 4),
                IsDeterministic = string.Equals(NullableString(reader, 5), "YES", StringComparison.OrdinalIgnoreCase),
                SqlDataAccess = NullableString(reader, 6),
                Created = NullableDateTime(reader, 7),
                LastAltered = NullableDateTime(reader, 8),
            },
            cancellationToken,
            ("@schema", database),
            ("@type", typeText)).ConfigureAwait(false);
        return rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(ConnectionDefinition definition, RoutineInfo routine, CancellationToken cancellationToken) =>
        await QueryAsync(
            definition,
            ParametersQuery,
            reader => new ParameterInfo(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                ParameterInfo.ParseMode(reader.IsDBNull(1) ? null : reader.GetString(1)),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Convert.ToInt32(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture)),
            cancellationToken,
            ("@schema", routine.Database),
            ("@name", routine.Name),
            ("@type", routine.InformationSchemaType)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventInfo>> GetEventsAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        await QueryAsync(
            definition,
            EventsQuery,
            reader => new EventInfo(
                reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6))
            {
                OnCompletion = NullableString(reader, 7),
                Definer = NullableString(reader, 8),
                Comment = NullableString(reader, 9),
                Created = NullableDateTime(reader, 10),
                LastAltered = NullableDateTime(reader, 11),
                LastExecuted = NullableDateTime(reader, 12),
            },
            cancellationToken,
            ("@schema", database)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<string?> GetCreateStatementAsync(ConnectionDefinition definition, Scripting.CodeObject codeObject, CancellationToken cancellationToken)
    {
        var sql = codeObject.ShowCreateStatement;
        _logger.Debug($"Query on '{definition.Label}': {sql}");

        using var connection = new MySqlConnection(ConnectionStringFactory.Build(definition));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = new MySqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var ordinal = reader.GetOrdinal(codeObject.SourceColumn);
        return NullableString(reader, ordinal);
    }

    /// <inheritdoc />
    public async Task<string?> GetCreateDatabaseStatementAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync(
            definition,
            "SHOW CREATE DATABASE IF NOT EXISTS " + DragDrop.MySqlIdentifier.Quote(database),
            reader => NullableString(reader, 1),
            cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Table, string ReferencedTable)>> GetTableDependenciesAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        await QueryAsync(
            definition,
            TableDependenciesQuery,
            reader => (reader.GetString(0), reader.GetString(1)),
            cancellationToken,
            ("@schema", database)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ReadRowsAsync(ConnectionDefinition definition, string database, string table, IReadOnlyList<string> columns, Action<object?[]> onRow, CancellationToken cancellationToken)
    {
        if (columns is null || columns.Count == 0)
        {
            return;
        }

        var sql = "SELECT " + string.Join(", ", columns.Select(DragDrop.MySqlIdentifier.Quote)) + " FROM " +
            DragDrop.MySqlIdentifier.Quote(database) + "." + DragDrop.MySqlIdentifier.Quote(table);
        _logger.Debug($"Query on '{definition.Label}': {sql}");
        var stopwatch = Stopwatch.StartNew();

        // Zero dates (0000-00-00) must survive the round trip, so they are read as MySqlDateTime.
        var builder = new MySqlConnectionStringBuilder(ConnectionStringFactory.Build(definition))
        {
            AllowZeroDateTime = true,
            ConvertZeroDateTime = false,
        };

        using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using (var timeZone = new MySqlCommand("SET time_zone = '+00:00'", connection))
        {
            await timeZone.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using var command = new MySqlCommand(sql, connection) { CommandTimeout = 0 };
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            onRow(values);
            count++;
        }

        _logger.Debug($"Read {count} row(s) of {database}.{table} in {stopwatch.ElapsedMilliseconds} ms.");
    }

    /// <inheritdoc />
    public void Disconnect(ConnectionDefinition definition)
    {
        using var connection = new MySqlConnection(ConnectionStringFactory.Build(definition));
        MySqlConnection.ClearPool(connection);
        _logger.Info($"Disconnected from '{definition.Label}'.");
    }

    private async Task<List<T>> QueryAsync<T>(ConnectionDefinition definition, string sql, Func<MySqlDataReader, T> read, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        var parameterText = parameters.Length == 0 ? string.Empty : " [" + string.Join(", ", parameters.Select(p => $"{p.Name}='{p.Value}'")) + "]";
        _logger.Debug($"Query on '{definition.Label}': {sql}{parameterText}");
        var stopwatch = Stopwatch.StartNew();

        using var connection = new MySqlConnection(ConnectionStringFactory.Build(definition));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(read(reader));
        }

        _logger.Debug($"Query returned {result.Count} row(s) in {stopwatch.ElapsedMilliseconds} ms.");
        return result;
    }

    // NULL and empty text both mean "not set".
    private static string? NullableString(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = Convert.ToString(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static long? NullableInt64(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    // A zero date (0000-00-00) cannot be converted; it is shown as empty.
    private static DateTime? NullableDateTime(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        try
        {
            return reader.GetDateTime(ordinal);
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }
}
