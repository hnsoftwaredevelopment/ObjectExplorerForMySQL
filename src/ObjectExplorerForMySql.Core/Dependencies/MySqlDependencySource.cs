using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Dependencies;

/// <summary>Reads a <see cref="DependencyCatalog"/> for one database (US-1303).</summary>
public interface IDependencySource
{
    /// <summary>Reads the objects of <paramref name="database"/> with their source, its foreign keys and view usage.</summary>
    Task<DependencyCatalog> LoadAsync(ConnectionDefinition definition, string database, string? serverVersion, CancellationToken cancellationToken);
}

/// <summary><see cref="IDependencySource"/> on INFORMATION_SCHEMA.</summary>
public sealed class MySqlDependencySource : IDependencySource
{
    // Sources are NULL (routines) or empty when the account may not see them; the finder reports that.
    private const string ObjectsSql =
        "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_TYPE IN ('BASE TABLE', 'VIEW');" +
        "SELECT TABLE_NAME, VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = @db;" +
        "SELECT ROUTINE_NAME, ROUTINE_TYPE, ROUTINE_DEFINITION FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_SCHEMA = @db;" +
        "SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, ACTION_STATEMENT FROM INFORMATION_SCHEMA.TRIGGERS WHERE TRIGGER_SCHEMA = @db;" +
        "SELECT EVENT_NAME, EVENT_DEFINITION FROM INFORMATION_SCHEMA.EVENTS WHERE EVENT_SCHEMA = @db;" +
        "SELECT CONSTRAINT_SCHEMA, TABLE_NAME, CONSTRAINT_NAME, UNIQUE_CONSTRAINT_SCHEMA, REFERENCED_TABLE_NAME " +
        "FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = @db OR UNIQUE_CONSTRAINT_SCHEMA = @db;";

    // MySQL 8.0.13+ only. A view that uses another view appears with TABLE_TYPE 'VIEW'.
    private const string ViewUsageSql =
        "SELECT u.VIEW_SCHEMA, u.VIEW_NAME, u.TABLE_SCHEMA, u.TABLE_NAME, t.TABLE_TYPE FROM INFORMATION_SCHEMA.VIEW_TABLE_USAGE u " +
        "LEFT JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = u.TABLE_SCHEMA AND t.TABLE_NAME = u.TABLE_NAME " +
        "WHERE u.VIEW_SCHEMA = @db OR u.TABLE_SCHEMA = @db;" +
        // VIEW_ROUTINE_USAGE names the view TABLE_SCHEMA/TABLE_NAME, unlike VIEW_TABLE_USAGE.
        "SELECT TABLE_SCHEMA, TABLE_NAME, SPECIFIC_SCHEMA, SPECIFIC_NAME FROM INFORMATION_SCHEMA.VIEW_ROUTINE_USAGE " +
        "WHERE TABLE_SCHEMA = @db OR SPECIFIC_SCHEMA = @db;";

    private readonly Logging.IExtensionLogger? _logger;

    /// <summary>Initializes a new instance of the <see cref="MySqlDependencySource"/> class.</summary>
    public MySqlDependencySource(Logging.IExtensionLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Returns true when the server has VIEW_TABLE_USAGE and VIEW_ROUTINE_USAGE: MySQL 8.0.13 or later.</summary>
    public static bool HasViewUsage(string? serverVersion)
    {
        var capabilities = ServerCapabilities.FromVersion(serverVersion);
        return !capabilities.IsMariaDb && capabilities.Version >= new Version(8, 0, 13);
    }

    /// <inheritdoc />
    public async Task<DependencyCatalog> LoadAsync(ConnectionDefinition definition, string database, string? serverVersion, CancellationToken cancellationToken)
    {
        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var objects = new List<CatalogObject>();
        var foreignKeys = new List<ForeignKeyLink>();
        using (var command = new MySqlCommand(ObjectsSql, connection))
        {
            command.Parameters.AddWithValue("@db", database);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var tables = new List<(string Name, bool IsView)>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add((reader.GetString(0), string.Equals(Text(reader, 1), "VIEW", StringComparison.OrdinalIgnoreCase)));
            }

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            var viewSources = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                viewSources[reader.GetString(0)] = Text(reader, 1);
            }

            foreach (var (name, isView) in tables)
            {
                objects.Add(isView
                    ? new CatalogObject(new SchemaObject(DependencyKind.View, database, name), viewSources.TryGetValue(name, out var source) ? source : null)
                    : new CatalogObject(new SchemaObject(DependencyKind.Table, database, name), null));
            }

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var kind = string.Equals(Text(reader, 1), "FUNCTION", StringComparison.OrdinalIgnoreCase) ? DependencyKind.Function : DependencyKind.Procedure;
                objects.Add(new CatalogObject(new SchemaObject(kind, database, reader.GetString(0)), Text(reader, 2)));
            }

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                objects.Add(new CatalogObject(new SchemaObject(DependencyKind.Trigger, database, reader.GetString(0), Text(reader, 1)), Text(reader, 2)));
            }

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                objects.Add(new CatalogObject(new SchemaObject(DependencyKind.Event, database, reader.GetString(0)), Text(reader, 1)));
            }

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                foreignKeys.Add(new ForeignKeyLink(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            }
        }

        var usage = HasViewUsage(serverVersion) ? await ReadViewUsageAsync(connection, database, cancellationToken).ConfigureAwait(false) : null;
        return new DependencyCatalog(objects, foreignKeys, usage);
    }

    // The usage tables only make the view lines exact; when reading them fails, views fall back to the text match.
    private async Task<List<ViewUsage>?> ReadViewUsageAsync(MySqlConnection connection, string database, CancellationToken cancellationToken)
    {
        var usage = new List<ViewUsage>();
        try
        {
            using var command = new MySqlCommand(ViewUsageSql, connection);
            command.Parameters.AddWithValue("@db", database);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var kind = string.Equals(Text(reader, 4), "VIEW", StringComparison.OrdinalIgnoreCase) ? DependencyKind.View : DependencyKind.Table;
                usage.Add(new ViewUsage(reader.GetString(0), reader.GetString(1), new SchemaObject(kind, reader.GetString(2), reader.GetString(3))));
            }

            await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                usage.Add(new ViewUsage(reader.GetString(0), reader.GetString(1), new SchemaObject(DependencyKind.Function, reader.GetString(2), reader.GetString(3))));
            }

            return usage;
        }
        catch (MySqlException ex)
        {
            _logger?.Warning("Reading VIEW_TABLE_USAGE / VIEW_ROUTINE_USAGE failed; views are matched on their text.", ex);
            return null;
        }
    }

    // VIEW_DEFINITION is empty, not NULL, when the account lacks SHOW VIEW: both mean "unknown".
    private static string? Text(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) || reader.GetValue(ordinal)?.ToString() is not { Length: > 0 } text ? null : text;
}
