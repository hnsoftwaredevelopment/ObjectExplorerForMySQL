using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Connections;

public class ConnectionTesterTests
{
    [Fact]
    public async Task TestAsync_ReturnsFailureWhenNothingListens()
    {
        var sink = new ListSink();
        var tester = new MySqlConnectionTester(new ExtensionLogger(sink, LogLevel.Debug));
        var definition = new ConnectionDefinition
        {
            Host = "127.0.0.1",
            Port = GetUnusedPort(),
            UserName = "root",
            Password = "Secret123",
            ConnectionTimeoutSeconds = 3,
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await tester.TestAsync(definition, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Null(result.ServerVersion);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Took {stopwatch.Elapsed}.");
        Assert.DoesNotContain(sink.Entries, entry => entry.Contains("Secret123"));
        Assert.Contains(sink.Entries, entry => entry.Contains("Password=*****"));
    }

    [Fact]
    public async Task TestAsync_ThrowsWhenAlreadyCancelled()
    {
        var tester = new MySqlConnectionTester(new ExtensionLogger(new ListSink()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tester.TestAsync(new ConnectionDefinition { Host = "127.0.0.1", UserName = "root" }, cancellation.Token));
    }

    [Theory]
    [InlineData("8.0.36", true)]
    [InlineData("5.7.44", false)]
    [InlineData("10.11.6-MariaDB", true)]
    public void Success_FlagsUnsupportedVersions(string version, bool supported)
    {
        var result = ConnectionTestResult.Success(version, TimeSpan.Zero);

        Assert.True(result.Succeeded);
        Assert.Equal(supported, result.IsSupportedVersion);
    }

    [Fact]
    public void Failure_MasksPasswordInMessage()
    {
        var result = ConnectionTestResult.Failure("Bad: Server=x;Password=Secret123", TimeSpan.Zero);

        Assert.DoesNotContain("Secret123", result.ErrorMessage);
    }

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class ListSink : ILogSink
    {
        private readonly object _gate = new();
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToList();
                }
            }
        }

        public void Write(string formattedEntry)
        {
            lock (_gate)
            {
                _entries.Add(formattedEntry);
            }
        }
    }
}
