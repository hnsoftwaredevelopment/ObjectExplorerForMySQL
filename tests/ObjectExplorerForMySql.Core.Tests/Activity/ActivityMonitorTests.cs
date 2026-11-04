using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Activity;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Activity;

// US-1501: the activity monitor.
public class ActivityMonitorTests
{
    private readonly FakeActivitySource _source = new();
    private readonly ActivityMonitorViewModel _model;

    public ActivityMonitorTests()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());
        var server = explorer.AddServer(TestExplorer.Definition("Prod"));
        _model = new ActivityMonitorViewModel(server, _source, new ActivityTexts(), new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));
    }

    private static SessionInfo Session(long id, string command, long time, string? statement = null) =>
        new(id, "app", "10.0.0.5:51234", "sakila", command, time, null, statement);

    [Fact]
    public async Task Refresh_ActiveFirst_LongestRunningOnTop()
    {
        _source.Sessions = [Session(1, "Sleep", 900), Session(2, "Query", 3, "SELECT 1"), Session(3, "Query", 120, "SELECT SLEEP(200)"), Session(4, "Sleep", 5)];

        await _model.RefreshAsync();

        Assert.Equal([3L, 2L, 1L, 4L], _model.Sessions.Select(s => s.Id));
        Assert.StartsWith("4 sessions, 2 active. Refreshed at ", _model.Summary, StringComparison.Ordinal);
        Assert.False(_model.HasError);
    }

    [Fact]
    public async Task HideSleeping_LeavesOutIdleSessions()
    {
        _source.Sessions = [Session(1, "Sleep", 900), Session(2, "Query", 3), Session(3, "Daemon", 5000)];
        await _model.RefreshAsync();

        _model.HideSleeping = true;

        Assert.Equal([3L, 2L], _model.Sessions.Select(s => s.Id));
        Assert.StartsWith("2 sessions, 2 active.", _model.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_Failure_ShowsTheErrorAndStopsAutoRefresh()
    {
        _source.LoadError = new InvalidOperationException("Access denied; Password=secret");

        await _model.RefreshAsync();

        Assert.True(_model.HasError);
        Assert.StartsWith("Reading the sessions failed: Access denied;", _model.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", _model.Error, StringComparison.Ordinal);
        Assert.False(_model.AutoRefresh);
        Assert.False(_model.IsLoading);
    }

    [Fact]
    public async Task Kill_RunsKillAndRefreshes()
    {
        _source.Sessions = [Session(7, "Query", 60, "UPDATE t SET a = 1")];
        await _model.RefreshAsync();
        _source.Sessions = [];

        await _model.KillAsync(_model.Sessions[0], KillKind.Query);

        Assert.Equal([(7L, KillKind.Query)], _source.Kills);
        Assert.Empty(_model.Sessions);
        Assert.Equal(2, _source.Loads);
    }

    [Fact]
    public async Task Kill_Failure_ShowsTheError()
    {
        _source.Sessions = [Session(7, "Query", 60)];
        await _model.RefreshAsync();
        _source.KillError = new InvalidOperationException("You are not owner of thread 7");

        await _model.KillAsync(_model.Sessions[0], KillKind.Connection);

        Assert.Equal("Stopping session 7 failed: You are not owner of thread 7", _model.Error);
        Assert.Equal(1, _source.Loads);
    }

    [Theory]
    [InlineData(KillKind.Query, "KILL QUERY 42")]
    [InlineData(KillKind.Connection, "KILL CONNECTION 42")]
    public void KillSql_TakesTheNumericId(KillKind kind, string expected) =>
        Assert.Equal(expected, MySqlActivitySource.KillSql(42, kind));

    [Fact]
    public void Sleeping_IsTheSleepCommand()
    {
        Assert.True(Session(1, "Sleep", 1).IsSleeping);
        Assert.True(Session(1, "sleep", 1).IsSleeping);
        Assert.False(Session(1, "Query", 1).IsSleeping);
        Assert.False(Session(1, "Daemon", 1).IsSleeping);
    }

    private sealed class FakeActivitySource : IActivitySource
    {
        public IReadOnlyList<SessionInfo> Sessions { get; set; } = [];

        public Exception? LoadError { get; set; }

        public Exception? KillError { get; set; }

        public int Loads { get; private set; }

        public List<(long Id, KillKind Kind)> Kills { get; } = [];

        public Task<IReadOnlyList<SessionInfo>> LoadAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
        {
            Loads++;
            return LoadError is { } error ? Task.FromException<IReadOnlyList<SessionInfo>>(error) : Task.FromResult(Sessions);
        }

        public Task KillAsync(ConnectionDefinition definition, long id, KillKind kind, CancellationToken cancellationToken)
        {
            if (KillError is { } error)
            {
                return Task.FromException(error);
            }

            Kills.Add((id, kind));
            return Task.CompletedTask;
        }
    }
}
