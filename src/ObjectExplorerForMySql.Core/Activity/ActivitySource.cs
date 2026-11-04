using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Activity;

/// <summary>One session (connection) on the server (US-1501).</summary>
/// <param name="Id">The connection id, as KILL takes it.</param>
/// <param name="User">The account.</param>
/// <param name="Host">The client host and port.</param>
/// <param name="Database">The default database, or null.</param>
/// <param name="Command">What the session does: Query, Sleep, Connect, Daemon, Binlog Dump and so on.</param>
/// <param name="Time">Seconds in the current state.</param>
/// <param name="State">What the statement waits for or does, for example <c>Waiting for table metadata lock</c>.</param>
/// <param name="Statement">The statement that runs, or null.</param>
public sealed record SessionInfo(long Id, string? User, string? Host, string? Database, string Command, long Time, string? State, string? Statement)
{
    /// <summary>Gets a value indicating whether the session waits for the client without running anything.</summary>
    public bool IsSleeping => string.Equals(Command, "Sleep", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What KILL stops.</summary>
public enum KillKind
{
    /// <summary>KILL QUERY: only the statement that runs; the session stays.</summary>
    Query,

    /// <summary>KILL CONNECTION: the whole session; its open transaction is rolled back.</summary>
    Connection,
}

/// <summary>Reads the sessions of a server and stops them (US-1501).</summary>
public interface IActivitySource
{
    /// <summary>Returns the sessions the account may see, without the connection that reads them.</summary>
    Task<IReadOnlyList<SessionInfo>> LoadAsync(ConnectionDefinition definition, CancellationToken cancellationToken);

    /// <summary>Runs KILL QUERY or KILL CONNECTION for session <paramref name="id"/>.</summary>
    Task KillAsync(ConnectionDefinition definition, long id, KillKind kind, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IActivitySource"/> on MySQL and MariaDB. Reads <c>performance_schema.processlist</c> (MySQL 8.0.22+,
/// does not take the global mutex of the old process list); when that table is missing or not readable it falls back
/// to <c>SHOW FULL PROCESSLIST</c> and remembers that for the connection. Every call opens a pooled connection.
/// </summary>
public sealed class MySqlActivitySource : IActivitySource
{
    /// <summary>The query on the Performance Schema.</summary>
    internal const string PerformanceSchemaSql = "SELECT ID, USER, HOST, DB, COMMAND, TIME, STATE, INFO FROM performance_schema.processlist";

    /// <summary>The fallback for MariaDB and older MySQL versions.</summary>
    internal const string ProcessListSql = "SHOW FULL PROCESSLIST";

    private readonly ConcurrentDictionary<Guid, bool> _useProcessList = new();
    private readonly IExtensionLogger? _logger;

    /// <summary>Initializes a new instance of the <see cref="MySqlActivitySource"/> class.</summary>
    public MySqlActivitySource(IExtensionLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Returns KILL QUERY or KILL CONNECTION for <paramref name="id"/>.</summary>
    public static string KillSql(long id, KillKind kind) =>
        (kind == KillKind.Query ? "KILL QUERY " : "KILL CONNECTION ") + id.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionInfo>> LoadAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var own = (long)connection.ServerThread;
        if (!_useProcessList.TryGetValue(definition.Id, out var useProcessList) || !useProcessList)
        {
            try
            {
                return await ReadAsync(connection, PerformanceSchemaSql, own, cancellationToken).ConfigureAwait(false);
            }
            catch (MySqlException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.Debug($"performance_schema.processlist is not available on '{definition.Label}' ({ex.Message}); using SHOW FULL PROCESSLIST.");
                _useProcessList[definition.Id] = true;
            }
        }

        return await ReadAsync(connection, ProcessListSql, own, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task KillAsync(ConnectionDefinition definition, long id, KillKind kind, CancellationToken cancellationToken)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = new MySqlCommand(KillSql(id, kind), connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Both queries return Id, User, Host, db, Command, Time, State, Info as the first eight columns.
    private static async Task<IReadOnlyList<SessionInfo>> ReadAsync(MySqlConnection connection, string sql, long own, CancellationToken cancellationToken)
    {
        var sessions = new List<SessionInfo>();
        using var command = new MySqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
            if (id == own)
            {
                continue;
            }

            sessions.Add(new SessionInfo(
                id,
                Text(reader, 1),
                Text(reader, 2),
                Text(reader, 3),
                Text(reader, 4) ?? string.Empty,
                reader.IsDBNull(5) ? 0 : Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
                Text(reader, 6),
                Text(reader, 7)));
        }

        return sessions;
    }

    // Some servers return the statement as binary; it is UTF-8 text.
    private static string? Text(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        var text = value is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
