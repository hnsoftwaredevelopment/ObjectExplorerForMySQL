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

public class DeleteObjectTests
{
    [Theory]
    [InlineData(DropObjectKind.Database, "shop", "shop", null, "DROP DATABASE `shop`;")]
    [InlineData(DropObjectKind.Table, "shop", "orders", null, "DROP TABLE `shop`.`orders`;")]
    [InlineData(DropObjectKind.View, "shop", "v_orders", null, "DROP VIEW `shop`.`v_orders`;")]
    [InlineData(DropObjectKind.Procedure, "shop", "do_it", null, "DROP PROCEDURE `shop`.`do_it`;")]
    [InlineData(DropObjectKind.Function, "shop", "fn", null, "DROP FUNCTION `shop`.`fn`;")]
    [InlineData(DropObjectKind.Trigger, "shop", "trg_bi", null, "DROP TRIGGER `shop`.`trg_bi`;")]
    [InlineData(DropObjectKind.Event, "shop", "nightly", null, "DROP EVENT `shop`.`nightly`;")]
    [InlineData(DropObjectKind.Index, "shop", "ix_date", "orders", "ALTER TABLE `shop`.`orders` DROP INDEX `ix_date`;")]
    [InlineData(DropObjectKind.Index, "shop", "PRIMARY", "orders", "ALTER TABLE `shop`.`orders` DROP PRIMARY KEY;")]
    [InlineData(DropObjectKind.Column, "shop", "note", "orders", "ALTER TABLE `shop`.`orders` DROP COLUMN `note`;")]
    public void Sql_PerKind(DropObjectKind kind, string database, string name, string? table, string expected)
    {
        Assert.Equal(expected, new DropTarget(kind, database, name, table).Sql);
    }

    [Fact]
    public void Sql_EscapesBackticksAndKeepsReservedWordsQuoted()
    {
        var target = new DropTarget(DropObjectKind.Column, "my db", "order", "we`ird");

        Assert.Equal("ALTER TABLE `my db`.`we``ird` DROP COLUMN `order`;", target.Sql);
        Assert.Equal("my db.we`ird.order", target.DisplayName);
    }

    [Theory]
    [InlineData(DropObjectKind.Index)]
    [InlineData(DropObjectKind.Column)]
    public void IndexAndColumn_NeedATable(DropObjectKind kind)
    {
        Assert.Throws<ArgumentException>(() => new DropTarget(kind, "shop", "x"));
    }

    [Theory]
    [InlineData("shop", true)]
    [InlineData("Shop", false)]
    [InlineData("shop ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Database_IsOnlyConfirmedByItsExactName(string? typed, bool confirmed)
    {
        Assert.Equal(confirmed, new DropTarget(DropObjectKind.Database, "shop", "shop").IsConfirmedBy(typed));
    }

    [Fact]
    public void OtherObjects_NeedNoName()
    {
        var target = new DropTarget(DropObjectKind.Table, "shop", "orders");

        Assert.False(target.RequiresNameConfirmation);
        Assert.True(target.IsConfirmedBy(null));
    }

    [Fact]
    public void Confirmation_EnablesDeleteWhenTheNameIsTyped()
    {
        var confirmation = new DeleteConfirmation(new DropTarget(DropObjectKind.Database, "shop", "shop"));
        var changed = new List<string?>();
        confirmation.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(confirmation.RequiresName);
        Assert.False(confirmation.CanDelete);
        confirmation.TypedName = "sho";
        Assert.False(confirmation.CanDelete);
        confirmation.TypedName = "shop";

        Assert.True(confirmation.CanDelete);
        Assert.Contains(nameof(DeleteConfirmation.CanDelete), changed);
        Assert.Equal("DROP DATABASE `shop`;", confirmation.Sql);
    }

    [Fact]
    public async Task Targets_OfTheTreeNodes()
    {
        var tree = await Tree.OpenAsync();

        Assert.Equal("DROP DATABASE `shop`;", ObjectDeleter.TargetOf(tree.Database)?.Sql);
        Assert.Equal("DROP TABLE `shop`.`orders`;", ObjectDeleter.TargetOf(tree.Table)?.Sql);
        Assert.Equal("ALTER TABLE `shop`.`orders` DROP COLUMN `note`;", ObjectDeleter.TargetOf(tree.Folder(tree.Table, "Columns").Children[1])?.Sql);
        Assert.Equal("DROP TRIGGER `shop`.`trg_bi`;", ObjectDeleter.TargetOf(tree.Folder(tree.Table, "Triggers").Children[0])?.Sql);
        var indexes = tree.Folder(tree.Table, "Indexes").Children;
        Assert.Equal("ALTER TABLE `shop`.`orders` DROP PRIMARY KEY;", ObjectDeleter.TargetOf(indexes[0])?.Sql);
        Assert.Equal("ALTER TABLE `shop`.`orders` DROP INDEX `ix_date`;", ObjectDeleter.TargetOf(indexes[1])?.Sql);
    }

    [Fact]
    public async Task NoTarget_ForServerFoldersKeysViewColumnsAndSystemDatabases()
    {
        var tree = await Tree.OpenAsync();
        var view = await Tree.ExpandAsync((ViewNodeViewModel)(await Tree.ExpandAsync(tree.Folder(tree.Database, "Views"))).Children[0]);
        var viewColumns = await Tree.ExpandAsync((FolderNodeViewModel)view.Children[0]);
        var systemFolder = await Tree.ExpandAsync((FolderNodeViewModel)tree.Databases.Children[0]);

        Assert.Null(ObjectDeleter.TargetOf(tree.Server));
        Assert.Null(ObjectDeleter.TargetOf(tree.Databases));
        Assert.Null(ObjectDeleter.TargetOf(tree.Folder(tree.Database, "Tables")));
        Assert.Null(ObjectDeleter.TargetOf(tree.Folder(tree.Table, "Keys").Children[0]));
        Assert.Null(ObjectDeleter.TargetOf(viewColumns.Children[0]));
        Assert.Equal("DROP VIEW `shop`.`v_orders`;", ObjectDeleter.TargetOf(view)?.Sql);
        Assert.Null(ObjectDeleter.TargetOf(systemFolder.Children.Single()));
        Assert.Null(ObjectDeleter.TargetOf(null));
    }

    [Fact]
    public async Task Delete_RunsTheSql_AndRefreshesTheFolder()
    {
        var tree = await Tree.OpenAsync();
        var tables = tree.Folder(tree.Database, "Tables");
        tree.Table.IsSelected = true;
        var runner = new FakeRunner();
        tree.Metadata.Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([]);

        var deleted = await new ObjectDeleter(runner, tree.Logger).DeleteAsync(tree.Table, ObjectDeleter.TargetOf(tree.Table)!, CancellationToken.None);

        Assert.True(deleted);
        Assert.Equal(["DROP TABLE `shop`.`orders`;"], runner.Executed);
        Assert.Empty(tables.Children);
        Assert.True(tables.IsSelected);
    }

    [Fact]
    public async Task Delete_OfAColumn_RefreshesTheTable()
    {
        var tree = await Tree.OpenAsync();
        var column = tree.Folder(tree.Table, "Columns").Children[1];
        tree.Metadata.Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([Tree.IdColumn]);
        tree.Metadata.Indexes = [];

        Assert.Same(tree.Table, ObjectDeleter.RefreshNodeOf(column, ObjectDeleter.TargetOf(column)!));
        await new ObjectDeleter(new FakeRunner(), tree.Logger).DeleteAsync(column, ObjectDeleter.TargetOf(column)!, CancellationToken.None);

        Assert.Equal(["id"], tree.Folder(tree.Table, "Columns").Children.Select(c => c.Key));
        Assert.Empty(tree.Folder(tree.Table, "Indexes").Children);
    }

    [Fact]
    public async Task Delete_ServerError_IsThrown_AndTheTreeStays()
    {
        var tree = await Tree.OpenAsync();
        var runner = new FakeRunner { Fail = new InvalidOperationException("Cannot drop table 'orders' referenced by a foreign key") };
        tree.Metadata.Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ObjectDeleter(runner, tree.Logger).DeleteAsync(tree.Table, ObjectDeleter.TargetOf(tree.Table)!, CancellationToken.None));

        Assert.Single(tree.Folder(tree.Database, "Tables").Children);
    }

    [Fact]
    public async Task Delete_CancelledPassword_RunsNothing()
    {
        var tree = await Tree.OpenAsync();
        tree.Server.Definition.Password = null;
        tree.Prompt.Answer = null;
        var runner = new FakeRunner();

        var deleted = await new ObjectDeleter(runner, tree.Logger).DeleteAsync(tree.Table, ObjectDeleter.TargetOf(tree.Table)!, CancellationToken.None);

        Assert.False(deleted);
        Assert.Empty(runner.Executed);
    }

    private sealed class FakeRunner : ISchemaCommandRunner
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

    private sealed class Tree
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

        public static async Task<Tree> OpenAsync()
        {
            var tree = new Tree();
            var m = tree.Metadata;
            m.Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", null, null), new DatabaseInfo("mysql", null, null)]);
            m.Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "orders", "InnoDB", null)]);
            m.Views = [new TableInfo("shop", "v_orders", null, null, isView: true)];
            m.Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([IdColumn, new ColumnInfo("note", "varchar(20)", true, false, false, string.Empty)]);
            m.Keys = [new KeyInfo("PRIMARY", KeyType.Primary, ["id"])];
            m.Triggers = [new TriggerInfo("trg_bi", "BEFORE", "INSERT")];
            m.Indexes = [new IndexInfo("PRIMARY", true, "BTREE", ["id"]), new IndexInfo("ix_date", false, "BTREE", ["note"])];

            var context = new ExplorerContext(m, tree.Prompt, new ExplorerTexts(), tree.Logger);
            tree.Server = await ExpandAsync(new ServerNodeViewModel(context, TestExplorer.Definition()));
            tree.Databases = await ExpandAsync((FolderNodeViewModel)tree.Server.Children[0]);
            tree.Database = await ExpandAsync(tree.Databases.Children.OfType<DatabaseNodeViewModel>().Single());
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
    }
}
