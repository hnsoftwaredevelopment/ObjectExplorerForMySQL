using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Security;

/// <summary>A user or role from <c>mysql.user</c> (US-1002). Values the server does not have are null.</summary>
public sealed record AccountInfo(string User, string Host)
{
    /// <summary>Gets a value indicating whether the account is a role.</summary>
    public bool IsRole { get; init; }

    /// <summary>Gets the authentication plugin, for example caching_sha2_password.</summary>
    public string? Plugin { get; init; }

    /// <summary>Gets a value indicating whether the account is locked (MySQL 5.7.6+, MariaDB 10.4+).</summary>
    public bool? IsLocked { get; init; }

    /// <summary>Gets a value indicating whether the password is expired.</summary>
    public bool? PasswordExpired { get; init; }

    /// <summary>Gets when the password was last changed (MySQL).</summary>
    public DateTime? PasswordLastChanged { get; init; }

    /// <summary>Gets the password lifetime in days; null means the server default.</summary>
    public int? PasswordLifetime { get; init; }

    /// <summary>Gets the text in the tree: <c>app@%</c>; a MariaDB role (no host) only shows its name.</summary>
    public string Label => Host.Length == 0 && IsRole ? User : $"{User}@{Host}";

    /// <summary>Gets the account in SQL: <c>`app`@`%`</c>, or <c>`role`</c> for a MariaDB role.</summary>
    public string Sql => Host.Length == 0 && IsRole ? MySqlIdentifier.Quote(User) : $"{MySqlIdentifier.Quote(User)}@{MySqlIdentifier.Quote(Host)}";
}

/// <summary>Reads accounts and their grants (US-1002); a fake in the tests.</summary>
public interface IAccountReader
{
    /// <summary>Returns the users and roles, sorted by user and host.</summary>
    Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(ConnectionDefinition definition, string? serverVersion, CancellationToken cancellationToken);

    /// <summary>Returns the lines of <c>SHOW GRANTS FOR</c> the account.</summary>
    Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, AccountInfo account, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the default roles of a user on MySQL 8 (<c>mysql.default_roles</c>), as role accounts (US-1005). A server
    /// error is thrown (for example no SELECT on that table).
    /// </summary>
    Task<IReadOnlyList<AccountInfo>> GetDefaultRolesAsync(ConnectionDefinition definition, AccountInfo user, CancellationToken cancellationToken);
}

/// <summary>Turns a line of SHOW GRANTS into the text of a tree node, without the account and anything after it.</summary>
public static class GrantText
{
    /// <summary>
    /// Returns the part between GRANT and TO (REVOKE ... FROM keeps the word REVOKE), with "(with grant option)" when the
    /// line has it. Everything after TO is left out: the account, and on MariaDB the password hash.
    /// </summary>
    public static string Label(string line, string withGrantOption = "WITH GRANT OPTION")
    {
        var body = Body(line);
        return GrantParser.Parse(line)?.WithGrantOption == true ? $"{body} ({withGrantOption})" : body;
    }

    /// <summary>Returns the part between GRANT and TO (or REVOKE ... FROM, with the word REVOKE), without the account.</summary>
    public static string Body(string line)
    {
        var text = (line ?? string.Empty).Trim().TrimEnd(';');
        var isRevoke = GrantParser.Parse(text)?.IsRevoke ?? text.StartsWith("REVOKE ", StringComparison.OrdinalIgnoreCase);
        var start = text.StartsWith("GRANT ", StringComparison.OrdinalIgnoreCase) ? "GRANT ".Length : 0;
        var end = IndexOutsideQuotes(text, isRevoke ? " FROM " : " TO ");
        return (end > start ? text.Substring(start, end - start) : text.Substring(start)).Trim();
    }

    /// <summary>Returns the position of <paramref name="keyword"/> outside quotes (a name such as `TO` does not count), or -1.</summary>
    public static int IndexOutsideQuotes(string text, string keyword)
    {
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } q)
            {
                if (c == q)
                {
                    quote = null;
                }
            }
            else if (c is '`' or '\'' or '"')
            {
                quote = c;
            }
            else if (string.Compare(text, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary><see cref="IAccountReader"/> on MySqlConnector, for MySQL 5.7, 8.x and MariaDB.</summary>
public sealed class MySqlAccountReader : IAccountReader
{
    private static readonly string[] _optional = ["plugin", "account_locked", "password_expired", "password_last_changed", "password_lifetime", "is_role"];

    /// <inheritdoc />
    /// <remarks>
    /// Only the columns that exist are read (they differ per server), never a password hash. A role is: MariaDB
    /// <c>is_role = 'Y'</c>; MySQL 8 an account in <c>mysql.role_edges</c> (when readable) or a locked account with an
    /// expired, empty password, which is how CREATE ROLE makes it.
    /// </remarks>
    public async Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(ConnectionDefinition definition, string? serverVersion, CancellationToken cancellationToken)
    {
        var capabilities = ServerCapabilities.FromVersion(serverVersion);
        var mysqlRoles = !capabilities.IsMariaDb && capabilities.Version >= new Version(8, 0);
        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = new MySqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'mysql' AND TABLE_NAME = 'user'", connection))
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                columns.Add(reader.GetString(0));
            }
        }

        var select = new List<string> { "User", "Host" };
        select.AddRange(_optional.Where(columns.Contains));
        if (mysqlRoles && columns.Contains("authentication_string"))
        {
            select.Add("(authentication_string = '') AS no_password");
        }

        var edges = mysqlRoles ? await ReadRoleEdgesAsync(connection, cancellationToken).ConfigureAwait(false) : new HashSet<(string, string)>();
        var accounts = new List<AccountInfo>();
        using (var command = new MySqlCommand($"SELECT {string.Join(", ", select)} FROM mysql.user", connection))
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string? Text(string name) => Has(reader, name) && !reader.IsDBNull(reader.GetOrdinal(name)) ? Convert.ToString(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture) : null;
                bool? YesNo(string name) => Text(name) is { } value ? string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase) : null;

                var user = Text("User") ?? string.Empty;
                var host = Text("Host") ?? string.Empty;
                var locked = YesNo("account_locked");
                var expired = YesNo("password_expired");
                var noPassword = Text("no_password") == "1";
                var isRole = YesNo("is_role") == true
                    || (mysqlRoles && (edges.Contains((user, host)) || (locked == true && expired == true && noPassword)));
                accounts.Add(new AccountInfo(user, host)
                {
                    IsRole = isRole,
                    Plugin = Text("plugin"),
                    IsLocked = locked,
                    PasswordExpired = expired,
                    PasswordLastChanged = Has(reader, "password_last_changed") && !reader.IsDBNull(reader.GetOrdinal("password_last_changed"))
                        ? reader.GetDateTime(reader.GetOrdinal("password_last_changed"))
                        : null,
                    PasswordLifetime = int.TryParse(Text("password_lifetime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : null,
                });
            }
        }

        return Sort(accounts);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, AccountInfo account, CancellationToken cancellationToken)
    {
        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = new MySqlCommand("SHOW GRANTS FOR " + account.Sql, connection);
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountInfo>> GetDefaultRolesAsync(ConnectionDefinition definition, AccountInfo user, CancellationToken cancellationToken)
    {
        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = new MySqlCommand("SELECT DEFAULT_ROLE_USER, DEFAULT_ROLE_HOST FROM mysql.default_roles WHERE USER = @user AND HOST = @host", connection);
        command.Parameters.AddWithValue("@user", user.User);
        command.Parameters.AddWithValue("@host", user.Host);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var roles = new List<AccountInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            roles.Add(new AccountInfo(reader.GetString(0), reader.GetString(1)) { IsRole = true });
        }

        return roles;
    }

    /// <summary>Sorts by user, then host (case-insensitive, as MySQL compares them).</summary>
    public static IReadOnlyList<AccountInfo> Sort(IEnumerable<AccountInfo> accounts) =>
        accounts.OrderBy(a => a.User, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Host, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool Has(MySqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // mysql.role_edges needs its own SELECT privilege; without it the heuristic decides.
    private static async Task<HashSet<(string, string)>> ReadRoleEdgesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var edges = new HashSet<(string, string)>();
        try
        {
            using var command = new MySqlCommand("SELECT DISTINCT FROM_USER, FROM_HOST FROM mysql.role_edges", connection);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                edges.Add((reader.GetString(0), reader.GetString(1)));
            }
        }
        catch (MySqlException)
        {
            // No access to mysql.role_edges.
        }

        return edges;
    }
}
