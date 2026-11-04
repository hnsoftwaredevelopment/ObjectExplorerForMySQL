using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Explorer;

public class RefreshTests
{
    private static async Task<T> ExpandAsync<T>(T node)
        where T : TreeNodeViewModel
    {
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        return node;
    }

    private static T Child<T>(TreeNodeViewModel parent, string label)
        where T : TreeNodeViewModel =>
        Assert.IsType<T>(parent.Children.Single(c => c.Label == label));

    private sealed class Scenario
    {
        public FakeMetadataProvider Metadata { get; } = new();

        public List<string> DatabaseNames { get; } = ["shop"];

        public List<string> TableNames { get; } = ["customers", "orders"];

        public ExplorerViewModel Explorer { get; private set; } = null!;

        public ServerNodeViewModel Server { get; private set; } = null!;

        public static async Task<Scenario> OpenAsync()
        {
            var s = new Scenario();
            s.Metadata.Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>(s.DatabaseNames.Select(n => new DatabaseInfo(n, null, null)).ToList());
            s.Metadata.Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>(s.TableNames.Select(n => new TableInfo(db, n, "InnoDB", null)).ToList());
            s.Metadata.Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([new ColumnInfo("id", "int", false, true, false, "")]);
            s.Explorer = new ExplorerViewModel(TestExplorer.Context(s.Metadata));
            s.Server = await ExpandAsync(s.Explorer.AddServer(TestExplorer.Definition()));
            var databases = await ExpandAsync(Child<FolderNodeViewModel>(s.Server, "Databases"));
            var shop = await ExpandAsync(Child<DatabaseNodeViewModel>(databases, "shop"));
            var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
            var orders = await ExpandAsync(Child<TableNodeViewModel>(tables, "orders"));
            await ExpandAsync(Child<FolderNodeViewModel>(orders, "Columns"));
            return s;
        }

        public FolderNodeViewModel Databases => Child<FolderNodeViewModel>(Server, "Databases");

        public DatabaseNodeViewModel Shop => Child<DatabaseNodeViewModel>(Databases, "shop");

        public FolderNodeViewModel Tables => Child<FolderNodeViewModel>(Shop, "Tables");
    }

    [Fact]
    public async Task ServerRefresh_KeepsExpandedNodesAtEveryLevel()
    {
        var s = await Scenario.OpenAsync();

        await s.Server.RefreshAsync();

        Assert.True(s.Server.IsExpanded);
        Assert.True(s.Databases.IsExpanded);
        Assert.True(s.Shop.IsExpanded);
        Assert.True(s.Tables.IsExpanded);
        var orders = Child<TableNodeViewModel>(s.Tables, "orders");
        Assert.True(orders.IsExpanded);
        Assert.True(Child<FolderNodeViewModel>(orders, "Columns").IsExpanded);
        Assert.Equal("id", Assert.IsType<ColumnNodeViewModel>(Assert.Single(Child<FolderNodeViewModel>(orders, "Columns").Children)).Key);
        Assert.False(Child<TableNodeViewModel>(s.Tables, "customers").IsExpanded);
    }

    [Fact]
    public async Task ServerRefresh_ShowsAddedAndDroppedDatabasesWithoutCollapsing()
    {
        var s = await Scenario.OpenAsync();
        s.DatabaseNames.Add("archive");

        await s.Server.RefreshAsync();

        Assert.True(s.Databases.IsExpanded);
        Assert.Contains(s.Databases.Children, c => c.Label == "archive");
        Assert.True(s.Shop.IsExpanded);

        s.DatabaseNames.Remove("archive");
        await s.Server.RefreshAsync();

        Assert.DoesNotContain(s.Databases.Children, c => c.Label == "archive");
        Assert.True(s.Shop.IsExpanded);
    }

    [Fact]
    public async Task FolderRefresh_LoadsNewContentAndKeepsOtherFolders()
    {
        var s = await Scenario.OpenAsync();
        var oldShop = s.Shop;
        s.TableNames.Add("invoices");

        await s.Tables.RefreshAsync();

        Assert.Same(oldShop, s.Shop);
        Assert.Equal(["customers", "orders", "invoices"], s.Tables.Children.Select(c => c.Label));
        Assert.True(Child<TableNodeViewModel>(s.Tables, "orders").IsExpanded);
    }

    [Fact]
    public async Task Refresh_KeepsCurrentChildrenVisibleWhileLoading()
    {
        var s = await Scenario.OpenAsync();
        var pending = new TaskCompletionSource<IReadOnlyList<TableInfo>>();
        s.Metadata.Tables = _ => pending.Task;

        var refresh = s.Tables.RefreshAsync();

        Assert.Equal(LoadState.Loading, s.Tables.LoadState);
        Assert.Equal(["customers", "orders"], s.Tables.Children.Select(c => c.Label));

        pending.SetResult([new TableInfo("shop", "orders", "InnoDB", null)]);
        await refresh;

        Assert.Equal(["orders"], s.Tables.Children.Select(c => c.Label));
        Assert.Equal(LoadState.Loaded, s.Tables.LoadState);
    }

    [Fact]
    public async Task Refresh_KeepsSelectionOnObjectThatStillExists()
    {
        var s = await Scenario.OpenAsync();
        Child<TableNodeViewModel>(s.Tables, "customers").IsSelected = true;

        await s.Server.RefreshAsync();

        Assert.True(Child<TableNodeViewModel>(s.Tables, "customers").IsSelected);
        Assert.False(s.Server.IsSelected);
    }

    [Fact]
    public async Task Refresh_SelectsRefreshedNodeWhenSelectedObjectIsDropped()
    {
        var s = await Scenario.OpenAsync();
        Child<TableNodeViewModel>(s.Tables, "customers").IsSelected = true;
        s.TableNames.Remove("customers");

        await s.Tables.RefreshAsync();

        Assert.True(s.Tables.IsSelected);
        Assert.DoesNotContain(s.Tables.Children, c => c.IsSelected);
    }

    [Fact]
    public async Task Refresh_MatchesFunctionByNameWhenReturnTypeChanged()
    {
        var s = await Scenario.OpenAsync();
        s.Metadata.Routines = [new RoutineInfo("shop", "total", RoutineType.Function, "int", null)];
        var functions = await ExpandAsync(Child<FolderNodeViewModel>(s.Shop, "Functions"));
        var total = await ExpandAsync(Assert.IsType<RoutineNodeViewModel>(Assert.Single(functions.Children)));
        total.IsSelected = true;
        s.Metadata.Routines = [new RoutineInfo("shop", "total", RoutineType.Function, "decimal(10,2)", null)];

        await functions.RefreshAsync();

        var refreshed = Assert.IsType<RoutineNodeViewModel>(Assert.Single(functions.Children));
        Assert.Equal("total (returns decimal(10,2))", refreshed.Label);
        Assert.True(refreshed.IsSelected);
        Assert.True(refreshed.IsExpanded);
    }

    [Fact]
    public async Task Refresh_OnCollapsedNodeReloadsOnNextExpand()
    {
        var s = await Scenario.OpenAsync();
        var tables = s.Tables;
        tables.IsExpanded = false;
        s.TableNames.Add("invoices");

        await tables.RefreshAsync();

        Assert.Equal(LoadState.NotLoaded, tables.LoadState);
        await ExpandAsync(tables);
        Assert.Contains(tables.Children, c => c.Label == "invoices");
    }

    [Fact]
    public async Task RefreshTarget_LeafUsesNearestParentThatHasChildren()
    {
        var s = await Scenario.OpenAsync();
        var columns = Child<FolderNodeViewModel>(Child<TableNodeViewModel>(s.Tables, "orders"), "Columns");

        s.Explorer.SelectedNode = columns.Children[0];

        Assert.Same(columns, s.Explorer.RefreshTarget);
    }

    [Fact]
    public async Task RefreshTarget_ErrorNodeRetriesItsFolder()
    {
        var s = await Scenario.OpenAsync();
        var fail = true;
        s.Metadata.Tables = db => fail
            ? Task.FromException<IReadOnlyList<TableInfo>>(new System.InvalidOperationException("boom"))
            : Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "orders", "InnoDB", null)]);
        await s.Tables.RefreshAsync();
        var error = Assert.IsType<PlaceholderNodeViewModel>(Assert.Single(s.Tables.Children));
        Assert.True(error.IsError);

        s.Explorer.SelectedNode = error;
        fail = false;
        await s.Explorer.RefreshSelectedAsync();

        Assert.Equal(["orders"], s.Tables.Children.Select(c => c.Label));
    }

    [Fact]
    public void RefreshTarget_IsNullForServerThatIsNotConnected()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());
        explorer.SelectedNode = explorer.AddServer(TestExplorer.Definition());

        Assert.Null(explorer.RefreshTarget);
    }

    [Fact]
    public async Task Children_KnowTheirParent()
    {
        var s = await Scenario.OpenAsync();

        Assert.Same(s.Server, s.Databases.Parent);
        Assert.Same(s.Shop, s.Tables.Parent);
        Assert.Null(s.Server.Parent);
    }
}
