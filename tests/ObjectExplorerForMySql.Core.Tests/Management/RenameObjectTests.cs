using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Management;

public class RenameObjectTests
{
    [Theory]
    [InlineData(RenameObjectKind.Table, null, "RENAME TABLE `shop`.`orders` TO `shop`.`order`;")]
    [InlineData(RenameObjectKind.View, null, "RENAME TABLE `shop`.`orders` TO `shop`.`order`;")]
    [InlineData(RenameObjectKind.Column, "t", "ALTER TABLE `shop`.`t` RENAME COLUMN `orders` TO `order`;")]
    public void Sql_PerKind(RenameObjectKind kind, string? table, string expected)
    {
        Assert.Equal(expected, new RenameTarget(kind, "shop", "orders", table).BuildSql("order"));
    }

    [Fact]
    public void Sql_EscapesBackticks()
    {
        Assert.Equal("RENAME TABLE `shop`.`a` TO `shop`.`b``c`;", new RenameTarget(RenameObjectKind.Table, "shop", "a").BuildSql("b`c"));
    }

    [Theory]
    [InlineData("orders", NameProblem.Unchanged)]
    [InlineData("", NameProblem.Empty)]
    [InlineData("   ", NameProblem.Empty)]
    [InlineData(null, NameProblem.Empty)]
    [InlineData("new ", NameProblem.EndsWithSpace)]
    [InlineData(" new", NameProblem.None)]
    [InlineData("Orders", NameProblem.None)]
    [InlineData("order details", NameProblem.None)]
    public void Check_TypedName(string? typed, NameProblem expected)
    {
        Assert.Equal(expected, new RenameTarget(RenameObjectKind.Table, "shop", "orders").Check(typed));
    }

    [Fact]
    public void Check_MaxLengthIs64()
    {
        var target = new RenameTarget(RenameObjectKind.Table, "shop", "orders");

        Assert.Equal(NameProblem.None, target.Check(new string('a', 64)));
        Assert.Equal(NameProblem.TooLong, target.Check(new string('a', 65)));
    }

    [Fact]
    public void BuildSql_RefusesABadName()
    {
        Assert.Throws<ArgumentException>(() => new RenameTarget(RenameObjectKind.Table, "shop", "orders").BuildSql("orders"));
    }

    [Theory]
    [InlineData("8.0.36", true)]
    [InlineData("5.7.44-log", false)]
    [InlineData("10.5.2-MariaDB", true)]
    [InlineData("10.4.32-MariaDB", false)]
    public void RenameColumn_DependsOnTheServerVersion(string version, bool supported)
    {
        Assert.Equal(supported, ServerCapabilities.FromVersion(version).SupportsRenameColumn);
    }

    [Fact]
    public async Task Targets_TableViewAndTableColumnOnly()
    {
        var tree = await TestTree.OpenAsync();
        var view = await TestTree.ExpandAsync((ViewNodeViewModel)(await TestTree.ExpandAsync(tree.Folder(tree.Database, "Views"))).Children[0]);
        var viewColumns = await TestTree.ExpandAsync((FolderNodeViewModel)view.Children[0]);

        Assert.Equal(RenameObjectKind.Table, ObjectRenamer.TargetOf(tree.Table)?.Kind);
        Assert.Equal(RenameObjectKind.View, ObjectRenamer.TargetOf(view)?.Kind);
        Assert.Equal("shop.orders.note", ObjectRenamer.TargetOf(tree.Column("note"))?.DisplayName);
        Assert.Null(ObjectRenamer.TargetOf(viewColumns.Children[0]));
        Assert.Null(ObjectRenamer.TargetOf(tree.Database));
        Assert.Null(ObjectRenamer.TargetOf(tree.Folder(tree.Table, "Indexes").Children[0]));
        Assert.Null(ObjectRenamer.TargetOf(tree.Server));
        Assert.Null(ObjectRenamer.TargetOf(null));
    }

    [Fact]
    public async Task NoTarget_InASystemDatabase_OrForAColumnOnMySql57()
    {
        var old = await TestTree.OpenAsync("5.7.44");
        Assert.Null(ObjectRenamer.TargetOf(old.Column("note")));
        Assert.NotNull(ObjectRenamer.TargetOf(old.Table));

        var system = await TestTree.OpenAsync(databaseName: "mysql");
        Assert.Null(ObjectRenamer.TargetOf(system.Table));
    }

    [Fact]
    public async Task Rename_Table_RunsTheSql_SelectsAndExpandsTheNewNode()
    {
        var tree = await TestTree.OpenAsync();
        var runner = new FakeRunner();
        tree.Metadata.Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "sales", "InnoDB", null)]);

        var renamed = await new ObjectRenamer(runner, tree.Logger).RenameAsync(tree.Table, ObjectRenamer.TargetOf(tree.Table)!, "sales", CancellationToken.None);

        Assert.True(renamed);
        Assert.Equal(["RENAME TABLE `shop`.`orders` TO `shop`.`sales`;"], runner.Executed);
        var node = Assert.IsType<TableNodeViewModel>(Assert.Single(tree.Folder(tree.Database, "Tables").Children));
        Assert.Equal("sales", node.Key);
        Assert.True(node.IsSelected);
        Assert.True(node.IsExpanded);
    }

    [Fact]
    public async Task Rename_Column_RefreshesTheTable_AndSelectsTheColumn()
    {
        var tree = await TestTree.OpenAsync();
        var column = tree.Column("note");
        column.IsSelected = true;
        tree.Metadata.Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([TestTree.IdColumn, new ColumnInfo("remark", "varchar(20)", true, false, false, string.Empty)]);
        var runner = new FakeRunner();

        await new ObjectRenamer(runner, tree.Logger).RenameAsync(column, ObjectRenamer.TargetOf(column)!, "remark", CancellationToken.None);

        Assert.Equal(["ALTER TABLE `shop`.`orders` RENAME COLUMN `note` TO `remark`;"], runner.Executed);
        Assert.Same(tree.Table, ObjectRenamer.RefreshNodeOf(column, ObjectRenamer.TargetOf(column)!));
        Assert.True(tree.Column("remark").IsSelected);
    }

    [Fact]
    public async Task Rename_ServerError_IsThrown_AndTheTreeStays()
    {
        var tree = await TestTree.OpenAsync();
        var runner = new FakeRunner { Fail = new InvalidOperationException("Table 'sales' already exists") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ObjectRenamer(runner, tree.Logger).RenameAsync(tree.Table, ObjectRenamer.TargetOf(tree.Table)!, "sales", CancellationToken.None));

        Assert.Same(tree.Table, Assert.Single(tree.Folder(tree.Database, "Tables").Children));
    }

    [Fact]
    public async Task Rename_CancelledPassword_RunsNothing()
    {
        var tree = await TestTree.OpenAsync();
        tree.Server.Definition.Password = null;
        var runner = new FakeRunner();

        Assert.False(await new ObjectRenamer(runner, tree.Logger).RenameAsync(tree.Table, ObjectRenamer.TargetOf(tree.Table)!, "sales", CancellationToken.None));
        Assert.Empty(runner.Executed);
    }

    [Fact]
    public void Node_IsEditing_RaisesPropertyChanged()
    {
        var node = new PlaceholderNodeViewModel(TestExplorer.Context(), NodeIcon.Loading, "x");
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsEditing = true;

        Assert.True(node.IsEditing);
        Assert.Equal([nameof(TreeNodeViewModel.IsEditing)], changed);
    }
}

internal sealed class FakeRunner : ISchemaCommandRunner
{
    public List<string> Executed { get; } = [];

    public Exception? Fail { get; set; }

    public List<ObjectExplorerForMySql.Core.Data.SqlCommandText> Commands { get; } = [];

    public Task ExecuteAsync(ConnectionDefinition definition, ObjectExplorerForMySql.Core.Data.SqlCommandText command, CancellationToken cancellationToken)
    {
        Commands.Add(command);
        return ExecuteAsync(definition, command.Sql, cancellationToken);
    }

    public Task ExecuteAsync(ConnectionDefinition definition, string sql, CancellationToken cancellationToken)
    {
        if (Fail is not null)
        {
            throw Fail;
        }

        Executed.Add(sql);
        return Task.CompletedTask;
    }
}

/// <summary>A connected server with database shop (or another name), table orders with all folders expanded, and a view.</summary>
internal sealed class TestTree
{
    public static readonly ColumnInfo IdColumn = new("id", "int", false, true, false, "auto_increment");

    public FakeMetadataProvider Metadata { get; } = new();

    public FakePasswordPrompt Prompt { get; } = new();

    public IExtensionLogger Logger { get; } = new ExtensionLogger(new ListSink(), LogLevel.Debug);

    public ServerNodeViewModel Server { get; private set; } = null!;

    public FolderNodeViewModel Databases { get; private set; } = null!;

    public DatabaseNodeViewModel Database { get; private set; } = null!;

    public TableNodeViewModel Table { get; private set; } = null!;

    public static async Task<T> ExpandAsync<T>(T node)
        where T : TreeNodeViewModel
    {
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        return node;
    }

    public static async Task<TestTree> OpenAsync(string serverVersion = "8.0.36", string databaseName = "shop")
    {
        var tree = new TestTree();
        var m = tree.Metadata;
        m.ConnectResults.Enqueue(_ => Task.FromResult(new ServerInfo(serverVersion, "app@%")));
        m.Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo(databaseName, null, null), new DatabaseInfo("sys", null, null)]);
        m.Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "orders", "InnoDB", null)]);
        m.Views = [new TableInfo(databaseName, "v_orders", null, null, isView: true)];
        m.Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([IdColumn, new ColumnInfo("note", "varchar(20)", true, false, false, string.Empty)]);
        m.Keys = [new KeyInfo("PRIMARY", KeyType.Primary, ["id"])];
        m.Triggers = [new TriggerInfo("trg_bi", "BEFORE", "INSERT")];
        m.Indexes = [new IndexInfo("PRIMARY", true, "BTREE", ["id"]), new IndexInfo("ix_date", false, "BTREE", ["note"])];

        var context = new ExplorerContext(m, tree.Prompt, new ExplorerTexts(), tree.Logger);
        tree.Server = await ExpandAsync(new ServerNodeViewModel(context, TestExplorer.Definition()));
        tree.Databases = await ExpandAsync((FolderNodeViewModel)tree.Server.Children[0]);
        var database = tree.Databases.Children.OfType<DatabaseNodeViewModel>().SingleOrDefault()
            ?? (await ExpandAsync((FolderNodeViewModel)tree.Databases.Children[0])).Children.OfType<DatabaseNodeViewModel>().Single(d => d.Key == databaseName);
        tree.Database = await ExpandAsync(database);
        var tables = await ExpandAsync(tree.Folder(tree.Database, "Tables"));
        tree.Table = await ExpandAsync((TableNodeViewModel)tables.Children[0]);
        foreach (var folder in tree.Table.Children.OfType<FolderNodeViewModel>())
        {
            await ExpandAsync(folder);
        }

        return tree;
    }

    public FolderNodeViewModel Folder(TreeNodeViewModel parent, string name) =>
        parent.Children.OfType<FolderNodeViewModel>().Single(f => f.Key == name);

    public TreeNodeViewModel Column(string name) =>
        Folder(Table, "Columns").Children.Single(c => c.Key == name);
}
