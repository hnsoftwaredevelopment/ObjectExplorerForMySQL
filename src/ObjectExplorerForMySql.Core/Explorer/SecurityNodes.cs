using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.ObjectProperties;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Explorer;

/// <summary>The Security folder of a server with Users and Roles (US-1002). Read-only.</summary>
public static class SecurityFolder
{
    /// <summary>Returns true when roles exist on the server: MySQL 8.0+ or MariaDB 10.0.5+.</summary>
    public static bool SupportsRoles(string? serverVersion)
    {
        var capabilities = ServerCapabilities.FromVersion(serverVersion);
        return capabilities.IsMariaDb ? capabilities.Version >= new Version(10, 0, 5) : capabilities.Version >= new Version(8, 0);
    }

    /// <summary>
    /// Creates the Security folder, or returns null when the account may not read users (US-1001) or no reader is set.
    /// Users and Roles each read the accounts when they are expanded.
    /// </summary>
    public static FolderNodeViewModel? Create(ExplorerContext context, ServerNodeViewModel server)
    {
        if (context?.AccountReader is not { } reader || server?.Privileges is not { CanReadUsers: true })
        {
            return null;
        }

        return new FolderNodeViewModel(context, context.Texts.Security, _ =>
        {
            var folders = new List<TreeNodeViewModel> { AccountsFolder(context, server, reader, context.Texts.Users, roles: false) };
            if (SupportsRoles(server.ServerVersion))
            {
                folders.Add(AccountsFolder(context, server, reader, context.Texts.Roles, roles: true));
            }

            return Task.FromResult<IReadOnlyList<TreeNodeViewModel>>(folders);
        });
    }

    private static FolderNodeViewModel AccountsFolder(ExplorerContext context, ServerNodeViewModel server, IAccountReader reader, string name, bool roles) =>
        new(context, name, async ct =>
            (await reader.GetAccountsAsync(server.Definition, server.ServerVersion, ct))
                .Where(a => a.IsRole == roles)
                .Select(a => (TreeNodeViewModel)new AccountNodeViewModel(context, server, a))
                .ToList(), isFilterable: true);
}

/// <summary>A user or role (US-1002). Children: its grants from SHOW GRANTS.</summary>
public sealed class AccountNodeViewModel : TreeNodeViewModel
{
    private readonly ServerNodeViewModel _server;

    /// <summary>Initializes a new instance of the <see cref="AccountNodeViewModel"/> class.</summary>
    public AccountNodeViewModel(ExplorerContext context, ServerNodeViewModel server, AccountInfo account)
        : base(context, canHaveChildren: true)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        Account = account ?? throw new ArgumentNullException(nameof(account));
    }

    /// <summary>Gets the account.</summary>
    public AccountInfo Account { get; }

    /// <inheritdoc />
    public override string Label => Account.Label;

    /// <inheritdoc />
    public override string Key => Account.Label;

    /// <inheritdoc />
    public override NodeIcon Icon => Account.IsRole ? NodeIcon.Role : Account.IsLocked == true ? NodeIcon.LockedUser : NodeIcon.User;

    /// <inheritdoc />
    public override object? CreateProperties() => new AccountProperties(Account, _server.Definition);

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken)
    {
        var reader = Context.AccountReader ?? throw new InvalidOperationException("No account reader.");
        var lines = await reader.GetGrantsAsync(_server.Definition, Account, cancellationToken);
        return lines
            .Select(line => new GrantNodeItem(GrantParser.Parse(line), Management.UserStatements.Revoke(line, Account)))
            .Zip(lines, (item, line) => (TreeNodeViewModel)new ObjectNodeViewModel(
                Context,
                item,
                GrantText.Label(line, Context.Texts.WithGrantOption),
                item.Entry?.Level == GrantLevel.Role ? NodeIcon.Role : NodeIcon.Grant))
            .ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// US-1003: only when the account may manage users, and never the own account or the MySQL system accounts
    /// (mysql.sys, mysql.session, mysql.infoschema), which the server needs.
    /// </remarks>
    public override Management.DropTarget? CreateDropTarget() =>
        _server.Privileges?.CanManageUsers == true
        && !string.Equals(Account.Label, _server.CurrentUser, StringComparison.Ordinal)
        && !Account.User.StartsWith("mysql.", StringComparison.Ordinal)
            ? Management.DropTarget.ForAccount(Account)
            : null;
}

/// <summary>What a grant node holds (US-1003): the parsed line and the REVOKE for it (null when it cannot be revoked).</summary>
/// <remarks>The raw line is not kept: on MariaDB it can contain a password hash.</remarks>
public sealed record GrantNodeItem(GrantEntry? Entry, string? RevokeSql);
