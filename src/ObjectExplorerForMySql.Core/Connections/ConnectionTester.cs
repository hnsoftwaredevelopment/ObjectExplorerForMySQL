using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Connections;

/// <summary>Result of a connection test.</summary>
public sealed class ConnectionTestResult
{
    private ConnectionTestResult(bool succeeded, string? serverVersion, bool isSupportedVersion, string? errorMessage, TimeSpan duration)
    {
        Succeeded = succeeded;
        ServerVersion = serverVersion;
        IsSupportedVersion = isSupportedVersion;
        ErrorMessage = errorMessage;
        Duration = duration;
    }

    /// <summary>Gets a value indicating whether the connection could be opened.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the version reported by the server, for example "8.0.36" or "10.11.6-MariaDB".</summary>
    public string? ServerVersion { get; }

    /// <summary>Gets a value indicating whether <see cref="ServerVersion"/> is supported (ADR-008).</summary>
    public bool IsSupportedVersion { get; }

    /// <summary>Gets the error message (passwords masked) when the test failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Gets how long the test took.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Creates a successful result.</summary>
    public static ConnectionTestResult Success(string serverVersion, TimeSpan duration) =>
        new(true, serverVersion, ProductInfo.IsSupportedServerVersion(serverVersion), null, duration);

    /// <summary>Creates a failed result.</summary>
    public static ConnectionTestResult Failure(string errorMessage, TimeSpan duration) =>
        new(false, null, false, SensitiveDataMasker.MaskText(errorMessage), duration);
}

/// <summary>Opens a connection to check that a <see cref="ConnectionDefinition"/> works.</summary>
public interface IConnectionTester
{
    /// <summary>
    /// Tries to connect. Returns a failed result for connection problems; throws
    /// <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<ConnectionTestResult> TestAsync(ConnectionDefinition definition, CancellationToken cancellationToken);
}

/// <summary><see cref="IConnectionTester"/> that uses MySqlConnector.</summary>
public sealed class MySqlConnectionTester : IConnectionTester
{
    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MySqlConnectionTester"/> class.</summary>
    public MySqlConnectionTester(IExtensionLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = ConnectionStringFactory.Build(definition, pooling: false);
        _logger.Info($"Testing connection to {definition.Host}:{definition.Port} as '{definition.UserName}'.");
        _logger.Debug($"Connection string: {connectionString}");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var version = connection.ServerVersion;
            var result = ConnectionTestResult.Success(version, stopwatch.Elapsed);
            if (result.IsSupportedVersion)
            {
                _logger.Info($"Connection test succeeded in {stopwatch.ElapsedMilliseconds} ms. Server version {version}.");
            }
            else
            {
                _logger.Warning($"Connection test succeeded, but server version {version} is not supported.");
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("Connection test cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Connection test to {definition.Host}:{definition.Port} failed after {stopwatch.ElapsedMilliseconds} ms.", ex);
            return ConnectionTestResult.Failure(ex.Message, stopwatch.Elapsed);
        }
    }
}
