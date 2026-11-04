using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.Dependencies;

/// <summary>Kind of an object in the dependency view (US-1303).</summary>
public enum DependencyKind
{
    /// <summary>Base table.</summary>
    Table,

    /// <summary>View.</summary>
    View,

    /// <summary>Stored procedure.</summary>
    Procedure,

    /// <summary>Stored function.</summary>
    Function,

    /// <summary>Trigger; <see cref="SchemaObject.Table"/> is its table.</summary>
    Trigger,

    /// <summary>Event.</summary>
    Event,
}

/// <summary>Why one object depends on another.</summary>
public enum DependencyReason
{
    /// <summary>A view selects from the object (INFORMATION_SCHEMA.VIEW_TABLE_USAGE or VIEW_ROUTINE_USAGE).</summary>
    View,

    /// <summary>A foreign key refers to the table.</summary>
    ForeignKey,

    /// <summary>The trigger belongs to the table.</summary>
    Trigger,

    /// <summary>The source of the object contains the name; a text match, so it can be wrong.</summary>
    SourceText,
}

/// <summary>An object of a database. Names compare without case, as MySQL on Windows does.</summary>
public sealed record SchemaObject(DependencyKind Kind, string Database, string Name, string? Table = null)
{
    /// <summary>Returns true when <paramref name="other"/> is the same object.</summary>
    public bool Is(SchemaObject? other) =>
        other is not null && other.Kind == Kind && SameName(other.Database, Database) && SameName(other.Name, Name);

    /// <summary>Returns true when the object is the table or view <paramref name="database"/>.<paramref name="name"/>.</summary>
    public bool IsTableOrView(string database, string name) =>
        Kind is DependencyKind.Table or DependencyKind.View && SameName(Database, database) && SameName(Name, name);

    internal static bool SameName(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One line in the dependency view: the other object and why.</summary>
public sealed record Dependency(SchemaObject Object, DependencyReason Reason, string? Detail = null);

/// <summary>What uses an object and what it uses.</summary>
public sealed record DependencyResult(
    SchemaObject Subject,
    IReadOnlyList<Dependency> UsedBy,
    IReadOnlyList<Dependency> Uses,
    bool SourcesIncomplete);

/// <summary>An object of the database with its source, as far as the account may read it.</summary>
/// <param name="Object">The object.</param>
/// <param name="Source">The source (view definition, routine body, trigger or event statement), or null.</param>
public sealed record CatalogObject(SchemaObject Object, string? Source);

/// <summary>A foreign key: <see cref="Table"/> refers to <see cref="ReferencedTable"/>.</summary>
public sealed record ForeignKeyLink(string Database, string Table, string Constraint, string ReferencedDatabase, string ReferencedTable);

/// <summary>A row of VIEW_TABLE_USAGE or VIEW_ROUTINE_USAGE: the view uses <see cref="Used"/>.</summary>
public sealed record ViewUsage(string ViewDatabase, string View, SchemaObject Used);

/// <summary>
/// What the server tells about one database: its objects with their source, the foreign keys from and to it, and the
/// view usage (MySQL 8.0.13+; null on MariaDB and older MySQL, then views are matched on their text).
/// </summary>
public sealed record DependencyCatalog(
    IReadOnlyList<CatalogObject> Objects,
    IReadOnlyList<ForeignKeyLink> ForeignKeys,
    IReadOnlyList<ViewUsage>? ViewUsage);
