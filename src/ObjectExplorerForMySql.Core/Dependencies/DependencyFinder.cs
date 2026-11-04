using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectExplorerForMySql.Core.Dependencies;

/// <summary>
/// Works out what uses an object and what it uses, from a <see cref="DependencyCatalog"/> (US-1303). Views and
/// foreign keys come from INFORMATION_SCHEMA; routines, triggers and events (and views on MariaDB) are matched on
/// their source text, so those lines can be wrong and are marked as a text match.
/// </summary>
public static class DependencyFinder
{
    /// <summary>Returns the dependencies of <paramref name="subject"/>.</summary>
    public static DependencyResult Find(DependencyCatalog catalog, SchemaObject subject)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (subject is null)
        {
            throw new ArgumentNullException(nameof(subject));
        }

        var incomplete = false;
        var usedBy = new List<Dependency>();
        var uses = new List<Dependency>();

        // Used by: views (structural), foreign keys of other tables, the triggers of a table.
        if (catalog.ViewUsage is { } usage)
        {
            usedBy.AddRange(usage
                .Where(u => IsSame(u.Used, subject))
                .Select(u => new Dependency(new SchemaObject(DependencyKind.View, u.ViewDatabase, u.View), DependencyReason.View)));
        }

        if (subject.Kind == DependencyKind.Table)
        {
            usedBy.AddRange(catalog.ForeignKeys
                .Where(f => subject.IsTableOrView(f.ReferencedDatabase, f.ReferencedTable) && !subject.IsTableOrView(f.Database, f.Table))
                .Select(f => new Dependency(new SchemaObject(DependencyKind.Table, f.Database, f.Table), DependencyReason.ForeignKey, f.Constraint)));
            usedBy.AddRange(catalog.Objects
                .Where(o => o.Object.Kind == DependencyKind.Trigger && SchemaObject.SameName(o.Object.Database, subject.Database)
                    && SchemaObject.SameName(o.Object.Table, subject.Name))
                .Select(o => new Dependency(o.Object, DependencyReason.Trigger)));
        }

        // Used by: everything whose source names the object.
        foreach (var candidate in catalog.Objects)
        {
            var kind = candidate.Object.Kind;
            if (kind == DependencyKind.Table || (kind == DependencyKind.View && catalog.ViewUsage is not null)
                || candidate.Object.Is(subject) || usedBy.Any(d => d.Object.Is(candidate.Object)))
            {
                continue;
            }

            if (candidate.Source is null)
            {
                incomplete = true;
            }
            else if (SourceMatcher.Mentions(candidate.Source, subject.Name))
            {
                usedBy.Add(new Dependency(candidate.Object, DependencyReason.SourceText));
            }
        }

        // Uses.
        if (subject.Kind == DependencyKind.Table)
        {
            uses.AddRange(catalog.ForeignKeys
                .Where(f => subject.IsTableOrView(f.Database, f.Table) && !subject.IsTableOrView(f.ReferencedDatabase, f.ReferencedTable))
                .Select(f => new Dependency(new SchemaObject(DependencyKind.Table, f.ReferencedDatabase, f.ReferencedTable), DependencyReason.ForeignKey, f.Constraint)));
        }
        else if (subject.Kind == DependencyKind.View && catalog.ViewUsage is { } viewUsage)
        {
            uses.AddRange(viewUsage
                .Where(u => SchemaObject.SameName(u.ViewDatabase, subject.Database) && SchemaObject.SameName(u.View, subject.Name))
                .Select(u => new Dependency(u.Used, DependencyReason.View)));
        }
        else
        {
            var source = catalog.Objects.FirstOrDefault(o => o.Object.Is(subject))?.Source;
            if (source is null)
            {
                incomplete = true;
            }
            else
            {
                uses.AddRange(catalog.Objects
                    .Where(o => o.Object.Kind is not (DependencyKind.Trigger or DependencyKind.Event) && !o.Object.Is(subject)
                        && SourceMatcher.Mentions(source, o.Object.Name))
                    .Select(o => new Dependency(o.Object, DependencyReason.SourceText)));
            }
        }

        return new DependencyResult(subject, Sorted(usedBy), Sorted(uses), incomplete);
    }

    // VIEW_TABLE_USAGE lists views as tables when TABLES has no row for them; compare tables and views by name only.
    private static bool IsSame(SchemaObject used, SchemaObject subject) =>
        subject.Kind is DependencyKind.Table or DependencyKind.View
            ? used.IsTableOrView(subject.Database, subject.Name)
            : used.Is(subject);

    private static List<Dependency> Sorted(IEnumerable<Dependency> dependencies) =>
        dependencies
            .GroupBy(d => (d.Object.Kind, Database: d.Object.Database.ToUpperInvariant(), Name: d.Object.Name.ToUpperInvariant(), d.Reason, d.Detail))
            .Select(g => g.First())
            .OrderBy(d => d.Object.Kind)
            .ThenBy(d => d.Object.Database, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Object.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Finds an object name in source text as a whole word (US-1303).</summary>
public static class SourceMatcher
{
    /// <summary>
    /// Returns true when <paramref name="source"/> contains <paramref name="name"/> without a letter, digit, <c>_</c>
    /// or <c>$</c> right before or after it, ignoring case. So <c>film</c> matches <c>`film`</c> and <c>sakila.film</c>,
    /// but not <c>film_actor</c>.
    /// </summary>
    public static bool Mentions(string? source, string? name)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        var index = source!.IndexOf(name!, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var end = index + name!.Length;
            if ((index == 0 || !IsNameChar(source[index - 1])) && (end == source.Length || !IsNameChar(source[end])))
            {
                return true;
            }

            index = source.IndexOf(name, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
