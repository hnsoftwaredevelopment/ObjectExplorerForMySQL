using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Security;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Security;

public class PrivilegeTests
{
    // SHOW GRANTS of root on MySQL 8.0 / 8.4 (static privileges, dynamic privileges without spaces, proxy).
    private static readonly string[] MySql8Root =
    [
        "GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, RELOAD, SHUTDOWN, PROCESS, FILE, REFERENCES, INDEX, ALTER, SHOW DATABASES, SUPER, CREATE TEMPORARY TABLES, LOCK TABLES, EXECUTE, REPLICATION SLAVE, REPLICATION CLIENT, CREATE VIEW, SHOW VIEW, CREATE ROUTINE, ALTER ROUTINE, CREATE USER, EVENT, TRIGGER, CREATE TABLESPACE, CREATE ROLE, DROP ROLE ON *.* TO `root`@`localhost` WITH GRANT OPTION",
        "GRANT APPLICATION_PASSWORD_ADMIN,AUDIT_ABORT_EXEMPT,AUDIT_ADMIN,BACKUP_ADMIN,BINLOG_ADMIN,CONNECTION_ADMIN,ROLE_ADMIN,SESSION_VARIABLES_ADMIN,SET_USER_ID,SHOW_ROUTINE,SYSTEM_USER,SYSTEM_VARIABLES_ADMIN,XA_RECOVER_ADMIN ON *.* TO `root`@`localhost` WITH GRANT OPTION",
        "GRANT PROXY ON ``@`` TO `root`@`localhost` WITH GRANT OPTION",
    ];

    // An application account on MySQL 8: database, column and routine grants and two roles.
    private static readonly string[] MySql8App =
    [
        "GRANT USAGE ON *.* TO `app`@`%`",
        "GRANT ALL PRIVILEGES ON `shop`.* TO `app`@`%`",
        "GRANT SELECT (`id`, `name`), UPDATE (`name`) ON `shop`.`customers` TO `app`@`%`",
        "GRANT EXECUTE ON PROCEDURE `shop`.`do_it` TO `app`@`%`",
        "GRANT `dba_role`@`%`,`reader`@`%` TO `app`@`%`",
    ];

    // MariaDB 10.11: authentication in the GRANT line, quotes in the proxy line, a role without host.
    private static readonly string[] MariaDbRoot =
    [
        "GRANT ALL PRIVILEGES ON *.* TO `root`@`localhost` IDENTIFIED VIA mysql_native_password USING '*81F5E21E35407D884A6CD4A731AEBFB6AF209E1B' OR unix_socket WITH GRANT OPTION",
        "GRANT PROXY ON ''@'%' TO 'root'@'localhost' WITH GRANT OPTION",
    ];

    private static readonly string[] MariaDbApp =
    [
        "GRANT USAGE ON *.* TO `app`@`%` IDENTIFIED BY PASSWORD '*2470C0C06DEE42FD1618BB99005ADCA2EC9D1E19'",
        "GRANT SELECT ON `mysql`.`user` TO `app`@`%`",
        "GRANT `reporting` TO `app`@`%`",
    ];

    // MySQL 5.7 quotes accounts with single quotes.
    private static readonly string[] MySql57 =
    [
        "GRANT USAGE ON *.* TO 'app'@'%'",
        "GRANT SELECT ON `mysql`.* TO 'app'@'%'",
    ];

    [Fact]
    public void Parse_GlobalGrant_WithGrantOption()
    {
        var entry = GrantParser.Parse(MySql8Root[0])!;

        Assert.Equal(GrantLevel.Global, entry.Level);
        Assert.False(entry.IsRevoke);
        Assert.True(entry.WithGrantOption);
        Assert.Contains("CREATE USER", entry.Privileges);
        Assert.Contains("SHOW DATABASES", entry.Privileges);
        Assert.Equal(30, entry.Privileges.Count);
    }

    [Fact]
    public void Parse_DynamicPrivileges_WithoutSpaces()
    {
        var entry = GrantParser.Parse(MySql8Root[1])!;

        Assert.Equal(13, entry.Privileges.Count);
        Assert.Equal("ROLE_ADMIN", entry.Privileges[6]);
    }

    [Theory]
    [InlineData("GRANT PROXY ON ``@`` TO `root`@`localhost` WITH GRANT OPTION")]
    [InlineData("GRANT PROXY ON ''@'%' TO 'root'@'localhost' WITH GRANT OPTION")]
    public void Parse_Proxy(string line)
    {
        Assert.Equal(GrantLevel.Proxy, GrantParser.Parse(line)?.Level);
    }

    [Fact]
    public void Parse_DatabaseTableColumnAndRoutineGrants()
    {
        var all = GrantParser.Parse(MySql8App[1])!;
        Assert.Equal((GrantLevel.Database, "shop"), (all.Level, all.Database));
        Assert.Equal(["ALL"], all.Privileges);

        var columns = GrantParser.Parse(MySql8App[2])!;
        Assert.Equal((GrantLevel.Table, "shop", "customers"), (columns.Level, columns.Database, columns.ObjectName));
        Assert.Equal(["SELECT", "UPDATE"], columns.Privileges);

        var routine = GrantParser.Parse(MySql8App[3])!;
        Assert.Equal((GrantLevel.Routine, "shop", "do_it"), (routine.Level, routine.Database, routine.ObjectName));
    }

    [Fact]
    public void Parse_Roles_MySqlAndMariaDb()
    {
        Assert.Equal(["dba_role", "reader"], GrantParser.Parse(MySql8App[4])?.Roles);
        Assert.Equal(GrantLevel.Role, GrantParser.Parse(MariaDbApp[2])?.Level);
        Assert.Equal(["reporting"], GrantParser.Parse(MariaDbApp[2])?.Roles);
    }

    [Fact]
    public void Parse_MariaDbAuthentication_DoesNotConfuseTheParser()
    {
        var entry = GrantParser.Parse(MariaDbRoot[0])!;

        Assert.Equal(GrantLevel.Global, entry.Level);
        Assert.Equal(["ALL"], entry.Privileges);
        Assert.True(entry.WithGrantOption);
    }

    [Fact]
    public void Parse_QuotedNamesWithDotsAndBackticks()
    {
        var entry = GrantParser.Parse("GRANT SELECT ON `my.db`.`we``ird` TO `u`@`%`")!;

        Assert.Equal(("my.db", "we`ird"), (entry.Database, entry.ObjectName));
    }

    [Fact]
    public void Parse_PartialRevoke()
    {
        var entry = GrantParser.Parse("REVOKE SELECT ON `mysql`.* FROM `u`@`%`")!;

        Assert.True(entry.IsRevoke);
        Assert.Equal((GrantLevel.Database, "mysql"), (entry.Level, entry.Database));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Grants for app@%")]
    [InlineData("GRANT SELECT ON")]
    [InlineData("SET DEFAULT ROLE `r` TO `u`@`%`")]
    public void Parse_Unreadable_IsNull(string line)
    {
        Assert.Null(GrantParser.Parse(line));
    }

    [Fact]
    public void ParseAll_CountsUnknownLines()
    {
        var grants = GrantParser.ParseAll([.. MySql57, "something else"], out var unknown);

        Assert.Equal(2, grants.Count);
        Assert.Equal(1, unknown);
    }

    [Theory]
    [MemberData(nameof(Accounts))]
    public void Evaluate_RealAccounts(string name, string[] lines, bool read, bool manage)
    {
        var grants = GrantParser.ParseAll(lines, out _);

        Assert.True(read == AccountPrivileges.AllowsReadingUsers(grants), name + " read");
        Assert.True(manage == AccountPrivileges.AllowsManagingUsers(grants), name + " manage");
    }

    public static TheoryData<string, string[], bool, bool> Accounts() => new()
    {
        { "MySQL 8 root", MySql8Root, true, true },
        { "MySQL 8 app", MySql8App, false, false },
        { "MariaDB root", MariaDbRoot, true, true },
        { "MariaDB app with SELECT on mysql.user", MariaDbApp, true, false },
        { "MySQL 5.7 SELECT on mysql.*", MySql57, true, false },
        { "CREATE USER only", ["GRANT CREATE USER ON *.* TO `admin`@`%`"], false, true },
        { "Global SELECT", ["GRANT SELECT ON *.* TO `u`@`%`"], true, false },
        { "Global SELECT, revoked on mysql", ["GRANT SELECT ON *.* TO `u`@`%`", "REVOKE SELECT ON `mysql`.* FROM `u`@`%`"], false, false },
        { "SELECT on another table of mysql", ["GRANT SELECT ON `mysql`.`db` TO `u`@`%`"], false, false },
        { "CREATE USER on a database is not global", ["GRANT ALL PRIVILEGES ON `mysql`.* TO `u`@`%`"], true, false },
    };

    [Fact]
    public void RolesOf_ListsAllGrantedRoles()
    {
        Assert.Equal(["dba_role", "reader"], AccountPrivileges.RolesOf(GrantParser.ParseAll(MySql8App, out _)));
    }

    [Fact]
    public async Task Check_FromGrants()
    {
        var source = new FakeGrantSource { Grants = MySql8Root };

        var result = await Checker(source).CheckAsync(TestExplorer.Definition(), "8.0.36", CancellationToken.None);

        Assert.Equal(new AccountPrivileges(true, true, PrivilegeSource.Grants), result);
        Assert.Equal(0, source.TestQueries);
    }

    [Fact]
    public async Task Check_RoleGrants_CountForManaging_AndTheTestQueryForReading()
    {
        var source = new FakeGrantSource
        {
            Grants = MySql8App,
            RoleGrants = ["GRANT CREATE USER ON *.* TO `app`@`%`"],
            CanSelect = true,
        };

        var result = await Checker(source).CheckAsync(TestExplorer.Definition(), "8.0.36", CancellationToken.None);

        Assert.Equal(new AccountPrivileges(true, true, PrivilegeSource.TestQuery), result);
        Assert.Equal(["dba_role", "reader"], source.RolesAsked);
        Assert.False(source.AskedForMariaDb);
    }

    [Fact]
    public async Task Check_ReadingThroughARole_NeedsTheTestQuery()
    {
        // The role may not be active in a new session (SET DEFAULT ROLE NONE): its grants do not count for reading.
        var source = new FakeGrantSource
        {
            Grants = ["GRANT USAGE ON *.* TO `u`@`%`", "GRANT `oe_role`@`%` TO `u`@`%`"],
            RoleGrants = ["GRANT USAGE ON *.* TO `u`@`%`", "GRANT SELECT ON `mysql`.* TO `u`@`%`", "GRANT `oe_role`@`%` TO `u`@`%`"],
            CanSelect = false,
        };

        var result = await Checker(source).CheckAsync(TestExplorer.Definition(), "8.0.36", CancellationToken.None);

        Assert.Equal(new AccountPrivileges(false, false, PrivilegeSource.Grants), result);
        Assert.Equal(1, source.TestQueries);
    }

    [Fact]
    public async Task Check_MariaDbRoles_AskPerRole()
    {
        var source = new FakeGrantSource { Grants = MariaDbApp, RoleGrants = [] };

        await Checker(source).CheckAsync(TestExplorer.Definition(), "10.11.6-MariaDB", CancellationToken.None);

        Assert.True(source.AskedForMariaDb);
    }

    [Fact]
    public async Task Check_NoAccess_TestQueryDenied()
    {
        var source = new FakeGrantSource { Grants = ["GRANT USAGE ON *.* TO `u`@`%`"], CanSelect = false };

        var result = await Checker(source).CheckAsync(TestExplorer.Definition(), "8.0.36", CancellationToken.None);

        Assert.Equal(new AccountPrivileges(false, false, PrivilegeSource.Grants), result);
        Assert.Equal(1, source.TestQueries);
    }

    [Fact]
    public async Task Check_ShowGrantsFails_TestQueryDecides()
    {
        var source = new FakeGrantSource { GrantsFail = true, CanSelect = true };

        Assert.Equal(new AccountPrivileges(true, false, PrivilegeSource.TestQuery), await Checker(source).CheckAsync(TestExplorer.Definition(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Check_EverythingFails_AllowsNothing()
    {
        var source = new FakeGrantSource { GrantsFail = true, TestQueryFails = true };

        Assert.Equal(AccountPrivileges.None, await Checker(source).CheckAsync(TestExplorer.Definition(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Check_RoleGrantsFail_StillUsesTheOwnGrants()
    {
        var source = new FakeGrantSource { Grants = [.. MySql8Root, "GRANT `r`@`%` TO `root`@`localhost`"], RoleGrantsFail = true };

        Assert.Equal(new AccountPrivileges(true, true, PrivilegeSource.Grants), await Checker(source).CheckAsync(TestExplorer.Definition(), "8.4.2", CancellationToken.None));
    }

    [Fact]
    public async Task Check_Cancellation_IsThrown()
    {
        var source = new FakeGrantSource { Cancel = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Checker(source).CheckAsync(TestExplorer.Definition(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Server_StoresThePrivileges_AfterConnect_AndForgetsThemOnDisconnect()
    {
        var context = TestExplorer.Context(new FakeMetadataProvider());
        context.PrivilegeChecker = Checker(new FakeGrantSource { Grants = MySql8Root });
        var server = new ServerNodeViewModel(context, TestExplorer.Definition());

        server.IsExpanded = true;
        await server.WaitForLoadAsync();

        Assert.Equal(ConnectionState.Connected, server.State);
        Assert.True(server.Privileges?.CanReadUsers);
        Assert.True(((ObjectExplorerForMySql.Core.ObjectProperties.ServerProperties)server.CreateProperties()!).CanManageUsers);

        server.Disconnect();
        Assert.Null(server.Privileges);
        Assert.Null(((ObjectExplorerForMySql.Core.ObjectProperties.ServerProperties)server.CreateProperties()!).CanReadUsers);
    }

    [Fact]
    public async Task Server_WithoutChecker_HasNoPrivileges()
    {
        var server = new ServerNodeViewModel(TestExplorer.Context(), TestExplorer.Definition());
        server.IsExpanded = true;
        await server.WaitForLoadAsync();

        Assert.Equal(ConnectionState.Connected, server.State);
        Assert.Null(server.Privileges);
    }

    private static PrivilegeChecker Checker(IGrantSource source) =>
        new(source, new ExtensionLogger(new ListSink(), LogLevel.Debug));

    private sealed class FakeGrantSource : IGrantSource
    {
        public IReadOnlyList<string> Grants { get; set; } = [];

        public IReadOnlyList<string> RoleGrants { get; set; } = [];

        public bool GrantsFail { get; set; }

        public bool RoleGrantsFail { get; set; }

        public bool CanSelect { get; set; }

        public bool TestQueryFails { get; set; }

        public bool Cancel { get; set; }

        public int TestQueries { get; private set; }

        public IReadOnlyList<string>? RolesAsked { get; private set; }

        public bool AskedForMariaDb { get; private set; }

        public Task<IReadOnlyList<string>> GetGrantsAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
        {
            if (Cancel)
            {
                throw new OperationCanceledException();
            }

            return GrantsFail ? throw new InvalidOperationException("denied") : Task.FromResult(Grants);
        }

        public Task<IReadOnlyList<string>> GetRoleGrantsAsync(ConnectionDefinition definition, IReadOnlyList<string> roles, bool isMariaDb, CancellationToken cancellationToken)
        {
            RolesAsked = roles;
            AskedForMariaDb = isMariaDb;
            return RoleGrantsFail ? throw new InvalidOperationException("role not granted") : Task.FromResult(RoleGrants);
        }

        public Task<bool> CanSelectUsersAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
        {
            TestQueries++;
            return TestQueryFails ? throw new InvalidOperationException("lost connection") : Task.FromResult(CanSelect);
        }
    }
}
