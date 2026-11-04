using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Completion;

/// <summary>Kind of a name in the completion list.</summary>
public enum CompletionKind
{
    /// <summary>Keyword.</summary>
    Keyword,

    /// <summary>Database.</summary>
    Database,

    /// <summary>Table.</summary>
    Table,

    /// <summary>View.</summary>
    View,

    /// <summary>Stored procedure.</summary>
    Procedure,

    /// <summary>Function.</summary>
    Function,

    /// <summary>Column.</summary>
    Column,
}

/// <summary>An object of a database in the cache.</summary>
public sealed record SchemaName(CompletionKind Kind, string Name, string? Description = null);

/// <summary>The tables, views and routines of one database.</summary>
/// <param name="Database">The database.</param>
/// <param name="Objects">The names for the completion list.</param>
/// <param name="Tables">Tables and views with their details, for quick info (US-1402).</param>
/// <param name="Routines">Procedures and functions, for parameter info (US-1402).</param>
public sealed record DatabaseSchema(string Database, IReadOnlyList<SchemaName> Objects, IReadOnlyList<TableInfo> Tables, IReadOnlyList<RoutineInfo> Routines);

/// <summary>
/// Metadata for the completion list (US-1401), read once per connection and database and kept until
/// <see cref="Clear"/> (Refresh in the explorer). Loads run on a background thread; a failed load is not kept, so the
/// next request tries again.
/// </summary>
public sealed class SchemaCache
{
    private readonly IMetadataProvider _metadata;
    private readonly IExtensionLogger _logger;
    private readonly ConcurrentDictionary<(Guid Connection, string Key), Lazy<Task<object>>> _entries = new();

    /// <summary>Initializes a new instance of the <see cref="SchemaCache"/> class.</summary>
    public SchemaCache(IMetadataProvider metadata, IExtensionLogger logger)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Returns the database names of the connection.</summary>
    public async Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionDefinition definition, CancellationToken cancellationToken) =>
        (IReadOnlyList<string>)await GetAsync(definition, "databases", async () =>
            (object)(await _metadata.GetDatabasesAsync(definition, CancellationToken.None).ConfigureAwait(false)).Select(d => d.Name).ToList(), cancellationToken).ConfigureAwait(false);

    /// <summary>Returns the tables, views and routines of <paramref name="database"/>.</summary>
    public async Task<DatabaseSchema> GetDatabaseAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        (DatabaseSchema)await GetAsync(definition, "db:" + database.ToUpperInvariant(), async () =>
        {
            var tables = await _metadata.GetTablesAsync(definition, database, CancellationToken.None).ConfigureAwait(false);
            var views = await _metadata.GetViewsAsync(definition, database, CancellationToken.None).ConfigureAwait(false);
            var procedures = await _metadata.GetRoutinesAsync(definition, database, RoutineType.Procedure, CancellationToken.None).ConfigureAwait(false);
            var functions = await _metadata.GetRoutinesAsync(definition, database, RoutineType.Function, CancellationToken.None).ConfigureAwait(false);
            var objects = tables.Select(t => new SchemaName(CompletionKind.Table, t.Name, t.Comment))
                .Concat(views.Select(v => new SchemaName(CompletionKind.View, v.Name, v.Comment)))
                .Concat(procedures.Select(r => new SchemaName(CompletionKind.Procedure, r.Name, r.Comment)))
                .Concat(functions.Select(r => new SchemaName(CompletionKind.Function, r.Name, r.Comment)))
                .ToList();
            return (object)new DatabaseSchema(database, objects, tables.Concat(views).ToList(), procedures.Concat(functions).ToList());
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>Returns the parameters of a routine (US-1402).</summary>
    public async Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(ConnectionDefinition definition, RoutineInfo routine, CancellationToken cancellationToken) =>
        (IReadOnlyList<ParameterInfo>)await GetAsync(
            definition,
            "parameters:" + routine.Database.ToUpperInvariant() + "." + routine.InformationSchemaType + "." + routine.Name.ToUpperInvariant(),
            async () => (object)await _metadata.GetParametersAsync(definition, routine, CancellationToken.None).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    /// <summary>Returns the columns of a table or view.</summary>
    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) =>
        (IReadOnlyList<ColumnInfo>)await GetAsync(definition, "columns:" + database.ToUpperInvariant() + "." + table.ToUpperInvariant(), async () =>
            (object)await _metadata.GetColumnsAsync(definition, database, table, CancellationToken.None).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>Forgets everything of the connection; the next completion reads it again.</summary>
    public void Clear(Guid connectionId)
    {
        foreach (var key in _entries.Keys.Where(k => k.Connection == connectionId).ToList())
        {
            _entries.TryRemove(key, out _);
        }
    }

    // One load per key, shared by concurrent requests. Cancelling a request does not cancel the load: the next
    // keystroke can use its result.
    private async Task<object> GetAsync(ConnectionDefinition definition, string key, Func<Task<object>> load, CancellationToken cancellationToken)
    {
        var cacheKey = (definition.Id, key);
        var entry = _entries.GetOrAdd(cacheKey, _ => new Lazy<Task<object>>(() => Task.Run(load)));
        try
        {
            return await WaitAsync(entry.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _entries.TryRemove(cacheKey, out _);
            _logger.Debug($"Reading completion data ({key}) of '{definition.Label}' failed: {ex.Message}");
            throw;
        }
    }

    private static async Task<object> WaitAsync(Task<object> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        var cancelled = new TaskCompletionSource<object>();
        using (cancellationToken.Register(() => cancelled.TrySetCanceled()))
        {
            return await (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
