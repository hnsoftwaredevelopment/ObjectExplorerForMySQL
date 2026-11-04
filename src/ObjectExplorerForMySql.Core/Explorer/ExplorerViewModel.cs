using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.DragDrop;

namespace ObjectExplorerForMySql.Core.Explorer;

/// <summary>Root view model of the explorer tool window.</summary>
public sealed class ExplorerViewModel : ObservableObject
{
    private readonly ExplorerContext _context;
    private TreeNodeViewModel? _selectedNode;

    /// <summary>Initializes a new instance of the <see cref="ExplorerViewModel"/> class.</summary>
    public ExplorerViewModel(ExplorerContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Raised when a connection changed in a way that must be saved (for example a password entered in the prompt with "Save password").</summary>
    public event EventHandler? ConnectionsChanged;

    /// <summary>Gets the server nodes, sorted by label (case-insensitive).</summary>
    public ObservableCollection<ServerNodeViewModel> Servers { get; } = [];

    /// <summary>Gets the metadata reader of the explorer, for commands that query the server (View Code, US-0601).</summary>
    public Metadata.IMetadataProvider Metadata => _context.Metadata;

    /// <summary>Gets the links between documents opened from the explorer and their connection (US-0601, E07).</summary>
    public Scripting.DocumentConnections Documents { get; } = new();

    /// <summary>Gets a value indicating whether no server has been added yet.</summary>
    public bool IsEmpty => Servers.Count == 0;

    /// <summary>Gets or sets the node selected in the tree.</summary>
    public TreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            var previous = _selectedNode;
            if (SetProperty(ref _selectedNode, value))
            {
                ClearMultiSelection(previous);
                OnPropertyChanged(nameof(SelectedServer));
            }
        }
    }

    /// <summary>Gets the selected node when it is a server node; otherwise null.</summary>
    public ServerNodeViewModel? SelectedServer => SelectedNode as ServerNodeViewModel;

    /// <summary>
    /// Gets the node that Refresh reloads (US-0307): the selected node, or for a node without children
    /// (column, key, "Loading...", error) its nearest parent. Null when nothing can be refreshed, for example
    /// a server that is not connected.
    /// </summary>
    public TreeNodeViewModel? RefreshTarget
    {
        get
        {
            var node = SelectedNode;
            while (node is { CanHaveChildren: false })
            {
                node = node.Parent;
            }

            return node is { CanRefresh: true } ? node : null;
        }
    }

    /// <summary>
    /// Gets the selected nodes in tree order: the selected node plus the columns added with Ctrl or Shift (US-0502).
    /// Empty when nothing is selected.
    /// </summary>
    public IReadOnlyList<TreeNodeViewModel> SelectedNodes
    {
        get
        {
            if (SelectedNode is not { } anchor)
            {
                return [];
            }

            if (anchor.Parent is not { } parent)
            {
                return [anchor];
            }

            return parent.Children.Where(c => ReferenceEquals(c, anchor) || c.IsMultiSelected).ToList();
        }
    }

    /// <summary>
    /// Ctrl+click: adds <paramref name="node"/> to or removes it from the selection. Only works for a column of the
    /// same table or view as the selected column; returns false otherwise, and the click selects as usual.
    /// </summary>
    public bool ToggleSelection(TreeNodeViewModel node)
    {
        if (!CanMultiSelect(node))
        {
            return false;
        }

        if (!ReferenceEquals(node, SelectedNode))
        {
            node.IsMultiSelected = !node.IsMultiSelected;
            OnPropertyChanged(nameof(SelectedNodes));
        }

        return true;
    }

    /// <summary>
    /// Shift+click: selects the columns from the selected column to <paramref name="node"/>. Same rules as
    /// <see cref="ToggleSelection"/>.
    /// </summary>
    public bool ExtendSelection(TreeNodeViewModel node)
    {
        if (!CanMultiSelect(node))
        {
            return false;
        }

        var siblings = node.Parent!.Children;
        var from = siblings.IndexOf(SelectedNode!);
        var to = siblings.IndexOf(node);
        var (first, last) = from <= to ? (from, to) : (to, from);
        for (var i = 0; i < siblings.Count; i++)
        {
            var child = siblings[i];
            child.IsMultiSelected = !ReferenceEquals(child, SelectedNode) && i >= first && i <= last && child.IsVisible;
        }

        OnPropertyChanged(nameof(SelectedNodes));
        return true;
    }

    /// <summary>Removes the extra selection, keeping only <see cref="SelectedNode"/>.</summary>
    public void ClearMultiSelection() => ClearMultiSelection(SelectedNode);

    /// <summary>
    /// Returns the objects to drag or copy for <paramref name="node"/> (US-0501, US-0502): all selected columns when
    /// the node is part of a multi-selection, all loaded columns for a Columns folder, otherwise the node itself.
    /// </summary>
    public IReadOnlyList<DragItem> CreateDragItems(TreeNodeViewModel node)
    {
        if (node is FolderNodeViewModel { IsColumnsFolder: true } columns)
        {
            return columns.Children.OfType<ColumnNodeViewModel>().Select(c => c.CreateDragItem()!).ToList();
        }

        var selected = SelectedNodes;
        if (selected.Count > 1 && selected.Contains(node))
        {
            return selected.Select(n => n.CreateDragItem()).OfType<DragItem>().ToList();
        }

        return node.CreateDragItem() is { } item ? [item] : [];
    }

    private bool CanMultiSelect(TreeNodeViewModel node) =>
        node is ColumnNodeViewModel && SelectedNode is ColumnNodeViewModel anchor && node.Parent is not null && ReferenceEquals(node.Parent, anchor.Parent);

    private void ClearMultiSelection(TreeNodeViewModel? anchor)
    {
        if (anchor?.Parent is not { } parent)
        {
            return;
        }

        var changed = false;
        foreach (var child in parent.Children.Where(c => c.IsMultiSelected))
        {
            child.IsMultiSelected = false;
            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(SelectedNodes));
        }
    }

    /// <summary>Refreshes <see cref="RefreshTarget"/>, if any.</summary>
    public Task RefreshSelectedAsync() => RefreshTarget?.RefreshAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Gets or sets the name filter (US-0308). Objects in the Tables, Views, Stored Procedures, Functions and Events
    /// folders whose name does not contain the text (case-insensitive) are hidden; it also applies to objects loaded later.
    /// </summary>
    public string FilterText
    {
        get => _context.Filter.Text;
        set
        {
            if (!_context.Filter.SetText(value))
            {
                return;
            }

            OnPropertyChanged();
            foreach (var folder in Servers.SelectMany(Descendants).OfType<FolderNodeViewModel>())
            {
                folder.ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Collapses every node, at every level, without unloading or disconnecting (US-0308). A selection that
    /// would become hidden moves to its server.
    /// </summary>
    public void CollapseAll()
    {
        if (SelectedNode is { Parent: not null } selected)
        {
            var root = selected;
            while (root.Parent is not null)
            {
                root = root.Parent;
            }

            selected.IsSelected = false;
            root.IsSelected = true;
            SelectedNode = root;
        }

        foreach (var server in Servers)
        {
            foreach (var node in Descendants(server).Reverse())
            {
                node.IsExpanded = false;
            }

            server.IsExpanded = false;
        }
    }

    private static IEnumerable<TreeNodeViewModel> Descendants(TreeNodeViewModel node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Adds a server node at its sorted position and returns it.</summary>
    public ServerNodeViewModel AddServer(ConnectionDefinition definition)
    {
        var node = new ServerNodeViewModel(_context, definition);
        node.DefinitionChanged += OnDefinitionChanged;
        Servers.Insert(SortedIndexFor(node.Label, ignore: null), node);
        OnPropertyChanged(nameof(IsEmpty));
        return node;
    }

    /// <summary>
    /// Replaces the connection of <paramref name="node"/>, keeping its identity, disconnects it, and moves
    /// the node to its new sorted position when the label changed.
    /// </summary>
    public void UpdateServer(ServerNodeViewModel node, ConnectionDefinition definition)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        var oldIndex = Servers.IndexOf(node);
        if (oldIndex < 0)
        {
            throw new ArgumentException("The node is not part of this explorer.", nameof(node));
        }

        definition.Id = node.Definition.Id;
        node.Definition = definition;

        var newIndex = SortedIndexFor(node.Label, ignore: node);
        if (newIndex > oldIndex)
        {
            newIndex--;
        }

        if (newIndex != oldIndex)
        {
            Servers.Move(oldIndex, newIndex);
        }
    }

    /// <summary>Disconnects and removes <paramref name="node"/>. Returns false when it was not in the list.</summary>
    public bool RemoveServer(ServerNodeViewModel node)
    {
        if (node is null || !Servers.Contains(node))
        {
            return false;
        }

        var selectionInside = SelectedNode is not null && (ReferenceEquals(SelectedNode, node) || IsDescendantOf(SelectedNode, node));

        node.Disconnect();
        node.DefinitionChanged -= OnDefinitionChanged;
        Servers.Remove(node);

        if (selectionInside)
        {
            SelectedNode = null;
        }

        OnPropertyChanged(nameof(IsEmpty));
        return true;
    }

    private static bool IsDescendantOf(TreeNodeViewModel candidate, TreeNodeViewModel ancestor)
    {
        foreach (var child in ancestor.Children)
        {
            if (ReferenceEquals(child, candidate) || IsDescendantOf(candidate, child))
            {
                return true;
            }
        }

        return false;
    }

    private void OnDefinitionChanged(object? sender, EventArgs e) => ConnectionsChanged?.Invoke(this, EventArgs.Empty);

    // Index where a node with this label belongs, skipping 'ignore' (the node being moved).
    private int SortedIndexFor(string label, ServerNodeViewModel? ignore)
    {
        var index = 0;
        while (index < Servers.Count &&
               (ReferenceEquals(Servers[index], ignore) ||
                string.Compare(Servers[index].Label, label, StringComparison.OrdinalIgnoreCase) <= 0))
        {
            index++;
        }

        return index;
    }
}
