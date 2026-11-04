using System;
using ObjectExplorerForMySql.Core.DragDrop;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.DragDrop;

public class DropFormatOptionsTests
{
    private static readonly DragItem Actor = new(DragObjectKind.Table, "actor", "sakila");
    private static readonly DragItem Order = new(DragObjectKind.Column, "order", "shop", "orders");
    private static readonly DragItem Procedure = new(DragObjectKind.Procedure, "film_in_stock", "sakila") { Parameters = ["p_film_id"] };

    [Fact]
    public void Defaults_MatchDecisionD5()
    {
        var options = DropFormatOptions.Default;

        Assert.Equal(QuoteMode.WhenNeeded, options.SqlQuoting);
        Assert.Equal(QuoteMode.Never, options.OtherQuoting);
        Assert.False(options.DatabasePrefixForTables);
        Assert.True(options.DatabasePrefixForRoutines);
        Assert.Equal(", ", options.SeparatorText);
    }

    [Fact]
    public void SqlAlways_QuotesEveryName()
    {
        var text = DropTextBuilder.Build(Actor, DropFormatOptions.Default with { SqlQuoting = QuoteMode.Always });

        Assert.Equal("`actor`", text.Sql);
        Assert.Equal("actor", text.Plain);
    }

    [Fact]
    public void SqlNever_StillQuotesReservedWords()
    {
        var text = DropTextBuilder.Build(Order, DropFormatOptions.Default with { SqlQuoting = QuoteMode.Never });

        Assert.Equal("`order`", text.Sql);
    }

    [Theory]
    [InlineData(QuoteMode.Never, "actor", "order")]
    [InlineData(QuoteMode.WhenNeeded, "actor", "`order`")]
    [InlineData(QuoteMode.Always, "`actor`", "`order`")]
    public void OtherQuoting_AppliesToPlainText(QuoteMode mode, string expectedActor, string expectedOrder)
    {
        var options = DropFormatOptions.Default with { OtherQuoting = mode };

        Assert.Equal(expectedActor, DropTextBuilder.Build(Actor, options).Plain);
        Assert.Equal(expectedOrder, DropTextBuilder.Build(Order, options).Plain);
    }

    [Fact]
    public void TablePrefix_AppliesToTablesAndViewsInBothTexts()
    {
        var options = DropFormatOptions.Default with { DatabasePrefixForTables = true, OtherQuoting = QuoteMode.Always };

        var table = DropTextBuilder.Build(Actor, options);
        var view = DropTextBuilder.Build(new DragItem(DragObjectKind.View, "actor_info", "sakila"), options);

        Assert.Equal("sakila.actor", table.Sql);
        Assert.Equal("`sakila`.`actor`", table.Plain);
        Assert.Equal("sakila.actor_info", view.Sql);
    }

    [Fact]
    public void TablePrefix_DoesNotApplyToColumnsDatabasesOrRoutineNames()
    {
        var options = DropFormatOptions.Default with { DatabasePrefixForTables = true };

        Assert.Equal("`order`", DropTextBuilder.Build(Order, options).Sql);
        Assert.Equal("sakila", DropTextBuilder.Build(new DragItem(DragObjectKind.Database, "sakila"), options).Sql);
        Assert.Equal("film_in_stock", DropTextBuilder.Build(Procedure, options).SqlName);
    }

    [Fact]
    public void RoutinePrefix_CanBeTurnedOff()
    {
        var text = DropTextBuilder.Build(Procedure, DropFormatOptions.Default with { DatabasePrefixForRoutines = false });

        Assert.Equal("CALL film_in_stock(@p_film_id);", text.Sql);
    }

    [Fact]
    public void SqlAlways_QuotesCallTargetButNotVariables()
    {
        var text = DropTextBuilder.Build(Procedure, DropFormatOptions.Default with { SqlQuoting = QuoteMode.Always });

        Assert.Equal("CALL `sakila`.`film_in_stock`(@p_film_id);", text.Sql);
        Assert.Equal("`film_in_stock`", text.SqlName);
    }

    [Fact]
    public void NewLineSeparator_PutsEachColumnOnItsOwnLine()
    {
        var options = DropFormatOptions.Default with { ColumnSeparator = ColumnSeparator.CommaNewLine };
        var items = new[] { new DragItem(DragObjectKind.Column, "id", "shop", "orders"), Order };

        var text = DropTextBuilder.Build(items, options);

        Assert.Equal("id," + Environment.NewLine + "`order`", text.Sql);
        Assert.Equal("id," + Environment.NewLine + "order", text.Plain);
    }
}
