using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Security;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using ObjectExplorerForMySql.Core.Tests.Management;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Security;

public class UserManagementTests
{
    private static readonly AccountInfo App = new("app", "%");
    private static readonly AccountInfo Root = new("root", "localhost");
    private static readonly AccountInfo Sys = new("mysql.sys", "localhost") { IsLocked = true };
    private static readonly AccountInfo ReaderRole = new("reader", "%") { IsRole = true };

    [Fact]
    public void CreateUser_PasswordIsAParameter_AndMaskedInTheDisplay()
    {
        var statement = UserStatements.CreateUser("o'neil", "%", "S3cret'!");

        Assert.Equal("CREATE USER `o'neil`@`%` IDENTIFIED BY '********';", statement.DisplaySql);
        Assert.Equal("CREATE USER `o'neil`@`%` IDENTIFIED BY @password;", statement.Command.Sql);
        Assert.Equal("S3cret'!", Assert.Single(statement.Command.Parameters).Value);
        Assert.DoesNotContain("S3cret", statement.DisplaySql);
    }

    [Fact]
    public void ChangePassword_UsesAlterUser()
    {
        var statement = UserStatements.ChangePassword(App, "x");

        Assert.Equal("ALTER USER `app`@`%` IDENTIFIED BY '********';", statement.DisplaySql);
        Assert.Equal("ALTER USER `app`@`%` IDENTIFIED BY @password;", statement.Command.Sql);
    }

    [Fact]
    public void Grant_OnADatabase()
    {
        Assert.Equal("GRANT SELECT, INSERT ON `shop`.* TO `app`@`%`;", UserStatements.Grant("shop", ["SELECT", "INSERT"], false, false, App));
        Assert.Equal("GRANT ALL PRIVILEGES ON `my db`.* TO `reader`@`%` WITH GRANT OPTION;", UserStatements.Grant("my db", ["SELECT"], true, true, ReaderRole));
        Assert.Null(UserStatements.Grant("shop", [], false, false, App));
        Assert.Null(UserStatements.Grant(string.Empty, ["SELECT"], false, false, App));
    }

    [Theory]
    [InlineData("GRANT SELECT, INSERT ON `shop`.* TO `app`@`%`", "REVOKE SELECT, INSERT ON `shop`.* FROM `app`@`%`;")]
    [InlineData("GRANT ALL PRIVILEGES ON `shop`.* TO `app`@`%` WITH GRANT OPTION", "REVOKE ALL PRIVILEGES, GRANT OPTION ON `shop`.* FROM `app`@`%`;")]
    [InlineData("GRANT SELECT (`id`, `name`) ON `shop`.`customers` TO `app`@`%`", "REVOKE SELECT (`id`, `name`) ON `shop`.`customers` FROM `app`@`%`;")]
    [InlineData("GRANT EXECUTE ON PROCEDURE `shop`.`p` TO `app`@`%`", "REVOKE EXECUTE ON PROCEDURE `shop`.`p` FROM `app`@`%`;")]
    [InlineData("GRANT `reader`@`%` TO `app`@`%`", "REVOKE `reader`@`%` FROM `app`@`%`;")]
    [InlineData("GRANT SELECT ON *.* TO `app`@`%` IDENTIFIED BY PASSWORD '*2470C0C06DEE42FD1618BB99005ADCA2EC9D1E19'", "REVOKE SELECT ON *.* FROM `app`@`%`;")]
    public void Revoke_FromAGrantLine(string line, string expected)
    {
        Assert.Equal(expected, UserStatements.Revoke(line, App));
    }

    [Theory]
    [InlineData("GRANT USAGE ON *.* TO `app`@`%`")]
    [InlineData("GRANT PROXY ON ``@`` TO `app`@`%` WITH GRANT OPTION")]
    [InlineData("REVOKE SELECT ON `mysql`.* FROM `app`@`%`")]
    [InlineData("something else")]
    public void Revoke_NothingToRevoke(string line)
    {
        Assert.Null(UserStatements.Revoke(line, App));
    }

    [Fact]
    public void NewUser_ChecksTheFields()
    {
        var model = new UserDialogViewModel([App, Root]);
        Assert.True(model.IsNew);
        Assert.Equal(UserProblem.NameEmpty, model.Problem);
        Assert.Equal(string.Empty, model.DisplaySql);

        model.UserName = "app";
        Assert.Equal(UserProblem.Exists, model.Problem);

        model.Host = "localhost";
        Assert.Equal(UserProblem.PasswordEmpty, model.Problem);

        model.Password = "secret";
        model.ConfirmPassword = "secreT";
        Assert.Equal(UserProblem.PasswordMismatch, model.Problem);
        Assert.False(model.CanRun);

        model.ConfirmPassword = "secret";
        Assert.True(model.CanRun);
        Assert.Equal("CREATE USER `app`@`localhost` IDENTIFIED BY '********';", model.DisplaySql);
        Assert.Equal("app@localhost", model.Label);
    }

    [Fact]
    public void NewUser_EmptyHostMeansAnyHost()
    {
        var model = new UserDialogViewModel([App]) { UserName = "app", Host = " " };

        Assert.Equal(UserProblem.Exists, model.Problem);
        Assert.Equal("app@%", model.Label);
    }

    [Fact]
    public void NewUser_ARoleWithTheSameName_IsNoConflict()
    {
        Assert.Equal(UserProblem.PasswordEmpty, new UserDialogViewModel([ReaderRole]) { UserName = "reader" }.Problem);
    }

    [Fact]
    public void ChangePassword_NeedsTwoEqualPasswords()
    {
        var model = new UserDialogViewModel(App);
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(model.IsNew);
        Assert.Equal(UserProblem.PasswordEmpty, model.Problem);
        model.Password = "a";
        model.ConfirmPassword = "a";

        Assert.True(model.CanRun);
        Assert.Equal("ALTER USER `app`@`%` IDENTIFIED BY '********';", model.DisplaySql);
        Assert.Contains(nameof(UserDialogViewModel.CanRun), changed);
    }

    [Fact]
    public void GrantDialog_BuildsTheStatement()
    {
        var model = new GrantDialogViewModel(App, ["shop", "sakila"], "sakila");
        Assert.Equal("sakila", model.Database);
        Assert.False(model.CanGrant);

        model.Privileges.Single(p => p.Name == "SELECT").IsChecked = true;
        model.Privileges.Single(p => p.Name == "EXECUTE").IsChecked = true;
        Assert.Equal("GRANT SELECT, EXECUTE ON `sakila`.* TO `app`@`%`;", model.Sql);

        model.All = true;
        model.WithGrantOption = true;
        model.Database = "shop";
        Assert.False(model.CanChoosePrivileges);
        Assert.Equal("GRANT ALL PRIVILEGES ON `shop`.* TO `app`@`%` WITH GRANT OPTION;", model.Sql);
        Assert.True(model.CanGrant);
    }

    [Fact]
    public void GrantDialog_UnknownDatabase_StartsAtTheFirst()
    {
        Assert.Equal("shop", new GrantDialogViewModel(App, ["shop", "sakila"], "other").Database);
        Assert.Null(new GrantDialogViewModel(App, []).Database);
    }

    [Fact]
    public void DropAccount_Statements()
    {
        Assert.Equal("DROP USER `app`@`%`;", DropTarget.ForAccount(App).Sql);
        Assert.Equal("DROP ROLE `reader`@`%`;", DropTarget.ForAccount(ReaderRole).Sql);
        Assert.Equal("DROP ROLE `reporting`;", DropTarget.ForAccount(new AccountInfo("reporting", string.Empty) { IsRole = true }).Sql);
        Assert.Equal(("app@%", DropObjectKind.User), (DropTarget.ForAccount(App).DisplayName, DropTarget.ForAccount(App).Kind));
    }

    [Fact]
    public async Task Targets_OnlyWithManagePrivilege_NotForOwnOrSystemAccounts()
    {
        var tree = await Tree.OpenAsync(canManage: true);
        var app = tree.User("app@%");

        Assert.True(UserManager.IsNewUserTarget(tree.Users));
        Assert.False(UserManager.IsNewUserTarget(tree.Roles));
        Assert.Same(app, UserManager.PasswordTarget(app));
        Assert.Null(UserManager.PasswordTarget(tree.Role("reader@%")));
        Assert.NotNull(UserManager.GrantTarget(tree.Role("reader@%")));
        Assert.Equal("DROP USER `app`@`%`;", ObjectDeleter.TargetOf(app)?.Sql);
        Assert.Equal("DROP ROLE `reader`@`%`;", ObjectDeleter.TargetOf(tree.Role("reader@%"))?.Sql);
        Assert.Null(ObjectDeleter.TargetOf(tree.User("root@localhost")));
        Assert.Null(ObjectDeleter.TargetOf(tree.User("mysql.sys@localhost")));
        Assert.Same(tree.Users, ObjectDeleter.RefreshNodeOf(app, ObjectDeleter.TargetOf(app)!));
    }

    [Fact]
    public async Task Targets_WithoutManagePrivilege_None()
    {
        var tree = await Tree.OpenAsync(canManage: false);
        var app = tree.User("app@%");

        Assert.False(UserManager.IsNewUserTarget(tree.Users));
        Assert.Null(UserManager.PasswordTarget(app));
        Assert.Null(UserManager.GrantTarget(app));
        Assert.Null(ObjectDeleter.TargetOf(app));
    }

    [Fact]
    public async Task GrantNodes_KnowTheirRevoke()
    {
        var tree = await Tree.OpenAsync(canManage: true);
        tree.Reader.Grants["app@%"] = ["GRANT USAGE ON *.* TO `app`@`%`", "GRANT SELECT ON `shop`.* TO `app`@`%`"];
        var app = await Tree.ExpandAsync(tree.User("app@%"));

        Assert.Null(UserManager.RevokeOf(app.Children[0]));
        Assert.Equal("REVOKE SELECT ON `shop`.* FROM `app`@`%`;", UserManager.RevokeOf(app.Children[1]));
    }

    [Fact]
    public async Task CreateUser_RunsWithTheParameter_RefreshesAndSelects()
    {
        var tree = await Tree.OpenAsync(canManage: true);
        var runner = new FakeRunner();
        tree.Reader.Accounts = [App, Root, Sys, ReaderRole, new AccountInfo("new", "%")];
        var model = new UserDialogViewModel(tree.Reader.Accounts) { UserName = "new", Password = "p", ConfirmPassword = "p" };

        await new UserManager(runner, tree.Logger).CreateUserAsync(tree.Users, model.Statement, model.Label, CancellationToken.None);

        var command = Assert.Single(runner.Commands);
        Assert.Equal("CREATE USER `new`@`%` IDENTIFIED BY @password;", command.Sql);
        Assert.True(tree.User("new@%").IsSelected);
        Assert.DoesNotContain(tree.Sink.Entries, e => e.Contains("IDENTIFIED BY @password") || e.Contains(" p;"));
        Assert.Contains(tree.Sink.Entries, e => e.Contains("IDENTIFIED BY '********'"));
    }

    [Fact]
    public async Task Grant_RefreshesAndExpandsTheAccount()
    {
        var tree = await Tree.OpenAsync(canManage: true);
        var runner = new FakeRunner();
        var app = tree.User("app@%");
        tree.Reader.Grants["app@%"] = ["GRANT SELECT ON `shop`.* TO `app`@`%`"];

        await new UserManager(runner, tree.Logger).ChangeGrantsAsync((AccountNodeViewModel)app, "GRANT SELECT ON `shop`.* TO `app`@`%`;", CancellationToken.None);

        Assert.Equal(["GRANT SELECT ON `shop`.* TO `app`@`%`;"], runner.Executed);
        Assert.True(app.IsExpanded);
        Assert.Equal(["SELECT ON `shop`.*"], app.Children.Select(c => c.Label));
    }

    [Fact]
    public async Task ServerError_IsThrown()
    {
        var tree = await Tree.OpenAsync(canManage: true);
        var runner = new FakeRunner { Fail = new InvalidOperationException("Operation CREATE USER failed") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UserManager(runner, tree.Logger).ChangePasswordAsync((AccountNodeViewModel)tree.User("app@%"), UserStatements.ChangePassword(App, "x"), CancellationToken.None));
    }

    [Fact]
    public void CreateRole_MySqlWithHost_MariaDbWithout()
    {
        Assert.Equal("CREATE ROLE `app_read`@`%`;", UserStatements.CreateRole("app_read", "%", isMariaDb: false));
        Assert.Equal("CREATE ROLE `app_read`;", UserStatements.CreateRole("app_read", "%", isMariaDb: true));
    }

    [Fact]
    public void NewRole_ChecksTheName()
    {
        var model = new RoleDialogViewModel([App, ReaderRole], isMariaDb: false);
        Assert.Equal(UserProblem.NameEmpty, model.Problem);
        Assert.Equal(string.Empty, model.Sql);

        model.Name = "reader";
        Assert.Equal(UserProblem.Exists, model.Problem);

        // MySQL: a user with the same name and host is the same account.
        model.Name = "app";
        Assert.Equal(UserProblem.Exists, model.Problem);

        model.Host = "localhost";
        Assert.True(model.CanRun);
        Assert.Equal("CREATE ROLE `app`@`localhost`;", model.Sql);
        Assert.Equal("app@localhost", model.Label);
    }

    [Fact]
    public void NewRole_OnMariaDb_OnlyRolesCount_AndNoHost()
    {
        var model = new RoleDialogViewModel([App, new AccountInfo("reporting", string.Empty) { IsRole = true }], isMariaDb: true) { Name = "app", Host = "localhost" };

        Assert.True(model.CanRun);
        Assert.Equal("CREATE ROLE `app`;", model.Sql);
        Assert.Equal("app", model.Label);

        model.Name = "reporting";
        Assert.Equal(UserProblem.Exists, model.Problem);
    }

    [Fact]
    public async Task NewRole_Target_IsTheRolesFolder()
    {
        var tree = await Tree.OpenAsync(canManage: true);

        Assert.True(UserManager.IsNewRoleTarget(tree.Roles));
        Assert.False(UserManager.IsNewRoleTarget(tree.Users));
        Assert.False(UserManager.IsNewRoleTarget(tree.Role("reader@%")));
        Assert.False(UserManager.IsNewRoleTarget((await Tree.OpenAsync(canManage: false)).Roles));
    }

    [Fact]
    public async Task CreateRole_RefreshesAndSelects()
    {
        var tree = await Tree.OpenAsync(canManage: true);
        var runner = new FakeRunner();
        tree.Reader.Accounts = [App, Root, Sys, ReaderRole, new AccountInfo("app_read", "%") { IsRole = true }];

        await new UserManager(runner, tree.Logger).CreateRoleAsync(tree.Roles, "CREATE ROLE `app_read`@`%`;", "app_read@%", CancellationToken.None);

        Assert.Equal(["CREATE ROLE `app_read`@`%`;"], runner.Executed);
        Assert.True(tree.Role("app_read@%").IsSelected);
    }

    private static readonly AccountInfo Writer = new("writer", "%") { IsRole = true };

    [Fact]
    public void GrantRole_MySql_KeepsTheCurrentDefaultRoles()
    {
        var model = new GrantRoleDialogViewModel(App, [Writer, ReaderRole], defaultRoles: [ReaderRole], isMariaDb: false);

        Assert.Same(Writer, model.Role);
        Assert.True(model.MakeDefault);
        Assert.False(model.DefaultRolesUnknown);
        Assert.Equal(
            ["GRANT `writer`@`%` TO `app`@`%`;", "SET DEFAULT ROLE `reader`@`%`, `writer`@`%` TO `app`@`%`;"],
            model.Statements);
        Assert.Equal(string.Join(Environment.NewLine, model.Statements), model.Sql);

        model.MakeDefault = false;
        Assert.Equal(["GRANT `writer`@`%` TO `app`@`%`;"], model.Statements);
    }

    [Fact]
    public void GrantRole_MySql_UnknownDefaults_StartsWithoutDefault()
    {
        var model = new GrantRoleDialogViewModel(App, [Writer], defaultRoles: null, isMariaDb: false);

        Assert.True(model.DefaultRolesUnknown);
        Assert.False(model.MakeDefault);

        model.MakeDefault = true;
        Assert.Equal("SET DEFAULT ROLE `writer`@`%` TO `app`@`%`;", model.Statements[1]);
    }

    [Fact]
    public void GrantRole_MariaDb_OneDefaultRole_WithFor()
    {
        var reporting = new AccountInfo("reporting", string.Empty) { IsRole = true };
        var model = new GrantRoleDialogViewModel(App, [reporting], defaultRoles: null, isMariaDb: true);

        Assert.True(model.MakeDefault);
        Assert.False(model.DefaultRolesUnknown);
        Assert.Equal(["GRANT `reporting` TO `app`@`%`;", "SET DEFAULT ROLE `reporting` FOR `app`@`%`;"], model.Statements);
    }

    [Fact]
    public void GrantRole_NoRoles_CannotGrant()
    {
        var model = new GrantRoleDialogViewModel(App, [], [], isMariaDb: false);

        Assert.False(model.CanGrant);
        Assert.Empty(model.Statements);
        Assert.Equal(string.Empty, model.Sql);
    }

    [Fact]
    public void AvailableRoles_LeaveOutTheRolesTheUserHas()
    {
        var available = GrantRoleDialogViewModel.AvailableRoles(
            [App, ReaderRole, Writer],
            ["GRANT USAGE ON *.* TO `app`@`%`", "GRANT `reader`@`%` TO `app`@`%`"]);

        Assert.Equal([Writer], available);
    }

    [Fact]
    public async Task GrantRole_Target_UsersOnly()
    {
        var tree = await Tree.OpenAsync(canManage: true);

        Assert.NotNull(UserManager.GrantRoleTarget(tree.User("app@%")));
        Assert.Null(UserManager.GrantRoleTarget(tree.Role("reader@%")));
        Assert.Null(UserManager.GrantRoleTarget(tree.Users));
        Assert.Null(UserManager.GrantRoleTarget((await Tree.OpenAsync(canManage: false)).User("app@%")));
    }

    [Fact]
    public async Task GrantRole_RunsBothStatements_InOrder()
    {
        var tree = await Tree.OpenAsync(canManage: true);
        var runner = new FakeRunner();

        await new UserManager(runner, tree.Logger).ChangeGrantsAsync(
            (AccountNodeViewModel)tree.User("app@%"),
            ["GRANT `reader`@`%` TO `app`@`%`;", "SET DEFAULT ROLE `reader`@`%` TO `app`@`%`;"],
            CancellationToken.None);

        Assert.Equal(["GRANT `reader`@`%` TO `app`@`%`;", "SET DEFAULT ROLE `reader`@`%` TO `app`@`%`;"], runner.Executed);
        Assert.True(tree.User("app@%").IsExpanded);
    }

    private sealed class Tree
    {
        public FakeAccountReader Reader { get; } = new();

        public ListSink Sink { get; } = new();

        public ObjectExplorerForMySql.Core.Logging.IExtensionLogger Logger { get; private set; } = null!;

        public FolderNodeViewModel Users { get; private set; } = null!;

        public FolderNodeViewModel Roles { get; private set; } = null!;

        public static async Task<T> ExpandAsync<T>(T node)
            where T : TreeNodeViewModel
        {
            node.IsExpanded = true;
            await node.WaitForLoadAsync();
            return node;
        }

        public static async Task<Tree> OpenAsync(bool canManage)
        {
            var tree = new Tree();
            tree.Logger = new ObjectExplorerForMySql.Core.Logging.ExtensionLogger(tree.Sink, ObjectExplorerForMySql.Core.Logging.LogLevel.Debug);
            tree.Reader.Accounts = [App, Root, Sys, ReaderRole];
            var metadata = new FakeMetadataProvider();
            metadata.ConnectResults.Enqueue(_ => Task.FromResult(new ServerInfo("8.0.36", "root@localhost")));
            var context = new ExplorerContext(metadata, new FakePasswordPrompt(), new ExplorerTexts(), tree.Logger)
            {
                AccountReader = tree.Reader,
                PrivilegeChecker = new PrivilegeChecker(new FixedGrants(canManage ? "GRANT ALL PRIVILEGES ON *.* TO `root`@`localhost`" : "GRANT SELECT ON *.* TO `root`@`localhost`"), tree.Logger),
            };
            var server = await ExpandAsync(new ServerNodeViewModel(context, TestExplorer.Definition()));
            var security = await ExpandAsync((FolderNodeViewModel)server.Children[1]);
            tree.Users = await ExpandAsync((FolderNodeViewModel)security.Children[0]);
            tree.Roles = await ExpandAsync((FolderNodeViewModel)security.Children[1]);
            return tree;
        }

        public TreeNodeViewModel User(string label) => Users.Children.Single(c => c.Label == label);

        public TreeNodeViewModel Role(string label) => Roles.Children.Single(c => c.Label == label);
    }

    private sealed class FakeAccountReader : IAccountReader
    {
        public IReadOnlyList<AccountInfo> Accounts { get; set; } = [];

        public Dictionary<string, IReadOnlyList<string>> Grants { get; } = [];

        public Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(ConnectionDefinition definition, string? serverVersion, CancellationToken cancellationToken) =>
            Task.FromResult(MySqlAccountReader.Sort(Accounts));

        public IReadOnlyList<AccountInfo>? DefaultRoles { get; set; } = [];

        public Task<IReadOnlyList<AccountInfo>> GetDefaultRolesAsync(ConnectionDefinition definition, AccountInfo user, CancellationToken cancellationToken) =>
            DefaultRoles is { } roles ? Task.FromResult(roles) : throw new InvalidOperationException("SELECT command denied");

        public Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, AccountInfo account, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.TryGetValue(account.Label, out var lines) ? lines : (IReadOnlyList<string>)[]);
    }

    private sealed class FixedGrants(string line) : IGrantSource
    {
        public Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([line]);

        public Task<IReadOnlyList<string>> GetRoleGrantsAsync(ConnectionDefinition definition, IReadOnlyList<string> roles, bool isMariaDb, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<bool> CanSelectUsersAsync(ConnectionDefinition definition, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
