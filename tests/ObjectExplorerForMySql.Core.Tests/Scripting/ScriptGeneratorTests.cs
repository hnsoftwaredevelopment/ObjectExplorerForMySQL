using System;
using System.Collections.Generic;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Scripting;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Scripting;

public class ScriptGeneratorTests
{
    private static readonly string Nl = Environment.NewLine;

    private static readonly CodeObject Film = new(CodeObjectKind.Table, "sakila", "film");

    private static readonly IReadOnlyList<ColumnInfo> FilmColumns =
    [
        new ColumnInfo("film_id", "smallint unsigned", false, true, false, "auto_increment"),
        new ColumnInfo("title", "varchar(128)", false, false, false, string.Empty),
        new ColumnInfo("order", "int", true, false, false, string.Empty),
        new ColumnInfo("title_length", "int", true, false, false, "VIRTUAL GENERATED"),
    ];

    private static string Generate(ScriptKind kind, CodeObject code, IReadOnlyList<ColumnInfo>? columns = null, IReadOnlyList<string>? parameters = null, string? create = null, DropFormatOptions? options = null) =>
        ScriptGenerator.Generate(new ScriptRequest(kind, code)
        {
            Columns = columns ?? [],
            Parameters = parameters ?? [],
            CreateStatement = create,
            Options = options ?? DropFormatOptions.Default,
        });

    [Theory]
    [InlineData(CodeObjectKind.Table, "Create,Drop,Select,Insert,Update,Delete")]
    [InlineData(CodeObjectKind.View, "Create,Drop,Select")]
    [InlineData(CodeObjectKind.Procedure, "Create,Drop,Call")]
    [InlineData(CodeObjectKind.Function, "Create,Drop,Select")]
    [InlineData(CodeObjectKind.Trigger, "")]
    [InlineData(CodeObjectKind.Event, "")]
    public void IsAvailable_MatchesTheTableOfUs0602(CodeObjectKind kind, string expected)
    {
        var available = new List<string>();
        foreach (ScriptKind script in Enum.GetValues(typeof(ScriptKind)))
        {
            if (ScriptGenerator.IsAvailable(kind, script))
            {
                available.Add(script.ToString());
            }
        }

        Assert.Equal(expected, string.Join(",", available));
    }

    [Fact]
    public void Select_ListsAllColumnsInOrder()
    {
        Assert.Equal($"SELECT{Nl}    film_id,{Nl}    title,{Nl}    `order`,{Nl}    title_length{Nl}FROM sakila.film;{Nl}", Generate(ScriptKind.Select, Film, FilmColumns));
    }

    [Fact]
    public void Insert_SkipsAutoIncrementAndGeneratedColumns()
    {
        Assert.Equal($"INSERT INTO sakila.film{Nl}    (title, `order`){Nl}VALUES{Nl}    (@title, @order);{Nl}", Generate(ScriptKind.Insert, Film, FilmColumns));
    }

    [Theory]
    [InlineData("VIRTUAL GENERATED", true)]
    [InlineData("STORED GENERATED", true)]
    [InlineData("PERSISTENT GENERATED", true)]
    [InlineData("DEFAULT_GENERATED", false)]
    [InlineData("DEFAULT_GENERATED on update CURRENT_TIMESTAMP", false)]
    [InlineData("auto_increment", false)]
    [InlineData("", false)]
    public void IsGenerated_IgnoresDefaultGenerated(string extra, bool expected) =>
        Assert.Equal(expected, new ColumnInfo("c", "timestamp", false, false, false, extra).IsGenerated);

    [Fact]
    public void Insert_KeepsColumnWithExpressionDefault()
    {
        var columns = new List<ColumnInfo>
        {
            new("actor_id", "smallint unsigned", false, true, false, "auto_increment"),
            new("first_name", "varchar(45)", false, false, false, string.Empty),
            new("last_update", "timestamp", false, false, false, "DEFAULT_GENERATED on update CURRENT_TIMESTAMP"),
        };

        Assert.Equal(
            $"INSERT INTO sakila.actor{Nl}    (first_name, last_update){Nl}VALUES{Nl}    (@first_name, @last_update);{Nl}",
            Generate(ScriptKind.Insert, new CodeObject(CodeObjectKind.Table, "sakila", "actor"), columns));
    }

    [Fact]
    public void Update_SetsNonKeyColumnsAndFiltersOnPrimaryKey()
    {
        Assert.Equal(
            $"UPDATE sakila.film{Nl}SET{Nl}    title = @title,{Nl}    `order` = @order{Nl}WHERE film_id = @film_id;{Nl}",
            Generate(ScriptKind.Update, Film, FilmColumns));
    }

    [Fact]
    public void Delete_FiltersOnCompositePrimaryKey()
    {
        var columns = new List<ColumnInfo>
        {
            new("actor_id", "smallint unsigned", false, true, true, string.Empty),
            new("film_id", "smallint unsigned", false, true, true, string.Empty),
            new("last_update", "timestamp", false, false, false, string.Empty),
        };

        Assert.Equal(
            $"DELETE FROM sakila.film_actor{Nl}WHERE actor_id = @actor_id{Nl}    AND film_id = @film_id;{Nl}",
            Generate(ScriptKind.Delete, new CodeObject(CodeObjectKind.Table, "sakila", "film_actor"), columns));
    }

    [Fact]
    public void UpdateAndDelete_WithoutPrimaryKey_TouchNoRows()
    {
        var columns = new List<ColumnInfo> { new("note", "text", true, false, false, string.Empty) };
        var log = new CodeObject(CodeObjectKind.Table, "shop", "log");

        Assert.EndsWith($"-- No primary key: replace 1 = 0 with a condition before you run this statement.{Nl}WHERE 1 = 0;{Nl}", Generate(ScriptKind.Delete, log, columns));
        Assert.EndsWith($"WHERE 1 = 0;{Nl}", Generate(ScriptKind.Update, log, columns));
    }

    [Theory]
    [InlineData(CodeObjectKind.Table, "DROP TABLE sakila.film;")]
    [InlineData(CodeObjectKind.View, "DROP VIEW sakila.film;")]
    [InlineData(CodeObjectKind.Procedure, "DROP PROCEDURE sakila.film;")]
    [InlineData(CodeObjectKind.Function, "DROP FUNCTION sakila.film;")]
    public void Drop_PerKind(CodeObjectKind kind, string expected) =>
        Assert.Equal(expected + Nl, Generate(ScriptKind.Drop, new CodeObject(kind, "sakila", "film")));

    [Fact]
    public void Create_UsesShowCreateWithDelimiterForRoutines()
    {
        Assert.Equal($"CREATE TABLE film (x int);{Nl}", Generate(ScriptKind.Create, Film, create: "CREATE TABLE `film` (x int)"));
        Assert.StartsWith("DELIMITER $$", Generate(ScriptKind.Create, new CodeObject(CodeObjectKind.Procedure, "sakila", "p"), create: "CREATE PROCEDURE p() BEGIN END"));
    }

    [Fact]
    public void CallAndFunctionSelect_UseUserVariablesAndAlwaysTheDatabase()
    {
        var noPrefix = DropFormatOptions.Default with { DatabasePrefixForRoutines = false };

        Assert.Equal($"CALL sakila.film_in_stock(@p_film_id, @p_store_id);{Nl}", Generate(ScriptKind.Call, new CodeObject(CodeObjectKind.Procedure, "sakila", "film_in_stock"), parameters: ["p_film_id", "p_store_id"], options: noPrefix));
        Assert.Equal($"SELECT sakila.get_balance(@p_id);{Nl}", Generate(ScriptKind.Select, new CodeObject(CodeObjectKind.Function, "sakila", "get_balance"), parameters: ["p_id"]));
    }

    [Fact]
    public void QuotingAlways_QuotesNamesButNotVariables()
    {
        var always = DropFormatOptions.Default with { SqlQuoting = QuoteMode.Always };

        Assert.Equal($"DELETE FROM `sakila`.`film`{Nl}WHERE `film_id` = @film_id;{Nl}", Generate(ScriptKind.Delete, Film, FilmColumns, options: always));
    }

    [Fact]
    public void ViewSelect_Works_AndUnavailableCombinationThrows()
    {
        var view = new CodeObject(CodeObjectKind.View, "sakila", "actor_info");

        Assert.StartsWith("SELECT", Generate(ScriptKind.Select, view, FilmColumns));
        Assert.Throws<InvalidOperationException>(() => Generate(ScriptKind.Insert, view, FilmColumns));
    }
}
