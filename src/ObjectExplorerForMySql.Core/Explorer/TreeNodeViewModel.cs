using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Explorer;

/// <summary>Icon of a tree node; the extension maps it to a Visual Studio image moniker.</summary>
public enum NodeIcon
{
    /// <summary>Server, not connected.</summary>
    ServerDisconnected,

    /// <summary>Server, connecting.</summary>
    ServerConnecting,

    /// <summary>Server, connected.</summary>
    ServerConnected,

    /// <summary>Server, last connection attempt failed.</summary>
    ServerError,

    /// <summary>Collapsed folder.</summary>
    FolderClosed,

    /// <summary>Expanded folder.</summary>
    FolderOpened,

    /// <summary>Database.</summary>
    Database,

    /// <summary>Table.</summary>
    Table,

    /// <summary>Column.</summary>
    Column,

    /// <summary>Column that is part of the primary key.</summary>
    PrimaryKeyColumn,

    /// <summary>Column that is part of a foreign key (and not of the primary key).</summary>
    ForeignKeyColumn,

    /// <summary>Generated (virtual or stored) column.</summary>
    GeneratedColumn,

    /// <summary>Primary key.</summary>
    PrimaryKey,

    /// <summary>Unique key.</summary>
    UniqueKey,

    /// <summary>Foreign key.</summary>
    ForeignKey,

    /// <summary>Check constraint.</summary>
    CheckConstraint,

    /// <summary>Primary key index (clustered in InnoDB).</summary>
    PrimaryIndex,

    /// <summary>Other index.</summary>
    Index,

    /// <summary>FULLTEXT index.</summary>
    FullTextIndex,

    /// <summary>Trigger.</summary>
    Trigger,

    /// <summary>View.</summary>
    View,

    /// <summary>Stored procedure.</summary>
    StoredProcedure,

    /// <summary>Stored function.</summary>
    Function,

    /// <summary>IN parameter.</summary>
    InputParameter,

    /// <summary>OUT parameter.</summary>
    OutputParameter,

    /// <summary>INOUT parameter.</summary>
    InputOutputParameter,

    /// <summary>Scheduled event.</summary>
    Event,

    /// <summary>User account (US-1002).</summary>
    User,

    /// <summary>Locked user account.</summary>
    LockedUser,

    /// <summary>Role.</summary>
    Role,

    /// <summary>Grant of a user or role.</summary>
    Grant,

    /// <summary>"Loading..." placeholder.</summary>
    Loading,

    /// <summary>Error message node.</summary>
    Error,
}

/// <summary>Loading state of a node's children.</summary>
public enum LoadState
{
    /// <summary>Not loaded yet; loads on expand.</summary>
    NotLoaded,

    /// <summary>Loading.</summary>
    Loading,

    /// <summary>Loaded.</summary>
    Loaded,

    /// <summary>Loading failed; the next expand tries again.</summary>
    Failed,
}

/// <summary>
/// Thrown by <see cref="TreeNodeViewModel.LoadChildrenAsync"/> when loading stops on purpose
/// (for example a cancelled password prompt): the node collapses without showing an error.
/// </summary>
public sealed class LoadAbortedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="LoadAbortedException"/> class.</summary>
    public LoadAbortedException()
        : base("Loading was cancelled by the user.")
    {
    }
}

/// <summary>
/// Base class of every node in the explorer tree. Children load lazily on the first expand,
/// off the UI thread in the metadata provider, and show "Loading..." meanwhile (US-0301).
/// </summary>
public abstract class TreeNodeViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isVisible = true;
    private bool _isMultiSelected;
    private bool _isEditing;
    private LoadState _loadState;
    private CancellationTokenSource? _loadCancellation;
    private Task? _currentLoad;

    /// <summary>Initializes a node that can have children (shows an expander before loading).</summary>
    protected TreeNodeViewModel(ExplorerContext context, bool canHaveChildren)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        CanHaveChildren = canHaveChildren;
        _loadState = canHaveChildren ? LoadState.NotLoaded : LoadState.Loaded;
        if (canHaveChildren)
        {
            ReplaceChildren([new PlaceholderNodeViewModel(context, NodeIcon.Loading, context.Texts.Loading)]);
        }
    }

    /// <summary>Gets the parent node, or null for a server node.</summary>
    public TreeNodeViewModel? Parent { get; private set; }

    /// <summary>
    /// Gets the identity of the node among its siblings, used to keep expanded and selected nodes after a refresh.
    /// Defaults to <see cref="Label"/>; nodes whose label contains details (types, status) override it with the object name.
    /// </summary>
    public virtual string Key => Label;

    /// <summary>Gets a value indicating whether the children of this node can be reloaded.</summary>
    public virtual bool CanRefresh => CanHaveChildren;

    /// <summary>Gets the server node this node belongs to, or null (for a server node: itself).</summary>
    public ServerNodeViewModel? OwningServer
    {
        get
        {
            TreeNodeViewModel? node = this;
            while (node is not null and not ServerNodeViewModel)
            {
                node = node.Parent;
            }

            return node as ServerNodeViewModel;
        }
    }

    /// <summary>
    /// Creates the object shown in the Visual Studio Properties window when this node is selected (E04),
    /// or null when the node has no properties.
    /// </summary>
    public virtual object? CreateProperties() => null;

    /// <summary>Returns the object to drag or copy into the editor (E05), or null when this node cannot be dragged.</summary>
    public virtual DragDrop.DragItem? CreateDragItem() => null;

    /// <summary>Returns the object whose source View Code shows (US-0601), or null for nodes without source.</summary>
    public virtual Scripting.CodeObject? CreateCodeObject() => null;

    /// <summary>
    /// Returns what Delete drops for this node (US-0901), or null. Use <see cref="Management.ObjectDeleter.TargetOf"/>,
    /// which also leaves system databases alone.
    /// </summary>
    public virtual Management.DropTarget? CreateDropTarget() => null;

    /// <summary>
    /// Returns what Rename changes for this node (US-0902), or null. Use <see cref="Management.ObjectRenamer.TargetOf"/>,
    /// which also leaves system databases alone and checks the server version for columns.
    /// </summary>
    public virtual Management.RenameTarget? CreateRenameTarget() => null;

    /// <summary>Gets or sets a value indicating whether the name is being edited in the tree (US-0902).</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    /// <summary>Returns the nearest parent of type <typeparamref name="T"/>, or null.</summary>
    public T? FindAncestor<T>()
        where T : TreeNodeViewModel
    {
        var node = Parent;
        while (node is not null and not T)
        {
            node = node.Parent;
        }

        return node as T;
    }

    /// <summary>Gets the shared services.</summary>
    protected ExplorerContext Context { get; }

    /// <summary>Gets the text shown in the tree.</summary>
    public abstract string Label { get; }

    /// <summary>Gets the icon.</summary>
    public abstract NodeIcon Icon { get; }

    /// <summary>Gets the tooltip text, or null.</summary>
    public virtual string? ToolTip => null;

    /// <summary>Gets the child nodes.</summary>
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    /// <summary>Gets a value indicating whether this node type can have children.</summary>
    public bool CanHaveChildren { get; }

    /// <summary>Gets the loading state of the children.</summary>
    public LoadState LoadState
    {
        get => _loadState;
        private set => SetProperty(ref _loadState, value);
    }

    /// <summary>Gets or sets a value indicating whether the node is expanded. Expanding loads the children.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(Icon));
                if (value)
                {
                    _ = EnsureLoadedAsync();
                }
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the node is selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Gets a value indicating whether the node is part of a multi-selection besides the selected node (US-0502).
    /// Only columns of the same table can be multi-selected; see <see cref="ExplorerViewModel.ToggleSelection"/>.
    /// </summary>
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        internal set => SetProperty(ref _isMultiSelected, value);
    }

    /// <summary>Gets a value indicating whether the node is shown; false when the filter hides it (US-0308).</summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }

    /// <summary>Waits for a running load, if any. Does not start a load.</summary>
    public Task WaitForLoadAsync() => _currentLoad ?? Task.CompletedTask;

    /// <summary>Loads the children when they are not loaded yet or the last load failed; otherwise waits for a running load.</summary>
    public Task EnsureLoadedAsync() => LoadState switch
    {
        LoadState.NotLoaded or LoadState.Failed => LoadAsync(),
        LoadState.Loading => _currentLoad ?? Task.CompletedTask,
        _ => Task.CompletedTask,
    };

    /// <summary>
    /// Reloads the children when the node is expanded; otherwise marks them to load on the next expand (US-0307).
    /// The current children stay visible until the new ones are loaded. Children that still exist keep their
    /// expanded and selected state, at every level. When the selected node no longer exists, this node is selected.
    /// </summary>
    public Task RefreshAsync()
    {
        if (!CanHaveChildren)
        {
            return Task.CompletedTask;
        }

        if (IsExpanded)
        {
            return RefreshExpandedAsync();
        }

        Unload();
        return Task.CompletedTask;
    }

    /// <summary>Stops a running load, removes the children and collapses the node.</summary>
    public void Collapse()
    {
        Unload();
        IsExpanded = false;
    }

    /// <summary>Loads the children. Runs on the caller's context (the UI thread in Visual Studio).</summary>
    protected abstract Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken);

    /// <summary>Called after new children were loaded and added, before their state is restored after a refresh.</summary>
    protected virtual void OnChildrenLoaded()
    {
    }

    /// <summary>Called when loading failed, before the error node is shown.</summary>
    protected virtual void OnLoadFailed(Exception exception)
    {
    }

    private void Unload()
    {
        if (!CanHaveChildren)
        {
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation = null;
        _currentLoad = null;
        ReplaceChildren([new PlaceholderNodeViewModel(Context, NodeIcon.Loading, Context.Texts.Loading)]);
        LoadState = LoadState.NotLoaded;
    }

    private static TreeNodeViewModel? FindSelected(IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsSelected)
            {
                return node;
            }

            if (FindSelected(node.Children) is { } selected)
            {
                return selected;
            }
        }

        return null;
    }

    private async Task RefreshExpandedAsync()
    {
        var hadSelectedChild = FindSelected(Children) is not null;
        var previous = LoadState == LoadState.Loaded ? Children.ToList() : null;
        var load = LoadAsync(previous, keepCurrentChildren: previous is not null);
        await load;

        // Only the latest load decides the selection; a newer refresh or a collapse takes over.
        if (hadSelectedChild && ReferenceEquals(load, _currentLoad) && FindSelected(Children) is null)
        {
            IsSelected = true;
        }
    }

    // Expands a node that replaces 'previous' after a refresh of its parent, and restores its children's state.
    private Task ExpandAndRestoreAsync(IReadOnlyList<TreeNodeViewModel> previousChildren)
    {
        _isExpanded = true;
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(Icon));
        return LoadAsync(previousChildren, keepCurrentChildren: false);
    }

    private Task LoadAsync(IReadOnlyList<TreeNodeViewModel>? previousChildren = null, bool keepCurrentChildren = false)
    {
        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        _currentLoad = LoadCoreAsync(previousChildren, keepCurrentChildren, _loadCancellation.Token);
        return _currentLoad;
    }

    private async Task LoadCoreAsync(IReadOnlyList<TreeNodeViewModel>? previousChildren, bool keepCurrentChildren, CancellationToken cancellationToken)
    {
        LoadState = LoadState.Loading;
        if (!keepCurrentChildren)
        {
            ReplaceChildren([new PlaceholderNodeViewModel(Context, NodeIcon.Loading, Context.Texts.Loading)]);
        }

        try
        {
            var children = await LoadChildrenAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ReplaceChildren(children);
            LoadState = LoadState.Loaded;
            OnChildrenLoaded();

            if (previousChildren is not null)
            {
                await RestoreStateAsync(previousChildren, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Replaced by a newer load, a refresh or a collapse.
        }
        catch (LoadAbortedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Collapse();
            }
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Context.Logger.Warning($"Loading '{Label}' failed.", ex);
            OnLoadFailed(ex);
            ReplaceChildren([new PlaceholderNodeViewModel(Context, NodeIcon.Error, SensitiveDataMasker.MaskText(ex.Message))]);
            LoadState = LoadState.Failed;
        }
    }

    // Gives the new children the expanded and selected state of the matching previous children (same type and key).
    private Task RestoreStateAsync(IReadOnlyList<TreeNodeViewModel> previousChildren, CancellationToken cancellationToken)
    {
        var loads = new List<Task>();
        foreach (var old in previousChildren)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var match = Children.FirstOrDefault(c => c.GetType() == old.GetType() && string.Equals(c.Key, old.Key, StringComparison.Ordinal));
            if (match is null)
            {
                continue;
            }

            if (old.IsSelected)
            {
                old.IsSelected = false;
                match.IsSelected = true;
            }

            if (old.IsExpanded && match.CanHaveChildren)
            {
                loads.Add(match.ExpandAndRestoreAsync(old.Children.ToList()));
            }
        }

        return Task.WhenAll(loads);
    }

    private void ReplaceChildren(IReadOnlyList<TreeNodeViewModel> children)
    {
        Children.Clear();
        foreach (var child in children)
        {
            child.Parent = this;
            Children.Add(child);
        }
    }
}

/// <summary>"Loading..." or error message node without children.</summary>
public sealed class PlaceholderNodeViewModel : TreeNodeViewModel
{
    private readonly NodeIcon _icon;

    /// <summary>Initializes a new instance of the <see cref="PlaceholderNodeViewModel"/> class.</summary>
    public PlaceholderNodeViewModel(ExplorerContext context, NodeIcon icon, string text)
        : base(context, canHaveChildren: false)
    {
        _icon = icon;
        Label = text;
    }

    /// <inheritdoc />
    public override string Label { get; }

    /// <inheritdoc />
    public override NodeIcon Icon => _icon;

    /// <inheritdoc />
    public override string? ToolTip => _icon == NodeIcon.Error ? Label : null;

    /// <summary>Gets a value indicating whether this node shows an error.</summary>
    public bool IsError => _icon == NodeIcon.Error;

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TreeNodeViewModel>>(Array.Empty<TreeNodeViewModel>());
}
