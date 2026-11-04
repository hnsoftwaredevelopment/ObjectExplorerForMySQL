using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.DragDrop;

public class RoutineCallTests
{
    [Fact]
    public void Procedure_BecomesCallWithUserVariables()
    {
        var item = new DragItem(DragObjectKind.Procedure, "film_in_stock", "sakila") { Parameters = ["p_film_id", "p_store_id", "p_film_count"] };

        var text = DropTextBuilder.Build(item);

        Assert.Equal("CALL sakila.film_in_stock(@p_film_id, @p_store_id, @p_film_count);", text.Sql);
        Assert.Equal("film_in_stock", text.SqlName);
        Assert.Equal("film_in_stock", text.Plain);
    }

    [Fact]
    public void Function_BecomesSelect()
    {
        var item = new DragItem(DragObjectKind.Function, "get_customer_balance", "sakila") { Parameters = ["p_customer_id", "p_effective_date"] };

        Assert.Equal("SELECT sakila.get_customer_balance(@p_customer_id, @p_effective_date);", DropTextBuilder.Build(item).Sql);
    }

    [Fact]
    public void RoutineWithoutParameters_HasEmptyArgumentList()
    {
        var item = new DragItem(DragObjectKind.Procedure, "refresh_all", "shop") { Parameters = [] };

        Assert.Equal("CALL shop.refresh_all();", DropTextBuilder.Build(item).Sql);
    }

    [Fact]
    public void Call_QuotesNamesWhenNeeded()
    {
        var item = new DragItem(DragObjectKind.Function, "order", "my-db") { Parameters = ["p id", "p_ok"] };

        var text = DropTextBuilder.Build(item);

        Assert.Equal("SELECT `my-db`.`order`(@`p id`, @p_ok);", text.Sql);
        Assert.Equal("`order`", text.SqlName);
    }

    [Fact]
    public void RoutineWithUnknownParameters_DropsOnlyTheName()
    {
        var text = DropTextBuilder.Build(new DragItem(DragObjectKind.Procedure, "film_in_stock", "sakila"));

        Assert.Equal("film_in_stock", text.Sql);
        Assert.Equal("film_in_stock", text.SqlName);
    }

    [Theory]
    [InlineData("p_id", "@p_id")]
    [InlineData("p.id$", "@p.id$")]
    [InlineData("p id", "@`p id`")]
    [InlineData("p`x", "@`p``x`")]
    public void UserVariable_QuotesWhenNeeded(string name, string expected) => Assert.Equal(expected, DropTextBuilder.UserVariable(name));

    [Fact]
    public void ColumnsAndTables_SqlNameEqualsSql()
    {
        var text = DropTextBuilder.Build(new DragItem(DragObjectKind.Column, "order", "shop", "orders"));

        Assert.Equal("`order`", text.Sql);
        Assert.Equal("`order`", text.SqlName);
    }

    [Fact]
    public async Task RoutineNode_LoadsParametersOnDemandAndFromTheFolder()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", null, null)]),
            Routines = [new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, null)],
            Parameters =
            [
                new ParameterInfo("p_film_id", ParameterMode.In, "int", 1),
                new ParameterInfo("p_store_id", ParameterMode.In, "int", 2),
                new ParameterInfo("p_film_count", ParameterMode.Out, "int", 3),
            ],
        };
        var server = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());
        TreeNodeViewModel node = server;
        foreach (var key in new[] { "Databases", "sakila", "Stored Procedures" })
        {
            node.IsExpanded = true;
            await node.WaitForLoadAsync();
            node = node.Children.Single(c => c.Key == key);
        }

        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        var routine = Assert.IsType<RoutineNodeViewModel>(Assert.Single(node.Children));

        Assert.Null(routine.CreateDragItem()!.Parameters);
        await routine.EnsureParametersAsync(TestContext.Current.CancellationToken);
        Assert.Equal("CALL sakila.film_in_stock(@p_film_id, @p_store_id, @p_film_count);", DropTextBuilder.Build(routine.CreateDragItem()!).Sql);

        routine.IsExpanded = true;
        await routine.WaitForLoadAsync();
        var parameters = Assert.IsType<FolderNodeViewModel>(Assert.Single(routine.Children));
        parameters.IsExpanded = true;
        await parameters.WaitForLoadAsync();
        Assert.Equal(["p_film_id", "p_store_id", "p_film_count"], routine.Parameters!.Select(p => p.Name));
    }
}
