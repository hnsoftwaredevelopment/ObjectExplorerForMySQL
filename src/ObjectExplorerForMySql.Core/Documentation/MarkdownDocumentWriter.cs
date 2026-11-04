using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ObjectExplorerForMySql.Core.Dependencies;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Documentation;

/// <summary>
/// Writes a <see cref="DatabaseDocument"/> as Markdown (US-1201): a header, a table of contents with links, and a
/// section per object. The text is English, like the scripts of Script Database. Links use the heading anchors of
/// GitHub and the Visual Studio Markdown preview; for names of lowercase letters, digits and <c>_</c> they also
/// work in Obsidian.
/// </summary>
public static class MarkdownDocumentWriter
{
    /// <summary>Writes <paramref name="document"/> to <paramref name="writer"/>.</summary>
    public static void Write(DatabaseDocument document, TextWriter writer)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        var anchors = new Anchors();
        var options = document.Options;
        writer.WriteLine($"# Database {document.Database}");
        writer.WriteLine();
        var server = string.Join(" ", new[] { options.ConnectionLabel, options.ServerVersion is null ? null : $"({options.ServerVersion})" }.Where(p => p is not null));
        if (server.Length > 0)
        {
            writer.WriteLine($"Server: {Text(server)}  ");
        }

        if (document.CharacterSet is not null)
        {
            writer.WriteLine($"Character set: {document.CharacterSet}, collation: {document.Collation}  ");
        }

        var generated = options.GeneratedAt is { } at ? at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " : string.Empty;
        writer.WriteLine($"Generated {generated}by Object Explorer for MySQL");
        writer.WriteLine();
        if (document.DependencyError is { } error)
        {
            Line(writer, "The dependencies could not be read: " + error);
        }
        else if (document.Dependencies?.Values.Any(d => d.SourcesIncomplete) == true)
        {
            Line(writer, "Some sources could not be read with this account, so dependencies found by a text match can be missing.");
        }

        // The anchors of the table of contents, in the order of the headings: GitHub numbers a repeated heading.
        anchors.Next("Database " + document.Database);
        anchors.Next("Contents");
        var sections = new List<(string Title, string Anchor, int Count, List<(string Name, string Anchor)> Items)>();
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string title, IEnumerable<string> names, DependencyKind? kind = null, int? count = null)
        {
            var anchor = anchors.Next(title);
            var items = names.Select(n => (n, anchors.Next(n))).ToList();
            if (kind is { } k)
            {
                foreach (var (name, itemAnchor) in items)
                {
                    links[DatabaseDocument.DependencyKey(k, name)] = itemAnchor;
                }
            }

            sections.Add((title, anchor, count ?? items.Count, items));
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

        writer.WriteLine("## Contents");
        writer.WriteLine();
        foreach (var (title, anchor, count, items) in sections)
        {
            writer.WriteLine($"- [{title}](#{anchor}) ({count})");
            foreach (var (name, itemAnchor) in items)
            {
                writer.WriteLine($"  - [{Text(name)}](#{itemAnchor})");
            }
        }

        writer.WriteLine();

        if (options.IncludeTables)
        {
            Section(writer, "Tables");
            foreach (var table in document.Tables)
            {
                WriteTable(writer, table);
                WriteDependencies(writer, document, DependencyKind.Table, table.Table.Name, links);
            }
        }

        if (options.IncludeViews)
        {
            Section(writer, "Views");
            foreach (var view in document.Views)
            {
                WriteView(writer, view);
                WriteDependencies(writer, document, DependencyKind.View, view.View.Name, links);
            }
        }

        if (options.IncludeRoutines)
        {
            Section(writer, "Procedures");
            foreach (var routine in document.Procedures)
            {
                WriteRoutine(writer, routine);
                WriteDependencies(writer, document, DependencyKind.Procedure, routine.Routine.Name, links);
            }

            Section(writer, "Functions");
            foreach (var routine in document.Functions)
            {
                WriteRoutine(writer, routine);
                WriteDependencies(writer, document, DependencyKind.Function, routine.Routine.Name, links);
            }
        }

        if (options.IncludeEvents)
        {
            Section(writer, "Events");
            if (document.Events.Count == 0)
            {
                writer.WriteLine("None.");
                writer.WriteLine();
            }
            else
            {
                Grid(writer, ["Event", "Schedule", "Status", "On completion", "Comment"], document.Events.Select(e => new[]
                {
                    e.Name, e.Schedule, e.Status, e.OnCompletion, e.Comment,
                }));
            }
        }
    }

    /// <summary>
    /// Returns the anchor GitHub makes of a heading: lower case, spaces to <c>-</c>, other punctuation removed.
    /// </summary>
    public static string Slug(string heading)
    {
        var builder = new StringBuilder();
        foreach (var c in (heading ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
            else if (c == ' ')
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }

    private static void Section(TextWriter writer, string title)
    {
        writer.WriteLine($"## {title}");
        writer.WriteLine();
    }

    private static void WriteTable(TextWriter writer, TableDocument table)
    {
        var info = table.Table;
        writer.WriteLine($"### {Text(info.Name)}");
        writer.WriteLine();
        Line(writer, info.Comment);
        Line(writer, string.Join(", ", new[] { info.Engine, info.Collation }.Where(p => !string.IsNullOrEmpty(p))));

        var foreignKeyColumns = new HashSet<string>(table.Keys.Where(k => k.Type == KeyType.Foreign).SelectMany(k => k.Columns), StringComparer.OrdinalIgnoreCase);
        Grid(writer, ["Column", "Type", "Null", "Default", "Key", "Comment"], table.Columns.Select(c => new[]
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
            writer.WriteLine($"**Primary key:** {Text(string.Join(", ", primary.Columns))}");
            writer.WriteLine();
        }

        var unique = table.Keys.Where(k => k.Type == KeyType.Unique).ToList();
        if (unique.Count > 0)
        {
            writer.WriteLine("**Unique keys**");
            writer.WriteLine();
            Grid(writer, ["Name", "Columns"], unique.Select(k => new[] { k.Name, string.Join(", ", k.Columns) }));
        }

        var foreign = table.Keys.Where(k => k.Type == KeyType.Foreign).ToList();
        if (foreign.Count > 0)
        {
            writer.WriteLine("**Foreign keys**");
            writer.WriteLine();
            Grid(writer, ["Name", "Columns", "References", "On update", "On delete"], foreign.Select(k => new[]
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
            writer.WriteLine("**Indexes**");
            writer.WriteLine();
            Grid(writer, ["Name", "Columns", "Unique", "Type"], table.Indexes.Select(i => new[]
            {
                i.Name, string.Join(", ", i.Columns), i.IsUnique ? "YES" : "NO", i.IndexType + (i.IsVisible ? string.Empty : ", invisible"),
            }));
        }

        if (table.Checks.Count > 0)
        {
            writer.WriteLine("**Check constraints**");
            writer.WriteLine();
            Grid(writer, ["Name", "Clause", "Enforced"], table.Checks.Select(c => new[] { c.Name, c.Clause, c.Enforced ? "YES" : "NO" }));
        }

        if (table.Triggers.Count > 0)
        {
            writer.WriteLine("**Triggers**");
            writer.WriteLine();
            Grid(writer, ["Name", "Timing", "Event"], table.Triggers.Select(t => new[] { t.Name, t.Timing, t.Manipulation }));
        }
    }

    private static void WriteView(TextWriter writer, ViewDocument view)
    {
        writer.WriteLine($"### {Text(view.View.Name)}");
        writer.WriteLine();
        Line(writer, view.View.Comment is { Length: > 0 } comment && !string.Equals(comment, "VIEW", StringComparison.Ordinal) ? comment : null);
        Grid(writer, ["Column", "Type", "Null", "Comment"], view.Columns.Select(c => new[] { c.Name, c.ColumnType, c.IsNullable ? "YES" : "NO", c.Comment }));
    }

    private static void WriteRoutine(TextWriter writer, RoutineDocument document)
    {
        var routine = document.Routine;
        writer.WriteLine($"### {Text(routine.Name)}");
        writer.WriteLine();
        Line(writer, routine.Comment);
        if (routine.Type == RoutineType.Function && routine.ReturnType is { Length: > 0 } returns)
        {
            writer.WriteLine($"**Returns:** {Text(returns)}  ");
        }

        var details = new[]
        {
            routine.IsDeterministic ? "deterministic" : "not deterministic",
            routine.SqlDataAccess?.ToLowerInvariant(),
            routine.SecurityType is { Length: > 0 } security ? "security " + security.ToLowerInvariant() : null,
        }.Where(p => !string.IsNullOrEmpty(p));
        writer.WriteLine(Text(string.Join(", ", details)));
        writer.WriteLine();

        var parameters = document.Parameters.OrderBy(p => p.Position).ToList();
        if (parameters.Count == 0)
        {
            writer.WriteLine("No parameters.");
            writer.WriteLine();
        }
        else if (routine.Type == RoutineType.Procedure)
        {
            Grid(writer, ["Parameter", "Mode", "Type"], parameters.Select(p => new[] { p.Name, p.Mode.ToString().ToUpperInvariant(), p.DataType }));
        }
        else
        {
            Grid(writer, ["Parameter", "Type"], parameters.Select(p => new[] { p.Name, p.DataType }));
        }
    }

    // What uses the object and what it uses (US-1303), as lists with links and, when chosen, a Mermaid diagram:
    // users on the left, the object in the middle, what it uses on the right. Text matches are dashed.
    private static void WriteDependencies(TextWriter writer, DatabaseDocument document, DependencyKind kind, string name, IReadOnlyDictionary<string, string> links)
    {
        if (document.DependenciesOf(kind, name) is not { } result)
        {
            return;
        }

        writer.WriteLine("**Dependencies**");
        writer.WriteLine();
        if (result.UsedBy.Count == 0 && result.Uses.Count == 0)
        {
            writer.WriteLine("None found.");
            writer.WriteLine();
            return;
        }

        DependencyList(writer, "Used by", result.UsedBy, document.Database, links);
        DependencyList(writer, "Uses", result.Uses, document.Database, links);
        if (document.Options.IncludeDiagrams)
        {
            Diagram(writer, result, document.Database);
        }
    }

    private static void DependencyList(TextWriter writer, string title, IReadOnlyList<Dependency> dependencies, string database, IReadOnlyDictionary<string, string> links)
    {
        if (dependencies.Count == 0)
        {
            return;
        }

        writer.WriteLine(title + ":");
        writer.WriteLine();
        foreach (var dependency in dependencies)
        {
            var target = dependency.Object;
            var label = Text(Label(target, database));
            var sameDatabase = string.Equals(target.Database, database, StringComparison.OrdinalIgnoreCase);
            var link = sameDatabase && links.TryGetValue(DatabaseDocument.DependencyKey(target.Kind, target.Name), out var anchor) ? $"[{label}](#{anchor})" : label;
            var note = string.Join(", ", new[] { KindText(target.Kind), Reason(dependency) }.Where(p => p.Length > 0));
            writer.WriteLine($"- {link} ({Text(note)})");
        }

        writer.WriteLine();
    }

    private static void Diagram(TextWriter writer, DependencyResult result, string database)
    {
        writer.WriteLine("```mermaid");
        writer.WriteLine("flowchart LR");
        writer.WriteLine($"  s[\"{Mermaid(result.Subject.Name)}<br/>{KindText(result.Subject.Kind)}\"]");
        var index = 0;
        foreach (var dependency in result.UsedBy)
        {
            var id = "u" + (index++).ToString(CultureInfo.InvariantCulture);
            writer.WriteLine($"  {id}[\"{Mermaid(Label(dependency.Object, database))}<br/>{KindText(dependency.Object.Kind)}\"] {Arrow(dependency)} s");
        }

        index = 0;
        foreach (var dependency in result.Uses)
        {
            var id = "d" + (index++).ToString(CultureInfo.InvariantCulture);
            writer.WriteLine($"  s {Arrow(dependency)} {id}[\"{Mermaid(Label(dependency.Object, database))}<br/>{KindText(dependency.Object.Kind)}\"]");
        }

        writer.WriteLine("  style s stroke-width:3px");
        writer.WriteLine("```");
        writer.WriteLine();
    }

    // Foreign keys carry their name; a text match is dashed.
    private static string Arrow(Dependency dependency) => dependency.Reason switch
    {
        DependencyReason.ForeignKey => $"-->|\"{Mermaid(dependency.Detail ?? string.Empty)}\"|",
        DependencyReason.SourceText => "-.->",
        _ => "-->",
    };

    internal static string Label(SchemaObject target, string database) =>
        string.Equals(target.Database, database, StringComparison.OrdinalIgnoreCase) ? target.Name : target.Database + "." + target.Name;

    internal static string KindText(DependencyKind kind) => kind switch
    {
        DependencyKind.Table => "table",
        DependencyKind.View => "view",
        DependencyKind.Procedure => "procedure",
        DependencyKind.Function => "function",
        DependencyKind.Trigger => "trigger",
        _ => "event",
    };

    internal static string Reason(Dependency dependency) => dependency.Reason switch
    {
        DependencyReason.ForeignKey => "foreign key " + dependency.Detail,
        DependencyReason.Trigger => "trigger on the table",
        DependencyReason.SourceText => "text match",
        _ => string.Empty,
    };

    // Text inside a quoted Mermaid label: quotes and angle brackets as entities.
    private static string Mermaid(string text) =>
        text.Replace("&", "#amp;").Replace("\"", "#quot;").Replace("<", "#lt;").Replace(">", "#gt;");

    private static void Line(TextWriter writer, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            writer.WriteLine(Text(text!));
            writer.WriteLine();
        }
    }

    private static void Grid(TextWriter writer, IReadOnlyList<string> headers, IEnumerable<string?[]> rows)
    {
        writer.WriteLine("| " + string.Join(" | ", headers) + " |");
        writer.WriteLine("|" + string.Concat(headers.Select(_ => "---|")));
        foreach (var row in rows)
        {
            writer.WriteLine("| " + string.Join(" | ", row.Select(Cell)) + " |");
        }

        writer.WriteLine();
    }

    // A pipe ends a cell and a line break ends the table.
    private static string Cell(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : Text(value!).Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

    // Characters that Markdown would read as formatting. An underscore inside a name is no emphasis in GitHub
    // Markdown or Obsidian, so names such as film_actor stay readable in the file.
    private static string Text(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '`' or '*' or '[' or ']' or '<')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    // Anchors as GitHub makes them: a repeated heading gets -1, -2 and so on.
    private sealed class Anchors
    {
        private readonly Dictionary<string, int> _used = new(StringComparer.Ordinal);

        public string Next(string heading)
        {
            var slug = Slug(heading);
            if (_used.TryGetValue(slug, out var count))
            {
                _used[slug] = count + 1;
                return slug + "-" + count.ToString(CultureInfo.InvariantCulture);
            }

            _used[slug] = 1;
            return slug;
        }
    }
}
