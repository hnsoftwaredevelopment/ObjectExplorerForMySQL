using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Data;

/// <summary>Reads the data of View Data (US-0801). Each call opens and closes its own connection.</summary>
public interface ITableDataSource
{
    /// <summary>Runs a SELECT and returns all its rows.</summary>
    Task<QueryResultSet> ReadAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken);

    /// <summary>Runs a COUNT(*).</summary>
    Task<long> CountAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken);

    /// <summary>Returns the row estimate of the storage engine (INFORMATION_SCHEMA.TABLES.TABLE_ROWS), or null.</summary>
    Task<long?> EstimateRowsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken);

    /// <summary>Runs an INSERT, UPDATE or DELETE (US-0802).</summary>
    Task<RowChangeResult> ExecuteAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken);
}

/// <summary>Result of a row change: rows found and the AUTO_INCREMENT value of an INSERT.</summary>
public sealed record RowChangeResult(long RowsAffected, long LastInsertId);

/// <summary><see cref="ITableDataSource"/> on MySqlConnector.</summary>
public sealed class MySqlTableDataSource : ITableDataSource
{
    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MySqlTableDataSource"/> class.</summary>
    public MySqlTableDataSource(IExtensionLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<QueryResultSet> ReadAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken)
    {
        _logger.Debug($"View Data on '{definition.Label}': {command.Sql}");
        using var connection = await OpenAsync(definition, cancellationToken).ConfigureAwait(false);
        using var sql = Create(connection, command);
        using var reader = await sql.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new string[reader.FieldCount];
        var isDate = new bool[reader.FieldCount];
        for (var i = 0; i < columns.Length; i++)
        {
            columns[i] = reader.GetName(i);
            isDate[i] = string.Equals(reader.GetDataTypeName(i), "DATE", StringComparison.OrdinalIgnoreCase);
        }

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new object?[columns.Length];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = MySqlQuerySession.ReadValue(reader, i, isDate[i]);
            }

            rows.Add(values);
        }

        return new QueryResultSet(columns, rows, rows.Count);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(definition, cancellationToken).ConfigureAwait(false);
        using var sql = Create(connection, command);
        var value = await sql.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<long?> EstimateRowsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(definition, cancellationToken).ConfigureAwait(false);
        using var sql = new MySqlCommand("SELECT TABLE_ROWS FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table", connection);
        sql.Parameters.AddWithValue("@db", database);
        sql.Parameters.AddWithValue("@table", table);
        var value = await sql.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<RowChangeResult> ExecuteAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken)
    {
        _logger.Debug($"Edit Data on '{definition.Label}': {command.Sql}");
        using var connection = await OpenAsync(definition, cancellationToken).ConfigureAwait(false);
        using var sql = Create(connection, command);

        // MySqlConnector reports found rows (not changed rows), so an UPDATE that sets the same values still gives 1.
        var affected = await sql.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new RowChangeResult(affected, sql.LastInsertedId);
    }

    private static async Task<MySqlConnection> OpenAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static MySqlCommand Create(MySqlConnection connection, SqlCommandText command)
    {
        var sql = new MySqlCommand(command.Sql, connection) { CommandTimeout = 0 };
        foreach (var parameter in command.Parameters)
        {
            sql.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        }

        return sql;
    }
}
