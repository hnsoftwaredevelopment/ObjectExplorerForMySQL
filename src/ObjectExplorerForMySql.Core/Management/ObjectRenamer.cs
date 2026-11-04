using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>Renames the object of a tree node and selects it again under its new name (US-0902).</summary>
public sealed class ObjectRenamer
{
    private readonly ISchemaCommandRunner _runner;
    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="ObjectRenamer"/> class.</summary>
    public ObjectRenamer(ISchemaCommandRunner runner, IExtensionLogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns what Rename changes for <paramref name="node"/>, or null when it cannot be renamed: anything other than
    /// a table, view or table column, anything in a system database, and a column on a server without RENAME COLUMN.
    /// </summary>
    public static RenameTarget? TargetOf(TreeNodeViewModel? node)
    {
        if (node?.CreateRenameTarget() is not { } target || SystemDatabases.IsSystem(target.Database))
        {
            return null;
        }

        return target.Kind == RenameObjectKind.Column && !ServerCapabilities.FromVersion(node.OwningServer?.ServerVersion).SupportsRenameColumn
            ? null
            : target;
    }

    /// <summary>Returns the node to refresh after the rename: the table for a column (keys and indexes show it too), otherwise the folder.</summary>
    public static TreeNodeViewModel? RefreshNodeOf(TreeNodeViewModel node, RenameTarget target)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return target?.Kind == RenameObjectKind.Column ? node.FindAncestor<TableNodeViewModel>() ?? node.Parent : node.Parent;
    }

    /// <summary>
    /// Renames the object, refreshes the tree and selects the object under its new name; a table or view that was
    /// expanded is expanded again. Returns false when the password prompt was cancelled; a server error is thrown.
    /// </summary>
    public async Task<bool> RenameAsync(TreeNodeViewModel node, RenameTarget target, string newName, CancellationToken cancellationToken)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var sql = target.BuildSql(newName);
        var server = node.OwningServer ?? throw new InvalidOperationException("The node has no server.");
        if (!await server.EnsurePasswordAsync(cancellationToken))
        {
            return false;
        }

        await _runner.ExecuteAsync(server.Definition, sql, cancellationToken);
        _logger.Info($"Renamed {target.Kind.ToString().ToLowerInvariant()} {target.DisplayName} to {newName} ('{server.Label}'): {sql}");

        var wasExpanded = node.IsExpanded;
        if (RefreshNodeOf(node, target) is not { } parent)
        {
            return true;
        }

        await parent.RefreshAsync();
        if (Find(parent, node.GetType(), newName) is { } renamed)
        {
            renamed.IsSelected = true;
            if (wasExpanded && renamed.CanHaveChildren)
            {
                renamed.IsExpanded = true;
            }
        }

        return true;
    }

    // Searches the loaded nodes below 'root' for the renamed object (a column sits two levels below its table).
    private static TreeNodeViewModel? Find(TreeNodeViewModel root, Type type, string key)
    {
        var level = new List<TreeNodeViewModel>(root.Children);
        for (var depth = 0; depth < 2 && level.Count > 0; depth++)
        {
            if (level.FirstOrDefault(n => n.GetType() == type && string.Equals(n.Key, key, StringComparison.Ordinal)) is { } match)
            {
                return match;
            }

            level = level.SelectMany(n => n.Children).ToList();
        }

        return null;
    }
}
