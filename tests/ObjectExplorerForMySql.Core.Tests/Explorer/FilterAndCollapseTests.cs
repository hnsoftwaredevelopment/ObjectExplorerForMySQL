using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Explorer;

public class FilterAndCollapseTests
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

    private static async Task<(ExplorerViewModel Explorer, ServerNodeViewModel Server, DatabaseNodeViewModel Shop, FakeMetadataProvider Metadata)> OpenAsync()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>(
                new[] { "customer", "customer_address", "orders" }.Select(n => new TableInfo(db, n, "InnoDB", null)).ToList()),
            Views = [new TableInfo("shop", "customer_list", null, null, isView: true), new TableInfo("shop", "sales", null, null, isView: true)],
            Routines = [new RoutineInfo("shop", "get_Customer_balance", RoutineType.Function, "int", null)],
        };
        var explorer = new ExplorerViewModel(TestExplorer.Context(metadata));
        var server = await ExpandAsync(explorer.AddServer(TestExplorer.Definition()));
        var databases = await ExpandAsync(Child<FolderNodeViewModel>(server, "Databases"));
        var shop = await ExpandAsync(Child<DatabaseNodeViewModel>(databases, "shop"));
        return (explorer, server, shop, metadata);
    }

    private static IEnumerable<string> Visible(TreeNodeViewModel folder) =>
        folder.Children.Where(c => c.IsVisible).Select(c => c.Key);

    [Theory]
    [InlineData("", "anything", true)]
    [InlineData("cust", "Customer", true)]
    [InlineData("CUST", "customer_list", true)]
    [InlineData("cust", "orders", false)]
    [InlineData("  ord ", "orders", true)]
    public void Filter_MatchesContainsIgnoringCase(string text, string name, bool expected)
    {
        var filter = new ExplorerFilter();
        filter.SetText(text);

        Assert.Equal(expected, filter.IsMatch(name));
    }

    [Fact]
    public void Filter_SetTextReportsChangesOnly()
    {
        var filter = new ExplorerFilter();

        Assert.True(filter.SetText(" a "));
        Assert.False(filter.SetText("a"));
        Assert.True(filter.SetText(null));
        Assert.False(filter.IsActive);
    }

    [Fact]
    public async Task FilterText_HidesNonMatchingObjectsInLoadedFolders()
    {
        var (explorer, _, shop, _) = await OpenAsync();
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        var views = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Views"));

        explorer.FilterText = "cust";

        Assert.Equal(["customer", "customer_address"], Visible(tables));
        Assert.Equal(["customer_list"], Visible(views));
        Assert.Equal("Tables (filtered)", tables.Label);
        Assert.Equal("Views (filtered)", views.Label);
    }

    [Fact]
    public async Task FilterText_AppliesToFoldersLoadedLater()
    {
        var (explorer, _, shop, _) = await OpenAsync();
        explorer.FilterText = "customer";

        var functions = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Functions"));
        var procedures = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Stored Procedures"));

        Assert.Equal(["get_Customer_balance"], Visible(functions));
        Assert.Equal("Functions (filtered)", functions.Label);
        Assert.Empty(Visible(procedures));
    }

    [Fact]
    public async Task FilterText_ClearShowsEverythingAgain()
    {
        var (explorer, _, shop, _) = await OpenAsync();
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        explorer.FilterText = "zzz";
        Assert.Empty(Visible(tables));

        explorer.FilterText = string.Empty;

        Assert.Equal(["customer", "customer_address", "orders"], Visible(tables));
        Assert.Equal("Tables", tables.Label);
    }

    [Fact]
    public async Task FilterText_DoesNotFilterDatabasesColumnsOrOtherFolders()
    {
        var (explorer, server, shop, metadata) = await OpenAsync();
        metadata.Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([new ColumnInfo("id", "int", false, true, false, "")]);
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        var customer = await ExpandAsync(Child<TableNodeViewModel>(tables, "customer"));
        var columns = await ExpandAsync(Child<FolderNodeViewModel>(customer, "Columns"));

        explorer.FilterText = "cust";

        Assert.True(shop.IsVisible);
        Assert.Equal("Databases", Child<FolderNodeViewModel>(server, "Databases").Label);
        Assert.Equal("Columns", columns.Label);
        Assert.All(columns.Children, c => Assert.True(c.IsVisible));
    }

    [Fact]
    public async Task FilterText_SurvivesRefresh()
    {
        var (explorer, _, shop, _) = await OpenAsync();
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        explorer.FilterText = "ord";

        await tables.RefreshAsync();

        Assert.Equal(["orders"], Visible(tables));
        Assert.Equal(3, tables.Children.Count);
    }

    [Fact]
    public async Task FilterText_KeepsErrorNodeVisible()
    {
        var (explorer, _, shop, metadata) = await OpenAsync();
        metadata.Tables = _ => Task.FromException<IReadOnlyList<TableInfo>>(new System.InvalidOperationException("boom"));
        explorer.FilterText = "cust";

        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));

        Assert.True(Assert.Single(tables.Children).IsVisible);
    }

    [Fact]
    public async Task CollapseAll_CollapsesEveryLevelAndKeepsConnectionAndData()
    {
        var (explorer, server, shop, _) = await OpenAsync();
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));

        explorer.CollapseAll();

        Assert.False(server.IsExpanded);
        Assert.False(shop.IsExpanded);
        Assert.False(tables.IsExpanded);
        Assert.Equal(ConnectionState.Connected, server.State);
        Assert.Equal(LoadState.Loaded, tables.LoadState);

        await ExpandAsync(server);
        Assert.False(Child<FolderNodeViewModel>(server, "Databases").IsExpanded);
    }

    [Fact]
    public async Task CollapseAll_MovesSelectionToServer()
    {
        var (explorer, server, shop, _) = await OpenAsync();
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        tables.Children[0].IsSelected = true;
        explorer.SelectedNode = tables.Children[0];

        explorer.CollapseAll();

        Assert.True(server.IsSelected);
        Assert.False(tables.Children[0].IsSelected);
        Assert.Same(server, explorer.SelectedNode);
    }
}
