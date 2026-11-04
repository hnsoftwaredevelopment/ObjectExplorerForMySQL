using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.ObjectProperties;
using ObjectExplorerForMySql.Core.Scripting;

namespace ObjectExplorerForMySql.Core.Explorer;

/// <summary>Folder node whose children come from a loader function.</summary>
public sealed class FolderNodeViewModel : TreeNodeViewModel
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<TreeNodeViewModel>>> _loader;
    private readonly string _name;

    /// <summary>Initializes a new instance of the <see cref="FolderNodeViewModel"/> class.</summary>
    /// <param name="context">Shared services.</param>
    /// <param name="label">Folder name.</param>
    /// <param name="loader">Loads the children.</param>
    /// <param name="isFilterable">True for a folder whose objects the name filter applies to (US-0308).</param>
    public FolderNodeViewModel(ExplorerContext context, string label, Func<CancellationToken, Task<IReadOnlyList<TreeNodeViewModel>>> loader, bool isFilterable = false)
        : base(context, canHaveChildren: true)
    {
        _name = label;
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        IsFilterable = isFilterable;
    }

    /// <summary>Gets a value indicating whether the name filter applies to the children of this folder.</summary>
    public bool IsFilterable { get; }

    /// <summary>Gets a value indicating whether this is the Columns folder of a table or view (dragging it drops all columns, US-0502).</summary>
    public bool IsColumnsFolder => Parent is TableNodeViewModel or ViewNodeViewModel && _name == Context.Texts.Columns;

    /// <summary>Gets a value indicating whether this is the Users folder under Security (US-1003: New User).</summary>
    public bool IsUsersFolder => Parent is FolderNodeViewModel { Parent: ServerNodeViewModel } && _name == Context.Texts.Users;

    /// <summary>Gets a value indicating whether this is the Roles folder under Security (US-1004: New Role).</summary>
    public bool IsRolesFolder => Parent is FolderNodeViewModel { Parent: ServerNodeViewModel } && _name == Context.Texts.Roles;

    /// <summary>
    /// Gets the kind of object New ... creates in this folder (US-0904), or null. Tables, Views, Stored Procedures,
    /// Functions and Events under a database; Columns, Triggers and Indexes under a table (not under a view).
    /// </summary>
    public NewObjectKind? NewObjectKind
    {
        get
        {
            var texts = Context.Texts;
            return Parent switch
            {
                DatabaseNodeViewModel when _name == texts.Tables => Scripting.NewObjectKind.Table,
                DatabaseNodeViewModel when _name == texts.Views => Scripting.NewObjectKind.View,
                DatabaseNodeViewModel when _name == texts.StoredProcedures => Scripting.NewObjectKind.Procedure,
                DatabaseNodeViewModel when _name == texts.Functions => Scripting.NewObjectKind.Function,
                DatabaseNodeViewModel when _name == texts.Events => Scripting.NewObjectKind.Event,
                TableNodeViewModel when _name == texts.Columns => Scripting.NewObjectKind.Column,
                TableNodeViewModel when _name == texts.Triggers => Scripting.NewObjectKind.Trigger,
                TableNodeViewModel when _name == texts.Indexes => Scripting.NewObjectKind.Index,
                _ => null,
            };
        }
    }

    /// <summary>Gets the label: the folder name, with "(filtered)" while a filter hides objects in it.</summary>
    public override string Label => IsFilterable && Context.Filter.IsActive
        ? string.Format(CultureInfo.CurrentCulture, Context.Texts.Filtered, _name)
        : _name;

    /// <inheritdoc />
    public override string Key => _name;

    /// <summary>Shows or hides the children by the current filter and updates the label.</summary>
    public void ApplyFilter()
    {
        if (!IsFilterable)
        {
            return;
        }

        OnPropertyChanged(nameof(Label));
        foreach (var child in Children)
        {
            child.IsVisible = child is PlaceholderNodeViewModel || Context.Filter.IsMatch(child.Key);
        }
    }

    /// <inheritdoc />
    protected override void OnChildrenLoaded() => ApplyFilter();

    /// <inheritdoc />
    public override NodeIcon Icon => IsExpanded ? NodeIcon.FolderOpened : NodeIcon.FolderClosed;

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken) => _loader(cancellationToken);

    /// <summary>
    /// Creates the "Databases" folder of <paramref name="server"/>: a "System Databases" folder first,
    /// then the user databases, both sorted by name (case-insensitive).
    /// </summary>
    public static FolderNodeViewModel CreateDatabasesFolder(ExplorerContext context, ServerNodeViewModel server) =>
        new(context, context.Texts.Databases, async cancellationToken =>
        {
            var databases = await context.Metadata.GetDatabasesAsync(server.Definition, cancellationToken);
            return BuildDatabaseNodes(context, server, databases);
        });

    /// <summary>Groups databases into a "System Databases" folder and user database nodes.</summary>
    internal static IReadOnlyList<TreeNodeViewModel> BuildDatabaseNodes(ExplorerContext context, ServerNodeViewModel server, IEnumerable<DatabaseInfo> databases)
    {
        var sorted = databases.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var systemNodes = sorted.Where(d => d.IsSystem).Select(d => (TreeNodeViewModel)new DatabaseNodeViewModel(context, server, d)).ToList();

        var result = new List<TreeNodeViewModel>
        {
            new FolderNodeViewModel(context, context.Texts.SystemDatabases, _ => Task.FromResult<IReadOnlyList<TreeNodeViewModel>>(systemNodes)),
        };
        result.AddRange(sorted.Where(d => !d.IsSystem).Select(d => new DatabaseNodeViewModel(context, server, d)));
        return result;
    }
}

/// <summary>
/// Node for one database. Children: the folders Tables, Views, Stored Procedures, Functions and Events (US-0302, US-0304 to US-0306).
/// </summary>
public sealed class DatabaseNodeViewModel : TreeNodeViewModel
{
    /// <summary>Initializes a new instance of the <see cref="DatabaseNodeViewModel"/> class.</summary>
    public DatabaseNodeViewModel(ExplorerContext context, ServerNodeViewModel server, DatabaseInfo database)
        : base(context, canHaveChildren: true)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        Database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>Gets the server this database belongs to.</summary>
    public ServerNodeViewModel Server { get; }

    /// <summary>Gets the database metadata.</summary>
    public DatabaseInfo Database { get; }

    /// <inheritdoc />
    public override string Label => Database.Name;

    /// <inheritdoc />
    public override string Key => Database.Name;

    /// <inheritdoc />
    public override NodeIcon Icon => NodeIcon.Database;

    /// <summary>Gets the number of tables when the Tables folder is loaded; otherwise null.</summary>
    public int? LoadedTableCount =>
        Children.OfType<FolderNodeViewModel>().FirstOrDefault(f => f.Key == Context.Texts.Tables) is { LoadState: LoadState.Loaded } tables
            ? tables.Children.OfType<TableNodeViewModel>().Count()
            : null;

    /// <inheritdoc />
    public override object? CreateProperties() => new DatabaseProperties(Database, LoadedTableCount, Server.Definition);

    /// <inheritdoc />
    public override DragItem? CreateDragItem() => new(DragObjectKind.Database, Database.Name);

    /// <inheritdoc />
    public override DropTarget? CreateDropTarget() => new(DropObjectKind.Database, Database.Name, Database.Name);

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken)
    {
        var metadata = Context.Metadata;
        var definition = Server.Definition;
        var name = Database.Name;

        IReadOnlyList<TreeNodeViewModel> folders =
        [
            new FolderNodeViewModel(Context, Context.Texts.Tables, async ct =>
                (await metadata.GetTablesAsync(definition, name, ct))
                    .Select(t => (TreeNodeViewModel)new TableNodeViewModel(Context, Server, t)).ToList(), isFilterable: true),
            new FolderNodeViewModel(Context, Context.Texts.Views, async ct =>
                (await metadata.GetViewsAsync(definition, name, ct))
                    .Select(v => (TreeNodeViewModel)new ViewNodeViewModel(Context, Server, v)).ToList(), isFilterable: true),
            new FolderNodeViewModel(Context, Context.Texts.StoredProcedures, async ct =>
                (await metadata.GetRoutinesAsync(definition, name, RoutineType.Procedure, ct))
                    .Select(r => (TreeNodeViewModel)new RoutineNodeViewModel(Context, Server, r)).ToList(), isFilterable: true),
            new FolderNodeViewModel(Context, Context.Texts.Functions, async ct =>
                (await metadata.GetRoutinesAsync(definition, name, RoutineType.Function, ct))
                    .Select(r => (TreeNodeViewModel)new RoutineNodeViewModel(Context, Server, r)).ToList(), isFilterable: true),
            new FolderNodeViewModel(Context, Context.Texts.Events, async ct =>
                (await metadata.GetEventsAsync(definition, name, ct))
                    .Select(e => (TreeNodeViewModel)new ObjectNodeViewModel(Context, e, e.Label, NodeIcon.Event, e.Schedule, e.Name)).ToList(), isFilterable: true),
        ];

        return Task.FromResult(folders);
    }
}

/// <summary>Node for one view (US-0304). Children: the "Columns" folder.</summary>
public sealed class ViewNodeViewModel : TreeNodeViewModel
{
    /// <summary>Initializes a new instance of the <see cref="ViewNodeViewModel"/> class.</summary>
    public ViewNodeViewModel(ExplorerContext context, ServerNodeViewModel server, TableInfo view)
        : base(context, canHaveChildren: true)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        View = view ?? throw new ArgumentNullException(nameof(view));
    }

    /// <summary>Gets the server this view belongs to.</summary>
    public ServerNodeViewModel Server { get; }

    /// <summary>Gets the view metadata.</summary>
    public TableInfo View { get; }

    /// <inheritdoc />
    public override string Label => View.Name;

    /// <inheritdoc />
    public override string Key => View.Name;

    /// <inheritdoc />
    public override NodeIcon Icon => NodeIcon.View;

    /// <inheritdoc />
    public override string? ToolTip => View.Comment;

    /// <inheritdoc />
    public override object? CreateProperties() => new ViewProperties(View, Server.Definition);

    /// <inheritdoc />
    public override DragItem? CreateDragItem() => new(DragObjectKind.View, View.Name, View.Database);

    /// <inheritdoc />
    public override DropTarget? CreateDropTarget() => new(DropObjectKind.View, View.Database, View.Name);

    /// <inheritdoc />
    public override RenameTarget? CreateRenameTarget() => new(RenameObjectKind.View, View.Database, View.Name);

    /// <inheritdoc />
    /// <remarks>The system views of information_schema have no view definition; SHOW CREATE TABLE shows their structure.</remarks>
    public override CodeObject? CreateCodeObject() =>
        new(string.Equals(View.Database, "information_schema", StringComparison.OrdinalIgnoreCase) ? CodeObjectKind.Table : CodeObjectKind.View, View.Database, View.Name);

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken)
    {
        var metadata = Context.Metadata;
        var definition = Server.Definition;
        IReadOnlyList<TreeNodeViewModel> folders =
        [
            new FolderNodeViewModel(Context, Context.Texts.Columns, async ct =>
                (await metadata.GetColumnsAsync(definition, View.Database, View.Name, ct))
                    .Select(c => (TreeNodeViewModel)new ColumnNodeViewModel(Context, View, c)).ToList()),
        ];
        return Task.FromResult(folders);
    }
}

/// <summary>Node for one stored procedure or function (US-0305). Children: the "Parameters" folder.</summary>
public sealed class RoutineNodeViewModel : TreeNodeViewModel
{
    /// <summary>Initializes a new instance of the <see cref="RoutineNodeViewModel"/> class.</summary>
    public RoutineNodeViewModel(ExplorerContext context, ServerNodeViewModel server, RoutineInfo routine)
        : base(context, canHaveChildren: true)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        Routine = routine ?? throw new ArgumentNullException(nameof(routine));
    }

    /// <summary>Gets the server this routine belongs to.</summary>
    public ServerNodeViewModel Server { get; }

    /// <summary>Gets the routine metadata.</summary>
    public RoutineInfo Routine { get; }

    /// <inheritdoc />
    public override string Label => Routine.Label;

    /// <inheritdoc />
    public override string Key => Routine.Name;

    /// <inheritdoc />
    public override NodeIcon Icon => Routine.Type == RoutineType.Function ? NodeIcon.Function : NodeIcon.StoredProcedure;

    /// <inheritdoc />
    public override string? ToolTip => Routine.Comment;

    /// <inheritdoc />
    public override object? CreateProperties() => new RoutineProperties(Routine, Server.Definition);

    /// <summary>Gets the parameters once they were loaded (Parameters folder or <see cref="EnsureParametersAsync"/>), otherwise null.</summary>
    public IReadOnlyList<ParameterInfo>? Parameters { get; private set; }

    /// <inheritdoc />
    public override DragItem? CreateDragItem() =>
        new(Routine.Type == RoutineType.Function ? DragObjectKind.Function : DragObjectKind.Procedure, Routine.Name, Routine.Database)
        {
            Parameters = Parameters?.Select(p => p.Name).ToList(),
        };

    /// <inheritdoc />
    public override CodeObject? CreateCodeObject() =>
        new(Routine.Type == RoutineType.Function ? CodeObjectKind.Function : CodeObjectKind.Procedure, Routine.Database, Routine.Name);

    /// <inheritdoc />
    public override DropTarget? CreateDropTarget() =>
        new(Routine.Type == RoutineType.Function ? DropObjectKind.Function : DropObjectKind.Procedure, Routine.Database, Routine.Name);

    /// <summary>Loads the parameters for a drag (US-0503) when the Parameters folder was never expanded.</summary>
    public async Task EnsureParametersAsync(CancellationToken cancellationToken = default)
    {
        if (Parameters is null)
        {
            Parameters = await Context.Metadata.GetParametersAsync(Server.Definition, Routine, cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken)
    {
        var metadata = Context.Metadata;
        var definition = Server.Definition;
        IReadOnlyList<TreeNodeViewModel> folders =
        [
            new FolderNodeViewModel(Context, Context.Texts.Parameters, async ct =>
                (Parameters = await metadata.GetParametersAsync(definition, Routine, ct))
                    .Select(p => (TreeNodeViewModel)new ObjectNodeViewModel(
                        Context,
                        p,
                        p.Label,
                        p.Mode switch { ParameterMode.Out => NodeIcon.OutputParameter, ParameterMode.InOut => NodeIcon.InputOutputParameter, _ => NodeIcon.InputParameter },
                        key: p.Name))
                    .ToList()),
        ];
        return Task.FromResult(folders);
    }
}

/// <summary>Node for one table. Children: the folders Columns, Keys, Constraints, Triggers and Indexes.</summary>
public sealed class TableNodeViewModel : TreeNodeViewModel
{
    /// <summary>Initializes a new instance of the <see cref="TableNodeViewModel"/> class.</summary>
    public TableNodeViewModel(ExplorerContext context, ServerNodeViewModel server, TableInfo table)
        : base(context, canHaveChildren: true)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
        Table = table ?? throw new ArgumentNullException(nameof(table));
    }

    /// <summary>Gets the server this table belongs to.</summary>
    public ServerNodeViewModel Server { get; }

    /// <summary>Gets the table metadata.</summary>
    public TableInfo Table { get; }

    /// <inheritdoc />
    public override string Label => Table.Name;

    /// <inheritdoc />
    public override string Key => Table.Name;

    /// <inheritdoc />
    public override NodeIcon Icon => NodeIcon.Table;

    /// <inheritdoc />
    public override string? ToolTip => Table.Comment;

    /// <inheritdoc />
    public override object? CreateProperties() => new TableProperties(Table, Server.Definition);

    /// <inheritdoc />
    public override DragItem? CreateDragItem() => new(DragObjectKind.Table, Table.Name, Table.Database);

    /// <inheritdoc />
    public override CodeObject? CreateCodeObject() => new(CodeObjectKind.Table, Table.Database, Table.Name);

    /// <inheritdoc />
    public override DropTarget? CreateDropTarget() => new(DropObjectKind.Table, Table.Database, Table.Name);

    /// <inheritdoc />
    public override RenameTarget? CreateRenameTarget() => new(RenameObjectKind.Table, Table.Database, Table.Name);

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken)
    {
        var metadata = Context.Metadata;
        var definition = Server.Definition;
        var capabilities = ServerCapabilities.FromVersion(Server.ServerVersion);

        // Same order as SQL Server Object Explorer: Columns, Keys, Constraints, Triggers, Indexes.
        var folders = new List<TreeNodeViewModel>
        {
            new FolderNodeViewModel(Context, Context.Texts.Columns, async ct =>
                (await metadata.GetColumnsAsync(definition, Table.Database, Table.Name, ct))
                    .Select(c => (TreeNodeViewModel)new ColumnNodeViewModel(Context, Table, c)).ToList()),
            new FolderNodeViewModel(Context, Context.Texts.Keys, async ct =>
                (await metadata.GetKeysAsync(definition, Table.Database, Table.Name, ct))
                    .Select(k => (TreeNodeViewModel)new ObjectNodeViewModel(
                        Context,
                        k,
                        k.LabelFor(Table.Database),
                        k.Type switch { KeyType.Primary => NodeIcon.PrimaryKey, KeyType.Unique => NodeIcon.UniqueKey, _ => NodeIcon.ForeignKey }))
                    .ToList()),
        };

        if (capabilities.SupportsCheckConstraints)
        {
            folders.Add(new FolderNodeViewModel(Context, Context.Texts.Constraints, async ct =>
                (await metadata.GetCheckConstraintsAsync(definition, Table.Database, Table.Name, capabilities.IsMariaDb, ct))
                    .Select(c => (TreeNodeViewModel)new ObjectNodeViewModel(Context, c, c.Label, NodeIcon.CheckConstraint, c.Clause))
                    .ToList()));
        }

        folders.Add(new FolderNodeViewModel(Context, Context.Texts.Triggers, async ct =>
            (await metadata.GetTriggersAsync(definition, Table.Database, Table.Name, ct))
                .Select(t => (TreeNodeViewModel)new ObjectNodeViewModel(Context, t, t.Label, NodeIcon.Trigger))
                .ToList()));

        folders.Add(new FolderNodeViewModel(Context, Context.Texts.Indexes, async ct =>
            (await metadata.GetIndexesAsync(definition, Table.Database, Table.Name, capabilities.IsMariaDb, ct))
                .Select(i => (TreeNodeViewModel)new ObjectNodeViewModel(
                    Context,
                    i,
                    i.Label,
                    i.IsPrimary ? NodeIcon.PrimaryIndex : i.IsFullText ? NodeIcon.FullTextIndex : NodeIcon.Index))
                .ToList()));

        return Task.FromResult<IReadOnlyList<TreeNodeViewModel>>(folders);
    }
}

/// <summary>Leaf node for a metadata object without children (key, constraint, index, trigger, parameter, event).</summary>
public sealed class ObjectNodeViewModel : TreeNodeViewModel
{
    private readonly NodeIcon _icon;
    private readonly string? _toolTip;
    private readonly string? _key;

    /// <summary>Initializes a new instance of the <see cref="ObjectNodeViewModel"/> class.</summary>
    public ObjectNodeViewModel(ExplorerContext context, object item, string label, NodeIcon icon, string? toolTip = null, string? key = null)
        : base(context, canHaveChildren: false)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Label = label;
        _icon = icon;
        _toolTip = toolTip;
        _key = key;
    }

    /// <summary>Gets the metadata object, for example a <see cref="KeyInfo"/>.</summary>
    public object Item { get; }

    /// <inheritdoc />
    public override string Label { get; }

    /// <inheritdoc />
    public override NodeIcon Icon => _icon;

    /// <inheritdoc />
    public override string? ToolTip => _toolTip;

    /// <inheritdoc />
    public override object? CreateProperties()
    {
        var connection = OwningServer?.Definition;
        var table = FindAncestor<TableNodeViewModel>()?.Table;
        return Item switch
        {
            KeyInfo key when table is not null => new KeyProperties(key, table, connection),
            CheckConstraintInfo check when table is not null => new CheckConstraintProperties(check, table, connection),
            IndexInfo index when table is not null => new IndexProperties(index, table, connection),
            TriggerInfo trigger when table is not null => new TriggerProperties(trigger, table, connection),
            ParameterInfo parameter => new ParameterProperties(parameter, FindAncestor<RoutineNodeViewModel>()?.Routine, connection),
            EventInfo @event => new EventProperties(@event, FindAncestor<DatabaseNodeViewModel>()?.Database.Name ?? string.Empty, connection),
            _ => null,
        };
    }

    /// <inheritdoc />
    public override string Key => _key ?? Label;

    /// <inheritdoc />
    public override CodeObject? CreateCodeObject() => Item switch
    {
        TriggerInfo trigger when FindAncestor<TableNodeViewModel>() is { } table => new(CodeObjectKind.Trigger, table.Table.Database, trigger.Name),
        EventInfo @event when FindAncestor<DatabaseNodeViewModel>() is { } database => new(CodeObjectKind.Event, database.Database.Name, @event.Name),
        _ => null,
    };

    /// <inheritdoc />
    /// <remarks>Triggers, events and indexes. Keys and constraints are not deleted from the tree (US-0901).</remarks>
    public override DropTarget? CreateDropTarget() => Item switch
    {
        TriggerInfo trigger when FindAncestor<TableNodeViewModel>() is { } table => new(DropObjectKind.Trigger, table.Table.Database, trigger.Name),
        EventInfo @event when FindAncestor<DatabaseNodeViewModel>() is { } database => new(DropObjectKind.Event, database.Database.Name, @event.Name),
        IndexInfo index when FindAncestor<TableNodeViewModel>() is { } table => new(DropObjectKind.Index, table.Table.Database, index.Name, table.Table.Name),
        _ => null,
    };

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TreeNodeViewModel>>(Array.Empty<TreeNodeViewModel>());
}

/// <summary>Node for one column of a table (US-0302).</summary>
public sealed class ColumnNodeViewModel : TreeNodeViewModel
{
    /// <summary>Initializes a new instance of the <see cref="ColumnNodeViewModel"/> class.</summary>
    public ColumnNodeViewModel(ExplorerContext context, TableInfo table, ColumnInfo column)
        : base(context, canHaveChildren: false)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        Column = column ?? throw new ArgumentNullException(nameof(column));
    }

    /// <summary>Gets the table of the column.</summary>
    public TableInfo Table { get; }

    /// <summary>Gets the column metadata.</summary>
    public ColumnInfo Column { get; }

    /// <inheritdoc />
    public override string Label => Column.Label;

    /// <inheritdoc />
    public override string Key => Column.Name;

    /// <inheritdoc />
    public override NodeIcon Icon =>
        Column.IsPrimaryKey ? NodeIcon.PrimaryKeyColumn
        : Column.IsForeignKey ? NodeIcon.ForeignKeyColumn
        : Column.IsGenerated ? NodeIcon.GeneratedColumn
        : NodeIcon.Column;

    /// <inheritdoc />
    public override object? CreateProperties() => new ColumnProperties(Column, Table, OwningServer?.Definition);

    /// <inheritdoc />
    public override DragItem? CreateDragItem() => new(DragObjectKind.Column, Column.Name, Table.Database, Table.Name);

    /// <inheritdoc />
    /// <remarks>Only columns of a base table; a view column is changed through the view definition.</remarks>
    public override DropTarget? CreateDropTarget() =>
        Table.IsView ? null : new(DropObjectKind.Column, Table.Database, Column.Name, Table.Name);

    /// <inheritdoc />
    /// <remarks>Only columns of a base table.</remarks>
    public override RenameTarget? CreateRenameTarget() =>
        Table.IsView ? null : new(RenameObjectKind.Column, Table.Database, Column.Name, Table.Name);

    /// <inheritdoc />
    protected override Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TreeNodeViewModel>>(Array.Empty<TreeNodeViewModel>());
}
