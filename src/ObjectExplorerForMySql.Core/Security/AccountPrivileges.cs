using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Security;

/// <summary>How <see cref="AccountPrivileges"/> was decided.</summary>
public enum PrivilegeSource
{
    /// <summary>From the lines of SHOW GRANTS (and the grants of the account's roles).</summary>
    Grants,

    /// <summary>SHOW GRANTS did not allow reading users; the test query on mysql.user decided.</summary>
    TestQuery,

    /// <summary>The check failed; nothing is allowed.</summary>
    Failed,
}

/// <summary>What the connected account may do with users (US-1001). Stored per connection on the server node.</summary>
public sealed record AccountPrivileges(bool CanReadUsers, bool CanManageUsers, PrivilegeSource Source)
{
    /// <summary>Gets the result when the check failed: nothing allowed.</summary>
    public static AccountPrivileges None { get; } = new(false, false, PrivilegeSource.Failed);

    /// <summary>
    /// Returns true when the grants allow SELECT on mysql.user: SELECT or ALL on *.* (and not revoked on mysql), on
    /// mysql.*, or on the table mysql.user. Reading users needs this; CREATE USER alone does not allow listing users.
    /// </summary>
    public static bool AllowsReadingUsers(IEnumerable<GrantEntry> grants)
    {
        var list = (grants ?? throw new ArgumentNullException(nameof(grants))).ToList();
        bool IsMysql(GrantEntry g) => string.Equals(g.Database, "mysql", StringComparison.OrdinalIgnoreCase);
        var revokedOnMysql = list.Any(g => g.IsRevoke && g.Level == GrantLevel.Database && IsMysql(g) && g.Has("SELECT"));

        var granted = list.Where(g => !g.IsRevoke && g.Has("SELECT")).ToList();
        return (granted.Any(g => g.Level == GrantLevel.Global) && !revokedOnMysql)
            || granted.Any(g => g.Level == GrantLevel.Database && IsMysql(g))
            || granted.Any(g => g.Level == GrantLevel.Table && IsMysql(g) && string.Equals(g.ObjectName, "user", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns true when the grants contain the global CREATE USER privilege (or ALL on *.*).</summary>
    public static bool AllowsManagingUsers(IEnumerable<GrantEntry> grants) =>
        (grants ?? throw new ArgumentNullException(nameof(grants))).Any(g => !g.IsRevoke && g.Level == GrantLevel.Global && g.Has("CREATE USER"));

    /// <summary>Returns the roles granted to the account (lines without ON).</summary>
    public static IReadOnlyList<string> RolesOf(IEnumerable<GrantEntry> grants) =>
        (grants ?? throw new ArgumentNullException(nameof(grants)))
            .Where(g => g.Level == GrantLevel.Role && !g.IsRevoke)
            .SelectMany(g => g.Roles)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

/// <summary>Runs the queries of the privilege check (US-1001); a fake in the tests.</summary>
public interface IGrantSource
{
    /// <summary>Returns the lines of <c>SHOW GRANTS FOR CURRENT_USER()</c>.</summary>
    Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the grants of the account's roles: MySQL <c>SHOW GRANTS FOR CURRENT_USER() USING ...</c>,
    /// MariaDB <c>SHOW GRANTS FOR role</c> per role.
    /// </summary>
    Task<IReadOnlyList<string>> GetRoleGrantsAsync(ConnectionDefinition definition, IReadOnlyList<string> roles, bool isMariaDb, CancellationToken cancellationToken);

    /// <summary>Runs <c>SELECT 1 FROM mysql.user LIMIT 0</c>; returns false when the server denies it.</summary>
    Task<bool> CanSelectUsersAsync(ConnectionDefinition definition, CancellationToken cancellationToken);
}

/// <summary>Decides <see cref="AccountPrivileges"/> after a connect (US-1001).</summary>
public sealed class PrivilegeChecker
{
    private readonly IGrantSource _source;
    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="PrivilegeChecker"/> class.</summary>
    public PrivilegeChecker(IGrantSource source, IExtensionLogger logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads SHOW GRANTS and decides what the account may do. Role grants are read too, so a role with CREATE USER counts.
    /// When the grants do not allow reading users (or SHOW GRANTS fails), the test query on mysql.user decides: it
    /// reflects the roles that are active in a new session. Never throws, except for cancellation.
    /// </summary>
    public async Task<AccountPrivileges> CheckAsync(ConnectionDefinition definition, string? serverVersion, CancellationToken cancellationToken)
    {
        var label = definition?.Label ?? throw new ArgumentNullException(nameof(definition));
        var read = false;
        var manage = false;
        var grantsRead = false;
        try
        {
            var grants = GrantParser.ParseAll(await _source.GetGrantsAsync(definition, cancellationToken), out var unknown).ToList();
            grantsRead = true;

            // Reading users counts only the account's own grants: a role may not be active in a new session, so for
            // roles the test query below decides. Managing also counts the grants of the roles.
            read = AccountPrivileges.AllowsReadingUsers(grants);
            var roles = AccountPrivileges.RolesOf(grants);
            if (roles.Count > 0)
            {
                try
                {
                    var isMariaDb = ServerCapabilities.FromVersion(serverVersion).IsMariaDb;
                    grants.AddRange(GrantParser.ParseAll(await _source.GetRoleGrantsAsync(definition, roles, isMariaDb, cancellationToken), out var unknownOfRoles));
                    unknown += unknownOfRoles;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Debug($"Reading the role grants on '{label}' failed: {SensitiveDataMasker.MaskText(ex.Message)}");
                }
            }

            manage = AccountPrivileges.AllowsManagingUsers(grants);
            _logger.Debug($"SHOW GRANTS on '{label}': {grants.Count} grant(s), {roles.Count} role(s), {unknown} line(s) not understood; read users {read}, manage users {manage}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning($"SHOW GRANTS on '{label}' failed; the test query decides: {SensitiveDataMasker.MaskText(ex.Message)}");
        }

        if (read)
        {
            return new AccountPrivileges(true, manage, PrivilegeSource.Grants);
        }

        try
        {
            if (await _source.CanSelectUsersAsync(definition, cancellationToken))
            {
                _logger.Debug($"The test query on mysql.user succeeded on '{label}'.");
                return new AccountPrivileges(true, manage, PrivilegeSource.TestQuery);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Debug($"The test query on mysql.user failed on '{label}': {SensitiveDataMasker.MaskText(ex.Message)}");
        }

        return grantsRead ? new AccountPrivileges(false, manage, PrivilegeSource.Grants) : AccountPrivileges.None;
    }
}

/// <summary><see cref="IGrantSource"/> on MySqlConnector.</summary>
public sealed class MySqlGrantSource : IGrantSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(definition, cancellationToken).ConfigureAwait(false);
        return await ReadLinesAsync(connection, "SHOW GRANTS FOR CURRENT_USER()", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRoleGrantsAsync(ConnectionDefinition definition, IReadOnlyList<string> roles, bool isMariaDb, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(definition, cancellationToken).ConfigureAwait(false);
        if (!isMariaDb)
        {
            // The role list comes from SHOW GRANTS; the host part is % for roles created without one, which USING accepts.
            var list = string.Join(", ", roles.Select(DragDrop.MySqlIdentifier.Quote));
            return await ReadLinesAsync(connection, $"SHOW GRANTS FOR CURRENT_USER() USING {list}", cancellationToken).ConfigureAwait(false);
        }

        var lines = new List<string>();
        foreach (var role in roles)
        {
            lines.AddRange(await ReadLinesAsync(connection, "SHOW GRANTS FOR " + DragDrop.MySqlIdentifier.Quote(role), cancellationToken).ConfigureAwait(false));
        }

        return lines;
    }

    /// <inheritdoc />
    public async Task<bool> CanSelectUsersAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(definition, cancellationToken).ConfigureAwait(false);
        using var command = new MySqlCommand("SELECT 1 FROM mysql.user LIMIT 0", connection);
        try
        {
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MySqlException ex) when (ex.ErrorCode is MySqlErrorCode.TableAccessDenied or MySqlErrorCode.DatabaseAccessDenied or MySqlErrorCode.AccessDenied)
        {
            return false;
        }
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

    private static async Task<IReadOnlyList<string>> ReadLinesAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        using var command = new MySqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var lines = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                lines.Add(reader.GetString(0));
            }
        }

        return lines;
    }
}
