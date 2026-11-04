using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.DragDrop;

public class MultiSelectTests
{
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

    private sealed record Tree(ExplorerViewModel Explorer, FolderNodeViewModel OrderColumns, FolderNodeViewModel CustomerColumns, TableNodeViewModel Orders);

    private static async Task<Tree> OpenAsync()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "customers", "InnoDB", null), new TableInfo(db, "orders", "InnoDB", null)]),
            Columns = (_, table) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
                table == "orders"
                    ? new[] { "id", "customer_id", "order", "created_at" }.Select(n => new ColumnInfo(n, "int", false, false, false, string.Empty)).ToList()
                    : [new ColumnInfo("id", "int", false, true, false, string.Empty)]),
        };
        var explorer = new ExplorerViewModel(TestExplorer.Context(metadata));
        var server = await ExpandAsync(explorer.AddServer(TestExplorer.Definition()));
        var databases = await ExpandAsync(Child<FolderNodeViewModel>(server, "Databases"));
        var shop = await ExpandAsync(Child<DatabaseNodeViewModel>(databases, "shop"));
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        var orders = await ExpandAsync(Child<TableNodeViewModel>(tables, "orders"));
        var customers = await ExpandAsync(Child<TableNodeViewModel>(tables, "customers"));
        var orderColumns = await ExpandAsync(Child<FolderNodeViewModel>(orders, "Columns"));
        var customerColumns = await ExpandAsync(Child<FolderNodeViewModel>(customers, "Columns"));
        return new Tree(explorer, orderColumns, customerColumns, orders);
    }

    private static void Select(ExplorerViewModel explorer, TreeNodeViewModel node)
    {
        node.IsSelected = true;
        explorer.SelectedNode = node;
    }

    private static IEnumerable<string> Keys(IEnumerable<TreeNodeViewModel> nodes) => nodes.Select(n => n.Key);

    [Fact]
    public async Task CtrlClick_AddsAndRemovesColumnsOfTheSameTable()
    {
        var t = await OpenAsync();
        Select(t.Explorer, t.OrderColumns.Children[0]);

        Assert.True(t.Explorer.ToggleSelection(t.OrderColumns.Children[2]));
        Assert.Equal(["id", "order"], Keys(t.Explorer.SelectedNodes));

        Assert.True(t.Explorer.ToggleSelection(t.OrderColumns.Children[2]));
        Assert.Equal(["id"], Keys(t.Explorer.SelectedNodes));
    }

    [Fact]
    public async Task CtrlClick_OnOtherTableOrNonColumnIsNotHandled()
    {
        var t = await OpenAsync();
        Select(t.Explorer, t.OrderColumns.Children[0]);

        Assert.False(t.Explorer.ToggleSelection(t.CustomerColumns.Children[0]));
        Assert.False(t.Explorer.ToggleSelection(t.Orders));
        Assert.False(t.CustomerColumns.Children[0].IsMultiSelected);

        Select(t.Explorer, t.Orders);
        Assert.False(t.Explorer.ToggleSelection(t.OrderColumns.Children[1]));
    }

    [Fact]
    public async Task ShiftClick_SelectsRangeInBothDirections()
    {
        var t = await OpenAsync();
        Select(t.Explorer, t.OrderColumns.Children[1]);

        Assert.True(t.Explorer.ExtendSelection(t.OrderColumns.Children[3]));
        Assert.Equal(["customer_id", "order", "created_at"], Keys(t.Explorer.SelectedNodes));

        Assert.True(t.Explorer.ExtendSelection(t.OrderColumns.Children[0]));
        Assert.Equal(["id", "customer_id"], Keys(t.Explorer.SelectedNodes));
    }

    [Fact]
    public async Task PlainSelection_ClearsMultiSelection()
    {
        var t = await OpenAsync();
        Select(t.Explorer, t.OrderColumns.Children[0]);
        t.Explorer.ExtendSelection(t.OrderColumns.Children[3]);

        Select(t.Explorer, t.CustomerColumns.Children[0]);

        Assert.All(t.OrderColumns.Children, c => Assert.False(c.IsMultiSelected));
        Assert.Equal(["id"], Keys(t.Explorer.SelectedNodes));
    }

    [Fact]
    public async Task DragItems_OfMultiSelectionAreInOrdinalOrder()
    {
        var t = await OpenAsync();
        Select(t.Explorer, t.OrderColumns.Children[3]);
        t.Explorer.ToggleSelection(t.OrderColumns.Children[0]);
        t.Explorer.ToggleSelection(t.OrderColumns.Children[2]);

        var items = t.Explorer.CreateDragItems(t.OrderColumns.Children[2]);
        var text = DropTextBuilder.Build(items);

        Assert.Equal("id, `order`, created_at", text.Sql);
        Assert.Equal("id, order, created_at", text.Plain);
    }

    [Fact]
    public async Task DragItems_OfColumnOutsideTheSelectionIsOnlyThatColumn()
    {
        var t = await OpenAsync();
        Select(t.Explorer, t.OrderColumns.Children[0]);
        t.Explorer.ToggleSelection(t.OrderColumns.Children[1]);

        var items = t.Explorer.CreateDragItems(t.OrderColumns.Children[3]);

        Assert.Equal(["created_at"], items.Select(i => i.Name));
    }

    [Fact]
    public async Task DragItems_OfColumnsFolderAreAllColumns()
    {
        var t = await OpenAsync();

        Assert.True(t.OrderColumns.IsColumnsFolder);
        Assert.Equal("id, customer_id, `order`, created_at", DropTextBuilder.Build(t.Explorer.CreateDragItems(t.OrderColumns)).Sql);
        Assert.False(t.OrderColumns.Parent!.Parent is FolderNodeViewModel { IsColumnsFolder: true });
    }

    [Fact]
    public async Task DragItems_OfOtherFoldersAreEmpty()
    {
        var t = await OpenAsync();
        var keys = Child<FolderNodeViewModel>(t.Orders, "Keys");

        Assert.False(keys.IsColumnsFolder);
        Assert.Empty(t.Explorer.CreateDragItems(keys));
    }

    [Fact]
    public async Task ShiftClick_SkipsColumnsHiddenByNothingButKeepsVisibleOnes()
    {
        var t = await OpenAsync();
        Select(t.Explorer, t.OrderColumns.Children[0]);

        t.Explorer.ExtendSelection(t.OrderColumns.Children[1]);
        t.Explorer.ClearMultiSelection();

        Assert.Equal(["id"], Keys(t.Explorer.SelectedNodes));
    }
}
