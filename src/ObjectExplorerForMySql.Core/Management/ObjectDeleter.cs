using System;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>Runs a DDL statement on a server (E09).</summary>
public interface ISchemaCommandRunner
{
    /// <summary>Runs <paramref name="sql"/> on its own connection.</summary>
    Task ExecuteAsync(ConnectionDefinition definition, string sql, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a statement with parameters (US-1003: a password is a parameter, so it is never part of the SQL text or a log).
    /// </summary>
    Task ExecuteAsync(ConnectionDefinition definition, Data.SqlCommandText command, CancellationToken cancellationToken);
}

/// <summary><see cref="ISchemaCommandRunner"/> on MySqlConnector.</summary>
public sealed class MySqlSchemaCommandRunner : ISchemaCommandRunner
{
    /// <inheritdoc />
    public Task ExecuteAsync(ConnectionDefinition definition, string sql, CancellationToken cancellationToken) =>
        ExecuteAsync(definition, new Data.SqlCommandText(sql, []), cancellationToken);

    /// <inheritdoc />
    /// <remarks>MySqlConnector puts the parameter values into the text on the client, so they also work in CREATE USER.</remarks>
    public async Task ExecuteAsync(ConnectionDefinition definition, Data.SqlCommandText command, CancellationToken cancellationToken)
    {
        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var sql = new MySqlCommand(command.Sql, connection) { CommandTimeout = 0 };
        foreach (var parameter in command.Parameters)
        {
            sql.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        }

        await sql.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Deletes the object of a tree node and refreshes the tree (US-0901).</summary>
public sealed class ObjectDeleter
{
    private readonly ISchemaCommandRunner _runner;
    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="ObjectDeleter"/> class.</summary>
    public ObjectDeleter(ISchemaCommandRunner runner, IExtensionLogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns what Delete would drop for <paramref name="node"/>, or null when the node cannot be deleted:
    /// folders, servers, keys, constraints, parameters, view columns and everything in a system database.
    /// </summary>
    public static DropTarget? TargetOf(TreeNodeViewModel? node) =>
        node?.CreateDropTarget() is { } target && !SystemDatabases.IsSystem(target.Database) ? target : null;

    /// <summary>
    /// Returns the node to refresh after the delete: the table for an index or column (its keys and indexes change too),
    /// otherwise the folder the object is in.
    /// </summary>
    public static TreeNodeViewModel? RefreshNodeOf(TreeNodeViewModel node, DropTarget target)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return target?.Kind is DropObjectKind.Index or DropObjectKind.Column
            ? node.FindAncestor<TableNodeViewModel>() ?? node.Parent
            : node.Parent;
    }

    /// <summary>
    /// Runs the statement of <paramref name="target"/> and refreshes the parent of <paramref name="node"/>.
    /// Returns false when the password prompt was cancelled; a server error is thrown.
    /// </summary>
    public async Task<bool> DeleteAsync(TreeNodeViewModel node, DropTarget target, CancellationToken cancellationToken)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var server = node.OwningServer ?? throw new InvalidOperationException("The node has no server.");
        if (!await server.EnsurePasswordAsync(cancellationToken))
        {
            return false;
        }

        _logger.Debug($"Delete on '{server.Label}': {target.Sql}");
        await _runner.ExecuteAsync(server.Definition, target.Sql, cancellationToken);
        _logger.Info($"Deleted {target.Kind.ToString().ToLowerInvariant()} {target.DisplayName} ('{server.Label}').");

        if (RefreshNodeOf(node, target) is { } parent)
        {
            await parent.RefreshAsync();
        }

        return true;
    }
}
