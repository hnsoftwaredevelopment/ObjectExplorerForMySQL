using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public class QueryRunnerTests
{
    private const string Path = @"C:\Temp\Query1.sql";

    private readonly FakePasswordPrompt _prompt = new();
    private readonly ExplorerViewModel _explorer;
    private readonly ServerNodeViewModel _server;
    private readonly List<FakeSession> _sessions = [];
    private readonly QueryRunner _runner;

    public QueryRunnerTests()
    {
        _explorer = new ExplorerViewModel(TestExplorer.Context(prompt: _prompt));
        _server = _explorer.AddServer(TestExplorer.Definition("Prod"));
        _explorer.Documents.Register(Path, _server.Definition.Id, "sakila");
        _runner = new QueryRunner(_explorer, () => Path, Create, new QueryTexts(), new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));
    }

    private FakeSession Session => _sessions.Last();

    private readonly Queue<FakeSession> _prepared = new();

    private FakeSession Add(FakeSession session)
    {
        _sessions.Add(session);
        return session;
    }

    private IQuerySession Create(ConnectionDefinition definition) =>
        Add(_prepared.Count > 0 ? _prepared.Dequeue() : new FakeSession(definition));

    private string[] Messages => _runner.Messages.Select(m => $"{m.Kind}: {m.Text}").ToArray();

    [Fact]
    public async Task Run_ExecutesEachStatement_InTheLinkedDatabase()
    {
        await _runner.ExecuteAsync("SELECT 1; UPDATE t SET a = 1;", 0);

        Assert.Equal(["USE sakila", "SELECT 1", "UPDATE t SET a = 1"], Session.Log);
        Assert.Single(_runner.ResultSets);
        Assert.Equal("Info: 1 row(s) returned.", Messages[0]);
        Assert.Equal("Info: 3 row(s) affected.", Messages[1]);
        Assert.StartsWith("Info: 2 statement(s) completed in ", Messages[2], StringComparison.Ordinal);
        Assert.False(_runner.IsRunning);
        Assert.True(_runner.HasRun);
    }

    [Fact]
    public async Task Run_KeepsTheSessionOpenBetweenRuns()
    {
        await _runner.ExecuteAsync("SET @x = 1;", 0);
        await _runner.ExecuteAsync("SELECT @x;", 0);

        Assert.Single(_sessions);
        Assert.Equal(["USE sakila", "SET @x = 1", "SELECT @x"], Session.Log);
    }

    [Fact]
    public async Task Run_StopsAtTheFirstError_WithTheEditorLine()
    {
        Session0().Fail = sql => sql.StartsWith("BAD", StringComparison.Ordinal) ? new InvalidOperationException("You have an error; Password=secret") : null;

        await _runner.ExecuteAsync("SELECT 1;\n\nBAD SQL;\nSELECT 3;", firstLine: 10);

        Assert.DoesNotContain("SELECT 3", Session.Log);
        Assert.Contains(Messages, m => m.StartsWith("Error: Error 0 at line 13: You have an error;", StringComparison.Ordinal) && !m.Contains("secret", StringComparison.Ordinal));
        Assert.StartsWith("Info: Stopped after an error in ", Messages.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_ReportsTruncatedResultsAndWarnings()
    {
        Session0().Result = _ => new StatementResult(
            [new QueryResultSet(["a"], [[1], [2]], totalRows: 5000)],
            -1,
            [new QueryWarning("Warning", 1292, "Truncated incorrect value")]);

        await _runner.ExecuteAsync("SELECT a FROM t", 0);

        Assert.Equal("Info: 5000 row(s) returned; the first 2 are shown.", Messages[0]);
        Assert.Equal("Warning: Warning 1292: Truncated incorrect value", Messages[1]);
    }

    [Fact]
    public async Task Run_TruncatedSelect_KeepsTheStatementForAFullExport()
    {
        var session = Session0();
        session.Result = sql => sql.StartsWith("SELECT", StringComparison.Ordinal)
            ? new StatementResult([new QueryResultSet(["a"], [[1]], totalRows: 5000)], -1, [])
            : new StatementResult([], 0, []);
        session.CurrentDatabase = "world";

        await _runner.ExecuteAsync("USE world;\nSELECT a FROM t", 0);

        var source = Assert.Single(_runner.ResultSets).Source;
        Assert.NotNull(source);
        Assert.Equal("SELECT a FROM t", source.Statement);
        Assert.Equal("world", source.Database);
        Assert.Equal(_server.Definition.Id, source.Connection.Id);
    }

    [Theory]
    [InlineData("SELECT a FROM t", 2)]
    [InlineData("CALL p()", 5000)]
    [InlineData("SELECT a INTO OUTFILE '/tmp/a' FROM t", 5000)]
    public async Task Run_NoFullExport_WhenNotCutOffOrNotSafeToRunAgain(string statement, long total)
    {
        Session0().Result = _ => new StatementResult([new QueryResultSet(["a"], [[1], [2]], total)], -1, []);

        await _runner.ExecuteAsync(statement, 0);

        Assert.Null(Assert.Single(_runner.ResultSets).Source);
    }

    [Fact]
    public async Task Run_WithoutConnection_SaysSo()
    {
        _explorer.Documents.Register(Path, null, null);

        await _runner.ExecuteAsync("SELECT 1", 0);

        Assert.Equal(["Error: Choose a connection in the bar above the editor."], Messages);
        Assert.Empty(_sessions);
    }

    [Fact]
    public async Task Run_NothingToRun()
    {
        await _runner.ExecuteAsync("-- just a comment", 0);

        Assert.Equal(["Info: There is no statement to run."], Messages);
    }

    [Fact]
    public async Task Run_CancelledPasswordPrompt_RunsNothing()
    {
        _server.Definition.Password = null;

        await _runner.ExecuteAsync("SELECT 1", 0);

        Assert.Equal(1, _prompt.Calls);
        Assert.Empty(_sessions);
        Assert.Empty(Messages);
    }

    [Fact]
    public async Task Run_ConnectFailure_IsReported_AndTheNextRunTriesAgain()
    {
        var first = true;
        var runner = new QueryRunner(_explorer, () => Path, d => Add(new FakeSession(d) { FailOpen = first }), new QueryTexts(), new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));

        await runner.ExecuteAsync("SELECT 1", 0);
        Assert.StartsWith("Connecting failed: ", runner.Messages[0].Text, StringComparison.Ordinal);
        Assert.Single(runner.Messages);

        first = false;
        await runner.ExecuteAsync("SELECT 1", 0);
        Assert.Equal(2, _sessions.Count);
        Assert.Contains("SELECT 1", Session.Log);
    }

    [Fact]
    public async Task Use_InTheScript_UpdatesTheLink()
    {
        Session0().CurrentDatabase = "world";

        await _runner.ExecuteAsync("USE world;", 0);

        Assert.Equal(new DocumentLink(_server.Definition.Id, "world"), _explorer.Documents.Find(Path));
    }

    [Fact]
    public async Task Cancel_StopsTheRun_AndKeepsTheSession()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Session0().Block = gate.Task;

        var run = _runner.ExecuteAsync("SELECT SLEEP(100); SELECT 2;", 0);
        Assert.True(_runner.IsRunning);
        _runner.Cancel();
        await run;

        Assert.StartsWith("Info: Cancelled after ", Messages.Last(), StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT 2", Session.Log);
        Assert.False(_runner.IsRunning);
        Assert.False(Session.Disposed);
    }

    [Fact]
    public async Task ChangedConnection_OpensANewSession()
    {
        await _runner.ExecuteAsync("SELECT 1", 0);
        var other = _explorer.AddServer(TestExplorer.Definition("Test"));
        _explorer.Documents.Register(Path, other.Definition.Id, null);

        await _runner.ExecuteAsync("SELECT 2", 0);

        Assert.Equal(2, _sessions.Count);
        Assert.True(_sessions[0].Disposed);
        Assert.Same(other.Definition, Session.Definition);
    }

    [Fact]
    public async Task Dispose_ClosesTheSession()
    {
        await _runner.ExecuteAsync("SELECT 1", 0);
        _runner.Dispose();

        Assert.All(_sessions, s => Assert.True(s.Disposed));
    }

    [Theory]
    [InlineData(85, "85 ms")]
    [InlineData(2400, "2.4 s")]
    [InlineData(185000, "3:05 min")]
    public void Duration_IsReadable(int milliseconds, string expected)
    {
        Assert.Equal(expected, QueryRunner.Duration(TimeSpan.FromMilliseconds(milliseconds)));
    }

    // Prepares the session the next run creates.
    private FakeSession Session0()
    {
        var pending = new FakeSession(_server.Definition);
        _prepared.Enqueue(pending);
        return pending;
    }

    [Fact]
    public async Task Explain_RunsExplainJson_AndShowsThePlan()
    {
        _runner.ResultSets.Add(new QueryResultSet(["x"], [], 0));
        _prepared.Enqueue(new FakeSession(_server.Definition)
        {
            Result = _ => new StatementResult(
                [new QueryResultSet(["EXPLAIN"], [["{\"query_block\":{\"select_id\":1,\"table\":{\"table_name\":\"t\",\"access_type\":\"ALL\"}}}"]], 1)],
                -1,
                [new QueryWarning("Note", 1003, "/* select#1 */ select ...")]),
        });

        await _runner.ExplainAsync("SELECT * FROM t; SELECT 2;", firstLine: 4);

        Assert.Equal(["USE sakila", "EXPLAIN FORMAT=JSON SELECT * FROM t"], Session.Log);
        Assert.Empty(_runner.ResultSets);
        Assert.Equal("Table t", Assert.Single(Assert.Single(_runner.Plan).Children).Title);
        Assert.StartsWith("Info: Estimated plan of the statement at line 5 in ", Messages[0], StringComparison.Ordinal);
        Assert.Equal("Warning: Note 1003: /* select#1 */ select ...", Messages[1]);
        Assert.False(_runner.IsRunning);
    }

    [Fact]
    public async Task Explain_StatementWithoutPlan_RunsNothing()
    {
        await _runner.ExplainAsync("\nCREATE TABLE t (a INT)", 0);

        Assert.Empty(_sessions);
        Assert.Equal("Info: The statement at line 2 has no plan: EXPLAIN supports SELECT, TABLE, WITH, INSERT, UPDATE, DELETE and REPLACE.", Assert.Single(Messages));
    }

    [Fact]
    public async Task Explain_ServerError_ReportsLine()
    {
        _prepared.Enqueue(new FakeSession(_server.Definition) { Fail = sql => sql.StartsWith("EXPLAIN", StringComparison.Ordinal) ? new InvalidOperationException("Table 'x' doesn't exist") : null });

        await _runner.ExplainAsync("SELECT * FROM x", firstLine: 2);

        Assert.Empty(_runner.Plan);
        Assert.Equal("Error: Error 0 at line 3: Table 'x' doesn't exist", Assert.Single(Messages));
    }

    [Fact]
    public async Task Explain_UnreadablePlan_IsAnError()
    {
        _prepared.Enqueue(new FakeSession(_server.Definition) { Result = _ => new StatementResult([new QueryResultSet(["EXPLAIN"], [["not json"]], 1)], -1, []) });

        await _runner.ExplainAsync("SELECT 1", 0);

        Assert.Empty(_runner.Plan);
        Assert.StartsWith("Error: The plan from the server could not be read: ", Assert.Single(Messages), StringComparison.Ordinal);
    }

    private static Func<string, StatementResult> Server(string version, string plan) => sql =>
        new StatementResult([new QueryResultSet(["x"], [[sql == "SELECT VERSION()" ? version : plan]], 1)], -1, []);

    [Fact]
    public async Task Analyze_MySql_RunsExplainAnalyze()
    {
        _prepared.Enqueue(new FakeSession(_server.Definition)
        {
            Result = Server("8.0.36", "-> Table scan on t  (cost=1 rows=10) (actual time=0.1..0.2 rows=10 loops=1)"),
        });

        await _runner.ExplainAnalyzeAsync("SELECT * FROM t", 0);

        Assert.Equal(["USE sakila", "SELECT VERSION()", "EXPLAIN ANALYZE SELECT * FROM t"], Session.Log);
        Assert.Equal("Table scan on t", Assert.Single(_runner.Plan).Title);
        Assert.StartsWith("Info: Plan of the statement at line 1 with the actual rows and times, in ", Assert.Single(Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_MariaDb_RunsAnalyzeJson()
    {
        _prepared.Enqueue(new FakeSession(_server.Definition)
        {
            Result = Server("10.11.6-MariaDB", "{\"query_block\":{\"select_id\":1,\"table\":{\"table_name\":\"t\",\"access_type\":\"ALL\",\"rows\":10,\"r_rows\":10}}}"),
        });

        await _runner.ExplainAnalyzeAsync("SELECT * FROM t", 0);

        Assert.Equal("ANALYZE FORMAT=JSON SELECT * FROM t", Session.Log.Last());
        Assert.Equal("Table t", Assert.Single(Assert.Single(_runner.Plan).Children).Title);
    }

    [Fact]
    public async Task Analyze_OldMySql_RunsNothing()
    {
        _prepared.Enqueue(new FakeSession(_server.Definition) { Result = Server("8.0.17", string.Empty) });

        await _runner.ExplainAnalyzeAsync("SELECT * FROM t", 0);

        Assert.Equal("SELECT VERSION()", Session.Log.Last());
        Assert.Equal("Info: Explain Analyze needs MySQL 8.0.18 or later, or MariaDB; this server is 8.0.17.", Assert.Single(Messages));
    }

    [Fact]
    public async Task Analyze_Update_RunsNothing()
    {
        await _runner.ExplainAnalyzeAsync("UPDATE t SET a = 1", 0);

        Assert.Empty(_sessions);
        Assert.Equal("Info: Explain Analyze runs the statement, so it only takes a SELECT; the statement at line 1 is not one.", Assert.Single(Messages));
    }

    [Fact]
    public async Task Execute_ClearsThePlan()
    {
        _prepared.Enqueue(new FakeSession(_server.Definition)
        {
            Result = sql => sql.StartsWith("EXPLAIN", StringComparison.Ordinal)
                ? new StatementResult([new QueryResultSet(["EXPLAIN"], [["{\"query_block\":{\"select_id\":1}}"]], 1)], -1, [])
                : new StatementResult([new QueryResultSet(["x"], [[1]], 1)], -1, []),
        });
        await _runner.ExplainAsync("SELECT 1", 0);
        Assert.Single(_runner.Plan);

        await _runner.ExecuteAsync("SELECT 1", 0);

        Assert.Empty(_runner.Plan);
        Assert.Single(_runner.ResultSets);
    }

    private sealed class FakeSession(ConnectionDefinition definition) : IQuerySession
    {
        public ConnectionDefinition Definition { get; } = definition;

        public List<string> Log { get; } = [];

        public bool FailOpen { get; init; }

        public bool Disposed { get; private set; }

        public string? CurrentDatabase { get; set; } = "sakila";

        public Func<string, Exception?> Fail { get; set; } = _ => null;

        public Func<string, StatementResult>? Result { get; set; }

        public Task? Block { get; set; }

        public bool IsBroken => Disposed;

        public Task OpenAsync(CancellationToken cancellationToken) =>
            FailOpen ? Task.FromException(new InvalidOperationException("Unable to connect")) : Task.CompletedTask;

        public Task ChangeDatabaseAsync(string database, CancellationToken cancellationToken)
        {
            Log.Add("USE " + database);
            return Task.CompletedTask;
        }

        public Task<string?> GetCurrentDatabaseAsync(CancellationToken cancellationToken) => Task.FromResult(CurrentDatabase);

        public async Task<StatementResult> ExecuteAsync(string sql, int maxRows, CancellationToken cancellationToken)
        {
            Log.Add(sql);
            if (Block is { } block)
            {
                await Task.WhenAny(block, Task.Delay(Timeout.Infinite, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (Fail(sql) is { } error)
            {
                throw error;
            }

            if (Result is { } result)
            {
                return result(sql);
            }

            return sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                ? new StatementResult([new QueryResultSet(["x"], [[1]], 1)], -1, [])
                : new StatementResult([], sql.StartsWith("SET", StringComparison.Ordinal) || sql.StartsWith("USE", StringComparison.Ordinal) ? 0 : 3, []);
        }

        public void Dispose() => Disposed = true;
    }
}
