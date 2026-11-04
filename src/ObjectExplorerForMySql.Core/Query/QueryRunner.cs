using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// Runs the statements of a query document and keeps the results and messages for the results pane (US-0703).
/// One runner per document; it keeps the server session open between runs (decision D-11) and stops at the first error.
/// </summary>
public sealed class QueryRunner : ObservableObject, IDisposable
{
    /// <summary>Default maximum number of rows shown per result set.</summary>
    public const int DefaultMaxRows = 1000;

    private readonly ExplorerViewModel _explorer;
    private readonly Func<string> _filePath;
    private readonly Func<ConnectionDefinition, IQuerySession> _sessionFactory;
    private readonly QueryTexts _texts;
    private readonly IExtensionLogger _logger;
    private IQuerySession? _session;
    private string? _sessionDatabase;
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;
    private bool _hasRun;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="QueryRunner"/> class.</summary>
    /// <param name="explorer">Servers and document links.</param>
    /// <param name="filePath">Returns the current path of the document (it changes after Save As).</param>
    /// <param name="sessionFactory">Creates a session for a connection.</param>
    /// <param name="texts">Texts of the Messages tab.</param>
    /// <param name="logger">Logger.</param>
    public QueryRunner(ExplorerViewModel explorer, Func<string> filePath, Func<ConnectionDefinition, IQuerySession> sessionFactory, QueryTexts texts, IExtensionLogger logger)
    {
        _explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Raised on the calling thread when a run starts, so the extension can show the results pane.</summary>
    public event EventHandler? RunStarted;

    /// <summary>Gets or sets the maximum number of rows shown per result set.</summary>
    public int MaxRows { get; set; } = DefaultMaxRows;

    /// <summary>Gets the result sets of the last run.</summary>
    public ObservableCollection<QueryResultSet> ResultSets { get; } = [];

    /// <summary>Gets the messages of the last run.</summary>
    public ObservableCollection<QueryMessage> Messages { get; } = [];

    /// <summary>Gets the steps of the last estimated plan (US-1301); empty after a normal run.</summary>
    public ObservableCollection<PlanNode> Plan { get; } = [];

    /// <summary>Gets or sets the texts of the Plan tab.</summary>
    public PlanTexts PlanTexts { get; set; } = new();

    /// <summary>Gets a value indicating whether a run is busy.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    /// <summary>Gets a value indicating whether no run is busy.</summary>
    public bool IsIdle => !_isRunning;

    /// <summary>Gets a value indicating whether a run took place; the results pane shows from then on.</summary>
    public bool HasRun
    {
        get => _hasRun;
        private set => SetProperty(ref _hasRun, value);
    }

    /// <summary>
    /// Runs the statements in <paramref name="script"/>. <paramref name="firstLine"/> is the 0-based document line where
    /// the script starts (the selection), so error lines match the editor. Does nothing while a run is busy.
    /// </summary>
    public async Task ExecuteAsync(string script, int firstLine)
    {
        if (_isRunning || _disposed)
        {
            return;
        }

        ResultSets.Clear();
        Messages.Clear();
        Plan.Clear();
        HasRun = true;
        RunStarted?.Invoke(this, EventArgs.Empty);

        var statements = SqlScriptSplitter.Split(script ?? string.Empty);
        if (statements.Count == 0)
        {
            Add(QueryMessageKind.Info, _texts.NothingToRun);
            return;
        }

        var path = _filePath();
        var link = _explorer.Documents.Find(path);
        var server = link?.ConnectionId is { } id ? _explorer.Servers.FirstOrDefault(s => s.Definition.Id == id) : null;
        if (server is null)
        {
            Add(QueryMessageKind.Error, _texts.NoConnection);
            return;
        }

        IsRunning = true;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var stopwatch = Stopwatch.StartNew();
        var completed = 0;
        var failed = false;
        try
        {
            // Cancelled password prompt or failed connection (the message is already added): nothing more to report.
            if (!await server.EnsurePasswordAsync(cancellation.Token)
                || await GetSessionAsync(server.Definition, cancellation.Token) is not { } session)
            {
                return;
            }

            if (link!.Database is { } database && !string.Equals(database, _sessionDatabase, StringComparison.Ordinal))
            {
                await session.ChangeDatabaseAsync(database, cancellation.Token);
                _sessionDatabase = database;
            }

            foreach (var statement in statements)
            {
                try
                {
                    var result = await session.ExecuteAsync(statement.Text, MaxRows, cancellation.Token);
                    if (result.ResultSets.Count == 1 && result.ResultSets[0].IsTruncated && ExportStatement.CanRunAgain(statement.Text))
                    {
                        // US-0705: what Save Results As needs to run the statement again for all rows.
                        result.ResultSets[0].Source = new ResultSource(server.Definition, await CurrentDatabaseAsync(session), statement.Text);
                    }

                    Report(result);
                    completed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !cancellation.IsCancellationRequested)
                {
                    var code = ex is MySqlConnector.MySqlException mySql ? mySql.Number : 0;
                    var line = firstLine + statement.Line + 1;
                    Add(QueryMessageKind.Error, Format(_texts.Error, code, line, SensitiveDataMasker.MaskText(ex.Message)));
                    _logger.Info($"Query on '{server.Label}' failed at line {line}: error {code}.");
                    failed = true;
                    break;
                }
            }

            await FollowDatabaseAsync(session, path, server.Definition.Id);
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            Add(QueryMessageKind.Info, Format(_texts.Cancelled, Duration(stopwatch.Elapsed)));
            await FollowDatabaseAfterCancelAsync(path, server.Definition.Id);
            return;
        }
        catch (Exception ex)
        {
            // For example USE of a database that no longer exists, or a lost connection.
            Add(QueryMessageKind.Error, SensitiveDataMasker.MaskText(ex.Message));
            _logger.Warning($"Running a query on '{server.Label}' failed.", ex);
            failed = true;
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
            if (_session is { IsBroken: true })
            {
                CloseSession();
            }

            IsRunning = false;
        }

        Add(
            QueryMessageKind.Info,
            failed ? Format(_texts.Failed, Duration(stopwatch.Elapsed)) : Format(_texts.Completed, completed, Duration(stopwatch.Elapsed)));
    }

    /// <summary>
    /// Shows the estimated plan of the first statement in <paramref name="script"/> with <c>EXPLAIN FORMAT=JSON</c>
    /// (US-1301); the statement itself does not run. <paramref name="firstLine"/> is the 0-based document line where the
    /// script starts. Does nothing while a run is busy.
    /// </summary>
    public Task ExplainAsync(string script, int firstLine) => RunPlanAsync(script, firstLine, analyze: false);

    /// <summary>
    /// Runs the first statement in <paramref name="script"/> with <c>EXPLAIN ANALYZE</c> (MySQL 8.0.18+) or
    /// <c>ANALYZE FORMAT=JSON</c> (MariaDB) and shows the plan with the actual rows and times (US-1302). The statement
    /// really runs, so only a SELECT is accepted. Does nothing while a run is busy.
    /// </summary>
    public Task ExplainAnalyzeAsync(string script, int firstLine) => RunPlanAsync(script, firstLine, analyze: true);

    // MySQL 8.0.18 is the first version with EXPLAIN ANALYZE; MariaDB has ANALYZE FORMAT=JSON since 10.1.
    private static string? AnalyzeCommand(string? serverVersion)
    {
        var capabilities = Metadata.ServerCapabilities.FromVersion(serverVersion);
        return capabilities.IsMariaDb ? "ANALYZE FORMAT=JSON "
            : capabilities.Version >= new Version(8, 0, 18) ? "EXPLAIN ANALYZE "
            : null;
    }

    private async Task RunPlanAsync(string script, int firstLine, bool analyze)
    {
        if (_isRunning || _disposed)
        {
            return;
        }

        ResultSets.Clear();
        Messages.Clear();
        Plan.Clear();
        HasRun = true;
        RunStarted?.Invoke(this, EventArgs.Empty);

        var statement = SqlScriptSplitter.Split(script ?? string.Empty).FirstOrDefault();
        if (statement is null)
        {
            Add(QueryMessageKind.Info, _texts.NothingToRun);
            return;
        }

        var line = firstLine + statement.Line + 1;
        if (analyze ? !ExplainStatement.IsAnalyzable(statement.Text) : !ExplainStatement.IsExplainable(statement.Text))
        {
            Add(QueryMessageKind.Info, Format(analyze ? _texts.NotAnalyzable : _texts.NotExplainable, line));
            return;
        }

        var link = _explorer.Documents.Find(_filePath());
        var server = link?.ConnectionId is { } id ? _explorer.Servers.FirstOrDefault(s => s.Definition.Id == id) : null;
        if (server is null)
        {
            Add(QueryMessageKind.Error, _texts.NoConnection);
            return;
        }

        IsRunning = true;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!await server.EnsurePasswordAsync(cancellation.Token)
                || await GetSessionAsync(server.Definition, cancellation.Token) is not { } session)
            {
                return;
            }

            if (link!.Database is { } database && !string.Equals(database, _sessionDatabase, StringComparison.Ordinal))
            {
                await session.ChangeDatabaseAsync(database, cancellation.Token);
                _sessionDatabase = database;
            }

            var command = "EXPLAIN FORMAT=JSON ";
            if (analyze)
            {
                var version = server.ServerVersion ?? await ReadVersionAsync(session, cancellation.Token);
                if (AnalyzeCommand(version) is not { } analyzeCommand)
                {
                    Add(QueryMessageKind.Info, Format(_texts.AnalyzeNotSupported, version ?? "?"));
                    return;
                }

                command = analyzeCommand;
            }

            StatementResult result;
            try
            {
                result = await session.ExecuteAsync(command + statement.Text, 1, cancellation.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                var code = ex is MySqlConnector.MySqlException mySql ? mySql.Number : 0;
                Add(QueryMessageKind.Error, Format(_texts.Error, code, line, SensitiveDataMasker.MaskText(ex.Message)));
                _logger.Info($"Explain on '{server.Label}' failed at line {line}: error {code}.");
                return;
            }

            ShowPlan(result, line, stopwatch.Elapsed, analyze);
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            Add(QueryMessageKind.Info, Format(_texts.Cancelled, Duration(stopwatch.Elapsed)));
        }
        catch (Exception ex)
        {
            Add(QueryMessageKind.Error, SensitiveDataMasker.MaskText(ex.Message));
            _logger.Warning($"Explaining a query on '{server.Label}' failed.", ex);
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
            if (_session is { IsBroken: true })
            {
                CloseSession();
            }

            IsRunning = false;
        }
    }

    /// <summary>Stops the running statement (MySqlConnector sends KILL QUERY on a second connection).</summary>
    public void Cancel()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run just ended.
        }
    }

    /// <summary>Closes the server session, for example when the document is closed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
        CloseSession();
    }

    /// <summary>Formats a duration: "85 ms", "2.4 s" or "3:05 min".</summary>
    internal static string Duration(TimeSpan elapsed) =>
        elapsed.TotalSeconds < 1 ? ((long)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + " ms"
        : elapsed.TotalMinutes < 1 ? elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
        : ((int)elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture) + " min";

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    // Reuses the open session of the same connection; a changed or broken connection gets a new one.
    private async Task<IQuerySession?> GetSessionAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        if (_session is { } current && ReferenceEquals(current.Definition, definition) && !current.IsBroken)
        {
            return current;
        }

        CloseSession();
        var session = _sessionFactory(definition);
        try
        {
            await session.OpenAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            session.Dispose();
            Add(QueryMessageKind.Error, Format(_texts.ConnectFailed, SensitiveDataMasker.MaskText(ex.Message)));
            _logger.Warning($"Opening a query session on '{definition.Label}' failed.", ex);
            return null;
        }

        _session = session;
        _sessionDatabase = definition.DefaultDatabase;
        return session;
    }

    // A USE in the script changes the database of the session; the bar shows it.
    private async Task FollowDatabaseAsync(IQuerySession session, string path, Guid connectionId)
    {
        if (session.IsBroken)
        {
            return;
        }

        try
        {
            var current = await session.GetCurrentDatabaseAsync(CancellationToken.None);
            _sessionDatabase = current;
            if (current is not null && _explorer.Documents.Find(path)?.Database != current)
            {
                _explorer.Documents.Register(path, connectionId, current);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Reading the current database after a query failed: {ex.Message}");
        }
    }

    // The database a statement ran in; a USE earlier in the same script may have changed it.
    private async Task<string?> CurrentDatabaseAsync(IQuerySession session)
    {
        try
        {
            return await session.GetCurrentDatabaseAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Debug($"Reading the current database for a full export failed: {ex.Message}");
            return _sessionDatabase;
        }
    }

    private Task FollowDatabaseAfterCancelAsync(string path, Guid connectionId) =>
        _session is { } session ? FollowDatabaseAsync(session, path, connectionId) : Task.CompletedTask;

    private void Report(StatementResult result)
    {
        foreach (var set in result.ResultSets)
        {
            ResultSets.Add(set);
            Add(
                QueryMessageKind.Info,
                set.IsTruncated ? Format(_texts.RowsTruncated, set.TotalRows, set.Rows.Count) : Format(_texts.RowsReturned, set.TotalRows));
        }

        if (result.ResultSets.Count == 0 && result.RecordsAffected >= 0)
        {
            Add(QueryMessageKind.Info, Format(_texts.RowsAffected, result.RecordsAffected));
        }

        foreach (var warning in result.Warnings)
        {
            Add(QueryMessageKind.Warning, Format(_texts.Warning, warning.Level, warning.Code, warning.Message));
        }
    }

    private static async Task<string?> ReadVersionAsync(IQuerySession session, CancellationToken cancellationToken)
    {
        var result = await session.ExecuteAsync("SELECT VERSION()", 1, cancellationToken);
        return result.ResultSets.FirstOrDefault() is { Rows.Count: > 0 } set ? set.Rows[0][0] as string : null;
    }

    // EXPLAIN returns one row with one column that holds the plan: JSON, or the text tree of MySQL EXPLAIN ANALYZE.
    private void ShowPlan(StatementResult result, int line, TimeSpan elapsed, bool analyze)
    {
        var value = result.ResultSets.FirstOrDefault() is { Rows.Count: > 0 } set && set.Rows[0].Length > 0 ? set.Rows[0][0] : null;
        var text = (value is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : value as string) ?? string.Empty;
        try
        {
            var nodes = text.TrimStart().StartsWith("{", StringComparison.Ordinal)
                ? ExplainPlanParser.Parse(text, PlanTexts)
                : AnalyzeTreeParser.Parse(text, PlanTexts);
            foreach (var node in nodes)
            {
                Plan.Add(node);
            }
        }
        catch (FormatException ex)
        {
            Add(QueryMessageKind.Error, Format(_texts.PlanUnreadable, ex.Message));
            _logger.Warning("Reading an EXPLAIN plan failed.", ex);
            return;
        }

        Add(QueryMessageKind.Info, Format(analyze ? _texts.PlanAnalyzed : _texts.PlanReady, line, Duration(elapsed)));
        foreach (var warning in result.Warnings)
        {
            Add(QueryMessageKind.Warning, Format(_texts.Warning, warning.Level, warning.Code, warning.Message));
        }
    }

    private void Add(QueryMessageKind kind, string text) => Messages.Add(new QueryMessage(kind, text));

    private void CloseSession()
    {
        var session = _session;
        _session = null;
        _sessionDatabase = null;
        try
        {
            session?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Debug($"Closing a query session failed: {ex.Message}");
        }
    }
}
