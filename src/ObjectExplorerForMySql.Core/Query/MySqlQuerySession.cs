using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// The server session of one query document (US-0703). It stays open between runs, so user variables, temporary
/// tables, USE and transactions carry over, as in MySQL Workbench and SSMS (decision D-11).
/// </summary>
public interface IQuerySession : IDisposable
{
    /// <summary>Gets the connection the session belongs to.</summary>
    ConnectionDefinition Definition { get; }

    /// <summary>Gets a value indicating whether the connection is lost; the next run opens a new session.</summary>
    bool IsBroken { get; }

    /// <summary>Opens the connection.</summary>
    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>Makes <paramref name="database"/> the default database (USE).</summary>
    Task ChangeDatabaseAsync(string database, CancellationToken cancellationToken);

    /// <summary>Returns the default database of the session (SELECT DATABASE()).</summary>
    Task<string?> GetCurrentDatabaseAsync(CancellationToken cancellationToken);

    /// <summary>Runs one statement and keeps at most <paramref name="maxRows"/> rows of each result set.</summary>
    Task<StatementResult> ExecuteAsync(string sql, int maxRows, CancellationToken cancellationToken);
}

/// <summary><see cref="IQuerySession"/> on a MySqlConnector connection.</summary>
public sealed class MySqlQuerySession : IQuerySession
{
    private readonly IExtensionLogger _logger;
    private readonly List<QueryWarning> _warnings = [];
    private MySqlConnection? _connection;

    /// <summary>Initializes a new instance of the <see cref="MySqlQuerySession"/> class.</summary>
    public MySqlQuerySession(ConnectionDefinition definition, IExtensionLogger logger)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ConnectionDefinition Definition { get; }

    /// <inheritdoc />
    public bool IsBroken => _connection is null || _connection.State != System.Data.ConnectionState.Open;

    /// <inheritdoc />
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        var builder = DataConnectionString(Definition, pooling: false);

        // @variables in a query are user variables, not command parameters.
        builder.AllowUserVariables = true;

        var connection = new MySqlConnection(builder.ConnectionString);
        connection.InfoMessage += OnInfoMessage;
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            connection.InfoMessage -= OnInfoMessage;
            connection.Dispose();
            throw;
        }

        _connection = connection;
        _logger.Debug($"Query session opened on '{Definition.Label}' (connection id {connection.ServerThread}).");
    }

    /// <inheritdoc />
    public Task ChangeDatabaseAsync(string database, CancellationToken cancellationToken) =>
        Connection.ChangeDatabaseAsync(database, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> GetCurrentDatabaseAsync(CancellationToken cancellationToken)
    {
        using var command = new MySqlCommand("SELECT DATABASE()", Connection);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<StatementResult> ExecuteAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        _warnings.Clear();
        var resultSets = new List<QueryResultSet>();
        using var command = new MySqlCommand(sql, Connection) { CommandTimeout = 0 };
        long affected;
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            do
            {
                if (reader.FieldCount == 0)
                {
                    continue;
                }

                var columns = new string[reader.FieldCount];
                var isDate = new bool[reader.FieldCount];
                for (var i = 0; i < columns.Length; i++)
                {
                    columns[i] = reader.GetName(i);
                    isDate[i] = string.Equals(reader.GetDataTypeName(i), "DATE", StringComparison.OrdinalIgnoreCase);
                }

                var rows = new List<object?[]>();
                long total = 0;
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Rows over the limit are read and counted but not kept, so the total is right.
                    total++;
                    if (rows.Count >= maxRows)
                    {
                        continue;
                    }

                    var values = new object?[columns.Length];
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = ReadValue(reader, i, isDate[i]);
                    }

                    rows.Add(values);
                }

                resultSets.Add(new QueryResultSet(columns, rows, total));
            }
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

            affected = reader.RecordsAffected;
        }

        return new StatementResult(resultSets, affected, _warnings.ToArray());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            connection.InfoMessage -= OnInfoMessage;
            connection.Dispose();
            _logger.Debug($"Query session on '{Definition.Label}' closed.");
        }
    }

    private MySqlConnection Connection => _connection ?? throw new InvalidOperationException("The query session is not open.");

    /// <summary>
    /// Connection string for reading data to show (US-0703, US-0801): values as MySQL stores them, so zero dates,
    /// TINYINT(1) as a number and CHAR(36) as text.
    /// </summary>
    internal static MySqlConnectionStringBuilder DataConnectionString(ConnectionDefinition definition, bool pooling) =>
        new(ConnectionStringFactory.Build(definition, pooling))
        {
            AllowZeroDateTime = true,
            ConvertZeroDateTime = false,
            TreatTinyAsBoolean = false,
            GuidFormat = MySqlGuidFormat.None,
        };

    /// <summary>Reads one value for display: DATE as text, zero dates as text, NULL as null.</summary>
    internal static object? ReadValue(MySqlDataReader reader, int ordinal, bool isDate)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is MySqlDateTime mySqlDate)
        {
            // Zero dates and dates with zero parts (2026-00-00) cannot be a DateTime.
            return isDate
                ? string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1:00}-{2:00}", mySqlDate.Year, mySqlDate.Month, mySqlDate.Day)
                : mySqlDate.IsValidDateTime ? QueryValueFormatter.Format(mySqlDate.GetDateTime()) : mySqlDate.ToString();
        }

        return isDate && value is DateTime date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : value;
    }

    private void OnInfoMessage(object sender, MySqlInfoMessageEventArgs e)
    {
        foreach (var error in e.Errors)
        {
            _warnings.Add(new QueryWarning(error.Level, (int)error.ErrorCode, error.Message));
        }
    }
}
