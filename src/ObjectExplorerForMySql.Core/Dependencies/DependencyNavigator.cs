using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Dependencies;

/// <summary>Connects the dependency view to the explorer tree (US-1303).</summary>
public static class DependencyNavigator
{
    /// <summary>Returns the object of <paramref name="node"/> when View Dependencies applies to it: a table, view or routine.</summary>
    public static SchemaObject? SubjectOf(TreeNodeViewModel? node) => node switch
    {
        TableNodeViewModel table => new SchemaObject(DependencyKind.Table, table.Table.Database, table.Table.Name),
        ViewNodeViewModel view => new SchemaObject(DependencyKind.View, view.View.Database, view.View.Name),
        RoutineNodeViewModel routine => new SchemaObject(
            routine.Routine.Type == RoutineType.Function ? DependencyKind.Function : DependencyKind.Procedure,
            routine.Routine.Database,
            routine.Routine.Name),
        _ => null,
    };

    /// <summary>
    /// Expands the tree of <paramref name="server"/> down to <paramref name="target"/> and selects it. Returns the
    /// node, or null when it is not found (for example dropped since the dependencies were read).
    /// </summary>
    public static async Task<TreeNodeViewModel?> SelectAsync(ServerNodeViewModel server, SchemaObject target, ExplorerTexts texts)
    {
        if (server is null)
        {
            throw new ArgumentNullException(nameof(server));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }

        if (await OpenAsync(server) is not { } serverChildren
            || Find(serverChildren, texts.Databases) is not { } databases
            || await OpenAsync(databases) is not { } databaseNodes)
        {
            return null;
        }

        var database = FindDatabase(databaseNodes, target.Database);
        if (database is null && Find(databaseNodes, texts.SystemDatabases) is { } system && await OpenAsync(system) is { } systemNodes)
        {
            database = FindDatabase(systemNodes, target.Database);
        }

        if (database is null || await OpenAsync(database) is not { } folders)
        {
            return null;
        }

        TreeNodeViewModel? node;
        if (target.Kind == DependencyKind.Trigger)
        {
            node = Find(folders, texts.Tables) is { } tables && await OpenAsync(tables) is { } tableNodes
                && Find(tableNodes, target.Table) is { } table && await OpenAsync(table) is { } tableFolders
                && Find(tableFolders, texts.Triggers) is { } triggers && await OpenAsync(triggers) is { } triggerNodes
                ? triggerNodes.FirstOrDefault(t => t is ObjectNodeViewModel { Item: TriggerInfo info } && Same(info.Name, target.Name))
                : null;
        }
        else
        {
            var folderName = target.Kind switch
            {
                DependencyKind.Table => texts.Tables,
                DependencyKind.View => texts.Views,
                DependencyKind.Procedure => texts.StoredProcedures,
                DependencyKind.Function => texts.Functions,
                _ => texts.Events,
            };
            node = Find(folders, folderName) is { } folder && await OpenAsync(folder) is { } items ? Find(items, target.Name) : null;
        }

        if (node is not null)
        {
            node.IsSelected = true;
        }

        return node;
    }

    // Expands the node and waits for its children; null when loading failed.
    private static async Task<IReadOnlyList<TreeNodeViewModel>?> OpenAsync(TreeNodeViewModel node)
    {
        node.IsExpanded = true;
        await node.EnsureLoadedAsync();
        return node.LoadState == LoadState.Loaded ? node.Children.ToList() : null;
    }

    private static TreeNodeViewModel? Find(IEnumerable<TreeNodeViewModel> nodes, string? key)
    {
        var list = nodes as IList<TreeNodeViewModel> ?? nodes.ToList();
        return list.FirstOrDefault(n => string.Equals(n.Key, key, StringComparison.Ordinal))
            ?? list.FirstOrDefault(n => Same(n.Key, key));
    }

    private static DatabaseNodeViewModel? FindDatabase(IEnumerable<TreeNodeViewModel> nodes, string name) =>
        Find(nodes.OfType<DatabaseNodeViewModel>(), name) as DatabaseNodeViewModel;

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
