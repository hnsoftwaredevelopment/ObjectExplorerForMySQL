using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>Decides whether a statement may run again for a full export (US-0705).</summary>
public static class ExportStatement
{
    private static readonly HashSet<string> Changing = new(StringComparer.OrdinalIgnoreCase) { "INSERT", "UPDATE", "DELETE", "REPLACE" };

    /// <summary>
    /// Returns true for statements without side effects: SELECT, TABLE, VALUES, SHOW, DESCRIBE and EXPLAIN, and WITH
    /// whose main statement only reads. Not for SELECT ... INTO (writes a file or variables) and not for EXPLAIN
    /// ANALYZE, which runs the statement. A function called by the SELECT can still change data; that cannot be seen
    /// from the text.
    /// </summary>
    public static bool CanRunAgain(string statement)
    {
        var words = ExplainStatement.TopLevelWords(statement ?? string.Empty).Select(w => w.ToUpperInvariant()).ToList();
        if (words.Count == 0)
        {
            return false;
        }

        var second = words.Count > 1 ? words[1] : string.Empty;
        return words[0] switch
        {
            // A statement that starts with a parenthesized SELECT: (SELECT ...) UNION (SELECT ...).
            "SELECT" or "TABLE" or "VALUES" or "UNION" or "INTERSECT" or "EXCEPT" or "ORDER" or "LIMIT" => !words.Contains("INTO"),
            "WITH" => !words.Contains("INTO") && !words.Any(Changing.Contains),
            "SHOW" => true,
            "EXPLAIN" or "DESCRIBE" or "DESC" => second != "ANALYZE",
            _ => false,
        };
    }
}

/// <summary>
/// Runs a statement again on its own connection and writes every row to a CSV or JSON file (US-0705). Rows go from
/// the server to the file one by one, so a result of millions of rows does not fill the memory. The format is the
/// one of <see cref="QueryResultExporter"/>.
/// </summary>
public static class FullResultExporter
{
    /// <summary>How often the progress is reported, in rows.</summary>
    internal const int ProgressStep = 1000;

    /// <summary>
    /// Runs <paramref name="source"/> and writes the first result set to <paramref name="writer"/>. Returns the number
    /// of rows. <paramref name="progress"/> gets the rows written so far. Cancelling throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<long> ExportAsync(ResultSource source, bool json, TextWriter writer, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        var builder = MySqlQuerySession.DataConnectionString(source.Connection, pooling: false);
        builder.AllowUserVariables = true;
        using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(source.Database))
        {
            await connection.ChangeDatabaseAsync(source.Database!, cancellationToken).ConfigureAwait(false);
        }

        using var command = new MySqlCommand(source.Statement, connection) { CommandTimeout = 0 };
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        // Reading continues synchronously; Cancel stops the statement on the server.
        using var registration = cancellationToken.Register(() => command.Cancel());
        var columns = new string[reader.FieldCount];
        var isDate = new bool[reader.FieldCount];
        for (var i = 0; i < columns.Length; i++)
        {
            columns[i] = reader.GetName(i);
            isDate[i] = string.Equals(reader.GetDataTypeName(i), "DATE", StringComparison.OrdinalIgnoreCase);
        }

        var rows = Counted(ReadRows(reader, isDate), progress, cancellationToken);
        long count;
        try
        {
            count = json ? QueryResultExporter.WriteJson(columns, rows, writer) : QueryResultExporter.WriteCsv(columns, rows, writer);
        }
        catch (MySqlException) when (cancellationToken.IsCancellationRequested)
        {
            // The server reports the cancelled statement as an error.
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(count);
        return count;
    }

    /// <summary>Passes <paramref name="rows"/> on, reports every <see cref="ProgressStep"/> rows and stops when cancelled.</summary>
    internal static IEnumerable<object?[]> Counted(IEnumerable<object?[]> rows, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        long count = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
            if (++count % ProgressStep == 0)
            {
                progress?.Report(count);
            }
        }
    }

    private static IEnumerable<object?[]> ReadRows(MySqlDataReader reader, bool[] isDate)
    {
        // One array per row: the writer is done with a row before it asks for the next.
        while (reader.Read())
        {
            var values = new object?[isDate.Length];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = MySqlQuerySession.ReadValue(reader, i, isDate[i]);
            }

            yield return values;
        }
    }
}
