using System;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.DragDrop;

/// <summary>Data object of a drag or copy from the explorer (US-0501, US-0502).</summary>
internal static class DragData
{
    /// <summary>
    /// Clipboard format with the text for SQL files (identifiers quoted when needed). The editor drop handler
    /// uses it in .sql files; every other target gets the plain text of the standard text formats.
    /// </summary>
    public const string SqlTextFormat = "ObjectExplorerForMySql.SqlText";

    /// <summary>Clipboard format with only the (quoted) name for SQL files: dropped when Alt is held (US-0503).</summary>
    public const string SqlNameFormat = "ObjectExplorerForMySql.SqlName";

    /// <summary>Clipboard format with the plain connection string of a dragged server (US-0505).</summary>
    public const string ConnectionStringFormat = "ObjectExplorerForMySql.ConnectionString";

    /// <summary>Clipboard format with the name of the connection, for the ConnectionStrings entry (US-0505).</summary>
    public const string ConnectionNameFormat = "ObjectExplorerForMySql.ConnectionName";

    /// <summary>
    /// Creates the data object of a connection string (US-0505): the plain string as text for other targets, plus the
    /// string and the connection name for the editor drop handler, which formats it for the file type.
    /// </summary>
    public static DataObject CreateConnectionString(string connectionString, string name)
    {
        var data = new DataObject();
        data.SetText(connectionString, TextDataFormat.UnicodeText);
        data.SetText(connectionString, TextDataFormat.Text);
        data.SetData(ConnectionStringFormat, connectionString);
        data.SetData(ConnectionNameFormat, name);
        return data;
    }

    /// <summary>Creates the data object: Text and UnicodeText with the plain text, plus the SQL formats.</summary>
    public static DataObject Create(DropText text)
    {
        var data = new DataObject();
        data.SetText(text.Plain, TextDataFormat.UnicodeText);
        data.SetText(text.Plain, TextDataFormat.Text);
        data.SetData(SqlTextFormat, text.Sql);
        data.SetData(SqlNameFormat, text.SqlName);
        return data;
    }

    /// <summary>Returns true when <paramref name="node"/> can be dragged or copied.</summary>
    public static bool CanDrag(ExplorerViewModel explorer, TreeNodeViewModel? node) =>
        node is ServerNodeViewModel or FolderNodeViewModel { IsColumnsFolder: true } || (node is not null && explorer.CreateDragItems(node).Count > 0);

    /// <summary>
    /// Creates the data object for <paramref name="node"/>, or null when there is nothing to drag. A Columns folder
    /// that was never expanded is loaded first (one short query), so dragging it always gives all columns.
    /// </summary>
    public static DataObject? CreateFor(ExplorerViewModel explorer, TreeNodeViewModel node)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // US-0505: a server gives its connection string, with a placeholder for the password.
        if (node is ServerNodeViewModel server)
        {
            return CreateConnectionString(ConnectionStringText.Build(server.Definition), server.Label);
        }

        if (node is FolderNodeViewModel { IsColumnsFolder: true, LoadState: not LoadState.Loaded } columns)
        {
            ThreadHelper.JoinableTaskFactory.Run(columns.EnsureLoadedAsync);
        }

        // A procedure or function becomes a call with its parameters (US-0503); load them when never expanded.
        if (node is RoutineNodeViewModel { Parameters: null } routine)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(() => routine.EnsureParametersAsync());
            }
            catch (Exception ex)
            {
                // Without parameters the drag still works and drops only the name.
                ExtensionLog.Logger.Warning($"Loading the parameters of '{routine.Label}' failed.", ex);
            }
        }

        var items = explorer.CreateDragItems(node);
        return items.Count == 0 ? null : Create(DropTextBuilder.Build(items, Settings.DragDropSettingsMonitor.Current));
    }
}
