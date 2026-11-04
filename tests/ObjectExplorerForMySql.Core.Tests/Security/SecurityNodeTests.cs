using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.ObjectProperties;
using ObjectExplorerForMySql.Core.Security;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Security;

public class SecurityNodeTests
{
    private static readonly AccountInfo App = new("app", "%") { Plugin = "caching_sha2_password", IsLocked = false, PasswordExpired = false, PasswordLifetime = 90 };
    private static readonly AccountInfo Root = new("root", "localhost") { Plugin = "caching_sha2_password", IsLocked = false };
    private static readonly AccountInfo Locked = new("old", "%") { IsLocked = true };
    private static readonly AccountInfo Reader = new("reader", "%") { IsRole = true, IsLocked = true };

    [Theory]
    [InlineData("SELECT, INSERT ON `shop`.* TO `app`@`%`", "SELECT, INSERT ON `shop`.*")]
    [InlineData("GRANT ALL PRIVILEGES ON *.* TO `root`@`localhost` WITH GRANT OPTION", "ALL PRIVILEGES ON *.* (WITH GRANT OPTION)")]
    [InlineData("GRANT SELECT (`id`, `name`) ON `shop`.`customers` TO `app`@`%`", "SELECT (`id`, `name`) ON `shop`.`customers`")]
    [InlineData("GRANT `reader`@`%`,`writer`@`%` TO `app`@`%`", "`reader`@`%`,`writer`@`%`")]
    [InlineData("REVOKE SELECT ON `mysql`.* FROM `u`@`%`", "REVOKE SELECT ON `mysql`.*")]
    [InlineData("GRANT SELECT ON `to`.`x` TO `u`@`%`", "SELECT ON `to`.`x`")]
    public void GrantText_LeavesOutTheAccount(string line, string expected)
    {
        Assert.Equal(expected, GrantText.Label(line));
    }

    [Fact]
    public void GrantText_NeverShowsAPasswordHash()
    {
        var label = GrantText.Label("GRANT USAGE ON *.* TO `app`@`%` IDENTIFIED BY PASSWORD '*2470C0C06DEE42FD1618BB99005ADCA2EC9D1E19'");

        Assert.Equal("USAGE ON *.*", label);
    }

    [Fact]
    public void Account_LabelAndSql()
    {
        Assert.Equal("app@%", App.Label);
        Assert.Equal("`app`@`%`", App.Sql);
        Assert.Equal("@localhost", new AccountInfo(string.Empty, "localhost").Label);

        var mariaRole = new AccountInfo("reporting", string.Empty) { IsRole = true };
        Assert.Equal("reporting", mariaRole.Label);
        Assert.Equal("`reporting`", mariaRole.Sql);
    }

    [Fact]
    public void Accounts_AreSortedByUserThenHost()
    {
        var sorted = MySqlAccountReader.Sort([new AccountInfo("b", "%"), new AccountInfo("A", "localhost"), new AccountInfo("a", "%")]);

        Assert.Equal(["a@%", "A@localhost", "b@%"], sorted.Select(a => a.Label));
    }

    [Theory]
    [InlineData("8.0.36", true)]
    [InlineData("5.7.44", false)]
    [InlineData("10.0.5-MariaDB", true)]
    [InlineData("10.0.4-MariaDB", false)]
    [InlineData(null, false)]
    public void Roles_DependOnTheServerVersion(string? version, bool supported)
    {
        Assert.Equal(supported, SecurityFolder.SupportsRoles(version));
    }

    [Fact]
    public async Task SecurityFolder_AfterDatabases_WithUsersAndRoles()
    {
        var (server, _) = await ConnectAsync(canRead: true);

        Assert.Equal(["Databases", "Security"], server.Children.Select(c => c.Label));
        var security = await ExpandAsync((FolderNodeViewModel)server.Children[1]);
        Assert.Equal(["Users", "Roles"], security.Children.Select(c => c.Label));

        var users = await ExpandAsync((FolderNodeViewModel)security.Children[0]);
        Assert.Equal(["app@%", "old@%", "root@localhost"], users.Children.Select(c => c.Label));
        Assert.Equal([NodeIcon.User, NodeIcon.LockedUser, NodeIcon.User], users.Children.Select(c => c.Icon));

        var roles = await ExpandAsync((FolderNodeViewModel)security.Children[1]);
        var role = Assert.IsType<AccountNodeViewModel>(Assert.Single(roles.Children));
        Assert.Equal(("reader@%", NodeIcon.Role), (role.Label, role.Icon));
    }

    [Fact]
    public async Task NoSecurityFolder_WhenTheAccountMayNotReadUsers()
    {
        var (server, _) = await ConnectAsync(canRead: false);

        Assert.Equal(["Databases"], server.Children.Select(c => c.Label));
    }

    [Fact]
    public async Task NoRolesFolder_OnMySql57()
    {
        var (server, _) = await ConnectAsync(canRead: true, version: "5.7.44");
        var security = await ExpandAsync((FolderNodeViewModel)server.Children[1]);

        Assert.Equal(["Users"], security.Children.Select(c => c.Label));
    }

    [Fact]
    public async Task User_ShowsItsGrants_WithoutTheAccount()
    {
        var (server, reader) = await ConnectAsync(canRead: true);
        reader.Grants["app@%"] =
        [
            "GRANT USAGE ON *.* TO `app`@`%`",
            "GRANT ALL PRIVILEGES ON `shop`.* TO `app`@`%` WITH GRANT OPTION",
            "GRANT `reader`@`%` TO `app`@`%`",
        ];
        var users = await ExpandAsync((FolderNodeViewModel)(await ExpandAsync((FolderNodeViewModel)server.Children[1])).Children[0]);

        var app = await ExpandAsync(users.Children.Single(c => c.Label == "app@%"));

        Assert.Equal(["USAGE ON *.*", "ALL PRIVILEGES ON `shop`.* (with grant option)", "`reader`@`%`"], app.Children.Select(c => c.Label));
        Assert.Equal([NodeIcon.Grant, NodeIcon.Grant, NodeIcon.Role], app.Children.Select(c => c.Icon));
        Assert.All(app.Children, c => Assert.Null(c.CreateDropTarget()));
        Assert.All(app.Children, c => Assert.Null(c.CreateRenameTarget()));
    }

    [Fact]
    public async Task User_GrantsDenied_ShowsTheError()
    {
        var (server, reader) = await ConnectAsync(canRead: true);
        reader.GrantsFail = true;
        var users = await ExpandAsync((FolderNodeViewModel)(await ExpandAsync((FolderNodeViewModel)server.Children[1])).Children[0]);

        var app = await ExpandAsync(users.Children[0]);

        var error = Assert.IsType<PlaceholderNodeViewModel>(Assert.Single(app.Children));
        Assert.True(error.IsError);
        Assert.Contains("SELECT command denied", error.Label);
    }

    [Fact]
    public void Properties_OfAUserAndARole()
    {
        var context = TestExplorer.Context();
        var server = new ServerNodeViewModel(context, TestExplorer.Definition());
        var user = (AccountProperties)new AccountNodeViewModel(context, server, App).CreateProperties()!;

        Assert.Equal(("app", "%", "caching_sha2_password"), (user.UserName, user.Host, user.AuthenticationPlugin));
        Assert.Equal((false, false, 90), (user.AccountLocked, user.PasswordExpired, user.PasswordLifetime));
        Assert.Equal("app@%", user.GetObjectName());
        Assert.Equal("User", user.GetTypeName());

        var role = (AccountProperties)new AccountNodeViewModel(context, server, Reader).CreateProperties()!;
        Assert.Equal("Role", role.GetTypeName());
        Assert.Null(role.PasswordExpired);
    }

    private static async Task<T> ExpandAsync<T>(T node)
        where T : TreeNodeViewModel
    {
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        return node;
    }

    private static async Task<(ServerNodeViewModel Server, FakeAccountReader Reader)> ConnectAsync(bool canRead, string version = "8.0.36")
    {
        var metadata = new FakeMetadataProvider();
        metadata.ConnectResults.Enqueue(_ => Task.FromResult(new ServerInfo(version, "root@localhost")));
        var reader = new FakeAccountReader { Accounts = [Root, App, Reader, Locked] };
        var context = TestExplorer.Context(metadata);
        context.AccountReader = reader;
        context.PrivilegeChecker = new PrivilegeChecker(
            new FixedGrants(canRead ? "GRANT SELECT ON `mysql`.* TO `u`@`%`" : "GRANT USAGE ON *.* TO `u`@`%`"),
            new ObjectExplorerForMySql.Core.Logging.ExtensionLogger(new ListSink(), ObjectExplorerForMySql.Core.Logging.LogLevel.Debug));
        var server = await ExpandAsync(new ServerNodeViewModel(context, TestExplorer.Definition()));
        return (server, reader);
    }

    private sealed class FakeAccountReader : IAccountReader
    {
        public IReadOnlyList<AccountInfo> Accounts { get; set; } = [];

        public Dictionary<string, IReadOnlyList<string>> Grants { get; } = [];

        public bool GrantsFail { get; set; }

        public Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(ConnectionDefinition definition, string? serverVersion, CancellationToken cancellationToken) =>
            Task.FromResult(MySqlAccountReader.Sort(Accounts));

        public IReadOnlyList<AccountInfo>? DefaultRoles { get; set; } = [];

        public Task<IReadOnlyList<AccountInfo>> GetDefaultRolesAsync(ConnectionDefinition definition, AccountInfo user, CancellationToken cancellationToken) =>
            DefaultRoles is { } roles ? Task.FromResult(roles) : throw new InvalidOperationException("SELECT command denied");

        public Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, AccountInfo account, CancellationToken cancellationToken) =>
            GrantsFail
                ? throw new InvalidOperationException("SELECT command denied to user 'u'@'%' for table 'user'")
                : Task.FromResult(Grants.TryGetValue(account.Label, out var lines) ? lines : (IReadOnlyList<string>)[]);
    }

    private sealed class FixedGrants(string line) : IGrantSource
    {
        public Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([line]);

        public Task<IReadOnlyList<string>> GetRoleGrantsAsync(ConnectionDefinition definition, IReadOnlyList<string> roles, bool isMariaDb, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<bool> CanSelectUsersAsync(ConnectionDefinition definition, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
