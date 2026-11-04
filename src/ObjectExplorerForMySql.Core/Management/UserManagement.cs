using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Data;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>A statement with the text the user sees (password masked) and the command that runs (password as parameter).</summary>
public sealed record UserStatement(string DisplaySql, SqlCommandText Command);

/// <summary>Statements of US-1003. Passwords are parameters; the displayed SQL shows <c>'********'</c>.</summary>
public static class UserStatements
{
    /// <summary>Text shown instead of a password.</summary>
    public const string MaskedPassword = "'********'";

    private const string PasswordParameter = "@password";

    /// <summary>Privileges that can be granted on a database, in the order of the MySQL manual.</summary>
    public static IReadOnlyList<string> DatabasePrivileges { get; } =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "EXECUTE", "SHOW VIEW", "CREATE", "ALTER", "DROP", "INDEX", "REFERENCES",
        "CREATE VIEW", "CREATE ROUTINE", "ALTER ROUTINE", "EVENT", "TRIGGER", "LOCK TABLES", "CREATE TEMPORARY TABLES",
    ];

    /// <summary>Returns the account in SQL: <c>`user`@`host`</c>.</summary>
    public static string Account(string user, string host) => $"{MySqlIdentifier.Quote(user)}@{MySqlIdentifier.Quote(host)}";

    /// <summary><c>CREATE USER `u`@`h` IDENTIFIED BY '...';</c></summary>
    public static UserStatement CreateUser(string user, string host, string password) =>
        WithPassword($"CREATE USER {Account(user, host)} IDENTIFIED BY ", password);

    /// <summary><c>CREATE ROLE `r`@`h`;</c> on MySQL, <c>CREATE ROLE `r`;</c> on MariaDB (roles have no host there).</summary>
    public static string CreateRole(string name, string host, bool isMariaDb) =>
        isMariaDb ? $"CREATE ROLE {MySqlIdentifier.Quote(name)};" : $"CREATE ROLE {Account(name, host)};";

    /// <summary><c>ALTER USER `u`@`h` IDENTIFIED BY '...';</c></summary>
    public static UserStatement ChangePassword(AccountInfo account, string password) =>
        WithPassword($"ALTER USER {(account ?? throw new ArgumentNullException(nameof(account))).Sql} IDENTIFIED BY ", password);

    /// <summary>
    /// <c>GRANT SELECT, INSERT ON `db`.* TO `u`@`h` [WITH GRANT OPTION];</c>. With <paramref name="all"/> the privileges
    /// are <c>ALL PRIVILEGES</c>. Returns null when nothing is chosen.
    /// </summary>
    public static string? Grant(string database, IEnumerable<string> privileges, bool all, bool withGrantOption, AccountInfo account)
    {
        if (string.IsNullOrEmpty(database) || account is null)
        {
            return null;
        }

        var list = all ? "ALL PRIVILEGES" : string.Join(", ", (privileges ?? []).Distinct(StringComparer.OrdinalIgnoreCase));
        return list.Length == 0
            ? null
            : $"GRANT {list} ON {MySqlIdentifier.Quote(database)}.* TO {account.Sql}{(withGrantOption ? " WITH GRANT OPTION" : string.Empty)};";
    }

    /// <summary>
    /// Returns the REVOKE for a line of SHOW GRANTS of <paramref name="account"/>, or null when there is nothing to revoke:
    /// USAGE only, PROXY, a partial revoke or a line that cannot be read. A grant option is revoked with it.
    /// </summary>
    public static string? Revoke(string line, AccountInfo account)
    {
        if (account is null || GrantParser.Parse(line) is not { IsRevoke: false } entry
            || entry.Level == GrantLevel.Proxy
            || (entry.Level != GrantLevel.Role && entry.Privileges.All(p => p == "USAGE")))
        {
            return null;
        }

        var body = GrantText.Body(line);
        if (entry.WithGrantOption && entry.Level != GrantLevel.Role && GrantText.IndexOutsideQuotes(body, " ON ") is var on and > 0)
        {
            body = body.Substring(0, on) + ", GRANT OPTION" + body.Substring(on);
        }

        return $"REVOKE {body} FROM {account.Sql};";
    }

    private static UserStatement WithPassword(string start, string password) =>
        new(start + MaskedPassword + ";", new SqlCommandText(start + PasswordParameter + ";", [new KeyValuePair<string, object?>(PasswordParameter, password ?? string.Empty)]));
}

/// <summary>Why a user cannot be created or a password cannot be set.</summary>
public enum UserProblem
{
    /// <summary>Everything is filled in correctly.</summary>
    None,

    /// <summary>The user name is empty.</summary>
    NameEmpty,

    /// <summary>The account already exists.</summary>
    Exists,

    /// <summary>The password is empty.</summary>
    PasswordEmpty,

    /// <summary>Password and confirmation differ.</summary>
    PasswordMismatch,
}

/// <summary>State of the New User and Change Password dialog (US-1003).</summary>
public sealed class UserDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<AccountInfo> _existing;
    private string _userName = string.Empty;
    private string _host = "%";
    private string _password = string.Empty;
    private string _confirm = string.Empty;

    /// <summary>Initializes the dialog for a new user; <paramref name="existing"/> are the accounts that exist.</summary>
    public UserDialogViewModel(IReadOnlyList<AccountInfo> existing)
    {
        _existing = existing ?? [];
    }

    /// <summary>Initializes the dialog to change the password of <paramref name="account"/>.</summary>
    public UserDialogViewModel(AccountInfo account)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        _existing = [];
        _userName = account.User;
        _host = account.Host;
    }

    /// <summary>Gets the account whose password changes, or null for a new user.</summary>
    public AccountInfo? Account { get; }

    /// <summary>Gets a value indicating whether this is New User (name and host can be typed).</summary>
    public bool IsNew => Account is null;

    /// <summary>Gets or sets the user name.</summary>
    public string UserName
    {
        get => _userName;
        set => Set(ref _userName, value);
    }

    /// <summary>Gets or sets the host; % means any host.</summary>
    public string Host
    {
        get => _host;
        set => Set(ref _host, value);
    }

    /// <summary>Gets or sets the password (from a PasswordBox; never shown).</summary>
    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    /// <summary>Gets or sets the confirmation of the password.</summary>
    public string ConfirmPassword
    {
        get => _confirm;
        set => Set(ref _confirm, value);
    }

    /// <summary>Gets what is missing or wrong; the first problem in the order of the fields.</summary>
    public UserProblem Problem
    {
        get
        {
            if (IsNew && string.IsNullOrWhiteSpace(_userName))
            {
                return UserProblem.NameEmpty;
            }

            // MySQL compares user names exactly and host names without case.
            if (IsNew && _existing.Any(a => !a.IsRole && a.User == _userName && string.Equals(a.Host, HostOrAny, StringComparison.OrdinalIgnoreCase)))
            {
                return UserProblem.Exists;
            }

            if (_password.Length == 0)
            {
                return UserProblem.PasswordEmpty;
            }

            return _password == _confirm ? UserProblem.None : UserProblem.PasswordMismatch;
        }
    }

    /// <summary>Gets a value indicating whether OK is enabled.</summary>
    public bool CanRun => Problem == UserProblem.None;

    /// <summary>Gets the statement; its display text masks the password.</summary>
    public UserStatement Statement => IsNew
        ? UserStatements.CreateUser(_userName, HostOrAny, _password)
        : UserStatements.ChangePassword(Account!, _password);

    /// <summary>Gets the SQL as shown (password masked); empty without a user name.</summary>
    public string DisplaySql => IsNew && string.IsNullOrWhiteSpace(_userName) ? string.Empty : Statement.DisplaySql;

    /// <summary>Gets the account as created: <c>app@%</c>.</summary>
    public string Label => $"{_userName}@{HostOrAny}";

    // An empty host means any host, as in MySQL.
    private string HostOrAny => string.IsNullOrWhiteSpace(_host) ? "%" : _host.Trim();

    private void Set(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value ?? string.Empty, name))
        {
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(DisplaySql));
        }
    }
}

/// <summary>State of the New Role dialog (US-1004).</summary>
public sealed class RoleDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<AccountInfo> _existing;
    private string _name = string.Empty;
    private string _host = "%";

    /// <summary>Initializes a new instance of the <see cref="RoleDialogViewModel"/> class.</summary>
    /// <param name="existing">All users and roles of the server.</param>
    /// <param name="isMariaDb">True on MariaDB: a role has no host.</param>
    public RoleDialogViewModel(IReadOnlyList<AccountInfo> existing, bool isMariaDb)
    {
        _existing = existing ?? [];
        IsMariaDb = isMariaDb;
    }

    /// <summary>Gets a value indicating whether the server is MariaDB (no host field).</summary>
    public bool IsMariaDb { get; }

    /// <summary>Gets or sets the role name.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>Gets or sets the host (MySQL only); empty means %.</summary>
    public string Host
    {
        get => _host;
        set => Set(ref _host, value);
    }

    /// <summary>
    /// Gets what is wrong: no name, or the name exists. On MySQL roles and users share one list of accounts, so a user
    /// with the same name and host also counts; on MariaDB only roles count.
    /// </summary>
    public UserProblem Problem =>
        string.IsNullOrWhiteSpace(_name) ? UserProblem.NameEmpty
        : _existing.Any(a => a.User == _name && (IsMariaDb ? a.IsRole : string.Equals(a.Host, HostOrAny, StringComparison.OrdinalIgnoreCase))) ? UserProblem.Exists
        : UserProblem.None;

    /// <summary>Gets a value indicating whether Create is enabled.</summary>
    public bool CanRun => Problem == UserProblem.None;

    /// <summary>Gets the CREATE ROLE statement, or an empty string without a name.</summary>
    public string Sql => string.IsNullOrWhiteSpace(_name) ? string.Empty : UserStatements.CreateRole(_name, HostOrAny, IsMariaDb);

    /// <summary>Gets the role as shown in the tree: <c>reader@%</c>, on MariaDB <c>reader</c>.</summary>
    public string Label => IsMariaDb ? _name : $"{_name}@{HostOrAny}";

    private string HostOrAny => string.IsNullOrWhiteSpace(_host) ? "%" : _host.Trim();

    private void Set(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value ?? string.Empty, name))
        {
            OnPropertyChanged(nameof(Problem));
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(Sql));
        }
    }
}

/// <summary>State of the Grant Role dialog (US-1005).</summary>
public sealed class GrantRoleDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<AccountInfo>? _defaultRoles;
    private AccountInfo? _role;
    private bool _makeDefault;

    /// <summary>Initializes a new instance of the <see cref="GrantRoleDialogViewModel"/> class.</summary>
    /// <param name="user">The user that gets the role.</param>
    /// <param name="roles">The roles that can be granted (the ones the user has are left out by the caller).</param>
    /// <param name="defaultRoles">
    /// MySQL: the current default roles of the user, or null when they could not be read. SET DEFAULT ROLE replaces the
    /// whole list, so the current ones are kept in it; when they are unknown, the check box starts off.
    /// </param>
    /// <param name="isMariaDb">True on MariaDB: one default role, set with SET DEFAULT ROLE ... FOR.</param>
    public GrantRoleDialogViewModel(AccountInfo user, IReadOnlyList<AccountInfo> roles, IReadOnlyList<AccountInfo>? defaultRoles, bool isMariaDb)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        Roles = roles ?? [];
        _defaultRoles = defaultRoles;
        IsMariaDb = isMariaDb;
        _role = Roles.FirstOrDefault();
        _makeDefault = isMariaDb || defaultRoles is not null;
    }

    /// <summary>Gets the user.</summary>
    public AccountInfo User { get; }

    /// <summary>Gets the roles to choose from.</summary>
    public IReadOnlyList<AccountInfo> Roles { get; }

    /// <summary>Gets a value indicating whether the server is MariaDB.</summary>
    public bool IsMariaDb { get; }

    /// <summary>Gets a value indicating whether the current default roles are unknown (MySQL; shown as a warning).</summary>
    public bool DefaultRolesUnknown => !IsMariaDb && _defaultRoles is null;

    /// <summary>Gets or sets the role to grant.</summary>
    public AccountInfo? Role
    {
        get => _role;
        set
        {
            if (SetProperty(ref _role, value))
            {
                Changed();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the role becomes a default role (active right after login).</summary>
    public bool MakeDefault
    {
        get => _makeDefault;
        set
        {
            if (SetProperty(ref _makeDefault, value))
            {
                Changed();
            }
        }
    }

    /// <summary>Gets the statements that run, in order: the GRANT and, with <see cref="MakeDefault"/>, the SET DEFAULT ROLE.</summary>
    public IReadOnlyList<string> Statements
    {
        get
        {
            if (_role is null)
            {
                return [];
            }

            var statements = new List<string> { $"GRANT {_role.Sql} TO {User.Sql};" };
            if (_makeDefault)
            {
                if (IsMariaDb)
                {
                    statements.Add($"SET DEFAULT ROLE {_role.Sql} FOR {User.Sql};");
                }
                else
                {
                    var roles = (_defaultRoles ?? []).Select(r => r.Sql).Where(sql => sql != _role.Sql).Append(_role.Sql);
                    statements.Add($"SET DEFAULT ROLE {string.Join(", ", roles)} TO {User.Sql};");
                }
            }

            return statements;
        }
    }

    /// <summary>Gets the SQL as shown: one statement per line.</summary>
    public string Sql => string.Join(Environment.NewLine, Statements);

    /// <summary>Gets a value indicating whether Grant is enabled.</summary>
    public bool CanGrant => _role is not null;

    /// <summary>
    /// Returns the roles that can still be granted: the roles of the server minus the ones in <paramref name="userGrants"/>
    /// (the SHOW GRANTS lines of the user), compared by name.
    /// </summary>
    public static IReadOnlyList<AccountInfo> AvailableRoles(IEnumerable<AccountInfo> accounts, IEnumerable<string> userGrants)
    {
        var granted = new HashSet<string>(
            (userGrants ?? []).Select(GrantParser.Parse).Where(g => g is { Level: GrantLevel.Role, IsRevoke: false }).SelectMany(g => g!.Roles),
            StringComparer.Ordinal);
        return (accounts ?? []).Where(a => a.IsRole && !granted.Contains(a.User)).ToList();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Statements));
        OnPropertyChanged(nameof(Sql));
        OnPropertyChanged(nameof(CanGrant));
    }
}

/// <summary>One privilege check box of the Grant dialog.</summary>
public sealed class PrivilegeOption : ObservableObject
{
    private bool _isChecked;

    /// <summary>Initializes a new instance of the <see cref="PrivilegeOption"/> class.</summary>
    public PrivilegeOption(string name) => Name = name;

    /// <summary>Gets the privilege.</summary>
    public string Name { get; }

    /// <summary>Gets or sets a value indicating whether the privilege is granted.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>State of the Grant on Database dialog (US-1003).</summary>
public sealed class GrantDialogViewModel : ObservableObject
{
    private string? _database;
    private bool _all;
    private bool _withGrantOption;

    /// <summary>Initializes a new instance of the <see cref="GrantDialogViewModel"/> class.</summary>
    public GrantDialogViewModel(AccountInfo account, IReadOnlyList<string> databases, string? database = null)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Databases = databases ?? [];
        _database = Databases.FirstOrDefault(d => string.Equals(d, database, StringComparison.Ordinal)) ?? Databases.FirstOrDefault();
        Privileges = UserStatements.DatabasePrivileges.Select(p => new PrivilegeOption(p)).ToList();
        foreach (var option in Privileges)
        {
            option.PropertyChanged += (_, _) => Changed();
        }
    }

    /// <summary>Gets the account that gets the privileges.</summary>
    public AccountInfo Account { get; }

    /// <summary>Gets the databases to choose from.</summary>
    public IReadOnlyList<string> Databases { get; }

    /// <summary>Gets the privilege check boxes.</summary>
    public IReadOnlyList<PrivilegeOption> Privileges { get; }

    /// <summary>Gets or sets the database.</summary>
    public string? Database
    {
        get => _database;
        set
        {
            if (SetProperty(ref _database, value))
            {
                Changed();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether ALL PRIVILEGES is granted (the check boxes are then ignored).</summary>
    public bool All
    {
        get => _all;
        set
        {
            if (SetProperty(ref _all, value))
            {
                OnPropertyChanged(nameof(CanChoosePrivileges));
                Changed();
            }
        }
    }

    /// <summary>Gets a value indicating whether the separate check boxes are enabled.</summary>
    public bool CanChoosePrivileges => !_all;

    /// <summary>Gets or sets a value indicating whether WITH GRANT OPTION is added.</summary>
    public bool WithGrantOption
    {
        get => _withGrantOption;
        set
        {
            if (SetProperty(ref _withGrantOption, value))
            {
                Changed();
            }
        }
    }

    /// <summary>Gets the GRANT statement, or an empty string while no database or privilege is chosen.</summary>
    public string Sql => UserStatements.Grant(_database ?? string.Empty, Privileges.Where(p => p.IsChecked).Select(p => p.Name), _all, _withGrantOption, Account) ?? string.Empty;

    /// <summary>Gets a value indicating whether Grant is enabled.</summary>
    public bool CanGrant => Sql.Length > 0;

    private void Changed()
    {
        OnPropertyChanged(nameof(Sql));
        OnPropertyChanged(nameof(CanGrant));
    }
}

/// <summary>Runs the user actions of US-1003 and updates the tree.</summary>
public sealed class UserManager
{
    private readonly ISchemaCommandRunner _runner;
    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="UserManager"/> class.</summary>
    public UserManager(ISchemaCommandRunner runner, IExtensionLogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Returns true when the account of the server of <paramref name="node"/> may manage users (US-1001).</summary>
    public static bool CanManage(TreeNodeViewModel? node) => node?.OwningServer is { State: ConnectionState.Connected, Privileges.CanManageUsers: true };

    /// <summary>Returns true when New User is available: the Users folder, with an account that may manage users.</summary>
    public static bool IsNewUserTarget(TreeNodeViewModel? node) => node is FolderNodeViewModel { IsUsersFolder: true } && CanManage(node);

    /// <summary>Returns the user whose password can be changed (not a role), or null.</summary>
    public static AccountNodeViewModel? PasswordTarget(TreeNodeViewModel? node) =>
        node is AccountNodeViewModel { Account.IsRole: false } account && CanManage(node) ? account : null;

    /// <summary>Returns the user or role that can get privileges, or null.</summary>
    public static AccountNodeViewModel? GrantTarget(TreeNodeViewModel? node) =>
        node is AccountNodeViewModel account && CanManage(node) ? account : null;

    /// <summary>Returns the REVOKE for a grant node, or null when it cannot be revoked.</summary>
    public static string? RevokeOf(TreeNodeViewModel? node) =>
        node is ObjectNodeViewModel { Item: GrantNodeItem item } && CanManage(node) ? item.RevokeSql : null;

    /// <summary>Returns true when New Role is available: the Roles folder, with an account that may manage users (US-1004).</summary>
    public static bool IsNewRoleTarget(TreeNodeViewModel? node) => node is FolderNodeViewModel { IsRolesFolder: true } && CanManage(node);

    /// <summary>Creates the user, refreshes the Users folder and selects the new user. A server error is thrown.</summary>
    public Task CreateUserAsync(TreeNodeViewModel usersFolder, UserStatement statement, string label, CancellationToken cancellationToken) =>
        CreateAccountAsync(usersFolder, statement.Command, statement.DisplaySql, label, cancellationToken);

    /// <summary>Creates the role, refreshes the Roles folder and selects the new role (US-1004). A server error is thrown.</summary>
    public Task CreateRoleAsync(TreeNodeViewModel rolesFolder, string sql, string label, CancellationToken cancellationToken) =>
        CreateAccountAsync(rolesFolder, new SqlCommandText(sql, []), sql, label, cancellationToken);

    // Runs the statement, refreshes the folder (expanding it when it was collapsed) and selects the new account.
    private async Task CreateAccountAsync(TreeNodeViewModel folder, SqlCommandText command, string displaySql, string label, CancellationToken cancellationToken)
    {
        await RunAsync(folder, command, displaySql, cancellationToken);
        await folder.RefreshAsync();
        if (!folder.IsExpanded)
        {
            folder.IsExpanded = true;
            await folder.WaitForLoadAsync();
        }

        if (folder.Children.FirstOrDefault(c => string.Equals(c.Key, label, StringComparison.OrdinalIgnoreCase)) is { } created)
        {
            created.IsSelected = true;
        }
    }

    /// <summary>Changes the password. A server error is thrown.</summary>
    public Task ChangePasswordAsync(AccountNodeViewModel account, UserStatement statement, CancellationToken cancellationToken) =>
        RunAsync(account, statement.Command, statement.DisplaySql, cancellationToken);

    /// <summary>Returns the user that can get a role (US-1005): a user (not a role) on a server with roles, or null.</summary>
    public static AccountNodeViewModel? GrantRoleTarget(TreeNodeViewModel? node) =>
        node is AccountNodeViewModel { Account.IsRole: false } account && CanManage(node) && SecurityFolder.SupportsRoles(node.OwningServer?.ServerVersion)
            ? account
            : null;

    /// <summary>Grants or revokes, then shows the grants of the account again (expanded). A server error is thrown.</summary>
    public Task ChangeGrantsAsync(AccountNodeViewModel account, string sql, CancellationToken cancellationToken) =>
        ChangeGrantsAsync(account, [sql], cancellationToken);

    /// <summary>
    /// Runs the statements in order (stops at the first server error, which is thrown), then shows the grants of the account
    /// again (expanded).
    /// </summary>
    public async Task ChangeGrantsAsync(AccountNodeViewModel account, IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        if (account is null)
        {
            throw new ArgumentNullException(nameof(account));
        }

        foreach (var sql in statements ?? [])
        {
            await RunAsync(account, new SqlCommandText(sql, []), sql, cancellationToken);
        }

        await account.RefreshAsync();
        if (!account.IsExpanded)
        {
            account.IsExpanded = true;
            await account.WaitForLoadAsync();
        }
    }

    private async Task RunAsync(TreeNodeViewModel node, SqlCommandText command, string displaySql, CancellationToken cancellationToken)
    {
        var server = node.OwningServer ?? throw new InvalidOperationException("The node has no server.");
        if (!await server.EnsurePasswordAsync(cancellationToken))
        {
            throw new OperationCanceledException();
        }

        await _runner.ExecuteAsync(server.Definition, command, cancellationToken);
        _logger.Info($"'{server.Label}': {displaySql}");
    }
}
