using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.DragDrop;

public class DropTextTests
{
    [Theory]
    [InlineData("customer_id", false)]
    [InlineData("Customer", false)]
    [InlineData("price$", false)]
    [InlineData("kundennummer_ä", false)]
    [InlineData("order", true)]
    [InlineData("ORDER", true)]
    [InlineData("Select", true)]
    [InlineData("restrict", true)]
    [InlineData("tablesample", true)]
    [InlineData("first name", true)]
    [InlineData("e-mail", true)]
    [InlineData("a`b", true)]
    [InlineData("1e3", true)]
    [InlineData("2024_sales", true)]
    [InlineData("", true)]
    [InlineData("name", false)]
    [InlineData("status", false)]
    public void NeedsQuoting_FollowsMySqlIdentifierRules(string name, bool expected) =>
        Assert.Equal(expected, MySqlIdentifier.NeedsQuoting(name));

    [Theory]
    [InlineData("order", "`order`")]
    [InlineData("a`b", "`a``b`")]
    [InlineData("``", "``````")]
    public void Quote_DoublesBackticks(string name, string expected) => Assert.Equal(expected, MySqlIdentifier.Quote(name));

    [Theory]
    [InlineData("customer_id", "customer_id")]
    [InlineData("order", "`order`")]
    [InlineData("first name", "`first name`")]
    public void Build_QuotesForSqlOnly(string name, string expectedSql)
    {
        var text = DropTextBuilder.Build(new DragItem(DragObjectKind.Column, name, "shop", "orders"));

        Assert.Equal(expectedSql, text.Sql);
        Assert.Equal(name, text.Plain);
    }

    private static async Task<T> ExpandAsync<T>(T node)
        where T : TreeNodeViewModel
    {
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        return node;
    }

    private static T Child<T>(TreeNodeViewModel parent, string key)
        where T : TreeNodeViewModel =>
        Assert.IsType<T>(parent.Children.Single(c => c.Key == key));

    [Fact]
    public async Task Nodes_CreateDragItemsForDraggableObjectsOnly()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "order", "InnoDB", null)]),
            Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([new ColumnInfo("id", "int", false, true, false, string.Empty)]),
            Views = [new TableInfo("shop", "order_list", null, null, isView: true)],
            Routines =
            [
                new RoutineInfo("shop", "close_order", RoutineType.Procedure, null, null),
                new RoutineInfo("shop", "order_total", RoutineType.Function, "int", null),
            ],
            Keys = [new KeyInfo("PRIMARY", KeyType.Primary, ["id"])],
        };
        var server = await ExpandAsync(new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition()));
        var databases = await ExpandAsync(Child<FolderNodeViewModel>(server, "Databases"));
        var shop = await ExpandAsync(Child<DatabaseNodeViewModel>(databases, "shop"));
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        var order = await ExpandAsync(Child<TableNodeViewModel>(tables, "order"));
        var columns = await ExpandAsync(Child<FolderNodeViewModel>(order, "Columns"));
        var keys = await ExpandAsync(Child<FolderNodeViewModel>(order, "Keys"));
        var views = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Views"));
        var procedures = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Stored Procedures"));
        var functions = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Functions"));

        var column = columns.Children[0].CreateDragItem()!;
        Assert.Equal((DragObjectKind.Column, "id", "shop", "order"), (column.Kind, column.Name, column.Database, column.Parent));
        Assert.Equal("`order`", DropTextBuilder.Build(order.CreateDragItem()!).Sql);
        Assert.Equal(DragObjectKind.Table, order.CreateDragItem()!.Kind);
        Assert.Equal(DragObjectKind.Database, shop.CreateDragItem()!.Kind);
        Assert.Equal(DragObjectKind.View, views.Children[0].CreateDragItem()!.Kind);
        Assert.Equal(DragObjectKind.Procedure, procedures.Children[0].CreateDragItem()!.Kind);
        Assert.Equal(DragObjectKind.Function, functions.Children[0].CreateDragItem()!.Kind);

        Assert.Null(server.CreateDragItem());
        Assert.Null(tables.CreateDragItem());
        Assert.Null(keys.Children[0].CreateDragItem());
    }
}
