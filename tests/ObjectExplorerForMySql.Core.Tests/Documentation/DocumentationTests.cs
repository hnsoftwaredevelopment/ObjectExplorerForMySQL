using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Documentation;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Documentation;

public class DocumentationTests
{
    private static FakeMetadataProvider Metadata() => new()
    {
        Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", "utf8mb4", "utf8mb4_0900_ai_ci")]),
        Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "language", "InnoDB", null), new TableInfo(db, "film", "InnoDB", "All films")]),
        Views = [new TableInfo("sakila", "film_list", null, "VIEW", isView: true)],
        Routines = [new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, "Stock | count")],
        Parameters = [new ParameterInfo("p_film_id", ParameterMode.In, "int", 1), new ParameterInfo("p_count", ParameterMode.Out, "int", 2)],
        Events = [new EventInfo("purge", "ENABLED", null, "1", "DAY", null, null) { Comment = "Daily" }],
        Columns = (_, table) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
        [
            new ColumnInfo("film_id", "smallint unsigned", false, true, false, "auto_increment"),
            new ColumnInfo("language_id", "tinyint unsigned", false, false, true, "") { Comment = "Spoken\nlanguage" },
        ]),
        Keys =
        [
            new KeyInfo("PRIMARY", KeyType.Primary, ["film_id"]),
            new KeyInfo("fk_film_language", KeyType.Foreign, ["language_id"], "sakila", "language", ["language_id"]) { UpdateRule = "CASCADE", DeleteRule = "RESTRICT" },
        ],
        Indexes = [new IndexInfo("idx_fk_language_id", false, "BTREE", ["language_id"])],
        Triggers = [new TriggerInfo("ins_film", "AFTER", "INSERT")],
    };

    private static Task<DatabaseDocument> ReadAsync(DocumentOptions options, IProgress<DocumentProgress>? progress = null) =>
        new DatabaseDocumenter(Metadata()).ReadAsync(TestExplorer.Definition(), "sakila", options, progress, CancellationToken.None);

    private static string Markdown(DatabaseDocument document)
    {
        using var writer = new StringWriter();
        MarkdownDocumentWriter.Write(document, writer);
        return writer.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public async Task Read_SortsAndReportsProgress()
    {
        var reports = new List<DocumentProgress>();

        var document = await ReadAsync(new DocumentOptions { ServerVersion = "8.0.36" }, new SyncProgress(reports.Add));

        Assert.Equal(["film", "language"], document.Tables.Select(t => t.Table.Name));
        Assert.Equal("utf8mb4", document.CharacterSet);
        Assert.Single(document.Views);
        Assert.Single(document.Procedures);
        Assert.Empty(document.Functions);
        Assert.Equal(DocumentStep.Listing, reports[0].Step);
        Assert.Equal(new DocumentProgress(DocumentStep.Routines, "film_in_stock", 4, 4), reports.Last());
    }

    [Fact]
    public async Task Read_LeavesOutWhatIsNotChosen()
    {
        var document = await ReadAsync(new DocumentOptions { IncludeViews = false, IncludeRoutines = false, IncludeEvents = false });
        var markdown = Markdown(document);

        Assert.Empty(document.Views);
        Assert.DoesNotContain("## Views", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Events", markdown, StringComparison.Ordinal);
        Assert.Contains("## Tables", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_CancelStops()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DatabaseDocumenter(Metadata()).ReadAsync(TestExplorer.Definition(), "sakila", new DocumentOptions(), null, cancellation.Token));
    }

    [Fact]
    public async Task Markdown_HeaderContentsAndSections()
    {
        var document = await ReadAsync(new DocumentOptions { ConnectionLabel = "Local", ServerVersion = "8.0.36", GeneratedAt = new DateTime(2026, 11, 3, 14, 5, 0) });
        var markdown = Markdown(document);

        Assert.StartsWith("# Database sakila\n\nServer: Local (8.0.36)  \nCharacter set: utf8mb4, collation: utf8mb4_0900_ai_ci  \nGenerated 2026-11-03 14:05 by Object Explorer for MySQL\n", markdown, StringComparison.Ordinal);
        Assert.Contains("- [Tables](#tables) (2)\n  - [film](#film)\n  - [language](#language)\n- [Views](#views) (1)\n  - [film_list](#film_list)\n", markdown, StringComparison.Ordinal);
        Assert.Contains("- [Events](#events) (1)\n", markdown, StringComparison.Ordinal);
        Assert.Contains("### film\n\nAll films\n\nInnoDB\n", markdown, StringComparison.Ordinal);
        Assert.Contains("| film_id | smallint unsigned | NO |  | PK, auto_increment |  |", markdown, StringComparison.Ordinal);
        Assert.Contains("| language_id | tinyint unsigned | NO |  | FK | Spoken<br>language |", markdown, StringComparison.Ordinal);
        Assert.Contains("**Primary key:** film_id", markdown, StringComparison.Ordinal);
        Assert.Contains("| fk_film_language | language_id | language(language_id) | CASCADE | RESTRICT |", markdown, StringComparison.Ordinal);
        Assert.Contains("| idx_fk_language_id | language_id | NO | BTREE |", markdown, StringComparison.Ordinal);
        Assert.Contains("| ins_film | AFTER | INSERT |", markdown, StringComparison.Ordinal);
        Assert.Contains("Stock | count", markdown, StringComparison.Ordinal);
        Assert.Contains("| p_count | OUT | int |", markdown, StringComparison.Ordinal);
        Assert.Contains("| purge | EVERY 1 DAY | ENABLED |  | Daily |", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("film_actor", "film_actor")]
    [InlineData("Order Lines", "order-lines")]
    [InlineData("a.b (x)", "ab-x")]
    public void Slug_AsGitHub(string heading, string expected) => Assert.Equal(expected, MarkdownDocumentWriter.Slug(heading));

    [Fact]
    public void Markdown_RepeatedNamesGetNumberedAnchors()
    {
        var routine = new RoutineDocument(new RoutineInfo("db", "tables", RoutineType.Procedure, null, null), []);
        var document = new DatabaseDocument("db", null, null, new DocumentOptions { IncludeViews = false, IncludeEvents = false }, [], [], [routine], [routine with { }], []);

        var markdown = Markdown(document);

        Assert.Contains("- [Procedures](#procedures) (1)\n  - [tables](#tables-1)\n- [Functions](#functions) (1)\n  - [tables](#tables-2)\n", markdown, StringComparison.Ordinal);
    }

    private sealed class FakeDependencies(Func<Core.Dependencies.DependencyCatalog> catalog) : Core.Dependencies.IDependencySource
    {
        public Task<Core.Dependencies.DependencyCatalog> LoadAsync(Core.Connections.ConnectionDefinition definition, string database, string? serverVersion, CancellationToken cancellationToken) =>
            Task.FromResult(catalog());
    }

    private static Core.Dependencies.DependencyCatalog Catalog()
    {
        static Core.Dependencies.SchemaObject Table(string name, string db = "sakila") => new(Core.Dependencies.DependencyKind.Table, db, name);
        return new(
            [
                new(Table("film"), null),
                new(Table("language"), null),
                new(new Core.Dependencies.SchemaObject(Core.Dependencies.DependencyKind.View, "sakila", "film_list"), "select * from film"),
                new(new Core.Dependencies.SchemaObject(Core.Dependencies.DependencyKind.Procedure, "sakila", "film_in_stock"), "SELECT 1 FROM film"),
            ],
            [new("sakila", "film", "fk_film_language", "sakila", "language"), new("shop", "promo", "fk_promo_film", "sakila", "film")],
            null);
    }

    [Fact]
    public async Task Dependencies_ListedWithLinksAndDiagram()
    {
        var documenter = new DatabaseDocumenter(Metadata(), new FakeDependencies(Catalog));
        var document = await documenter.ReadAsync(TestExplorer.Definition(), "sakila", new DocumentOptions(), null, CancellationToken.None);
        var markdown = Markdown(document);
        var film = markdown.Substring(markdown.IndexOf("### film\n", StringComparison.Ordinal));
        film = film.Substring(0, film.IndexOf("### language", StringComparison.Ordinal));

        Assert.Contains("**Dependencies**\n\nUsed by:\n\n- shop.promo (table, foreign key fk_promo_film)\n- [film_list](#film_list) (view, text match)\n- [film_in_stock](#film_in_stock) (procedure, text match)\n\nUses:\n\n- [language](#language) (table, foreign key fk_film_language)\n", film, StringComparison.Ordinal);
        Assert.Contains("```mermaid\nflowchart LR\n  s[\"film<br/>table\"]\n  u0[\"shop.promo<br/>table\"] -->|\"fk_promo_film\"| s\n  u1[\"film_list<br/>view\"] -.-> s\n", film, StringComparison.Ordinal);
        Assert.Contains("  s -->|\"fk_film_language\"| d0[\"language<br/>table\"]\n  style s stroke-width:3px\n```", film, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dependencies_WithoutDiagram_AndNoneFound()
    {
        var documenter = new DatabaseDocumenter(Metadata(), new FakeDependencies(() => new([], [], null)));
        var document = await documenter.ReadAsync(TestExplorer.Definition(), "sakila", new DocumentOptions { IncludeDiagrams = false }, null, CancellationToken.None);
        var markdown = Markdown(document);

        Assert.Contains("**Dependencies**\n\nNone found.\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("```mermaid", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dependencies_ReadError_IsNotedAtTheTop()
    {
        var documenter = new DatabaseDocumenter(Metadata(), new FakeDependencies(() => throw new InvalidOperationException("denied")));
        var document = await documenter.ReadAsync(TestExplorer.Definition(), "sakila", new DocumentOptions(), null, CancellationToken.None);
        var markdown = Markdown(document);

        Assert.Contains("The dependencies could not be read: denied", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("**Dependencies**", markdown, StringComparison.Ordinal);
        Assert.Equal(2, document.Tables.Count);
    }

    private sealed class SyncProgress(Action<DocumentProgress> report) : IProgress<DocumentProgress>
    {
        public void Report(DocumentProgress value) => report(value);
    }
}
