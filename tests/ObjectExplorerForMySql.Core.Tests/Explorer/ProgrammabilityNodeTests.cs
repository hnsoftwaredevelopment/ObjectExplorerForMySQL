using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Explorer;

public class ProgrammabilityNodeTests
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

    private static async Task<FolderNodeViewModel> OpenFolderAsync(FakeMetadataProvider metadata, string label)
    {
        var database = await OpenDatabaseAsync(metadata);
        return await ExpandAsync((FolderNodeViewModel)database.Children.Single(c => c.Label == label));
    }

    [Fact]
    public async Task Database_HasFoldersInSqlServerObjectExplorerOrder()
    {
        var database = await OpenDatabaseAsync(new FakeMetadataProvider());

        Assert.Equal(["Tables", "Views", "Stored Procedures", "Functions", "Events"], database.Children.Select(c => c.Label));
    }

    [Fact]
    public async Task ViewsFolder_ShowsViewsWithColumns()
    {
        var metadata = new FakeMetadataProvider
        {
            Views = [new TableInfo("shop", "customer_list", null, "VIEW", isView: true)],
            Columns = (_, table) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
                table == "customer_list" ? [new ColumnInfo("name", "varchar(45)", true, false, false, "")] : []),
        };

        var views = await OpenFolderAsync(metadata, "Views");
        var view = await ExpandAsync(Assert.IsType<ViewNodeViewModel>(Assert.Single(views.Children)));

        Assert.Equal("customer_list", view.Label);
        Assert.Equal(NodeIcon.View, view.Icon);
        Assert.True(view.View.IsView);
        var columns = await ExpandAsync(Assert.IsType<FolderNodeViewModel>(Assert.Single(view.Children)));
        Assert.Equal("Columns", columns.Label);
        Assert.Equal("name", Assert.IsType<ColumnNodeViewModel>(Assert.Single(columns.Children)).Column.Name);
    }

    [Fact]
    public async Task RoutineFolders_SplitProceduresAndFunctions()
    {
        var metadata = new FakeMetadataProvider
        {
            Routines =
            [
                new RoutineInfo("shop", "film_in_stock", RoutineType.Procedure, null, "Stock check"),
                new RoutineInfo("shop", "get_balance", RoutineType.Function, "decimal(5,2)", null),
            ],
        };
        var database = await OpenDatabaseAsync(metadata);

        var procedures = await ExpandAsync((FolderNodeViewModel)database.Children[2]);
        var functions = await ExpandAsync((FolderNodeViewModel)database.Children[3]);

        var procedure = Assert.IsType<RoutineNodeViewModel>(Assert.Single(procedures.Children));
        Assert.Equal("film_in_stock", procedure.Label);
        Assert.Equal(NodeIcon.StoredProcedure, procedure.Icon);
        Assert.Equal("Stock check", procedure.ToolTip);

        var function = Assert.IsType<RoutineNodeViewModel>(Assert.Single(functions.Children));
        Assert.Equal("get_balance (returns decimal(5,2))", function.Label);
        Assert.Equal(NodeIcon.Function, function.Icon);
        Assert.Equal([RoutineType.Procedure, RoutineType.Function], metadata.RoutineTypesRequested);
    }

    [Fact]
    public async Task Routine_ParametersFolderShowsModeIconsAndLabels()
    {
        var metadata = new FakeMetadataProvider
        {
            Routines = [new RoutineInfo("shop", "film_in_stock", RoutineType.Procedure, null, null)],
            Parameters =
            [
                new ParameterInfo("p_film_id", ParameterMode.In, "int", 1),
                new ParameterInfo("p_count", ParameterMode.Out, "int", 2),
                new ParameterInfo("p_state", ParameterMode.InOut, "varchar(10)", 3),
            ],
        };

        var procedures = await OpenFolderAsync(metadata, "Stored Procedures");
        var procedure = await ExpandAsync((RoutineNodeViewModel)procedures.Children[0]);
        var parameters = await ExpandAsync(Assert.IsType<FolderNodeViewModel>(Assert.Single(procedure.Children)));

        Assert.Equal("Parameters", parameters.Label);
        Assert.Equal(["p_film_id (IN, int)", "p_count (OUT, int)", "p_state (INOUT, varchar(10))"], parameters.Children.Select(c => c.Label));
        Assert.Equal([NodeIcon.InputParameter, NodeIcon.OutputParameter, NodeIcon.InputOutputParameter], parameters.Children.Select(c => c.Icon));
    }

    [Fact]
    public async Task EventsFolder_ShowsStatusAndScheduleToolTip()
    {
        var metadata = new FakeMetadataProvider
        {
            Events =
            [
                new EventInfo("purge_log", "ENABLED", null, "1", "DAY", new DateTime(2026, 1, 1), null),
                new EventInfo("one_off", "DISABLED", new DateTime(2026, 10, 27, 12, 0, 0), null, null, null, null),
            ],
        };

        var events = await OpenFolderAsync(metadata, "Events");

        Assert.Equal(["purge_log (enabled)", "one_off (disabled)"], events.Children.Select(c => c.Label));
        Assert.All(events.Children, c => Assert.Equal(NodeIcon.Event, c.Icon));
        Assert.Equal("EVERY 1 DAY STARTS '2026-01-01 00:00:00'", events.Children[0].ToolTip);
        Assert.Equal("AT '2026-10-27 12:00:00'", events.Children[1].ToolTip);
    }
}
