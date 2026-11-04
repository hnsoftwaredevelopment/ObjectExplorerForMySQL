using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Dependencies;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Documentation;

/// <summary>What the documentation contains (US-1201).</summary>
public sealed record DocumentOptions
{
    /// <summary>Gets a value indicating whether tables are included, with keys, indexes, checks and triggers.</summary>
    public bool IncludeTables { get; init; } = true;

    /// <summary>Gets a value indicating whether views are included.</summary>
    public bool IncludeViews { get; init; } = true;

    /// <summary>Gets a value indicating whether procedures and functions are included.</summary>
    public bool IncludeRoutines { get; init; } = true;

    /// <summary>Gets a value indicating whether events are included.</summary>
    public bool IncludeEvents { get; init; } = true;

    /// <summary>Gets a value indicating whether each table, view and routine lists what uses it and what it uses (as US-1303).</summary>
    public bool IncludeDependencies { get; init; } = true;

    /// <summary>Gets a value indicating whether the dependencies of each object also get a diagram (Mermaid in Markdown).</summary>
    public bool IncludeDiagrams { get; init; } = true;

    /// <summary>Gets the name of the connection, shown at the top.</summary>
    public string? ConnectionLabel { get; init; }

    /// <summary>Gets the server version, shown at the top; also decides the MariaDB queries.</summary>
    public string? ServerVersion { get; init; }

    /// <summary>Gets the generation time, shown at the top.</summary>
    public DateTime? GeneratedAt { get; init; }
}

/// <summary>A table with everything that belongs to it.</summary>
public sealed record TableDocument(
    TableInfo Table,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<KeyInfo> Keys,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<CheckConstraintInfo> Checks,
    IReadOnlyList<TriggerInfo> Triggers);

/// <summary>A view with its columns.</summary>
public sealed record ViewDocument(TableInfo View, IReadOnlyList<ColumnInfo> Columns);

/// <summary>A procedure or function with its parameters.</summary>
public sealed record RoutineDocument(RoutineInfo Routine, IReadOnlyList<ParameterInfo> Parameters);

/// <summary>The content of the documentation, independent of the file format (Markdown now, PDF in US-1202).</summary>
/// <param name="Database">The database.</param>
/// <param name="CharacterSet">The default character set.</param>
/// <param name="Collation">The default collation.</param>
/// <param name="Options">What is included.</param>
/// <param name="Tables">The tables.</param>
/// <param name="Views">The views.</param>
/// <param name="Procedures">The procedures.</param>
/// <param name="Functions">The functions.</param>
/// <param name="Events">The events.</param>
/// <param name="Dependencies">Per object (see <see cref="DependencyKey"/>) what uses it and what it uses; null when not included.</param>
/// <param name="DependencyError">Why the dependencies could not be read, or null.</param>
public sealed record DatabaseDocument(
    string Database,
    string? CharacterSet,
    string? Collation,
    DocumentOptions Options,
    IReadOnlyList<TableDocument> Tables,
    IReadOnlyList<ViewDocument> Views,
    IReadOnlyList<RoutineDocument> Procedures,
    IReadOnlyList<RoutineDocument> Functions,
    IReadOnlyList<EventInfo> Events,
    IReadOnlyDictionary<string, DependencyResult>? Dependencies = null,
    string? DependencyError = null)
{
    /// <summary>Returns the key of an object in <see cref="Dependencies"/>.</summary>
    public static string DependencyKey(DependencyKind kind, string name) => kind + ":" + name.ToUpperInvariant();

    /// <summary>Returns the dependencies of an object, or null.</summary>
    public DependencyResult? DependenciesOf(DependencyKind kind, string name) =>
        Dependencies is not null && Dependencies.TryGetValue(DependencyKey(kind, name), out var result) ? result : null;
}

/// <summary>Part being read.</summary>
public enum DocumentStep
{
    /// <summary>Listing the objects.</summary>
    Listing,

    /// <summary>Tables.</summary>
    Tables,

    /// <summary>Views.</summary>
    Views,

    /// <summary>Procedures and functions.</summary>
    Routines,

    /// <summary>Dependencies between the objects.</summary>
    Dependencies,
}

/// <summary>Progress: the object being read, <paramref name="Current"/> of <paramref name="Total"/>.</summary>
public sealed record DocumentProgress(DocumentStep Step, string Name, int Current, int Total);

/// <summary>Reads everything the documentation of a database needs (US-1201).</summary>
public sealed class DatabaseDocumenter
{
    private readonly IMetadataProvider _metadata;
    private readonly IDependencySource? _dependencySource;

    /// <summary>Initializes a new instance of the <see cref="DatabaseDocumenter"/> class.</summary>
    /// <param name="metadata">Reads the objects.</param>
    /// <param name="dependencySource">Reads the dependencies; without it the document has none.</param>
    public DatabaseDocumenter(IMetadataProvider metadata, IDependencySource? dependencySource = null)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _dependencySource = dependencySource;
    }

    /// <summary>Reads the database; objects are sorted by name. Cancelling throws <see cref="OperationCanceledException"/>.</summary>
    public async Task<DatabaseDocument> ReadAsync(
        ConnectionDefinition definition, string database, DocumentOptions options, IProgress<DocumentProgress>? progress, CancellationToken cancellationToken)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        options ??= new DocumentOptions();
        var capabilities = ServerCapabilities.FromVersion(options.ServerVersion);
        var isMariaDb = capabilities.IsMariaDb;
        progress?.Report(new DocumentProgress(DocumentStep.Listing, database, 0, 0));

        var info = (await _metadata.GetDatabasesAsync(definition, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => string.Equals(d.Name, database, StringComparison.OrdinalIgnoreCase));
        var tables = options.IncludeTables ? Sorted(await _metadata.GetTablesAsync(definition, database, cancellationToken).ConfigureAwait(false), t => t.Name) : [];
        var views = options.IncludeViews ? Sorted(await _metadata.GetViewsAsync(definition, database, cancellationToken).ConfigureAwait(false), v => v.Name) : [];
        var procedures = options.IncludeRoutines
            ? Sorted(await _metadata.GetRoutinesAsync(definition, database, RoutineType.Procedure, cancellationToken).ConfigureAwait(false), r => r.Name) : [];
        var functions = options.IncludeRoutines
            ? Sorted(await _metadata.GetRoutinesAsync(definition, database, RoutineType.Function, cancellationToken).ConfigureAwait(false), r => r.Name) : [];
        var events = options.IncludeEvents ? Sorted(await _metadata.GetEventsAsync(definition, database, cancellationToken).ConfigureAwait(false), e => e.Name) : [];

        var total = tables.Count + views.Count + procedures.Count + functions.Count;
        var current = 0;

        var tableDocuments = new List<TableDocument>();
        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DocumentProgress(DocumentStep.Tables, table.Name, ++current, total));
            tableDocuments.Add(new TableDocument(
                table,
                await _metadata.GetColumnsAsync(definition, database, table.Name, cancellationToken).ConfigureAwait(false),
                await _metadata.GetKeysAsync(definition, database, table.Name, cancellationToken).ConfigureAwait(false),
                await _metadata.GetIndexesAsync(definition, database, table.Name, isMariaDb, cancellationToken).ConfigureAwait(false),
                capabilities.SupportsCheckConstraints
                    ? await _metadata.GetCheckConstraintsAsync(definition, database, table.Name, isMariaDb, cancellationToken).ConfigureAwait(false)
                    : [],
                await _metadata.GetTriggersAsync(definition, database, table.Name, cancellationToken).ConfigureAwait(false)));
        }

        var viewDocuments = new List<ViewDocument>();
        foreach (var view in views)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DocumentProgress(DocumentStep.Views, view.Name, ++current, total));
            viewDocuments.Add(new ViewDocument(view, await _metadata.GetColumnsAsync(definition, database, view.Name, cancellationToken).ConfigureAwait(false)));
        }

        var procedureDocuments = new List<RoutineDocument>();
        var functionDocuments = new List<RoutineDocument>();
        foreach (var routine in procedures.Concat(functions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DocumentProgress(DocumentStep.Routines, routine.Name, ++current, total));
            var document = new RoutineDocument(routine, await _metadata.GetParametersAsync(definition, routine, cancellationToken).ConfigureAwait(false));
            (routine.Type == RoutineType.Function ? functionDocuments : procedureDocuments).Add(document);
        }

        // Dependencies (US-1303) of every documented table, view and routine, from one read of the catalog.
        Dictionary<string, DependencyResult>? dependencies = null;
        string? dependencyError = null;
        if (options.IncludeDependencies && _dependencySource is not null)
        {
            progress?.Report(new DocumentProgress(DocumentStep.Dependencies, database, current, total));
            try
            {
                var catalog = await _dependencySource.LoadAsync(definition, database, options.ServerVersion, cancellationToken).ConfigureAwait(false);
                var subjects = tables.Select(t => new SchemaObject(DependencyKind.Table, database, t.Name))
                    .Concat(views.Select(v => new SchemaObject(DependencyKind.View, database, v.Name)))
                    .Concat(procedures.Select(r => new SchemaObject(DependencyKind.Procedure, database, r.Name)))
                    .Concat(functions.Select(r => new SchemaObject(DependencyKind.Function, database, r.Name)));
                dependencies = subjects.ToDictionary(s => DatabaseDocument.DependencyKey(s.Kind, s.Name), s => DependencyFinder.Find(catalog, s));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The rest of the document is still useful; it says why the dependencies are missing.
                dependencyError = Security.SensitiveDataMasker.MaskText(ex.Message);
            }
        }

        return new DatabaseDocument(
            info?.Name ?? database, info?.CharacterSet, info?.Collation, options, tableDocuments, viewDocuments, procedureDocuments, functionDocuments, events,
            dependencies, dependencyError);
    }

    private static List<T> Sorted<T>(IEnumerable<T> items, Func<T, string> name) => items.OrderBy(name, StringComparer.OrdinalIgnoreCase).ToList();
}
