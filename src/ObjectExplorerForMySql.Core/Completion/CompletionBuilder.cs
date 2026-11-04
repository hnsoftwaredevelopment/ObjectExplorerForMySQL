using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Completion;

/// <summary>One line of the completion list.</summary>
/// <param name="Display">The name shown.</param>
/// <param name="Insert">The text inserted, quoted when needed.</param>
/// <param name="Kind">The kind, for the icon and the sorting.</param>
/// <param name="Description">The tooltip: column type, comment or the table of a column.</param>
public sealed record CompletionEntry(string Display, string Insert, CompletionKind Kind, string? Description = null);

/// <summary>Builds the completion list from the context at the caret and the cached metadata (US-1401).</summary>
public static class CompletionBuilder
{
    /// <summary>
    /// Returns the entries for <paramref name="context"/>. Without <paramref name="definition"/> (no connection, or no
    /// password known yet: the list never asks for one) only keywords are offered. Metadata that cannot be read is
    /// left out.
    /// </summary>
    public static async Task<IReadOnlyList<CompletionEntry>> BuildAsync(
        SqlCompletionContext context,
        SchemaCache cache,
        ConnectionDefinition? definition,
        string? database,
        DropFormatOptions options,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (cache is null)
        {
            throw new ArgumentNullException(nameof(cache));
        }

        options ??= DropFormatOptions.Default;
        string Quote(string name) => context.InBackticks ? MySqlIdentifier.Quote(name) : options.QuoteForSql(name);

        var result = new List<CompletionEntry>();
        if (context.Situation == CompletionSituation.None)
        {
            return result;
        }

        if (context.Situation == CompletionSituation.Members)
        {
            if (definition is not null)
            {
                result.AddRange(await MembersAsync(context, cache, definition, database, Quote, cancellationToken).ConfigureAwait(false));
            }

            return result;
        }

        if (!context.InBackticks)
        {
            result.AddRange(MySqlKeywords.All.Select(k => k.ToUpperInvariant()).Distinct().Select(k => new CompletionEntry(k, k, CompletionKind.Keyword)));
        }

        if (definition is null)
        {
            return Sorted(result);
        }

        var databases = await TryAsync(() => cache.GetDatabasesAsync(definition, cancellationToken)).ConfigureAwait(false) ?? [];
        result.AddRange(databases.Select(d => new CompletionEntry(d, Quote(d), CompletionKind.Database)));
        if (database is not null && await TryAsync(() => cache.GetDatabaseAsync(definition, database, cancellationToken)).ConfigureAwait(false) is { } schema)
        {
            result.AddRange(schema.Objects.Select(o => new CompletionEntry(o.Name, Quote(o.Name), o.Kind, o.Description)));
        }

        // Columns of the tables in the statement.
        foreach (var table in context.Tables)
        {
            var tableDatabase = table.Database ?? database;
            if (tableDatabase is null)
            {
                continue;
            }

            var columns = await TryAsync(() => cache.GetColumnsAsync(definition, tableDatabase, table.Name, cancellationToken)).ConfigureAwait(false) ?? [];
            result.AddRange(columns.Select(c => new CompletionEntry(c.Name, Quote(c.Name), CompletionKind.Column, $"{table.Name}.{c.Name} {c.ColumnType}")));
        }

        return Sorted(result);
    }

    // After "x.": an alias or table gives its columns, a database gives its objects; "db.table." gives columns.
    private static async Task<IEnumerable<CompletionEntry>> MembersAsync(
        SqlCompletionContext context, SchemaCache cache, ConnectionDefinition definition, string? database, Func<string, string> quote, CancellationToken cancellationToken)
    {
        string? tableDatabase;
        string tableName;
        if (context.Qualifier.Count == 2)
        {
            tableDatabase = context.Qualifier[0];
            tableName = context.Qualifier[1];
        }
        else
        {
            var name = context.Qualifier[0];
            var reference = context.Tables.FirstOrDefault(t => Same(t.Alias, name)) ?? context.Tables.FirstOrDefault(t => t.Alias is null && Same(t.Name, name));
            if (reference is not null)
            {
                tableDatabase = reference.Database ?? database;
                tableName = reference.Name;
            }
            else
            {
                var schema = database is null ? null : await TryAsync(() => cache.GetDatabaseAsync(definition, database, cancellationToken)).ConfigureAwait(false);
                if (schema?.Objects.Any(o => o.Kind is CompletionKind.Table or CompletionKind.View && Same(o.Name, name)) == true)
                {
                    tableDatabase = database;
                    tableName = name;
                }
                else
                {
                    // A database: its tables, views and routines.
                    var databases = await TryAsync(() => cache.GetDatabasesAsync(definition, cancellationToken)).ConfigureAwait(false) ?? [];
                    var match = databases.FirstOrDefault(d => Same(d, name));
                    if (match is null || await TryAsync(() => cache.GetDatabaseAsync(definition, match, cancellationToken)).ConfigureAwait(false) is not { } other)
                    {
                        return [];
                    }

                    return Sorted(other.Objects.Select(o => new CompletionEntry(o.Name, quote(o.Name), o.Kind, o.Description)));
                }
            }
        }

        if (tableDatabase is null)
        {
            return [];
        }

        var columns = await TryAsync(() => cache.GetColumnsAsync(definition, tableDatabase, tableName, cancellationToken)).ConfigureAwait(false) ?? [];
        return columns.Select(c => new CompletionEntry(c.Name, quote(c.Name), CompletionKind.Column, c.ColumnType));
    }

    // Columns first after a dot keep their table order; the general list is sorted by name.
    private static List<CompletionEntry> Sorted(IEnumerable<CompletionEntry> entries) =>
        entries
            .GroupBy(e => (e.Kind, Name: e.Display.ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(e => e.Display, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Kind)
            .ToList();

    private static async Task<T?> TryAsync<T>(Func<Task<T>> read)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Logged by the cache; the list shows what it has.
            return null;
        }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
