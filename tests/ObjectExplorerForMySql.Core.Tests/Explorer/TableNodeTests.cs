using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Explorer;

public class TableNodeTests
{
    private static async Task<T> ExpandAsync<T>(T node)
        where T : TreeNodeViewModel
    {
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        return node;
    }

    private static async Task<DatabaseNodeViewModel> OpenDatabaseAsync(FakeMetadataProvider metadata)
    {
        metadata.Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", null, null)]);
        var server = await ExpandAsync(new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition()));
        var databases = await ExpandAsync((FolderNodeViewModel)server.Children[0]);
        return await ExpandAsync((DatabaseNodeViewModel)databases.Children.Single(c => c.Label == "shop"));
    }

    [Fact]
    public async Task Database_HasTablesFolderWithSortedTables()
    {
        var metadata = new FakeMetadataProvider
        {
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>(
            [
                new TableInfo(db, "orders", "InnoDB", null),
                new TableInfo(db, "Customers", "InnoDB", "All customers"),
            ]),
        };
        var database = await OpenDatabaseAsync(metadata);

        var tablesFolder = await ExpandAsync(Assert.IsType<FolderNodeViewModel>(database.Children[0]));

        Assert.Equal("Tables", tablesFolder.Label);
        Assert.Equal(["orders", "Customers"], tablesFolder.Children.Select(c => c.Label));
        Assert.All(tablesFolder.Children, c => Assert.Equal(NodeIcon.Table, c.Icon));
        Assert.Equal("All customers", tablesFolder.Children[1].ToolTip);
    }

    private static async Task<TableNodeViewModel> OpenTableAsync(FakeMetadataProvider metadata, string serverVersion = "8.0.36")
    {
        metadata.ConnectResults.Enqueue(_ => Task.FromResult(new ServerInfo(serverVersion, "app@%")));
        metadata.Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "orders", "InnoDB", null)]);
        var database = await OpenDatabaseAsync(metadata);
        var tables = await ExpandAsync((FolderNodeViewModel)database.Children[0]);
        return await ExpandAsync((TableNodeViewModel)tables.Children[0]);
    }

    [Fact]
    public async Task Table_HasFoldersInSqlServerObjectExplorerOrder()
    {
        var table = await OpenTableAsync(new FakeMetadataProvider());

        Assert.Equal(["Columns", "Keys", "Constraints", "Triggers", "Indexes"], table.Children.Select(c => c.Label));
    }

    [Theory]
    [InlineData("8.0.15")]
    [InlineData("10.1.48-MariaDB")]
    public async Task Table_HidesConstraintsFolderWhenServerHasNoCheckConstraints(string version)
    {
        var table = await OpenTableAsync(new FakeMetadataProvider(), version);

        Assert.Equal(["Columns", "Keys", "Triggers", "Indexes"], table.Children.Select(c => c.Label));
    }

    [Fact]
    public async Task Table_FoldersShowKeysConstraintsTriggersAndIndexesWithIcons()
    {
        var metadata = new FakeMetadataProvider
        {
            Keys =
            [
                new KeyInfo("PRIMARY", KeyType.Primary, ["id"]),
                new KeyInfo("uq_number", KeyType.Unique, ["number"]),
                new KeyInfo("fk_customer", KeyType.Foreign, ["customer_id"], "shop", "customers", ["id"]),
            ],
            CheckConstraints = [new CheckConstraintInfo("chk_total", "(`total` >= 0)", true)],
            Triggers = [new TriggerInfo("trg_bi", "BEFORE", "INSERT")],
            Indexes =
            [
                new IndexInfo("PRIMARY", true, "BTREE", ["id"]),
                new IndexInfo("ft_note", false, "FULLTEXT", ["note"]),
                new IndexInfo("ix_date", false, "BTREE", ["order_date"]),
            ],
        };
        var table = await OpenTableAsync(metadata);

        var keys = await ExpandAsync((FolderNodeViewModel)table.Children[1]);
        var constraints = await ExpandAsync((FolderNodeViewModel)table.Children[2]);
        var triggers = await ExpandAsync((FolderNodeViewModel)table.Children[3]);
        var indexes = await ExpandAsync((FolderNodeViewModel)table.Children[4]);

        Assert.Equal(["PRIMARY (id)", "uq_number (number)", "fk_customer (customer_id) → customers (id)"], keys.Children.Select(c => c.Label));
        Assert.Equal([NodeIcon.PrimaryKey, NodeIcon.UniqueKey, NodeIcon.ForeignKey], keys.Children.Select(c => c.Icon));
        Assert.Equal(NodeIcon.CheckConstraint, Assert.Single(constraints.Children).Icon);
        Assert.Equal("(`total` >= 0)", constraints.Children[0].ToolTip);
        Assert.False(metadata.CheckConstraintsRequestedForMariaDb);
        Assert.Equal("trg_bi (BEFORE INSERT)", Assert.Single(triggers.Children).Label);
        Assert.Equal([NodeIcon.PrimaryIndex, NodeIcon.FullTextIndex, NodeIcon.Index], indexes.Children.Select(c => c.Icon));
        Assert.IsType<KeyInfo>(((ObjectNodeViewModel)keys.Children[0]).Item);
    }

    [Fact]
    public async Task Table_HasColumnsFolderWithColumnsInOrderAndIcons()
    {
        string? requestedTable = null;
        var metadata = new FakeMetadataProvider
        {
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "orders", "InnoDB", null)]),
            Columns = (_, table) =>
            {
                requestedTable = table;
                return Task.FromResult<IReadOnlyList<ColumnInfo>>(
                [
                    new ColumnInfo("id", "int unsigned", false, true, false, "auto_increment"),
                    new ColumnInfo("customer_id", "int", false, false, true, ""),
                    new ColumnInfo("total", "decimal(10,2)", true, false, false, ""),
                    new ColumnInfo("total_vat", "decimal(10,2)", true, false, false, "VIRTUAL GENERATED"),
                ]);
            },
        };
        var database = await OpenDatabaseAsync(metadata);
        var tables = await ExpandAsync((FolderNodeViewModel)database.Children[0]);
        var table = await ExpandAsync((TableNodeViewModel)tables.Children[0]);

        var columns = await ExpandAsync(Assert.IsType<FolderNodeViewModel>(table.Children[0]));

        Assert.Equal("orders", requestedTable);
        Assert.Equal("Columns", columns.Label);
        Assert.Equal(
            ["id (PK, int unsigned, not null)", "customer_id (FK, int, not null)", "total (decimal(10,2), null)", "total_vat (decimal(10,2), null)"],
            columns.Children.Select(c => c.Label));
        Assert.Equal(
            [NodeIcon.PrimaryKeyColumn, NodeIcon.ForeignKeyColumn, NodeIcon.Column, NodeIcon.GeneratedColumn],
            columns.Children.Select(c => c.Icon));
        Assert.All(columns.Children, c => Assert.False(c.CanHaveChildren));
    }
}
