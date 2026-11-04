using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Dependencies;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Dependencies;

public class DependencyTests
{
    private static readonly IReadOnlyList<CatalogObject> Objects =
    [
        new(Table("film"), null),
        new(Table("film_actor"), null),
        new(Table("language"), null),
        new(View("film_list"), "select `f`.`title` from `sakila`.`film` `f` join `sakila`.`film_actor` `fa`"),
        new(Procedure("film_in_stock"), "BEGIN SELECT inventory_id FROM inventory WHERE film_id = p_film_id; END"),
        new(Procedure("rewards_report"), "BEGIN SELECT * FROM film_actor; END"),
        new(Procedure("secret"), null),
        new(Function("get_title"), "RETURN (SELECT title FROM film WHERE film_id = id)"),
        new(new SchemaObject(DependencyKind.Trigger, "sakila", "ins_film", "film"), "INSERT INTO film_text VALUES (new.film_id)"),
        new(new SchemaObject(DependencyKind.Trigger, "sakila", "upd_actor", "film_actor"), "UPDATE film SET last_update = NOW()"),
        new(new SchemaObject(DependencyKind.Event, "sakila", "purge"), "DELETE FROM film_actor WHERE 1 = 0"),
    ];

    private static readonly IReadOnlyList<ForeignKeyLink> ForeignKeys =
    [
        new("sakila", "film_actor", "fk_film_actor_film", "sakila", "film"),
        new("sakila", "film", "fk_film_language", "sakila", "language"),
        new("shop", "promo", "fk_promo_film", "sakila", "film"),
    ];

    private static readonly IReadOnlyList<ViewUsage> Usage =
    [
        new("sakila", "film_list", Table("film")),
        new("sakila", "film_list", Table("film_actor")),
        new("sakila", "film_list", Function("get_title")),
        new("report", "top_films", View("film_list")),
    ];

    private static SchemaObject Table(string name, string db = "sakila") => new(DependencyKind.Table, db, name);

    private static SchemaObject View(string name, string db = "sakila") => new(DependencyKind.View, db, name);

    private static SchemaObject Procedure(string name) => new(DependencyKind.Procedure, "sakila", name);

    private static SchemaObject Function(string name) => new(DependencyKind.Function, "sakila", name);

    private static string[] Lines(IEnumerable<Dependency> dependencies) =>
        dependencies.Select(d => $"{d.Object.Kind} {d.Object.Database}.{d.Object.Name} {d.Reason}{(d.Detail is null ? string.Empty : " " + d.Detail)}").ToArray();

    [Fact]
    public void Table_UsedByViewsForeignKeysTriggersAndSourceText()
    {
        var result = DependencyFinder.Find(new DependencyCatalog(Objects, ForeignKeys, Usage), Table("film"));

        Assert.Equal(
            [
                "Table sakila.film_actor ForeignKey fk_film_actor_film",
                "Table shop.promo ForeignKey fk_promo_film",
                "View sakila.film_list View",
                "Function sakila.get_title SourceText",
                "Trigger sakila.ins_film Trigger",
                "Trigger sakila.upd_actor SourceText",
            ],
            Lines(result.UsedBy));
        Assert.Equal(["Table sakila.language ForeignKey fk_film_language"], Lines(result.Uses));
        Assert.True(result.SourcesIncomplete);
    }

    [Fact]
    public void View_WithUsage_UsesItsTablesAndFunctions()
    {
        var result = DependencyFinder.Find(new DependencyCatalog(Objects, ForeignKeys, Usage), View("film_list"));

        Assert.Equal(["Table sakila.film View", "Table sakila.film_actor View", "Function sakila.get_title View"], Lines(result.Uses));
        Assert.Equal(["View report.top_films View"], Lines(result.UsedBy));
    }

    [Fact]
    public void View_WithoutUsage_MatchesTheDefinition()
    {
        var result = DependencyFinder.Find(new DependencyCatalog(Objects, ForeignKeys, null), View("film_list"));

        Assert.Equal(["Table sakila.film SourceText", "Table sakila.film_actor SourceText"], Lines(result.Uses));
    }

    [Fact]
    public void Table_WithoutUsage_FindsViewsOnText()
    {
        var result = DependencyFinder.Find(new DependencyCatalog(Objects, ForeignKeys, null), Table("film_actor"));
        var usedBy = Lines(result.UsedBy);

        Assert.Contains("View sakila.film_list SourceText", usedBy);
        Assert.Contains("Procedure sakila.rewards_report SourceText", usedBy);
        Assert.Contains("Event sakila.purge SourceText", usedBy);
        Assert.Contains("Trigger sakila.upd_actor Trigger", usedBy);
        Assert.DoesNotContain("Trigger sakila.upd_actor SourceText", usedBy);
        Assert.Equal(["Table sakila.film ForeignKey fk_film_actor_film"], Lines(result.Uses));
    }

    [Fact]
    public void Routine_UsesWhatItsSourceNames()
    {
        var result = DependencyFinder.Find(new DependencyCatalog(Objects, ForeignKeys, Usage), Function("get_title"));

        Assert.Equal(["Table sakila.film SourceText"], Lines(result.Uses));
        Assert.Equal(["View sakila.film_list View"], Lines(result.UsedBy));
    }

    [Fact]
    public void Routine_WithoutSource_IsIncomplete()
    {
        var result = DependencyFinder.Find(new DependencyCatalog(Objects, ForeignKeys, Usage), Procedure("secret"));

        Assert.Empty(result.Uses);
        Assert.True(result.SourcesIncomplete);
    }

    [Theory]
    [InlineData("SELECT * FROM film", "film", true)]
    [InlineData("SELECT * FROM `film`", "film", true)]
    [InlineData("SELECT * FROM sakila.FILM f", "film", true)]
    [InlineData("SELECT * FROM film_actor", "film", false)]
    [InlineData("SELECT * FROM my_film", "film", false)]
    [InlineData("SELECT film$x FROM t", "film", false)]
    [InlineData("SELECT * FROM film_actor JOIN film ON 1", "film", true)]
    [InlineData(null, "film", false)]
    public void Mentions(string? source, string name, bool expected) => Assert.Equal(expected, SourceMatcher.Mentions(source, name));

    [Fact]
    public void SubjectOf_TablesViewsAndRoutines()
    {
        var context = TestExplorer.Context();
        var server = new ServerNodeViewModel(context, TestExplorer.Definition());

        Assert.Equal(Table("film"), DependencyNavigator.SubjectOf(new TableNodeViewModel(context, server, new TableInfo("sakila", "film", "InnoDB", null))));
        Assert.Equal(Function("f"), DependencyNavigator.SubjectOf(new RoutineNodeViewModel(context, server, new RoutineInfo("sakila", "f", RoutineType.Function, null, null))));
        Assert.Null(DependencyNavigator.SubjectOf(server));
        Assert.Null(DependencyNavigator.SubjectOf(null));
    }

    private sealed class FakeSource(Func<DependencyCatalog> catalog) : IDependencySource
    {
        public string? Version { get; private set; }

        public Task<DependencyCatalog> LoadAsync(ObjectExplorerForMySql.Core.Connections.ConnectionDefinition definition, string database, string? serverVersion, System.Threading.CancellationToken cancellationToken)
        {
            Version = serverVersion;
            return Task.FromResult(catalog());
        }
    }

    [Fact]
    public async Task ViewModel_ShowsGroupsWithNotes()
    {
        var model = new DependencyViewModel(new FakeSource(() => new DependencyCatalog(Objects, ForeignKeys, Usage)), new DependencyTexts(), new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));
        var server = new ServerNodeViewModel(TestExplorer.Context(), TestExplorer.Definition());

        await model.LoadAsync(server, Table("film"));

        Assert.Equal("Table sakila.film", model.Title);
        Assert.Null(model.Status);
        Assert.Equal(["Used by (6)", "Uses (1)"], model.Groups.Select(g => g.Title));
        var usedBy = model.Groups[0].Items;
        Assert.Equal(["film_actor", "shop.promo", "film_list", "get_title", "ins_film", "upd_actor"], usedBy.Select(i => i.Label));
        Assert.Equal(["foreign key fk_film_actor_film", "foreign key fk_promo_film", "", "text match", "trigger on the table", "text match"], usedBy.Select(i => i.Note));
        Assert.Equal(NodeIcon.Function, usedBy[3].Icon);
        Assert.NotNull(model.Note);
    }

    [Fact]
    public async Task ViewModel_EmptyGroupAndError()
    {
        var calls = 0;
        var model = new DependencyViewModel(
            new FakeSource(() => ++calls == 1 ? new DependencyCatalog([], [], null) : throw new InvalidOperationException("Access denied")),
            new DependencyTexts(),
            new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));
        var server = new ServerNodeViewModel(TestExplorer.Context(), TestExplorer.Definition());

        await model.LoadAsync(server, Table("film"));
        Assert.Equal("(none found)", model.Groups[0].EmptyText);
        Assert.Null(model.Note);

        await model.LoadAsync(server, Table("film"));
        Assert.Empty(model.Groups);
        Assert.Equal("Reading the dependencies failed: Access denied", model.Status);
    }

    [Fact]
    public async Task SelectAsync_ExpandsTheTreeToTheObject()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", null, null), new DatabaseInfo("mysql", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "film", "InnoDB", null)]),
            Routines = [new RoutineInfo("sakila", "get_title", RoutineType.Function, null, null)],
            Triggers = [new TriggerInfo("ins_film", "AFTER", "INSERT")],
        };
        var server = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());
        var texts = new ExplorerTexts();

        var table = await DependencyNavigator.SelectAsync(server, Table("FILM"), texts);
        var function = await DependencyNavigator.SelectAsync(server, Function("get_title"), texts);
        var trigger = await DependencyNavigator.SelectAsync(server, new SchemaObject(DependencyKind.Trigger, "sakila", "ins_film", "film"), texts);
        var system = await DependencyNavigator.SelectAsync(server, Table("film", "mysql"), texts);
        var missing = await DependencyNavigator.SelectAsync(server, Table("gone"), texts);

        Assert.True(Assert.IsType<TableNodeViewModel>(table).IsSelected);
        Assert.True(table!.Parent!.IsExpanded);
        Assert.True(Assert.IsType<RoutineNodeViewModel>(function).IsSelected);
        Assert.Equal("ins_film", Assert.IsType<TriggerInfo>(Assert.IsType<ObjectNodeViewModel>(trigger).Item).Name);
        Assert.Equal("mysql", Assert.IsType<TableNodeViewModel>(system).Table.Database);
        Assert.Null(missing);
    }
}
