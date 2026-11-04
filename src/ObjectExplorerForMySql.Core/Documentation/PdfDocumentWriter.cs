using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using ObjectExplorerForMySql.Core.Dependencies;
using ObjectExplorerForMySql.Core.Metadata;
using PdfSharp.Fonts;

namespace ObjectExplorerForMySql.Core.Documentation;

/// <summary>
/// Writes a <see cref="DatabaseDocument"/> as PDF (US-1202), with the content of the Markdown version: a header,
/// a table of contents with page numbers and links, a section per object, and per object a dependency diagram
/// drawn in the style of Mermaid (<see cref="DependencyDiagram"/>). Every page has a page number; the PDF
/// outline lists the sections and objects. Uses MigraDoc and PDFsharp (D-20) with the Windows fonts.
/// </summary>
public static class PdfDocumentWriter
{
    /// <summary>The font of the document and the diagrams.</summary>
    internal const string FontName = "Segoe UI";

    private const double TableFontSize = 8;
    private const string TitleStyle = "DocTitle";
    private const string SubheadStyle = "Subhead";
    private const string Toc1Style = "Toc1";
    private const string Toc2Style = "Toc2";
    private const string ItemStyle = "Item";
    private const string NoteStyle = "Note";

    private static readonly object FontLock = new();
    private static readonly Color HeadingColor = new(0x1F, 0x38, 0x64);
    private static readonly Color MutedColor = new(0x59, 0x59, 0x59);
    private static readonly Color LinkColor = new(0x1F, 0x5F, 0xBF);
    private static readonly Color GridColor = new(0xBF, 0xBF, 0xBF);
    private static readonly Color HeaderFill = new(0xEC, 0xEC, 0xFF);
    private static bool _fontsReady;

    /// <summary>Gets the width of the text on an A4 page with margins of 2 cm, in points.</summary>
    internal static double ContentWidth => Unit.FromCentimeter(17).Point;

    /// <summary>Writes <paramref name="document"/> as PDF to <paramref name="stream"/>, which stays open.</summary>
    public static void Write(DatabaseDocument document, Stream stream)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        EnsureFonts();
        var renderer = new PdfDocumentRenderer { Document = Build(document) };
        renderer.RenderDocument();
        renderer.PdfDocument.Info.Title = "Database " + document.Database;
        renderer.PdfDocument.Info.Creator = "Object Explorer for MySQL";
        renderer.PdfDocument.Save(stream, false);
    }

    /// <summary>
    /// Lets PDFsharp use Segoe UI from the Windows font folder (<see cref="WindowsFontResolver"/>). When another
    /// extension in the same process set a font resolver first, ours becomes the fallback.
    /// </summary>
    internal static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsReady)
            {
                return;
            }

            try
            {
                if (GlobalFontSettings.FontResolver is null)
                {
                    GlobalFontSettings.FontResolver = new WindowsFontResolver();
                }
                else if (GlobalFontSettings.FallbackFontResolver is null)
                {
                    GlobalFontSettings.FallbackFontResolver = new WindowsFontResolver();
                }
            }
            catch (InvalidOperationException)
            {
                // Fonts were already resolved in this process; the settings in effect stay.
            }

            _fontsReady = true;
        }
    }

    /// <summary>Builds the MigraDoc document; internal for the tests.</summary>
    internal static Document Build(DatabaseDocument document)
    {
        var options = document.Options;
        var pdf = new Document();
        pdf.Info.Title = "Database " + document.Database;
        pdf.Info.Author = "Object Explorer for MySQL";
        DefineStyles(pdf);

        var section = pdf.AddSection();
        var setup = section.PageSetup;
        setup.PageWidth = Unit.FromCentimeter(21);
        setup.PageHeight = Unit.FromCentimeter(29.7);
        setup.TopMargin = Unit.FromCentimeter(2.2);
        setup.BottomMargin = Unit.FromCentimeter(2);
        setup.LeftMargin = Unit.FromCentimeter(2);
        setup.RightMargin = Unit.FromCentimeter(2);
        setup.HeaderDistance = Unit.FromCentimeter(1);
        setup.FooterDistance = Unit.FromCentimeter(1);

        var header = section.Headers.Primary.AddParagraph("Database " + document.Database);
        header.Format.Alignment = ParagraphAlignment.Right;
        header.Format.Font.Size = 8;
        header.Format.Font.Color = MutedColor;
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = MutedColor;
        footer.AddText("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        section.AddParagraph("Database " + document.Database, TitleStyle);
        var server = string.Join(" ", new[] { options.ConnectionLabel, options.ServerVersion is null ? null : $"({options.ServerVersion})" }.Where(p => p is not null));
        var info = section.AddParagraph();
        if (server.Length > 0)
        {
            info.AddText("Server: " + server);
            info.AddLineBreak();
        }

        if (document.CharacterSet is not null)
        {
            info.AddText($"Character set: {document.CharacterSet}, collation: {document.Collation}");
            info.AddLineBreak();
        }

        var generated = options.GeneratedAt is { } at ? at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " : string.Empty;
        info.AddText($"Generated {generated}by Object Explorer for MySQL");
        if (document.DependencyError is { } error)
        {
            section.AddParagraph("The dependencies could not be read: " + error, NoteStyle);
        }
        else if (document.Dependencies?.Values.Any(d => d.SourcesIncomplete) == true)
        {
            section.AddParagraph("Some sources could not be read with this account, so dependencies found by a text match can be missing.", NoteStyle);
        }

        // Bookmarks: one per section and object, so the contents and the dependency lists can link to them.
        var bookmarks = new Bookmarks();
        var sections = new List<(string Title, string Bookmark, int Count, List<(string Name, string Bookmark)> Items)>();
        void Add(string title, IEnumerable<string> names, DependencyKind? kind = null, int? count = null)
        {
            var items = names.Select(n => (n, kind is { } k ? bookmarks.Of(k, n) : bookmarks.Next())).ToList();
            sections.Add((title, bookmarks.Next(), count ?? items.Count, items));
        }

        if (options.IncludeTables)
        {
            Add("Tables", document.Tables.Select(t => t.Table.Name), DependencyKind.Table);
        }

        if (options.IncludeViews)
        {
            Add("Views", document.Views.Select(v => v.View.Name), DependencyKind.View);
        }

        if (options.IncludeRoutines)
        {
            Add("Procedures", document.Procedures.Select(r => r.Routine.Name), DependencyKind.Procedure);
            Add("Functions", document.Functions.Select(r => r.Routine.Name), DependencyKind.Function);
        }

        if (options.IncludeEvents)
        {
            Add("Events", [], count: document.Events.Count);
        }

        var contents = section.AddParagraph("Contents", StyleNames.Heading1);
        contents.Format.PageBreakBefore = false;
        foreach (var (title, bookmark, count, items) in sections)
        {
            TocLine(section, Toc1Style, $"{title} ({count})", bookmark);
            foreach (var (name, itemBookmark) in items)
            {
                TocLine(section, Toc2Style, name, itemBookmark);
            }
        }

        var next = 0;
        if (options.IncludeTables)
        {
            Heading(section, StyleNames.Heading1, "Tables", sections[next++].Bookmark);
            foreach (var table in document.Tables)
            {
                WriteTable(section, table, bookmarks.Of(DependencyKind.Table, table.Table.Name));
                WriteDependencies(section, document, DependencyKind.Table, table.Table.Name, bookmarks);
            }
        }

        if (options.IncludeViews)
        {
            Heading(section, StyleNames.Heading1, "Views", sections[next++].Bookmark);
            foreach (var view in document.Views)
            {
                WriteView(section, view, bookmarks.Of(DependencyKind.View, view.View.Name));
                WriteDependencies(section, document, DependencyKind.View, view.View.Name, bookmarks);
            }
        }

        if (options.IncludeRoutines)
        {
            Heading(section, StyleNames.Heading1, "Procedures", sections[next++].Bookmark);
            foreach (var routine in document.Procedures)
            {
                WriteRoutine(section, routine, bookmarks.Of(DependencyKind.Procedure, routine.Routine.Name));
                WriteDependencies(section, document, DependencyKind.Procedure, routine.Routine.Name, bookmarks);
            }

            Heading(section, StyleNames.Heading1, "Functions", sections[next++].Bookmark);
            foreach (var routine in document.Functions)
            {
                WriteRoutine(section, routine, bookmarks.Of(DependencyKind.Function, routine.Routine.Name));
                WriteDependencies(section, document, DependencyKind.Function, routine.Routine.Name, bookmarks);
            }
        }

        if (options.IncludeEvents)
        {
            Heading(section, StyleNames.Heading1, "Events", sections[next].Bookmark);
            if (document.Events.Count == 0)
            {
                section.AddParagraph("None.");
            }
            else
            {
                Grid(section, ["Event", "Schedule", "Status", "On completion", "Comment"], [3, 4, 1.6, 2, 4], document.Events.Select(e => new[]
                {
                    e.Name, e.Schedule, e.Status, e.OnCompletion, e.Comment,
                }));
            }
        }

        return pdf;
    }

    private static void WriteTable(Section section, TableDocument table, string bookmark)
    {
        var info = table.Table;
        Heading(section, StyleNames.Heading2, info.Name, bookmark);
        Text(section, info.Comment);
        Text(section, string.Join(", ", new[] { info.Engine, info.Collation }.Where(p => !string.IsNullOrEmpty(p))), NoteStyle);

        var foreignKeyColumns = new HashSet<string>(table.Keys.Where(k => k.Type == KeyType.Foreign).SelectMany(k => k.Columns), StringComparer.OrdinalIgnoreCase);
        Grid(section, ["Column", "Type", "Null", "Default", "Key", "Comment"], [2.6, 2.6, 0.8, 3.1, 3.1, 3.0], table.Columns.Select(c => new[]
        {
            c.Name,
            c.ColumnType,
            c.IsNullable ? "YES" : "NO",
            c.Default,
            string.Join(", ", new[] { c.IsPrimaryKey ? "PK" : null, foreignKeyColumns.Contains(c.Name) || c.IsForeignKey ? "FK" : null, c.Extra.Length > 0 ? c.Extra : null }.Where(p => p is not null)),
            c.Comment,
        }));

        if (table.Keys.FirstOrDefault(k => k.Type == KeyType.Primary) is { } primary)
        {
            var paragraph = section.AddParagraph();
            paragraph.AddFormattedText("Primary key: ", TextFormat.Bold);
            paragraph.AddText(string.Join(", ", primary.Columns));
        }

        var unique = table.Keys.Where(k => k.Type == KeyType.Unique).ToList();
        if (unique.Count > 0)
        {
            section.AddParagraph("Unique keys", SubheadStyle);
            Grid(section, ["Name", "Columns"], [1, 2], unique.Select(k => new[] { k.Name, string.Join(", ", k.Columns) }));
        }

        var foreign = table.Keys.Where(k => k.Type == KeyType.Foreign).ToList();
        if (foreign.Count > 0)
        {
            section.AddParagraph("Foreign keys", SubheadStyle);
            Grid(section, ["Name", "Columns", "References", "On update", "On delete"], [3.4, 2.4, 3.4, 1.6, 1.6], foreign.Select(k => new[]
            {
                k.Name,
                string.Join(", ", k.Columns),
                $"{k.ReferencedTable}({string.Join(", ", k.ReferencedColumns)})",
                k.UpdateRule,
                k.DeleteRule,
            }));
        }

        if (table.Indexes.Count > 0)
        {
            section.AddParagraph("Indexes", SubheadStyle);
            Grid(section, ["Name", "Columns", "Unique", "Type"], [3, 3, 1, 1.6], table.Indexes.Select(i => new[]
            {
                i.Name, string.Join(", ", i.Columns), i.IsUnique ? "YES" : "NO", i.IndexType + (i.IsVisible ? string.Empty : ", invisible"),
            }));
        }

        if (table.Checks.Count > 0)
        {
            section.AddParagraph("Check constraints", SubheadStyle);
            Grid(section, ["Name", "Clause", "Enforced"], [2, 4, 1], table.Checks.Select(c => new[] { c.Name, c.Clause, c.Enforced ? "YES" : "NO" }));
        }

        if (table.Triggers.Count > 0)
        {
            section.AddParagraph("Triggers", SubheadStyle);
            Grid(section, ["Name", "Timing", "Event"], [3, 1, 1], table.Triggers.Select(t => new[] { t.Name, t.Timing, t.Manipulation }));
        }
    }

    private static void WriteView(Section section, ViewDocument view, string bookmark)
    {
        Heading(section, StyleNames.Heading2, view.View.Name, bookmark);
        Text(section, view.View.Comment is { Length: > 0 } comment && !string.Equals(comment, "VIEW", StringComparison.Ordinal) ? comment : null);
        Grid(section, ["Column", "Type", "Null", "Comment"], [3, 3, 1, 5], view.Columns.Select(c => new[] { c.Name, c.ColumnType, c.IsNullable ? "YES" : "NO", c.Comment }));
    }

    private static void WriteRoutine(Section section, RoutineDocument document, string bookmark)
    {
        var routine = document.Routine;
        Heading(section, StyleNames.Heading2, routine.Name, bookmark);
        Text(section, routine.Comment);
        if (routine.Type == RoutineType.Function && routine.ReturnType is { Length: > 0 } returns)
        {
            var paragraph = section.AddParagraph();
            paragraph.AddFormattedText("Returns: ", TextFormat.Bold);
            paragraph.AddText(returns);
        }

        var details = new[]
        {
            routine.IsDeterministic ? "deterministic" : "not deterministic",
            routine.SqlDataAccess?.ToLowerInvariant(),
            routine.SecurityType is { Length: > 0 } security ? "security " + security.ToLowerInvariant() : null,
        }.Where(p => !string.IsNullOrEmpty(p));
        Text(section, string.Join(", ", details), NoteStyle);

        var parameters = document.Parameters.OrderBy(p => p.Position).ToList();
        if (parameters.Count == 0)
        {
            section.AddParagraph("No parameters.");
        }
        else if (routine.Type == RoutineType.Procedure)
        {
            Grid(section, ["Parameter", "Mode", "Type"], [3, 1, 3], parameters.Select(p => new[] { p.Name, p.Mode.ToString().ToUpperInvariant(), p.DataType }));
        }
        else
        {
            Grid(section, ["Parameter", "Type"], [3, 4], parameters.Select(p => new[] { p.Name, p.DataType }));
        }
    }

    // What uses the object and what it uses (US-1303), as lists with links and, when chosen, a diagram.
    private static void WriteDependencies(Section section, DatabaseDocument document, DependencyKind kind, string name, Bookmarks bookmarks)
    {
        if (document.DependenciesOf(kind, name) is not { } result)
        {
            return;
        }

        section.AddParagraph("Dependencies", SubheadStyle);
        if (result.UsedBy.Count == 0 && result.Uses.Count == 0)
        {
            section.AddParagraph("None found.");
            return;
        }

        DependencyList(section, "Used by", result.UsedBy, document.Database, bookmarks);
        DependencyList(section, "Uses", result.Uses, document.Database, bookmarks);
        if (!document.Options.IncludeDiagrams)
        {
            return;
        }

        var diagram = DependencyDiagram.Render(result, document.Database);
        var image = section.AddParagraph().AddImage("base64:" + Convert.ToBase64String(diagram.Pdf));
        image.LockAspectRatio = true;

        // Never wider than the text and never taller than most of a page; a diagram is never enlarged.
        var scale = Math.Min(1, Math.Min(ContentWidth / diagram.Width, Unit.FromCentimeter(20).Point / diagram.Height));
        image.Width = Unit.FromPoint(diagram.Width * scale);
    }

    private static void DependencyList(Section section, string title, IReadOnlyList<Dependency> dependencies, string database, Bookmarks bookmarks)
    {
        if (dependencies.Count == 0)
        {
            return;
        }

        var caption = section.AddParagraph(title + ":");
        caption.Format.KeepWithNext = true;
        caption.Format.SpaceBefore = 3;
        caption.Format.SpaceAfter = 2;
        foreach (var dependency in dependencies)
        {
            var target = dependency.Object;
            var paragraph = section.AddParagraph(string.Empty, ItemStyle);
            paragraph.AddText("•");
            paragraph.AddTab();
            var label = MarkdownDocumentWriter.Label(target, database);
            if (string.Equals(target.Database, database, StringComparison.OrdinalIgnoreCase) && bookmarks.Find(target.Kind, target.Name) is { } bookmark)
            {
                var link = paragraph.AddHyperlink(bookmark);
                link.Font.Color = LinkColor;
                link.AddText(label);
            }
            else
            {
                paragraph.AddText(label);
            }

            var note = string.Join(", ", new[] { MarkdownDocumentWriter.KindText(target.Kind), MarkdownDocumentWriter.Reason(dependency) }.Where(p => p.Length > 0));
            paragraph.AddFormattedText(" (" + note + ")").Font.Color = MutedColor;
        }
    }

    private static void TocLine(Section section, string style, string text, string bookmark)
    {
        var link = section.AddParagraph(string.Empty, style).AddHyperlink(bookmark);
        link.AddText(text);
        link.AddTab();
        link.AddPageRefField(bookmark);
    }

    private static void Heading(Section section, string style, string text, string bookmark)
    {
        var paragraph = section.AddParagraph(string.Empty, style);
        paragraph.AddBookmark(bookmark);
        paragraph.AddText(text);
    }

    private static void Text(Section section, string? text, string? style = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var paragraph = style is null ? section.AddParagraph() : section.AddParagraph(string.Empty, style);
        AddLines(paragraph, text!);
    }

    // A line break in a comment or clause stays a line break.
    private static void AddLines(Paragraph paragraph, string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                paragraph.AddLineBreak();
            }

            paragraph.AddText(lines[i]);
        }
    }

    // A table over the full width; the header row repeats on every page the table continues on.
    private static void Grid(Section section, IReadOnlyList<string> headers, double[] weights, IEnumerable<string?[]> rows)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = GridColor;
        table.Format.Font.Size = TableFontSize;
        table.Format.SpaceAfter = 0;
        table.LeftPadding = 3;
        table.RightPadding = 3;
        table.TopPadding = 1.5;
        table.BottomPadding = 1.5;
        var total = weights.Sum();
        var widths = weights.Select(w => ContentWidth * w / total).ToArray();
        foreach (var width in widths)
        {
            table.AddColumn(Unit.FromPoint(width));
        }

        // The text width of a cell: the column without the padding and a small margin.
        double Inner(int column) => widths[column] - 6 - 2;

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = HeaderFill;
        header.Format.Font.Bold = true;
        for (var i = 0; i < headers.Count; i++)
        {
            AddLines(header.Cells[i].AddParagraph(), CellText.Fit(headers[i], Inner(i), TableFontSize, bold: true));
        }

        foreach (var values in rows)
        {
            var row = table.AddRow();
            for (var i = 0; i < headers.Count && i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i]))
                {
                    AddLines(row.Cells[i].AddParagraph(), CellText.Fit(values[i]!, Inner(i), TableFontSize, bold: false));
                }
            }
        }

        var spacer = section.AddParagraph();
        spacer.Format.Font.Size = 4;
        spacer.Format.SpaceAfter = 2;
    }

    private static void DefineStyles(Document pdf)
    {
        var normal = pdf.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontName;
        normal.Font.Size = 9;
        normal.ParagraphFormat.SpaceAfter = 4;

        var title = pdf.Styles.AddStyle(TitleStyle, StyleNames.Normal);
        title.Font.Size = 20;
        title.Font.Bold = true;
        title.Font.Color = HeadingColor;
        title.ParagraphFormat.SpaceAfter = 8;
        title.ParagraphFormat.OutlineLevel = OutlineLevel.BodyText;

        var heading1 = pdf.Styles[StyleNames.Heading1]!;
        heading1.Font.Size = 15;
        heading1.Font.Bold = true;
        heading1.Font.Color = HeadingColor;
        heading1.ParagraphFormat.PageBreakBefore = true;
        heading1.ParagraphFormat.SpaceBefore = 0;
        heading1.ParagraphFormat.SpaceAfter = 8;
        heading1.ParagraphFormat.KeepWithNext = true;
        heading1.ParagraphFormat.OutlineLevel = OutlineLevel.Level1;

        var heading2 = pdf.Styles[StyleNames.Heading2]!;
        heading2.Font.Size = 12;
        heading2.Font.Bold = true;
        heading2.Font.Color = HeadingColor;

        // MigraDoc bases Heading2 on Heading1; an object continues on the page.
        heading2.ParagraphFormat.PageBreakBefore = false;
        heading2.ParagraphFormat.SpaceBefore = 14;
        heading2.ParagraphFormat.SpaceAfter = 4;
        heading2.ParagraphFormat.KeepWithNext = true;
        heading2.ParagraphFormat.OutlineLevel = OutlineLevel.Level2;

        var subhead = pdf.Styles.AddStyle(SubheadStyle, StyleNames.Normal);
        subhead.Font.Bold = true;
        subhead.ParagraphFormat.SpaceBefore = 4;
        subhead.ParagraphFormat.SpaceAfter = 2;
        subhead.ParagraphFormat.KeepWithNext = true;

        var note = pdf.Styles.AddStyle(NoteStyle, StyleNames.Normal);
        note.Font.Color = MutedColor;

        var item = pdf.Styles.AddStyle(ItemStyle, StyleNames.Normal);
        item.ParagraphFormat.LeftIndent = Unit.FromCentimeter(0.5);
        item.ParagraphFormat.FirstLineIndent = Unit.FromCentimeter(-0.35);
        item.ParagraphFormat.SpaceAfter = 1;
        item.ParagraphFormat.TabStops.AddTabStop(Unit.FromCentimeter(0.5));

        var toc1 = pdf.Styles.AddStyle(Toc1Style, StyleNames.Normal);
        toc1.Font.Bold = true;
        toc1.ParagraphFormat.SpaceBefore = 4;
        toc1.ParagraphFormat.SpaceAfter = 1;
        toc1.ParagraphFormat.TabStops.AddTabStop(Unit.FromCentimeter(17), TabAlignment.Right, TabLeader.Dots);

        var toc2 = pdf.Styles.AddStyle(Toc2Style, StyleNames.Normal);
        toc2.ParagraphFormat.LeftIndent = Unit.FromCentimeter(0.6);
        toc2.ParagraphFormat.SpaceAfter = 0;
        toc2.ParagraphFormat.TabStops.AddTabStop(Unit.FromCentimeter(17), TabAlignment.Right, TabLeader.Dots);
    }

    // Unique bookmark names; an object keeps the same name for its heading, the contents and the links to it.
    private sealed class Bookmarks
    {
        private readonly Dictionary<string, string> _objects = new(StringComparer.Ordinal);
        private int _count;

        public string Next() => "b" + (++_count).ToString(CultureInfo.InvariantCulture);

        public string Of(DependencyKind kind, string name)
        {
            var key = DatabaseDocument.DependencyKey(kind, name);
            if (!_objects.TryGetValue(key, out var bookmark))
            {
                bookmark = Next();
                _objects[key] = bookmark;
            }

            return bookmark;
        }

        public string? Find(DependencyKind kind, string name) =>
            _objects.TryGetValue(DatabaseDocument.DependencyKey(kind, name), out var bookmark) ? bookmark : null;
    }
}
