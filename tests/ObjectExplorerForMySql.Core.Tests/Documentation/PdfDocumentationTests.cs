using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Dependencies;
using ObjectExplorerForMySql.Core.Documentation;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Documentation;

// US-1202: the documentation as PDF. The text in the PDF is stored as glyphs, so the tests read the structure:
// pages, the outline, the links of the contents and the dependency diagrams.
public class PdfDocumentationTests
{
    private static readonly SchemaObject Film = new(DependencyKind.Table, "sakila", "film");

    private static FakeMetadataProvider Metadata() => new()
    {
        Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", "utf8mb4", "utf8mb4_0900_ai_ci")]),
        Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "language", "InnoDB", null), new TableInfo(db, "film", "InnoDB", "All films")]),
        Views = [new TableInfo("sakila", "film_list", null, "VIEW", isView: true)],
        Routines = [new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, "Stock")],
        Parameters = [new ParameterInfo("p_film_id", ParameterMode.In, "int", 1)],
        Events = [new EventInfo("purge", "ENABLED", null, "1", "DAY", null, null) { Comment = "Daily" }],
        Columns = (_, table) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
        [
            new ColumnInfo("film_id", "smallint unsigned", false, true, false, "auto_increment"),
            new ColumnInfo("language_id", "tinyint unsigned", false, false, true, "") { Comment = "Spoken\nlanguage" },
            new ColumnInfo("last_update", "timestamp", false, false, false, "DEFAULT_GENERATED on update CURRENT_TIMESTAMP") { Default = "CURRENT_TIMESTAMP" },
        ]),
        Keys =
        [
            new KeyInfo("PRIMARY", KeyType.Primary, ["film_id"]),
            new KeyInfo("fk_film_language", KeyType.Foreign, ["language_id"], "sakila", "language", ["language_id"]) { UpdateRule = "CASCADE", DeleteRule = "RESTRICT" },
        ],
        Indexes = [new IndexInfo("idx_fk_language_id", false, "BTREE", ["language_id"])],
        Triggers = [new TriggerInfo("ins_film", "AFTER", "INSERT")],
    };

    private sealed class FakeDependencies : IDependencySource
    {
        public Task<DependencyCatalog> LoadAsync(Core.Connections.ConnectionDefinition definition, string database, string? serverVersion, CancellationToken cancellationToken) =>
            Task.FromResult(new DependencyCatalog(
                [
                    new(Film, null),
                    new(new SchemaObject(DependencyKind.Table, "sakila", "language"), null),
                    new(new SchemaObject(DependencyKind.View, "sakila", "film_list"), "select * from film"),
                    new(new SchemaObject(DependencyKind.Procedure, "sakila", "film_in_stock"), "SELECT 1 FROM film"),
                ],
                [new("sakila", "film", "fk_film_language", "sakila", "language"), new("shop", "promo", "fk_promo_film", "sakila", "film")],
                null));
    }

    private static async Task<PdfDocument> PdfAsync(DocumentOptions options)
    {
        var document = await new DatabaseDocumenter(Metadata(), new FakeDependencies())
            .ReadAsync(TestExplorer.Definition(), "sakila", options with { ConnectionLabel = "Local", ServerVersion = "8.0.36" }, null, CancellationToken.None);
        using var stream = new MemoryStream();
        PdfDocumentWriter.Write(document, stream);
        Assert.True(stream.CanRead, "The stream stays open.");
        stream.Position = 0;
        return PdfReader.Open(stream, PdfDocumentOpenMode.Import);
    }

    private static IEnumerable<string> Outline(PdfOutlineCollection outlines, string indent = "") =>
        outlines.SelectMany(o => new[] { indent + o.Title }.Concat(Outline(o.Outlines, indent + "  ")));

    [Fact]
    public async Task Pdf_OutlineListsSectionsAndObjects()
    {
        using var pdf = await PdfAsync(new DocumentOptions());

        Assert.Equal(
            ["Contents", "Tables", "  film", "  language", "Views", "  film_list", "Procedures", "  film_in_stock", "Functions", "Events"],
            Outline(pdf.Outlines).ToList());
        Assert.Equal("Database sakila", pdf.Info.Title);
    }

    [Fact]
    public async Task Pdf_EverySectionStartsOnANewPage()
    {
        using var pdf = await PdfAsync(new DocumentOptions { IncludeDependencies = false });

        // Contents, Tables, Views, Procedures, Functions and Events; without diagrams every section fits on a page.
        Assert.Equal(6, pdf.PageCount);
    }

    [Fact]
    public async Task Pdf_ContentsAndDependenciesLinkToTheObjects()
    {
        using var pdf = await PdfAsync(new DocumentOptions());

        // The contents link to 4 sections with objects, the events and 4 objects; the dependency lists link to
        // film (from its users and language) and the other way round.
        var links = pdf.Pages.Cast<PdfPage>().Sum(p => p.Annotations.Count);
        Assert.True(links >= 10, $"Expected at least 10 links, found {links}.");
    }

    [Fact]
    public async Task Pdf_LeavesOutWhatIsNotChosen()
    {
        using var pdf = await PdfAsync(new DocumentOptions { IncludeViews = false, IncludeRoutines = false, IncludeEvents = false, IncludeDependencies = false });

        Assert.Equal(["Contents", "Tables", "  film", "  language"], Outline(pdf.Outlines).ToList());
        Assert.Equal(2, pdf.PageCount);
    }

    [Theory]
    [InlineData("on update CURRENT_TIMESTAMP")]
    [InlineData("DEFAULT_GENERATED on update CURRENT_TIMESTAMP")]
    [InlineData("enum('G','PG','PG-13','R','NC-17')")]
    public void CellText_BreaksWordsWiderThanTheColumn(string text)
    {
        const double width = 50;
        var fitted = CellText.Fit(text, width, 8, bold: false);
        var measure = PdfSharp.Drawing.XGraphics.CreateMeasureContext(new PdfSharp.Drawing.XSize(1000, 1000), PdfSharp.Drawing.XGraphicsUnit.Point, PdfSharp.Drawing.XPageDirection.Downwards);
        var font = new PdfSharp.Drawing.XFont(PdfDocumentWriter.FontName, 8);

        Assert.Equal(text.Replace(" ", string.Empty), fitted.Replace("\n", string.Empty).Replace(" ", string.Empty));
        Assert.All(fitted.Split('\n', ' '), word => Assert.True(measure.MeasureString(word, font).Width <= width, $"'{word}' is wider than {width}."));
    }

    [Fact]
    public void CellText_PrefersABreakAfterAnUnderscore()
    {
        Assert.StartsWith("CURRENT_\n", CellText.Fit("CURRENT_TIMESTAMP", 60, 8, bold: false), StringComparison.Ordinal);
        Assert.Equal("short words stay", CellText.Fit("short words stay", 200, 8, bold: false));
    }

    [Fact]
    public void Diagram_UsersLeftSubjectMiddleUsedRight()
    {
        var language = new SchemaObject(DependencyKind.Table, "sakila", "language");
        var promo = new SchemaObject(DependencyKind.Table, "shop", "promo");
        var view = new SchemaObject(DependencyKind.View, "sakila", "film_list");
        var only = DependencyDiagram.Render(new DependencyResult(Film, [], [], false), "sakila");
        var both = DependencyDiagram.Render(
            new DependencyResult(
                Film,
                [new Dependency(promo, DependencyReason.ForeignKey, "fk_promo_film"), new Dependency(view, DependencyReason.SourceText, null)],
                [new Dependency(language, DependencyReason.ForeignKey, "fk_film_language")],
                false),
            "sakila");

        Assert.True(both.Width > only.Width * 3, $"{both.Width} should hold three columns next to {only.Width}.");
        Assert.True(both.Height > only.Height, "Two users stack above each other.");
        using var pdf = PdfReader.Open(new MemoryStream(both.Pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(1, pdf.PageCount);
        Assert.Equal(both.Width, pdf.Pages[0].Width.Point, 1);
    }
}
