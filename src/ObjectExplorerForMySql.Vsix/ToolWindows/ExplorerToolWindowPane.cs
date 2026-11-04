using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ObjectExplorerForMySql.Commands;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.ToolWindows;

/// <summary>
/// The "Object Explorer for MySQL" tool window. Classic WPF pane, so the tree can support
/// drag &amp; drop and the Properties window (ADR-001). Toolbar and context menus from the VSCT file (ADR-011).
/// </summary>
[Guid(ToolWindowGuidString)]
public sealed class ExplorerToolWindowPane : ToolWindowPane
{
    /// <summary>Tool window GUID as string.</summary>
    public const string ToolWindowGuidString = "df76e75a-1432-437d-86bc-69df8d28db9e";

    /// <summary>Tool window GUID.</summary>
    public static readonly Guid ToolWindowGuid = new(ToolWindowGuidString);

    private readonly ExplorerViewModel _explorer;
    private TreeNodeViewModel? _observedNode;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExplorerToolWindowPane"/> class.
    /// Called by <see cref="ObjectExplorerPackage"/> with the object from <c>InitializeToolWindowAsync</c>.
    /// </summary>
    public ExplorerToolWindowPane(ExplorerViewModel explorer)
        : base(null)
    {
        Caption = Strings.ToolWindowTitle;
        BitmapImageMoniker = KnownMonikers.Database;
        ToolBar = new CommandID(CommandIds.CommandSet, CommandIds.ExplorerToolbar);

        _explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
        var control = new ExplorerControl { DataContext = _explorer };
        control.ContextMenuRequested += OnContextMenuRequested;
        control.RemoveServerRequested += OnRemoveServerRequested;
        control.RenameCommitted += OnRenameCommitted;
        Content = control;
        _explorer.PropertyChanged += OnExplorerPropertyChanged;
    }

    // The command service is a service of the package (where the commands are registered), not a global one.
    private OleMenuCommandService? CommandService =>
        (Package as IServiceProvider)?.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

    /// <summary>Shows the Visual Studio search box above the tree; it filters objects by name (US-0308).</summary>
    public override bool SearchEnabled => true;

    /// <inheritdoc />
    public override IVsSearchTask? CreateSearch(uint dwCookie, IVsSearchQuery pSearchQuery, IVsSearchCallback pSearchCallback) =>
        pSearchQuery is null || pSearchCallback is null ? null : new ExplorerSearchTask(dwCookie, pSearchQuery, pSearchCallback, _explorer);

    /// <inheritdoc />
    public override void ClearSearch()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _explorer.FilterText = string.Empty;
    }

    /// <inheritdoc />
    public override void ProvideSearchSettings(IVsUIDataSource pSearchSettings)
    {
        // Filter while typing, like Solution Explorer; no progress bar, filtering is in memory.
        Utilities.SetValue(pSearchSettings, SearchSettingsDataSource.SearchStartTypeProperty.Name, (uint)VSSEARCHSTARTTYPE.SST_INSTANT);
        Utilities.SetValue(pSearchSettings, SearchSettingsDataSource.SearchProgressTypeProperty.Name, (uint)VSSEARCHPROGRESSTYPE.SPT_NONE);
        Utilities.SetValue(pSearchSettings, SearchSettingsDataSource.SearchWatermarkProperty.Name, Strings.SearchWatermark);
    }

    /// <inheritdoc />
    public override void OnToolWindowCreated()
    {
        base.OnToolWindowCreated();
        ThreadHelper.ThrowIfNotOnUIThread();

        // Use the key bindings of our own scope (F5 = Refresh) while this window has focus (US-0307).
        if (Frame is IVsWindowFrame frame)
        {
            var scope = ToolWindowGuid;
            var hr = frame.SetGuidProperty((int)__VSFPROPID.VSFPROPID_InheritKeyBindings, ref scope);
            if (ErrorHandler.Failed(hr))
            {
                ExtensionLog.Logger.Warning($"Setting the key binding scope of the tool window failed (0x{hr:X8}); F5 will not refresh.");
            }
        }

        UpdatePropertiesWindow();
        RegisterCopyCommand();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _explorer.PropertyChanged -= OnExplorerPropertyChanged;
            ObserveSelectedNode(null);
        }

        base.Dispose(disposing);
    }

    private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e.PropertyName == nameof(ExplorerViewModel.SelectedNode))
        {
            ObserveSelectedNode(_explorer.SelectedNode);
            UpdatePropertiesWindow();
        }
    }

    // A server that connects, disconnects or is renamed while selected updates the Properties window (US-0402).
    private void ObserveSelectedNode(TreeNodeViewModel? node)
    {
        if (_observedNode is not null)
        {
            _observedNode.PropertyChanged -= OnSelectedNodePropertyChanged;
        }

        _observedNode = node;
        if (_observedNode is not null)
        {
            _observedNode.PropertyChanged += OnSelectedNodePropertyChanged;
        }
    }

    private void OnSelectedNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e.PropertyName is nameof(ServerNodeViewModel.Info) or nameof(ServerNodeViewModel.State) or nameof(TreeNodeViewModel.Label))
        {
            UpdatePropertiesWindow();
        }
    }

    // Edit.Copy (Ctrl+C) while this window is active: copies the name of the selected object (US-0501).
    // Registered on the pane's own command service, so the standard Copy of other windows stays untouched.
    private void RegisterCopyCommand()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (GetService(typeof(IMenuCommandService)) is not OleMenuCommandService paneCommands)
        {
            ExtensionLog.Logger.Warning("Command service of the tool window not available; Copy will not work.");
            return;
        }

        var copy = new OleMenuCommand(OnCopy, new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)VSConstants.VSStd97CmdID.Copy));
        copy.BeforeQueryStatus += (_, _) =>
        {
            copy.Visible = true;
            // Not while a text box has focus (rename box, search box): Ctrl+C copies the text there.
            copy.Enabled = DragData.CanDrag(_explorer, _explorer.SelectedNode)
                && System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase;
        };
        paneCommands.AddCommand(copy);
    }

    // Copies the selected object, all selected columns (US-0502) or all columns of a Columns folder.
    private void OnCopy(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not { } node)
        {
            return;
        }

        try
        {
            if (DragData.CreateFor(_explorer, node) is { } data)
            {
                Clipboard.SetDataObject(data, copy: true);
            }
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            ExtensionLog.Logger.Error($"Copying '{node.Label}' to the clipboard failed.", ex);
        }
    }

    // Tells Visual Studio what the Properties window (F4) shows while this window is active (E04).
    private void UpdatePropertiesWindow()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (GetService(typeof(STrackSelection)) is not ITrackSelection trackSelection)
        {
            return;
        }

        object? properties = null;
        try
        {
            properties = _explorer.SelectedNode?.CreateProperties();
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error("Creating the properties of the selected node failed.", ex);
        }

        var objects = properties is null ? Array.Empty<object>() : new[] { properties };
        var container = new SelectionContainer(true, false)
        {
            SelectableObjects = objects,
            SelectedObjects = objects,
        };
        trackSelection.OnSelectChange(container);
    }

    private void OnContextMenuRequested(object? sender, Point screenPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (CommandService is { } commandService)
        {
            var menu = _explorer.SelectedNode is ServerNodeViewModel ? CommandIds.ServerContextMenu : CommandIds.NodeContextMenu;
            commandService.ShowContextMenu(new CommandID(CommandIds.CommandSet, menu), (int)screenPoint.X, (int)screenPoint.Y);
        }
        else
        {
            ExtensionLog.Logger.Error("Menu command service not available; the context menu cannot be shown.");
        }
    }

    private void OnRemoveServerRequested(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        InvokeCommand(CommandIds.RemoveServer);
    }

    private void OnRenameCommitted(object? sender, RenameCommittedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        (Package as ObjectExplorerPackage)?.CommitRename(e.Node, e.NewName);
    }

    private void InvokeCommand(int commandId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        CommandService?.FindCommand(new CommandID(CommandIds.CommandSet, commandId))?.Invoke();
    }
}
