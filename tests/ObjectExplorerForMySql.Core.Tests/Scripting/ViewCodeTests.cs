using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Scripting;

public class ViewCodeTests
{
    [Theory]
    [InlineData(CodeObjectKind.Table, "SHOW CREATE TABLE `sakila`.`film`", "Create Table")]
    [InlineData(CodeObjectKind.View, "SHOW CREATE VIEW `sakila`.`film`", "Create View")]
    [InlineData(CodeObjectKind.Procedure, "SHOW CREATE PROCEDURE `sakila`.`film`", "Create Procedure")]
    [InlineData(CodeObjectKind.Function, "SHOW CREATE FUNCTION `sakila`.`film`", "Create Function")]
    [InlineData(CodeObjectKind.Trigger, "SHOW CREATE TRIGGER `sakila`.`film`", "SQL Original Statement")]
    [InlineData(CodeObjectKind.Event, "SHOW CREATE EVENT `sakila`.`film`", "Create Event")]
    public void ShowCreate_StatementAndSourceColumnPerKind(CodeObjectKind kind, string statement, string column)
    {
        var codeObject = new CodeObject(kind, "sakila", "film");

        Assert.Equal(statement, codeObject.ShowCreateStatement);
        Assert.Equal(column, codeObject.SourceColumn);
    }

    [Fact]
    public void ShowCreate_DoublesBackticksInNames()
    {
        Assert.Equal("SHOW CREATE TABLE `my``db`.`order`", new CodeObject(CodeObjectKind.Table, "my`db", "order").ShowCreateStatement);
    }

    [Theory]
    [InlineData("sakila", "film", "sakila.film.sql")]
    [InlineData("shop", "a/b:c", "shop.a_b_c.sql")]
    public void FileName_IsDatabaseObjectSql(string database, string name, string expected) =>
        Assert.Equal(expected, new CodeObject(CodeObjectKind.Table, database, name).FileName);

    [Fact]
    public void Format_TableEndsWithSemicolonAndHasHeader()
    {
        var text = CodeDocument.Format(new CodeObject(CodeObjectKind.Table, "sakila", "actor"), "CREATE TABLE `actor` (\n  `actor_id` int\n)", "Local");

        var nl = Environment.NewLine;
        Assert.Equal($"-- Table sakila.actor on Local{nl}{nl}CREATE TABLE `actor` (\n  `actor_id` int\n);{nl}", text);
    }

    [Fact]
    public void Format_RoutineIsWrappedInDelimiter()
    {
        var text = CodeDocument.Format(new CodeObject(CodeObjectKind.Procedure, "sakila", "p"), "CREATE PROCEDURE p() BEGIN SELECT 1; END;", null);

        var nl = Environment.NewLine;
        Assert.Equal($"-- Procedure sakila.p{nl}{nl}DELIMITER $${nl}{nl}CREATE PROCEDURE p() BEGIN SELECT 1; END$${nl}{nl}DELIMITER ;{nl}", text);
    }

    [Theory]
    [InlineData(CodeObjectKind.Table, false)]
    [InlineData(CodeObjectKind.View, false)]
    [InlineData(CodeObjectKind.Procedure, true)]
    [InlineData(CodeObjectKind.Function, true)]
    [InlineData(CodeObjectKind.Trigger, true)]
    [InlineData(CodeObjectKind.Event, true)]
    public void NeedsDelimiter_ForObjectsWithABody(CodeObjectKind kind, bool expected) =>
        Assert.Equal(expected, new CodeObject(kind, "d", "o").NeedsDelimiter);

    [Fact]
    public void DocumentConnections_RegisterFindRemove()
    {
        var links = new DocumentConnections();
        var id = Guid.NewGuid();

        links.Register(@"C:\Temp\Sakila.Film.sql", id, "sakila");

        Assert.Equal(new DocumentLink(id, "sakila"), links.Find(@"c:\temp\sakila.film.sql"));
        links.Remove(@"C:\Temp\sakila.film.sql");
        Assert.Null(links.Find(@"C:\Temp\sakila.film.sql"));
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
    public async Task Nodes_CreateCodeObjectsForObjectsWithSourceOnly()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", null, null), new DatabaseInfo("information_schema", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "film", "InnoDB", null)]),
            Views = [new TableInfo("sakila", "actor_info", null, null, isView: true)],
            Routines = [new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, null), new RoutineInfo("sakila", "get_balance", RoutineType.Function, "int", null)],
            Triggers = [new TriggerInfo("ins_film", "AFTER", "INSERT")],
            Events = [new EventInfo("purge", "ENABLED", null, "1", "DAY", null, null)],
            Keys = [new KeyInfo("PRIMARY", KeyType.Primary, ["film_id"])],
        };
        var server = await ExpandAsync(new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition()));
        var databases = await ExpandAsync(Child<FolderNodeViewModel>(server, "Databases"));
        var sakila = await ExpandAsync(Child<DatabaseNodeViewModel>(databases, "sakila"));
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(sakila, "Tables"));
        var film = await ExpandAsync(Child<TableNodeViewModel>(tables, "film"));
        var triggers = await ExpandAsync(Child<FolderNodeViewModel>(film, "Triggers"));
        var keys = await ExpandAsync(Child<FolderNodeViewModel>(film, "Keys"));
        var views = await ExpandAsync(Child<FolderNodeViewModel>(sakila, "Views"));
        var procedures = await ExpandAsync(Child<FolderNodeViewModel>(sakila, "Stored Procedures"));
        var functions = await ExpandAsync(Child<FolderNodeViewModel>(sakila, "Functions"));
        var events = await ExpandAsync(Child<FolderNodeViewModel>(sakila, "Events"));

        Assert.Equal((CodeObjectKind.Table, "sakila", "film"), Describe(film.CreateCodeObject()));
        Assert.Equal((CodeObjectKind.View, "sakila", "actor_info"), Describe(views.Children[0].CreateCodeObject()));
        Assert.Equal((CodeObjectKind.Procedure, "sakila", "film_in_stock"), Describe(procedures.Children[0].CreateCodeObject()));
        Assert.Equal((CodeObjectKind.Function, "sakila", "get_balance"), Describe(functions.Children[0].CreateCodeObject()));
        Assert.Equal((CodeObjectKind.Trigger, "sakila", "ins_film"), Describe(triggers.Children[0].CreateCodeObject()));
        Assert.Equal((CodeObjectKind.Event, "sakila", "purge"), Describe(events.Children[0].CreateCodeObject()));

        Assert.Null(server.CreateCodeObject());
        Assert.Null(sakila.CreateCodeObject());
        Assert.Null(tables.CreateCodeObject());
        Assert.Null(keys.Children[0].CreateCodeObject());
    }

    [Fact]
    public void SystemView_UsesShowCreateTable()
    {
        var context = TestExplorer.Context();
        var server = new ServerNodeViewModel(context, TestExplorer.Definition());
        var view = new ViewNodeViewModel(context, server, new TableInfo("information_schema", "TABLES", null, null, isView: true));

        Assert.Equal(CodeObjectKind.Table, view.CreateCodeObject()!.Kind);
    }

    private static (CodeObjectKind, string, string) Describe(CodeObject? codeObject) =>
        (codeObject!.Kind, codeObject.Database, codeObject.Name);
}
