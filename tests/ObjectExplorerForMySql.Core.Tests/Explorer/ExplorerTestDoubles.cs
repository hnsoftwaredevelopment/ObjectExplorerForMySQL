using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Tests.Explorer;

internal sealed class FakeMetadataProvider : IMetadataProvider
{
    public Queue<Func<ConnectionDefinition, Task<ServerInfo>>> ConnectResults { get; } = new();

    public Func<ConnectionDefinition, Task<IReadOnlyList<DatabaseInfo>>> Databases { get; set; } =
        _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>(Array.Empty<DatabaseInfo>());

    public int ConnectCalls { get; private set; }

    public int DisconnectCalls { get; private set; }

    public string? PasswordAtConnect { get; private set; }

    public Task<ServerInfo> ConnectAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        ConnectCalls++;
        PasswordAtConnect = definition.Password;
        return ConnectResults.Count > 0
            ? ConnectResults.Dequeue()(definition)
            : Task.FromResult(new ServerInfo("8.0.36", "app@%"));
    }

    public Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(ConnectionDefinition definition, CancellationToken cancellationToken) =>
        Databases(definition);

    public Func<string, Task<IReadOnlyList<TableInfo>>> Tables { get; set; } =
        _ => Task.FromResult<IReadOnlyList<TableInfo>>(Array.Empty<TableInfo>());

    public Func<string, string, Task<IReadOnlyList<ColumnInfo>>> Columns { get; set; } =
        (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>(Array.Empty<ColumnInfo>());

    public Task<IReadOnlyList<TableInfo>> GetTablesAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        Tables(database);

    public Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) =>
        Columns(database, table);

    public IReadOnlyList<KeyInfo> Keys { get; set; } = Array.Empty<KeyInfo>();

    public IReadOnlyList<CheckConstraintInfo> CheckConstraints { get; set; } = Array.Empty<CheckConstraintInfo>();

    public IReadOnlyList<IndexInfo> Indexes { get; set; } = Array.Empty<IndexInfo>();

    public IReadOnlyList<TriggerInfo> Triggers { get; set; } = Array.Empty<TriggerInfo>();

    public bool? CheckConstraintsRequestedForMariaDb { get; private set; }

    public Task<IReadOnlyList<KeyInfo>> GetKeysAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) =>
        Task.FromResult(Keys);

    public Task<IReadOnlyList<CheckConstraintInfo>> GetCheckConstraintsAsync(ConnectionDefinition definition, string database, string table, bool isMariaDb, CancellationToken cancellationToken)
    {
        CheckConstraintsRequestedForMariaDb = isMariaDb;
        return Task.FromResult(CheckConstraints);
    }

    public bool? IndexesRequestedForMariaDb { get; private set; }

    public Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ConnectionDefinition definition, string database, string table, bool isMariaDb, CancellationToken cancellationToken)
    {
        IndexesRequestedForMariaDb = isMariaDb;
        return Task.FromResult(Indexes);
    }

    public Task<IReadOnlyList<TriggerInfo>> GetTriggersAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) =>
        Task.FromResult(TriggersByTable?.Invoke(table) ?? Triggers);

    public IReadOnlyList<TableInfo> Views { get; set; } = Array.Empty<TableInfo>();

    public IReadOnlyList<RoutineInfo> Routines { get; set; } = Array.Empty<RoutineInfo>();

    public IReadOnlyList<ParameterInfo> Parameters { get; set; } = Array.Empty<ParameterInfo>();

    public IReadOnlyList<EventInfo> Events { get; set; } = Array.Empty<EventInfo>();

    public List<RoutineType> RoutineTypesRequested { get; } = [];

    public Task<IReadOnlyList<TableInfo>> GetViewsAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        Task.FromResult(Views);

    public Task<IReadOnlyList<RoutineInfo>> GetRoutinesAsync(ConnectionDefinition definition, string database, RoutineType type, CancellationToken cancellationToken)
    {
        RoutineTypesRequested.Add(type);
        return Task.FromResult<IReadOnlyList<RoutineInfo>>(Routines.Where(r => r.Type == type).ToList());
    }

    public Task<IReadOnlyList<ParameterInfo>> GetParametersAsync(ConnectionDefinition definition, RoutineInfo routine, CancellationToken cancellationToken) =>
        Task.FromResult(Parameters);

    public Task<IReadOnlyList<EventInfo>> GetEventsAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        Task.FromResult(Events);

    public Func<ObjectExplorerForMySql.Core.Scripting.CodeObject, string?> CreateStatement { get; set; } = o => $"CREATE {o.Keyword} {o.Name}";

    public Task<string?> GetCreateStatementAsync(ConnectionDefinition definition, ObjectExplorerForMySql.Core.Scripting.CodeObject codeObject, CancellationToken cancellationToken) =>
        Task.FromResult(CreateStatement(codeObject));

    public string? CreateDatabaseStatement { get; set; }

    public IReadOnlyList<(string Table, string ReferencedTable)> TableDependencies { get; set; } = Array.Empty<(string, string)>();

    public Dictionary<string, IReadOnlyList<object?[]>> Rows { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Func<string, IReadOnlyList<TriggerInfo>>? TriggersByTable { get; set; }

    public Task<string?> GetCreateDatabaseStatementAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        Task.FromResult(CreateDatabaseStatement);

    public Task<IReadOnlyList<(string Table, string ReferencedTable)>> GetTableDependenciesAsync(ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        Task.FromResult(TableDependencies);

    public Task ReadRowsAsync(ConnectionDefinition definition, string database, string table, IReadOnlyList<string> columns, Action<object?[]> onRow, CancellationToken cancellationToken)
    {
        if (Rows.TryGetValue(table, out var rows))
        {
            foreach (var row in rows)
            {
                onRow(row);
            }
        }

        return Task.CompletedTask;
    }

    public void Disconnect(ConnectionDefinition definition) => DisconnectCalls++;
}

internal sealed class FakePasswordPrompt : IPasswordPrompt
{
    public PasswordPromptResult? Answer { get; set; }

    public int Calls { get; private set; }

    public Task<PasswordPromptResult?> PromptAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Answer);
    }
}

internal sealed class ListSink : ILogSink
{
    public List<string> Entries { get; } = [];

    public void Write(string formattedEntry) => Entries.Add(formattedEntry);
}

internal static class TestExplorer
{
    public static ExplorerContext Context(FakeMetadataProvider? metadata = null, FakePasswordPrompt? prompt = null, ListSink? sink = null) =>
        new(metadata ?? new FakeMetadataProvider(), prompt ?? new FakePasswordPrompt(), new ExplorerTexts(), new ExtensionLogger(sink ?? new ListSink(), LogLevel.Debug));

    public static ConnectionDefinition Definition(string name = "Prod", string? password = "pw") =>
        new() { DisplayName = name, Host = "db.local", UserName = "app", Password = password };
}
