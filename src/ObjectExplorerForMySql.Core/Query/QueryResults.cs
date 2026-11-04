using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ObjectExplorerForMySql.Core.Connections;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>One result set of a statement (US-0703). <see cref="Rows"/> holds at most the row limit.</summary>
public sealed class QueryResultSet
{
    /// <summary>Initializes a new instance of the <see cref="QueryResultSet"/> class.</summary>
    public QueryResultSet(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, long totalRows)
    {
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        TotalRows = totalRows;
    }

    /// <summary>Gets the column names.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Gets the rows that are shown.</summary>
    public IReadOnlyList<object?[]> Rows { get; }

    /// <summary>Gets the number of rows the statement returned.</summary>
    public long TotalRows { get; }

    /// <summary>Gets a value indicating whether rows were left out because of the row limit.</summary>
    public bool IsTruncated => TotalRows > Rows.Count;

    /// <summary>
    /// Gets or sets how to run the statement again for a full export (US-0705); only set when the result was cut off
    /// and the statement has no side effects.
    /// </summary>
    public ResultSource? Source { get; set; }
}

/// <summary>The statement behind a result set that was cut off, so an export can run it again (US-0705).</summary>
/// <remarks>Not a record: the connection holds the password and must not show up in a ToString.</remarks>
public sealed class ResultSource
{
    /// <summary>Initializes a new instance of the <see cref="ResultSource"/> class.</summary>
    public ResultSource(ConnectionDefinition connection, string? database, string statement)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Database = database;
        Statement = statement ?? throw new ArgumentNullException(nameof(statement));
    }

    /// <summary>Gets the connection the statement ran on.</summary>
    public ConnectionDefinition Connection { get; }

    /// <summary>Gets the default database of the session when the statement ran.</summary>
    public string? Database { get; }

    /// <summary>Gets the statement.</summary>
    public string Statement { get; }
}

/// <summary>A warning or note the server returned for a statement.</summary>
public sealed record QueryWarning(string Level, int Code, string Message);

/// <summary>What one statement returned.</summary>
public sealed class StatementResult
{
    /// <summary>Initializes a new instance of the <see cref="StatementResult"/> class.</summary>
    public StatementResult(IReadOnlyList<QueryResultSet> resultSets, long recordsAffected, IReadOnlyList<QueryWarning> warnings)
    {
        ResultSets = resultSets ?? throw new ArgumentNullException(nameof(resultSets));
        RecordsAffected = recordsAffected;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    /// <summary>Gets the result sets (SELECT, SHOW, a CALL can return several).</summary>
    public IReadOnlyList<QueryResultSet> ResultSets { get; }

    /// <summary>Gets the rows changed by INSERT, UPDATE or DELETE; -1 when the statement only read.</summary>
    public long RecordsAffected { get; }

    /// <summary>Gets the warnings.</summary>
    public IReadOnlyList<QueryWarning> Warnings { get; }
}

/// <summary>Kind of a line in the Messages tab.</summary>
public enum QueryMessageKind
{
    /// <summary>Rows returned or affected, duration.</summary>
    Info,

    /// <summary>Warning or note from the server.</summary>
    Warning,

    /// <summary>Error; execution stopped.</summary>
    Error,
}

/// <summary>A line in the Messages tab.</summary>
public sealed record QueryMessage(QueryMessageKind Kind, string Text);

/// <summary>Texts of the Messages tab. The extension fills them from its resources (ADR-005); the defaults are English.</summary>
public sealed class QueryTexts
{
    /// <summary>Gets or sets the text for rows returned; {0} = count.</summary>
    public string RowsReturned { get; set; } = "{0} row(s) returned.";

    /// <summary>Gets or sets the text for a result over the row limit; {0} = count, {1} = limit.</summary>
    public string RowsTruncated { get; set; } = "{0} row(s) returned; the first {1} are shown.";

    /// <summary>Gets or sets the text for changed rows; {0} = count.</summary>
    public string RowsAffected { get; set; } = "{0} row(s) affected.";

    /// <summary>Gets or sets the text for a warning; {0} = level, {1} = code, {2} = message.</summary>
    public string Warning { get; set; } = "{0} {1}: {2}";

    /// <summary>Gets or sets the text for an error; {0} = code, {1} = line, {2} = message.</summary>
    public string Error { get; set; } = "Error {0} at line {1}: {2}";

    /// <summary>Gets or sets the text when the connection fails; {0} = message.</summary>
    public string ConnectFailed { get; set; } = "Connecting failed: {0}";

    /// <summary>Gets or sets the text after a successful run; {0} = number of statements, {1} = duration.</summary>
    public string Completed { get; set; } = "{0} statement(s) completed in {1}.";

    /// <summary>Gets or sets the text after an error; {0} = duration.</summary>
    public string Failed { get; set; } = "Stopped after an error in {0}.";

    /// <summary>Gets or sets the text after Cancel; {0} = duration.</summary>
    public string Cancelled { get; set; } = "Cancelled after {0}.";

    /// <summary>Gets or sets the text when the document has no connection.</summary>
    public string NoConnection { get; set; } = "Choose a connection in the bar above the editor.";

    /// <summary>Gets or sets the text when the selection or document has no statement.</summary>
    public string NothingToRun { get; set; } = "There is no statement to run.";

    /// <summary>Gets or sets the text when EXPLAIN does not support the statement; {0} = line.</summary>
    public string NotExplainable { get; set; } = "The statement at line {0} has no plan: EXPLAIN supports SELECT, TABLE, WITH, INSERT, UPDATE, DELETE and REPLACE.";

    /// <summary>Gets or sets the text after EXPLAIN; {0} = line, {1} = duration.</summary>
    public string PlanReady { get; set; } = "Estimated plan of the statement at line {0} in {1}; the statement did not run.";

    /// <summary>Gets or sets the text when the plan could not be read; {0} = message.</summary>
    public string PlanUnreadable { get; set; } = "The plan from the server could not be read: {0}";

    /// <summary>Gets or sets the text when Explain Analyze does not run the statement; {0} = line.</summary>
    public string NotAnalyzable { get; set; } = "Explain Analyze runs the statement, so it only takes a SELECT; the statement at line {0} is not one.";

    /// <summary>Gets or sets the text when the server has no EXPLAIN ANALYZE; {0} = server version.</summary>
    public string AnalyzeNotSupported { get; set; } = "Explain Analyze needs MySQL 8.0.18 or later, or MariaDB; this server is {0}.";

    /// <summary>Gets or sets the text after Explain Analyze; {0} = line, {1} = duration.</summary>
    public string PlanAnalyzed { get; set; } = "Plan of the statement at line {0} with the actual rows and times, in {1}; the statement ran.";
}

/// <summary>Formats values for the result grid and the clipboard (US-0703).</summary>
public static class QueryValueFormatter
{
    /// <summary>Text of a NULL value.</summary>
    public const string NullText = "NULL";

    /// <summary>Maximum number of bytes shown of a binary value.</summary>
    public const int MaxBinaryBytes = 64;

    /// <summary>Returns the text of <paramref name="value"/>: invariant numbers, ISO dates, hex for binary data.</summary>
    public static string Format(object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return NullText;
            case string text:
                return text;
            case byte[] bytes:
                return Hex(bytes);
            case bool flag:
                return flag ? "1" : "0";
            case DateTime date:
                // DATE columns arrive as text (see MySqlQuerySession), so a DateTime always has a time part.
                return date.ToString(date.Ticks % TimeSpan.TicksPerSecond == 0 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture);
            case TimeSpan time:
                return (time < TimeSpan.Zero ? "-" : string.Empty)
                    + ((long)Math.Abs(time.TotalHours)).ToString("00", CultureInfo.InvariantCulture)
                    + time.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture)
                    + (time.Ticks % TimeSpan.TicksPerSecond == 0 ? string.Empty : time.ToString(@"\.FFFFFF", CultureInfo.InvariantCulture));
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString() ?? string.Empty;
        }
    }

    private static string Hex(byte[] bytes)
    {
        var shown = Math.Min(bytes.Length, MaxBinaryBytes);
        var builder = new StringBuilder(2 + (shown * 2) + 3);
        builder.Append("0x");
        for (var i = 0; i < shown; i++)
        {
            builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        if (bytes.Length > shown)
        {
            builder.Append("...");
        }

        return builder.ToString();
    }
}
