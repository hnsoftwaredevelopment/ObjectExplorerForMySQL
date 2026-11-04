using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using ObjectExplorerForMySql.Commands;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Security;
using ObjectExplorerForMySql.Dialogs;
using ObjectExplorerForMySql.Editor;
using ObjectExplorerForMySql.Logging;
using ObjectExplorerForMySql.Resources;
using ObjectExplorerForMySql.ToolWindows;
using Task = System.Threading.Tasks.Task;

namespace ObjectExplorerForMySql;

/// <summary>
/// VSSDK package that owns the explorer tool window and its toolbar commands. Loaded on demand
/// when the tool window is shown.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(ExplorerToolWindowPane), Style = VsDockStyle.Tabbed, Window = SolutionExplorerGuidString)]
// The tool window is its own key binding scope, so F5 means Refresh only while it has focus (US-0307).
// Name of the scope in Tools > Options > Keyboard: resource 110 in VSPackage.resx.
[ProvideKeyBindingTable(ExplorerToolWindowPane.ToolWindowGuidString, 110)]
// MySQL Query Editor (US-0701), name: resource 111. In Open With for .sql files with a low priority, so .sql files keep
// their default editor unless the user picks this one as default.
[ProvideEditorFactory(typeof(MySqlEditorFactory), 111, CommonPhysicalViewAttributes = (int)__VSPHYSICALVIEWATTRIBUTES.PVA_SupportsPreview, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]
[ProvideEditorExtension(typeof(MySqlEditorFactory), ".sql", 0, NameResourceID = 111)]
[ProvideEditorLogicalView(typeof(MySqlEditorFactory), VSConstants.LOGVIEWID.TextView_string)]
// Key binding scope of the MySQL Query Editor (US-0703, ADR-013): Ctrl+Shift+E and Alt+Break only there.
[ProvideKeyBindingTable(MySqlEditorFactory.GuidString, 111, AllowNavKeyBinding = true)]
// View Data (US-0801): one window per table in the document well, not reopened at startup.
[ProvideToolWindow(typeof(ObjectExplorerForMySql.Data.TableDataToolWindow), MultiInstances = true, Transient = true, Style = VsDockStyle.MDI)]
// View Dependencies (US-1303): one window, as a tab next to the Output window, not reopened at startup.
[ProvideToolWindow(typeof(DependenciesToolWindow), Transient = true, Style = VsDockStyle.Tabbed, Window = OutputWindowGuidString)]
// Activity Monitor (US-1501): one window per connection in the document well, not reopened at startup.
[ProvideToolWindow(typeof(ActivityMonitorToolWindow), MultiInstances = true, Transient = true, Style = VsDockStyle.MDI)]
public sealed class ObjectExplorerPackage : AsyncPackage
{
    /// <summary>Package GUID.</summary>
    public const string PackageGuidString = "2f7a445f-2947-4e18-b780-619cebe06f63";

    /// <summary>Solution Explorer tool window GUID; the explorer opens as a tab next to it, like SQL Server Object Explorer.</summary>
    private const string SolutionExplorerGuidString = "3ae79031-e1bc-11d0-8f78-00a0c9110057";

    /// <summary>Output window GUID; the dependency window opens as a tab next to it.</summary>
    private const string OutputWindowGuidString = "34e76e81-ee4a-11d0-ae2e-00a0c90fffc3";

    // Folder names of the tree; also used to find objects in it (US-1303).
    private static readonly ExplorerTexts ExplorerTexts = new()
    {
        Loading = Strings.NodeLoading, Databases = Strings.NodeDatabases, SystemDatabases = Strings.NodeSystemDatabases, Tables = Strings.NodeTables, Columns = Strings.NodeColumns,
        Keys = Strings.NodeKeys, Constraints = Strings.NodeConstraints, Triggers = Strings.NodeTriggers, Indexes = Strings.NodeIndexes,
        Views = Strings.NodeViews, StoredProcedures = Strings.NodeStoredProcedures, Functions = Strings.NodeFunctions, Events = Strings.NodeEvents, Parameters = Strings.NodeParameters,
        Filtered = Strings.NodeFiltered, Security = Strings.NodeSecurity, Users = Strings.NodeUsers, Roles = Strings.NodeRoles,
        WithGrantOption = Strings.NodeWithGrantOption,
    };

    private static readonly Core.Dependencies.DependencyTexts DependencyTexts = new()
    {
        Title = Strings.DependenciesTitle, UsedBy = Strings.DependenciesUsedBy, Uses = Strings.DependenciesUses, None = Strings.DependenciesNone,
        ForeignKey = Strings.DependenciesForeignKey, Trigger = Strings.DependenciesTrigger, SourceText = Strings.DependenciesSourceText,
        Incomplete = Strings.DependenciesIncomplete, Loading = Strings.DependenciesLoading, Failed = Strings.DependenciesReadFailed,
        Kinds = [Strings.KindTable, Strings.KindView, Strings.KindProcedure, Strings.KindFunction, Strings.KindTrigger, Strings.KindEvent],
    };

    private static readonly Core.Activity.ActivityTexts ActivityTexts = new()
    {
        Summary = Strings.ActivitySummary, LoadFailed = Strings.ActivityLoadFailed, KillFailed = Strings.ActivityKillFailed,
    };

    private Core.Dependencies.DependencyViewModel? _dependencies;
    private Core.Activity.MySqlActivitySource? _activitySource;
    private readonly Dictionary<Guid, int> _activityWindows = [];

    private readonly ExplorerViewModel _explorer = new(new ExplorerContext(
        new MySqlMetadataProvider(ExtensionLog.Logger),
        new PasswordPromptService(),
        ExplorerTexts,
        ExtensionLog.Logger)
    {
        // US-1001: what the account may do with users, checked after every connect.
        PrivilegeChecker = new PrivilegeChecker(new MySqlGrantSource(), ExtensionLog.Logger),

        // US-1002: the Security folder with users, roles and grants.
        AccountReader = _accountReader,
    });

    private static readonly MySqlAccountReader _accountReader = new();
    private readonly ConnectionStore _store = new(ConnectionStore.DefaultFilePath, new DpapiSecretProtector(), ExtensionLog.Logger);
    private readonly DocumentLinkStore _linkStore = new(DocumentLinkStore.DefaultFilePath, ExtensionLog.Logger);
    private readonly Core.Data.MySqlTableDataSource _dataSource = new(ExtensionLog.Logger);
    private readonly ObjectDeleter _deleter = new(new MySqlSchemaCommandRunner(), ExtensionLog.Logger);
    private readonly ObjectRenamer _renamer = new(new MySqlSchemaCommandRunner(), ExtensionLog.Logger);
    private readonly UserManager _userManager = new(new MySqlSchemaCommandRunner(), ExtensionLog.Logger);
    private readonly DatabaseCreator _databaseCreator = new(new MySqlSchemaCommandRunner(), new MySqlCharacterSetSource(), ExtensionLog.Logger);
    private readonly Dictionary<(Guid ConnectionId, string Database, string Table), int> _dataWindows = [];
    private int _nextDataWindowId;

    /// <inheritdoc />
    public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
        toolWindowType == ExplorerToolWindowPane.ToolWindowGuid ? this : null;

    /// <inheritdoc />
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        var version = typeof(ObjectExplorerPackage).Assembly.GetName().Version;
        ExtensionLog.Logger.Info($"Object Explorer for MySQL loaded (assembly version {version}).");

        // File access stays on the background thread; the tree is filled on the UI thread.
        var saved = _store.Load();
        var links = _linkStore.Load();

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        foreach (var definition in saved)
        {
            _explorer.AddServer(definition);
        }

        _explorer.ConnectionsChanged += OnConnectionsChanged;

        // Query editor (US-0701): the links and servers must be in place before the first query document opens.
        _explorer.Documents.Load(links);
        _explorer.Documents.Changed += (_, _) => _linkStore.Save(_explorer.Documents.Snapshot());
        QueryEditorServices.Schema = new Core.Completion.SchemaCache(_explorer.Metadata, ExtensionLog.Logger);
        QueryEditorServices.Explorer = _explorer;
        if (await GetServiceAsync(typeof(SVsRunningDocumentTable)) is IVsRunningDocumentTable documentTable)
        {
            QueryEditorKeyBindings.Start(documentTable);
        }

        RegisterEditorFactory(new MySqlEditorFactory());

        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
        {
            commandService.AddCommand(new MenuCommand(OnAddServer, new CommandID(CommandIds.CommandSet, CommandIds.AddServer)));
            AddServerCommand(commandService, CommandIds.RemoveServer, OnRemoveServer, _ => true);
            AddServerCommand(commandService, CommandIds.ServerProperties, OnServerProperties, _ => true);
            var refresh = new OleMenuCommand(OnRefresh, new CommandID(CommandIds.CommandSet, CommandIds.Refresh));
            refresh.BeforeQueryStatus += (_, _) =>
            {
                refresh.Visible = true;
                refresh.Enabled = _explorer.RefreshTarget is not null;
            };
            commandService.AddCommand(refresh);
            var viewCode = new OleMenuCommand(OnViewCode, new CommandID(CommandIds.CommandSet, CommandIds.ViewCode));
            viewCode.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                viewCode.Visible = node?.CreateCodeObject() is not null;
                viewCode.Enabled = viewCode.Visible && node?.OwningServer is { State: ConnectionState.Connected };
            };
            commandService.AddCommand(viewCode);
            var viewData = new OleMenuCommand(OnViewData, new CommandID(CommandIds.CommandSet, CommandIds.ViewData));
            viewData.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                viewData.Visible = node?.CreateCodeObject() is { Kind: CodeObjectKind.Table or CodeObjectKind.View };
                viewData.Enabled = viewData.Visible && node?.OwningServer is { State: ConnectionState.Connected };
            };
            commandService.AddCommand(viewData);
            var viewDependencies = new OleMenuCommand(OnViewDependencies, new CommandID(CommandIds.CommandSet, CommandIds.ViewDependencies));
            viewDependencies.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                viewDependencies.Visible = Core.Dependencies.DependencyNavigator.SubjectOf(node) is not null;
                viewDependencies.Enabled = viewDependencies.Visible && node?.OwningServer is { State: ConnectionState.Connected };
            };
            commandService.AddCommand(viewDependencies);
            var deleteObject = new OleMenuCommand(OnDeleteObject, new CommandID(CommandIds.CommandSet, CommandIds.DeleteObject));
            deleteObject.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                deleteObject.Visible = ObjectDeleter.TargetOf(node) is not null;

                // Disabled while a text box has focus (the search box), so Del keeps deleting text there.
                deleteObject.Enabled = deleteObject.Visible && node?.OwningServer is { State: ConnectionState.Connected }
                    && System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase;
            };
            commandService.AddCommand(deleteObject);
            var renameObject = new OleMenuCommand(OnRenameObject, new CommandID(CommandIds.CommandSet, CommandIds.RenameObject));
            renameObject.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                renameObject.Visible = ObjectRenamer.TargetOf(node) is not null;
                renameObject.Enabled = renameObject.Visible && node is { IsEditing: false, OwningServer.State: ConnectionState.Connected }
                    && System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase;
            };
            commandService.AddCommand(renameObject);
            var newDatabase = new OleMenuCommand(OnNewDatabase, new CommandID(CommandIds.CommandSet, CommandIds.NewDatabase));
            newDatabase.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                newDatabase.Visible = DatabaseCreator.IsTarget(node);
                newDatabase.Enabled = newDatabase.Visible && node?.OwningServer is { State: ConnectionState.Connected };
            };
            commandService.AddCommand(newDatabase);
            var newObject = new OleMenuCommand(OnNewObject, new CommandID(CommandIds.CommandSet, CommandIds.NewObject));
            newObject.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                var request = NewObjectTemplates.TargetOf(node);
                newObject.Visible = request is not null;
                newObject.Enabled = request is not null && node?.OwningServer is { State: ConnectionState.Connected };
                if (request is not null)
                {
                    newObject.Text = Strings.NewObjectText(request.Kind);
                }
            };
            commandService.AddCommand(newObject);

            // US-1003: user management; each command is only shown when the account may manage users.
            AddNodeCommand(commandService, CommandIds.NewUser, OnNewUser, UserManager.IsNewUserTarget);
            AddNodeCommand(commandService, CommandIds.ChangePassword, OnChangePassword, node => UserManager.PasswordTarget(node) is not null);
            AddNodeCommand(commandService, CommandIds.GrantOnDatabase, OnGrantOnDatabase, node => UserManager.GrantTarget(node) is not null);
            AddNodeCommand(commandService, CommandIds.Revoke, OnRevoke, node => UserManager.RevokeOf(node) is not null);
            AddNodeCommand(commandService, CommandIds.NewRole, OnNewRole, UserManager.IsNewRoleTarget);
            AddNodeCommand(commandService, CommandIds.GrantRole, OnGrantRole, node => UserManager.GrantRoleTarget(node) is not null);
            AddNodeCommand(commandService, CommandIds.CopyConnectionString, OnCopyConnectionString, node => node is ServerNodeViewModel or DatabaseNodeViewModel);
            var scriptDatabase = new OleMenuCommand(OnScriptDatabase, new CommandID(CommandIds.CommandSet, CommandIds.ScriptDatabase));
            scriptDatabase.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                scriptDatabase.Visible = node is DatabaseNodeViewModel { Database.IsSystem: false };
                scriptDatabase.Enabled = scriptDatabase.Visible && node?.OwningServer is { State: ConnectionState.Connected };
            };
            commandService.AddCommand(scriptDatabase);
            var generateDocumentation = new OleMenuCommand(OnGenerateDocumentation, new CommandID(CommandIds.CommandSet, CommandIds.GenerateDocumentation));
            generateDocumentation.BeforeQueryStatus += (_, _) =>
            {
                var node = _explorer.SelectedNode;
                generateDocumentation.Visible = node is DatabaseNodeViewModel { Database.IsSystem: false };
                generateDocumentation.Enabled = generateDocumentation.Visible && node?.OwningServer is { State: ConnectionState.Connected };
            };
            commandService.AddCommand(generateDocumentation);
            var newQuery = new OleMenuCommand(OnNewQuery, new CommandID(CommandIds.CommandSet, CommandIds.NewQuery));
            newQuery.BeforeQueryStatus += (_, _) =>
            {
                newQuery.Visible = _explorer.SelectedNode is ServerNodeViewModel or DatabaseNodeViewModel;
                newQuery.Enabled = newQuery.Visible;
            };
            commandService.AddCommand(newQuery);
            var activityMonitor = new OleMenuCommand(OnActivityMonitor, new CommandID(CommandIds.CommandSet, CommandIds.ActivityMonitor));
            activityMonitor.BeforeQueryStatus += (_, _) =>
            {
                activityMonitor.Visible = _explorer.SelectedNode is ServerNodeViewModel;
                activityMonitor.Enabled = _explorer.SelectedNode is ServerNodeViewModel { State: ConnectionState.Connected };
            };
            commandService.AddCommand(activityMonitor);

            // US-0703: Execute and Cancel Query work on the active document of the MySQL Query Editor.
            var executeQuery = new OleMenuCommand(
                (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (ActiveQueryView() is { } view)
                    {
                        QueryEditorServices.Execute(view);
                    }
                },
                new CommandID(CommandIds.CommandSet, CommandIds.ExecuteQuery));
            executeQuery.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var view = ActiveQueryView();
                executeQuery.Visible = view is not null;
                executeQuery.Enabled = view is not null && QueryEditorServices.GetRunner(view.TextDataModel.DocumentBuffer) is { IsRunning: false };
            };
            commandService.AddCommand(executeQuery);

            // US-1301: Explain shows the estimated plan of the selection or the statement at the caret.
            var explainQuery = new OleMenuCommand(
                (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (ActiveQueryView() is { } view)
                    {
                        QueryEditorServices.Explain(view);
                    }
                },
                new CommandID(CommandIds.CommandSet, CommandIds.ExplainQuery));
            explainQuery.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var view = ActiveQueryView();
                explainQuery.Visible = view is not null;
                explainQuery.Enabled = view is not null && QueryEditorServices.GetRunner(view.TextDataModel.DocumentBuffer) is { IsRunning: false };
            };
            commandService.AddCommand(explainQuery);

            // US-1302: Explain Analyze runs the SELECT and shows the actual rows and times.
            var explainAnalyzeQuery = new OleMenuCommand(
                (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (ActiveQueryView() is { } view)
                    {
                        QueryEditorServices.Explain(view, analyze: true);
                    }
                },
                new CommandID(CommandIds.CommandSet, CommandIds.ExplainAnalyzeQuery));
            explainAnalyzeQuery.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var view = ActiveQueryView();
                explainAnalyzeQuery.Visible = view is not null;
                explainAnalyzeQuery.Enabled = view is not null && QueryEditorServices.GetRunner(view.TextDataModel.DocumentBuffer) is { IsRunning: false };
            };
            commandService.AddCommand(explainAnalyzeQuery);
            var cancelQuery = new OleMenuCommand(
                (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (ActiveQueryView() is { } view)
                    {
                        QueryEditorServices.GetRunner(view.TextDataModel.DocumentBuffer)?.Cancel();
                    }
                },
                new CommandID(CommandIds.CommandSet, CommandIds.CancelQuery));
            cancelQuery.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var view = ActiveQueryView();
                cancelQuery.Visible = view is not null;
                cancelQuery.Enabled = view is not null && QueryEditorServices.GetRunner(view.TextDataModel.DocumentBuffer) is { IsRunning: true };
            };
            commandService.AddCommand(cancelQuery);
            foreach (ScriptKind kind in Enum.GetValues(typeof(ScriptKind)))
            {
                AddScriptCommand(commandService, kind, toClipboard: false);
                AddScriptCommand(commandService, kind, toClipboard: true);
            }

            var collapseAll = new OleMenuCommand(OnCollapseAll, new CommandID(CommandIds.CommandSet, CommandIds.CollapseAll));
            collapseAll.BeforeQueryStatus += (_, _) => collapseAll.Enabled = !_explorer.IsEmpty;
            commandService.AddCommand(collapseAll);
            AddServerCommand(commandService, CommandIds.DisconnectServer, OnDisconnectServer, server => server.CanDisconnect);
        }
        else
        {
            ExtensionLog.Logger.Error("Menu command service not available; toolbar commands will not work.");
        }
    }

    /// <inheritdoc />
    protected override string? GetToolWindowTitle(Type toolWindowType, int id) =>
        toolWindowType == typeof(ExplorerToolWindowPane) ? Strings.ToolWindowTitle : base.GetToolWindowTitle(toolWindowType, id);

    /// <inheritdoc />
    protected override Task<object?> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(_explorer);

    private void AddServerCommand(OleMenuCommandService commandService, int commandId, EventHandler handler, Func<ServerNodeViewModel, bool> isEnabled)
    {
        var command = new OleMenuCommand(handler, new CommandID(CommandIds.CommandSet, commandId));
        command.BeforeQueryStatus += (_, _) =>
        {
            command.Visible = true;
            command.Enabled = _explorer.SelectedServer is { } server && isEnabled(server);
        };
        commandService.AddCommand(command);
    }

    private void OnConnectionsChanged(object? sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SaveConnections();
    }

    private void OnRefresh(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.RefreshTarget is { } node)
        {
            ExtensionLog.Logger.Debug($"Refreshing '{node.Label}'.");

            // US-1401: the completion list reads the metadata of this connection again.
            if (node.OwningServer is { } server)
            {
                QueryEditorServices.Schema?.Clear(server.Definition.Id);
            }

            _ = node.RefreshAsync();
        }
    }

    private void OnViewCode(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not { } node || node.CreateCodeObject() is not { } code || node.OwningServer is not { } server)
        {
            return;
        }

#pragma warning disable VSSDK007 // Fire and forget on purpose: the menu click must not wait for the server; errors are reported inside.
        _ = JoinableTaskFactory.RunAsync(() => ViewCodeAsync(server.Definition, code));
#pragma warning restore VSSDK007
    }

    // US-0801: one View Data window per connection, database and table; a second View Data shows the same window.
    private void OnViewData(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not { } node || node.CreateCodeObject() is not { Kind: CodeObjectKind.Table or CodeObjectKind.View } code
            || node.OwningServer is not { } server)
        {
            return;
        }

        var key = (server.Definition.Id, code.Database, code.Name);
        try
        {
            if (!_dataWindows.TryGetValue(key, out var id))
            {
                id = _nextDataWindowId++;
                _dataWindows[key] = id;
            }

            if (FindToolWindow(typeof(ObjectExplorerForMySql.Data.TableDataToolWindow), id, create: true) is not ObjectExplorerForMySql.Data.TableDataToolWindow window
                || window.Frame is not IVsWindowFrame frame)
            {
                throw new InvalidOperationException("The View Data window could not be created.");
            }

            // A closed window keeps its pane; it gets a new model, so it loads fresh data.
            if (window.Model is null)
            {
                window.Show(new Core.Data.TableDataViewModel(server, _explorer.Metadata, _dataSource, code.Database, code.Name, code.Kind == CodeObjectKind.View, ExtensionLog.Logger));
                ExtensionLog.Logger.Info($"View Data of {code.Database}.{code.Name} ('{server.Label}').");
            }

            ErrorHandler.ThrowOnFailure(frame.Show());
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            ExtensionLog.Logger.Error($"View Data of {code.Database}.{code.Name} failed.", ex);
            ShowMessage(Strings.Format(Strings.ViewDataFailed, $"{code.Database}.{code.Name}", ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // US-1501: one activity monitor per connection; a second Activity Monitor shows the same window.
    private void OnActivityMonitor(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not ServerNodeViewModel server)
        {
            return;
        }

        try
        {
            if (!_activityWindows.TryGetValue(server.Definition.Id, out var id))
            {
                id = _activityWindows.Count;
                _activityWindows[server.Definition.Id] = id;
            }

            if (FindToolWindow(typeof(ActivityMonitorToolWindow), id, create: true) is not ActivityMonitorToolWindow window
                || window.Frame is not IVsWindowFrame frame)
            {
                throw new InvalidOperationException("The activity monitor could not be created.");
            }

            // A closed window keeps its pane; it gets a new model, so it reads the sessions again.
            if (window.Model is null)
            {
                _activitySource ??= new Core.Activity.MySqlActivitySource(ExtensionLog.Logger);
                window.Show(new Core.Activity.ActivityMonitorViewModel(server, _activitySource, ActivityTexts, ExtensionLog.Logger));
                ExtensionLog.Logger.Info($"Activity Monitor of '{server.Label}'.");
            }

            ErrorHandler.ThrowOnFailure(frame.Show());
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            ExtensionLog.Logger.Error($"Opening the activity monitor of '{server.Label}' failed.", ex);
            ShowMessage(Strings.Format(Strings.ActivityOpenFailed, server.Label, ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // US-1303: one dependency window; every View Dependencies replaces its content.
    private void OnViewDependencies(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Core.Dependencies.DependencyNavigator.SubjectOf(_explorer.SelectedNode) is not { } subject || _explorer.SelectedNode?.OwningServer is not { } server)
        {
            return;
        }

        try
        {
            if (FindToolWindow(typeof(DependenciesToolWindow), 0, create: true) is not DependenciesToolWindow window
                || window.Frame is not IVsWindowFrame frame)
            {
                throw new InvalidOperationException("The dependency window could not be created.");
            }

            _dependencies ??= new Core.Dependencies.DependencyViewModel(new Core.Dependencies.MySqlDependencySource(ExtensionLog.Logger), DependencyTexts, ExtensionLog.Logger);

            // Once per pane, also when Visual Studio created a new pane.
            window.ObjectActivated -= OnDependencyActivated;
            window.ObjectActivated += OnDependencyActivated;
            window.Show(_dependencies);
            ErrorHandler.ThrowOnFailure(frame.Show());
            var model = _dependencies;
#pragma warning disable VSSDK007 // Fire and forget: the window shows loading and errors itself.
            _ = JoinableTaskFactory.RunAsync(() => model.LoadAsync(server, subject));
#pragma warning restore VSSDK007
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            ExtensionLog.Logger.Error($"View Dependencies of {subject.Database}.{subject.Name} failed.", ex);
            ShowMessage(Strings.Format(Strings.DependenciesFailed, $"{subject.Database}.{subject.Name}", ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // Double-click in the dependency window: show the explorer and select the object there.
    private void OnDependencyActivated(object? sender, Core.Dependencies.SchemaObject target)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_dependencies?.Server is not { } server)
        {
            return;
        }

#pragma warning disable VSSDK007 // Fire and forget: a missing object is only logged.
        _ = JoinableTaskFactory.RunAsync(async () =>
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            await ShowToolWindowAsync(typeof(ExplorerToolWindowPane), 0, create: true, DisposalToken);
            if (await Core.Dependencies.DependencyNavigator.SelectAsync(server, target, ExplorerTexts) is null)
            {
                ExtensionLog.Logger.Info($"{target.Database}.{target.Name} was not found in the explorer.");
            }
        });
#pragma warning restore VSSDK007
    }

    // US-0901: ask with the exact SQL, then drop on a background thread and refresh the parent node.
    private void OnDeleteObject(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not { OwningServer: { State: ConnectionState.Connected } } node || ObjectDeleter.TargetOf(node) is not { } target)
        {
            return;
        }

        if (new DeleteObjectDialog(target).ShowModal() != true)
        {
            return;
        }

#pragma warning disable VSSDK007 // Fire and forget on purpose: the menu click must not wait for the server; errors are reported inside.
        _ = JoinableTaskFactory.RunAsync(() => DeleteObjectAsync(node, target));
#pragma warning restore VSSDK007
    }

    private async Task DeleteObjectAsync(TreeNodeViewModel node, DropTarget target)
    {
        try
        {
            await _deleter.DeleteAsync(node, target, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            // Visual Studio is closing.
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"Deleting {target.DisplayName} failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(Strings.DeleteFailed, Strings.DropKind(target.Kind), target.DisplayName, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // A node command that is visible and enabled when the check on the selected node passes.
    private void AddNodeCommand(OleMenuCommandService commandService, int commandId, EventHandler handler, Func<TreeNodeViewModel?, bool> isAvailable)
    {
        var command = new OleMenuCommand(handler, new CommandID(CommandIds.CommandSet, commandId));
        command.BeforeQueryStatus += (_, _) =>
        {
            command.Visible = isAvailable(_explorer.SelectedNode);
            command.Enabled = command.Visible;
        };
        commandService.AddCommand(command);
    }

    // US-1003: New User on the Users folder. The users are loaded first, so the dialog can say a name exists.
    private void OnNewUser(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is { } folder && UserManager.IsNewUserTarget(folder))
        {
#pragma warning disable VSSDK007 // Fire and forget on purpose: errors are reported inside.
            _ = JoinableTaskFactory.RunAsync(() => NewUserAsync(folder));
#pragma warning restore VSSDK007
        }
    }

    private async Task NewUserAsync(TreeNodeViewModel folder)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        await folder.EnsureLoadedAsync();
        var existing = folder.Children.OfType<AccountNodeViewModel>().Select(a => a.Account).ToList();
        var dialog = new UserDialog(new UserDialogViewModel(existing));
        if (dialog.ShowModal() != true)
        {
            return;
        }

        var model = dialog.Model;
        await RunUserActionAsync(() => _userManager.CreateUserAsync(folder, model.Statement, model.Label, DisposalToken), Strings.NewUserFailed, model.Label);
    }

    // US-1004: New Role on the Roles folder. All accounts are read, because on MySQL a role may not have the name of a user.
    private void OnNewRole(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is { OwningServer: { } server } folder && UserManager.IsNewRoleTarget(folder))
        {
#pragma warning disable VSSDK007 // Fire and forget on purpose: errors are reported inside.
            _ = JoinableTaskFactory.RunAsync(() => NewRoleAsync(folder, server));
#pragma warning restore VSSDK007
        }
    }

    private async Task NewRoleAsync(TreeNodeViewModel folder, ServerNodeViewModel server)
    {
        IReadOnlyList<AccountInfo> existing = [];
        try
        {
            existing = await _accountReader.GetAccountsAsync(server.Definition, server.ServerVersion, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // The dialog still works; a duplicate name then gets the server error.
            ExtensionLog.Logger.Warning($"Reading the accounts for New Role failed: {SensitiveDataMasker.MaskText(ex.Message)}");
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        var dialog = new RoleDialog(new RoleDialogViewModel(existing, ServerCapabilities.FromVersion(server.ServerVersion).IsMariaDb));
        if (dialog.ShowModal() == true)
        {
            var model = dialog.Model;
            await RunUserActionAsync(() => _userManager.CreateRoleAsync(folder, model.Sql, model.Label, DisposalToken), Strings.NewRoleFailed, model.Label);
        }
    }

    // US-0505: the plain connection string, with the database of a database node and a placeholder for the password.
    private void OnCopyConnectionString(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var text = _explorer.SelectedNode switch
        {
            ServerNodeViewModel server => Core.DragDrop.ConnectionStringText.Build(server.Definition),
            DatabaseNodeViewModel { OwningServer: { } server } database => Core.DragDrop.ConnectionStringText.Build(server.Definition, database.Database.Name),
            _ => null,
        };
        if (text is null)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            ExtensionLog.Logger.Info($"Copied the connection string of '{_explorer.SelectedNode?.Label}' (password placeholder).");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            ExtensionLog.Logger.Error("Copying the connection string failed.", ex);
        }
    }

    // US-1005: Grant Role on a user. Reads the roles, the user's grants (roles it has are not offered) and, on MySQL,
    // its default roles (SET DEFAULT ROLE replaces the whole list, so the current ones are kept).
    private void OnGrantRole(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (UserManager.GrantRoleTarget(_explorer.SelectedNode) is { OwningServer: { } server } user)
        {
#pragma warning disable VSSDK007 // Fire and forget on purpose: errors are reported inside.
            _ = JoinableTaskFactory.RunAsync(() => GrantRoleAsync(user, server));
#pragma warning restore VSSDK007
        }
    }

    private async Task GrantRoleAsync(AccountNodeViewModel user, ServerNodeViewModel server)
    {
        var isMariaDb = ServerCapabilities.FromVersion(server.ServerVersion).IsMariaDb;
        IReadOnlyList<AccountInfo> roles;
        IReadOnlyList<AccountInfo>? defaults = null;
        try
        {
            var accounts = await _accountReader.GetAccountsAsync(server.Definition, server.ServerVersion, DisposalToken);
            var grants = await _accountReader.GetGrantsAsync(server.Definition, user.Account, DisposalToken);
            roles = GrantRoleDialogViewModel.AvailableRoles(accounts, grants);
            if (!isMariaDb)
            {
                try
                {
                    defaults = await _accountReader.GetDefaultRolesAsync(server.Definition, user.Account, DisposalToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ExtensionLog.Logger.Warning($"Reading the default roles of {user.Label} failed: {SensitiveDataMasker.MaskText(ex.Message)}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"Reading the roles for {user.Label} failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(Strings.GrantRoleFailed, user.Label, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (roles.Count == 0)
        {
            ShowMessage(Strings.Format(Strings.GrantRoleNone, user.Label), OLEMSGICON.OLEMSGICON_INFO);
            return;
        }

        var dialog = new GrantRoleDialog(new GrantRoleDialogViewModel(user.Account, roles, defaults, isMariaDb));
        if (dialog.ShowModal() == true)
        {
            var statements = dialog.Statements;
            await RunUserActionAsync(() => _userManager.ChangeGrantsAsync(user, statements, DisposalToken), Strings.GrantRoleFailed, user.Label);
        }
    }

    private void OnChangePassword(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (UserManager.PasswordTarget(_explorer.SelectedNode) is not { } account)
        {
            return;
        }

        var dialog = new UserDialog(new UserDialogViewModel(account.Account));
        if (dialog.ShowModal() == true)
        {
            var statement = dialog.Model.Statement;
#pragma warning disable VSSDK007 // Fire and forget on purpose: errors are reported inside.
            _ = JoinableTaskFactory.RunAsync(() => RunUserActionAsync(() => _userManager.ChangePasswordAsync(account, statement, DisposalToken), Strings.ChangePasswordFailed, account.Label));
#pragma warning restore VSSDK007
        }
    }

    private void OnGrantOnDatabase(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (UserManager.GrantTarget(_explorer.SelectedNode) is { OwningServer: { } server } account)
        {
#pragma warning disable VSSDK007 // Fire and forget on purpose: errors are reported inside.
            _ = JoinableTaskFactory.RunAsync(() => GrantOnDatabaseAsync(account, server));
#pragma warning restore VSSDK007
        }
    }

    private async Task GrantOnDatabaseAsync(AccountNodeViewModel account, ServerNodeViewModel server)
    {
        IReadOnlyList<string> databases;
        try
        {
            databases = (await _explorer.Metadata.GetDatabasesAsync(server.Definition, DisposalToken)).Select(d => d.Name).ToList();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error("Reading the databases for Grant failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(Strings.GrantLoadFailed, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        var dialog = new GrantDialog(new GrantDialogViewModel(account.Account, databases));
        if (dialog.ShowModal() == true)
        {
            var sql = dialog.Sql;
            await RunUserActionAsync(() => _userManager.ChangeGrantsAsync(account, sql, DisposalToken), Strings.GrantFailed, account.Label);
        }
    }

    private void OnRevoke(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not { Parent: AccountNodeViewModel account } node || UserManager.RevokeOf(node) is not { } sql)
        {
            return;
        }

        var dialog = new ConfirmSqlDialog(Strings.RevokeTitle, Strings.Format(Strings.RevokePrompt, account.Label), sql, Strings.ButtonRevoke);
        if (dialog.ShowModal() == true)
        {
#pragma warning disable VSSDK007 // Fire and forget on purpose: errors are reported inside.
            _ = JoinableTaskFactory.RunAsync(() => RunUserActionAsync(() => _userManager.ChangeGrantsAsync(account, sql, DisposalToken), Strings.RevokeFailed, account.Label));
#pragma warning restore VSSDK007
        }
    }

    // Runs a user action and shows a server error with the account in the message.
    private async Task RunUserActionAsync(Func<Task> action, string failedFormat, string account)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Password prompt cancelled or Visual Studio is closing.
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"User action on {account} failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(failedFormat, account, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // US-0904: a new query document with the template, linked to the server and database of the folder.
    private void OnNewObject(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not { OwningServer: { State: ConnectionState.Connected } server } node
            || NewObjectTemplates.TargetOf(node) is not { } request)
        {
            return;
        }

        try
        {
            var text = NewObjectTemplates.Generate(request, server.Definition.Label, Settings.DragDropSettingsMonitor.Current);
            OpenSqlDocument(server.Definition, request.Database, request.FileName, text, alwaysNew: true);
            ExtensionLog.Logger.Info($"Opened the New {request.Kind} template for {request.Database}{(request.Table is null ? string.Empty : "." + request.Table)} ('{server.Label}').");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            ExtensionLog.Logger.Error($"Opening the New {request.Kind} template failed.", ex);
            ShowMessage(Strings.Format(Strings.NewObjectFailed, ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // US-0903: read the character sets, show the dialog, create the database and select it.
    private void OnNewDatabase(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is { OwningServer.State: ConnectionState.Connected } folder && DatabaseCreator.IsTarget(folder))
        {
#pragma warning disable VSSDK007 // Fire and forget on purpose: errors are reported inside.
            _ = JoinableTaskFactory.RunAsync(() => NewDatabaseAsync(folder));
#pragma warning restore VSSDK007
        }
    }

    private async Task NewDatabaseAsync(TreeNodeViewModel folder)
    {
        var server = folder.OwningServer!;
        CharacterSetCatalog? catalog;
        try
        {
            catalog = await _databaseCreator.LoadCatalogAsync(folder, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"Reading the character sets of '{server.Label}' failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(Strings.NewDatabaseLoadFailed, server.Label, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (catalog is null)
        {
            return;
        }

        var dialog = new NewDatabaseDialog(catalog);
        if (dialog.ShowModal() != true)
        {
            return;
        }

        var name = dialog.DatabaseName;
        try
        {
            await _databaseCreator.CreateAsync(folder, name, dialog.Sql, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            // Visual Studio is closing.
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"Creating database {name} failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(Strings.NewDatabaseFailed, name, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // US-0902: F2 or Rename starts the inline edit in the tree; the tree calls CommitRename when it ends.
    private void OnRenameObject(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is { OwningServer.State: ConnectionState.Connected } node && ObjectRenamer.TargetOf(node) is not null)
        {
            _explorer.ClearMultiSelection();
            node.IsEditing = true;
        }
    }

    /// <summary>
    /// Renames the object of <paramref name="node"/> after the inline edit (US-0902). An unchanged or empty name does
    /// nothing; a name MySQL does not accept gets a message before anything runs.
    /// </summary>
    internal void CommitRename(TreeNodeViewModel node, string newName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ObjectRenamer.TargetOf(node) is not { } target)
        {
            return;
        }

        switch (target.Check(newName))
        {
            case NameProblem.None:
                break;
            case NameProblem.TooLong:
                ShowMessage(Strings.Format(Strings.RenameNameTooLong, RenameTarget.MaxNameLength), OLEMSGICON.OLEMSGICON_WARNING);
                return;
            case NameProblem.EndsWithSpace:
                ShowMessage(Strings.RenameNameEndsWithSpace, OLEMSGICON.OLEMSGICON_WARNING);
                return;
            default:
                return;
        }

#pragma warning disable VSSDK007 // Fire and forget on purpose: the edit box must close at once; errors are reported inside.
        _ = JoinableTaskFactory.RunAsync(() => RenameObjectAsync(node, target, newName));
#pragma warning restore VSSDK007
    }

    private async Task RenameObjectAsync(TreeNodeViewModel node, RenameTarget target, string newName)
    {
        try
        {
            await _renamer.RenameAsync(node, target, newName, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            // Visual Studio is closing.
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"Renaming {target.DisplayName} to {newName} failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(
                Strings.Format(Strings.RenameFailed, Strings.RenameKind(target.Kind), target.DisplayName, newName, SensitiveDataMasker.MaskText(ex.Message)),
                OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // US-0601: SHOW CREATE on a background thread, then write a .sql file in %TEMP% and open it in Visual Studio.
    private async Task ViewCodeAsync(ConnectionDefinition definition, CodeObject code)
    {
        var objectName = $"{code.Database}.{code.Name}";
        try
        {
            await TaskScheduler.Default;
            var source = await _explorer.Metadata.GetCreateStatementAsync(definition, code, DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

            if (source is null)
            {
                ExtensionLog.Logger.Warning($"No source returned for {code.Keyword} {objectName} on '{definition.Label}'.");
                ShowMessage(Strings.Format(Strings.ViewCodeNoSource, objectName), OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            OpenSqlDocument(definition, code.Database, code.FileName, CodeDocument.Format(code, source, definition.Label));
            ExtensionLog.Logger.Info($"Opened the code of {code.Keyword} {objectName} ('{definition.Label}').");
        }
        catch (OperationCanceledException)
        {
            // Visual Studio is closing.
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"Showing the code of {objectName} failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(Strings.ViewCodeFailed, objectName, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    // Writes a .sql file in %TEMP%\ObjectExplorerForMySql\<connection id> and opens it, linked to the connection
    // (US-0601, US-0602). When the document is already open it is shown as it is, so unsaved edits are kept.
    // With alwaysNew (Script Database, US-0603) an open document is left alone and the new text gets a free name:
    // "sakila.DATABASE (2).sql".
    private void OpenSqlDocument(ConnectionDefinition definition, string database, string fileName, string text, bool alwaysNew = false)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var folder = QueryFolder(definition);
        var path = Path.Combine(folder, fileName);
        for (var number = 2; alwaysNew && VsShellUtilities.IsDocumentOpen(this, path, Guid.Empty, out _, out _, out _); number++)
        {
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)} ({number}){Path.GetExtension(fileName)}");
        }

        if (VsShellUtilities.IsDocumentOpen(this, path, Guid.Empty, out _, out _, out var openFrame) && openFrame is not null)
        {
            openFrame.Show();
            return;
        }

        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        _explorer.Documents.Register(path, definition.Id, database);
        OpenInQueryEditor(path);
    }

    private static string QueryFolder(ConnectionDefinition definition)
    {
        // One folder per connection, so equal names on two servers do not overwrite each other.
        var folder = Path.Combine(Path.GetTempPath(), "ObjectExplorerForMySql", definition.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // The text view of the active document when it is a MySQL Query Editor, else null (US-0703).
    private Microsoft.VisualStudio.Text.Editor.IWpfTextView? ActiveQueryView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (GetService(typeof(Microsoft.VisualStudio.TextManager.Interop.SVsTextManager)) is not Microsoft.VisualStudio.TextManager.Interop.IVsTextManager textManager
            || ErrorHandler.Failed(textManager.GetActiveView(1, null, out var vsView)) || vsView is null
            || GetService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel)) is not Microsoft.VisualStudio.ComponentModelHost.IComponentModel componentModel)
        {
            return null;
        }

        var view = componentModel.GetService<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService>().GetWpfTextView(vsView);
        return QueryEditorServices.IsQueryView(view) ? view : null;
    }

    // Opens the file in the MySQL Query Editor (US-0701), also when another editor is the default for .sql.
    private void OpenInQueryEditor(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.OpenDocumentWithSpecificEditor(this, path, MySqlEditorFactory.Guid, VSConstants.LOGVIEWID.Primary_guid, out _, out _, out var frame);
        frame?.Show();
    }

    // US-0701: an empty Query<n>.sql in the folder of the connection, linked to the server and, on a database node,
    // that database (on a server node the default database of the connection).
    private void OnNewQuery(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var (definition, database) = _explorer.SelectedNode switch
        {
            ServerNodeViewModel server => (server.Definition, server.Definition.DefaultDatabase),
            DatabaseNodeViewModel { OwningServer: { } server } node => (server.Definition, node.Database.Name),
            _ => (null, null),
        };
        if (definition is null)
        {
            return;
        }

        try
        {
            var folder = QueryFolder(definition);
            var path = Enumerable.Range(1, int.MaxValue - 1)
                .Select(n => Path.Combine(folder, $"Query{n}.sql"))
                .First(p => !File.Exists(p) && !VsShellUtilities.IsDocumentOpen(this, p, Guid.Empty, out _, out _, out _));
            File.WriteAllText(path, string.Empty);
            _explorer.Documents.Register(path, definition.Id, database);
            OpenInQueryEditor(path);
            ExtensionLog.Logger.Info($"Opened new query '{Path.GetFileName(path)}' on '{definition.Label}'{(database is null ? string.Empty : $", database {database}")}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            ExtensionLog.Logger.Error("Opening a new query failed.", ex);
            ShowMessage(Strings.Format(Strings.NewQueryFailed, ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void AddScriptCommand(OleMenuCommandService commandService, ScriptKind kind, bool toClipboard)
    {
        var id = CommandIds.ScriptBase + (2 * (int)kind) + (toClipboard ? 1 : 0);
        var command = new OleMenuCommand(
            (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                OnScript(kind, toClipboard);
            },
            new CommandID(CommandIds.CommandSet, id));
        command.BeforeQueryStatus += (_, _) =>
        {
            var node = _explorer.SelectedNode;
            command.Visible = node?.CreateCodeObject() is { } code && ScriptGenerator.IsAvailable(code.Kind, kind);
            command.Enabled = command.Visible && node?.OwningServer is { State: ConnectionState.Connected };
        };
        commandService.AddCommand(command);
    }

    private void OnScript(ScriptKind kind, bool toClipboard)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not { } node || node.CreateCodeObject() is not { } code || node.OwningServer is not { } server)
        {
            return;
        }

#pragma warning disable VSSDK007 // Fire and forget on purpose: the menu click must not wait for the server; errors are reported inside.
        _ = JoinableTaskFactory.RunAsync(() => ScriptAsync(server.Definition, node, code, kind, toClipboard));
#pragma warning restore VSSDK007
    }

    // US-0602: read what the statement needs on a background thread, generate it, then open it or copy it.
    private async Task ScriptAsync(ConnectionDefinition definition, TreeNodeViewModel node, CodeObject code, ScriptKind kind, bool toClipboard)
    {
        var objectName = $"{code.Database}.{code.Name}";
        try
        {
            IReadOnlyList<string> parameters = [];
            if (node is RoutineNodeViewModel routine && kind is ScriptKind.Call or ScriptKind.Select)
            {
                await routine.EnsureParametersAsync(DisposalToken);
                parameters = routine.Parameters?.Select(p => p.Name).ToList() ?? [];
            }

            await TaskScheduler.Default;
            string? create = null;
            if (kind == ScriptKind.Create)
            {
                create = await _explorer.Metadata.GetCreateStatementAsync(definition, code, DisposalToken);
                if (create is null)
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                    ShowMessage(Strings.Format(Strings.ViewCodeNoSource, objectName), OLEMSGICON.OLEMSGICON_WARNING);
                    return;
                }
            }

            var columns = ScriptGenerator.NeedsColumns(kind) && code.Kind is CodeObjectKind.Table or CodeObjectKind.View
                ? await _explorer.Metadata.GetColumnsAsync(definition, code.Database, code.Name, DisposalToken)
                : [];

            var script = ScriptGenerator.Generate(new ScriptRequest(kind, code)
            {
                Columns = columns,
                Parameters = parameters,
                CreateStatement = create,
                Options = Settings.DragDropSettingsMonitor.Current,
            });

            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var title = ScriptGenerator.Title(kind);
            if (toClipboard)
            {
                System.Windows.Clipboard.SetText(script);
                ExtensionLog.Logger.Info($"Copied the {title} script of {objectName} to the clipboard.");
            }
            else
            {
                var fileName = code.FileName.Substring(0, code.FileName.Length - ".sql".Length) + "." + title + ".sql";
                OpenSqlDocument(definition, code.Database, fileName, CodeDocument.Header(title, code, definition.Label) + Environment.NewLine + script);
                ExtensionLog.Logger.Info($"Opened the {title} script of {objectName} ('{definition.Label}').");
            }
        }
        catch (OperationCanceledException)
        {
            // Visual Studio is closing.
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error($"Generating the {kind} script of {objectName} failed.", ex);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage(Strings.Format(Strings.ScriptFailed, ScriptGenerator.Title(kind), objectName, SensitiveDataMasker.MaskText(ex.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void OnScriptDatabase(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not DatabaseNodeViewModel { Database.IsSystem: false } node || node.OwningServer is not { } server)
        {
            return;
        }

        var database = node.Database.Name;
        var definition = server.Definition;
        var dialog = new ScriptDatabaseDialog(database, definition.Label);
        if (dialog.ShowModal() != true)
        {
            return;
        }

        string? path = null;
        if (dialog.ToFile)
        {
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = Strings.ScriptDatabaseTitle,
                FileName = SafeFileName(database) + ".sql",
                DefaultExt = ".sql",
                Filter = Strings.ScriptDatabaseFileFilter,
                OverwritePrompt = true,
            };
            if (save.ShowDialog() != true)
            {
                return;
            }

            path = save.FileName;
        }

        var options = new DatabaseScriptOptions
        {
            IncludeCreateDatabase = dialog.IncludeCreateDatabase,
            IncludeData = dialog.IncludeData,
            Quoting = Settings.DragDropSettingsMonitor.Current,
            ConnectionLabel = definition.Label,
            GeneratedAt = DateTime.Now,
        };

#pragma warning disable VSSDK007 // Fire and forget on purpose: the wait dialog shows the progress; errors are reported inside.
        _ = JoinableTaskFactory.RunAsync(() => ScriptDatabaseAsync(definition, database, options, path));
#pragma warning restore VSSDK007
    }

    // US-0603: the script is written on a background thread behind the Visual Studio wait dialog, which can cancel it.
    // Without a path the script opens in a new query window; with a path it goes to that file (UTF-8 without BOM).
    private async Task ScriptDatabaseAsync(ConnectionDefinition definition, string database, DatabaseScriptOptions options, string? path)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        var waitMessage = Strings.Format(Strings.ScriptDatabaseWaitMessage, database);
        var factory = await GetServiceAsync(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory;
        var session = factory?.StartWaitDialog(Strings.ScriptDatabaseTitle, new ThreadedWaitDialogProgressData(waitMessage, isCancelable: true));
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(DisposalToken, session?.UserCancellationToken ?? CancellationToken.None);
        var progress = new ActionProgress<DatabaseScriptProgress>(p => session?.Progress.Report(new ThreadedWaitDialogProgressData(
            waitMessage,
            Strings.Format(Strings.ScriptDatabaseProgress, StepText(p.Step), p.Name, p.Current, p.Total),
            null,
            true,
            p.Current,
            p.Total)));

        DatabaseScriptResult? result = null;
        string? text = null;
        Exception? failure = null;
        try
        {
            await TaskScheduler.Default;
            var scripter = new DatabaseScripter(_explorer.Metadata);
            if (path is null)
            {
                using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                result = await scripter.WriteAsync(definition, database, writer, options, progress, cancellation.Token);
                text = writer.ToString();
            }
            else
            {
                try
                {
                    using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(false));
                    result = await scripter.WriteAsync(definition, database, writer, options, progress, cancellation.Token);
                }
                catch
                {
                    TryDelete(path);
                    throw;
                }
            }
        }
        catch (OperationCanceledException)
        {
            ExtensionLog.Logger.Info($"Script Database of {database} ('{definition.Label}') cancelled.");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            session?.Dispose();
            cancellation.Dispose();
        }

        if (failure is not null)
        {
            ExtensionLog.Logger.Error($"Script Database of {database} failed.", failure);
            ShowMessage(Strings.Format(Strings.ScriptDatabaseFailed, database, SensitiveDataMasker.MaskText(failure.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
            return;
        }

        if (result is null)
        {
            return;
        }

        ExtensionLog.Logger.Info($"Scripted database {database} ('{definition.Label}'): {result.ObjectCount} object(s), {result.RowCount} row(s){(path is null ? string.Empty : $", saved to '{path}'")}.");
        if (path is null)
        {
            OpenSqlDocument(definition, database, SafeFileName(database) + ".DATABASE.sql", text ?? string.Empty, alwaysNew: true);
        }
        else
        {
            ShowMessage(Strings.Format(Strings.ScriptDatabaseSaved, database, path), OLEMSGICON.OLEMSGICON_INFO);
        }

        if (result.MissingSource.Count > 0)
        {
            ShowMessage(Strings.Format(Strings.ScriptDatabaseMissingSource, database, string.Join(Environment.NewLine, result.MissingSource)), OLEMSGICON.OLEMSGICON_WARNING);
        }
    }

    // US-1201: choose the parts and the file, then read and write behind the wait dialog, which can cancel.
    private void OnGenerateDocumentation(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedNode is not DatabaseNodeViewModel { Database.IsSystem: false } node || node.OwningServer is not { } server)
        {
            return;
        }

        var database = node.Database.Name;
        var definition = server.Definition;
        var dialog = new DocumentationDialog(database, definition.Label);
        if (dialog.ShowModal() != true || dialog.Options is not { } parts)
        {
            return;
        }

        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.DocumentationTitle,
            FileName = SafeFileName(database) + (dialog.IsPdf ? ".pdf" : ".md"),
            DefaultExt = dialog.IsPdf ? ".pdf" : ".md",
            Filter = dialog.IsPdf ? Strings.DocumentationPdfFileFilter : Strings.DocumentationFileFilter,
            OverwritePrompt = true,
        };
        if (save.ShowDialog() != true)
        {
            return;
        }

        var options = parts with { ConnectionLabel = definition.Label, ServerVersion = server.ServerVersion, GeneratedAt = DateTime.Now };
#pragma warning disable VSSDK007 // Fire and forget on purpose: the wait dialog shows the progress; errors are reported inside.
        var pdf = dialog.IsPdf;
        _ = JoinableTaskFactory.RunAsync(() => GenerateDocumentationAsync(definition, database, options, save.FileName, pdf));
#pragma warning restore VSSDK007
    }

    private async Task GenerateDocumentationAsync(ConnectionDefinition definition, string database, Core.Documentation.DocumentOptions options, string path, bool pdf)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        var waitMessage = Strings.Format(Strings.DocumentationWaitMessage, database);
        var factory = await GetServiceAsync(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory;
        var session = factory?.StartWaitDialog(Strings.DocumentationTitle, new ThreadedWaitDialogProgressData(waitMessage, isCancelable: true));
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(DisposalToken, session?.UserCancellationToken ?? CancellationToken.None);
        var progress = new ActionProgress<Core.Documentation.DocumentProgress>(p => session?.Progress.Report(new ThreadedWaitDialogProgressData(
            waitMessage,
            p.Total == 0 ? string.Empty : Strings.Format(Strings.DocumentationProgress, DocumentStepText(p.Step), p.Name, p.Current, p.Total),
            null,
            true,
            p.Current,
            p.Total)));

        var done = false;
        Exception? failure = null;
        try
        {
            await TaskScheduler.Default;
            var document = await new Core.Documentation.DatabaseDocumenter(_explorer.Metadata, new Core.Dependencies.MySqlDependencySource(ExtensionLog.Logger)).ReadAsync(definition, database, options, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            try
            {
                if (pdf)
                {
                    using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
                    Core.Documentation.PdfDocumentWriter.Write(document, file);
                }
                else
                {
                    using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(false));
                    Core.Documentation.MarkdownDocumentWriter.Write(document, writer);
                }
            }
            catch
            {
                TryDelete(path);
                throw;
            }

            done = true;
        }
        catch (OperationCanceledException)
        {
            ExtensionLog.Logger.Info($"Generate Documentation of {database} ('{definition.Label}') cancelled.");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            session?.Dispose();
            cancellation.Dispose();
        }

        if (failure is not null)
        {
            ExtensionLog.Logger.Error($"Generate Documentation of {database} failed.", failure);
            ShowMessage(Strings.Format(Strings.DocumentationFailed, database, SensitiveDataMasker.MaskText(failure.Message)), OLEMSGICON.OLEMSGICON_CRITICAL);
            return;
        }

        if (!done)
        {
            return;
        }

        ExtensionLog.Logger.Info($"Documented database {database} ('{definition.Label}') to '{path}'.");
        if (VsShellUtilities.ShowMessageBox(this, Strings.Format(Strings.DocumentationSaved, database, path), Strings.DocumentationTitle,
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST) == (int)VSConstants.MessageBoxResult.IDYES)
        {
            OpenDocumentation(path, pdf);
        }
    }

    // Markdown opens in Visual Studio; a PDF in the PDF viewer of Windows.
    private void OpenDocumentation(string path, bool pdf)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!pdf)
        {
            VsShellUtilities.OpenDocument(this, path);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ExtensionLog.Logger.Warning($"Opening '{path}' failed: {ex.Message}");
            ShowMessage(Strings.Format(Strings.DocumentationOpenFailed, path, ex.Message), OLEMSGICON.OLEMSGICON_WARNING);
        }
    }

    private static string DocumentStepText(Core.Documentation.DocumentStep step) => step switch
    {
        Core.Documentation.DocumentStep.Tables => Strings.NodeTables,
        Core.Documentation.DocumentStep.Views => Strings.NodeViews,
        Core.Documentation.DocumentStep.Routines => Strings.DocumentationRoutines.Replace("_", string.Empty),
        Core.Documentation.DocumentStep.Dependencies => Strings.DocumentationStepDependencies,
        _ => string.Empty,
    };

    private static string StepText(DatabaseScriptStep step) => step switch
    {
        DatabaseScriptStep.Tables => Strings.NodeTables,
        DatabaseScriptStep.Data => Strings.ScriptDatabaseStepData,
        DatabaseScriptStep.Functions => Strings.NodeFunctions,
        DatabaseScriptStep.Procedures => Strings.NodeStoredProcedures,
        DatabaseScriptStep.Views => Strings.NodeViews,
        DatabaseScriptStep.Triggers => Strings.NodeTriggers,
        _ => Strings.NodeEvents,
    };

    private static string SafeFileName(string name) =>
        new string(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());

    // Reports on the calling thread. Progress<T> would post every report to the UI thread; the wait dialog
    // accepts updates from any thread.
    private sealed class ActionProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExtensionLog.Logger.Warning($"Could not delete the incomplete script '{path}': {ex.Message}");
        }
    }

    private void ShowMessage(string message, OLEMSGICON icon)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(this, message, Strings.ToolWindowTitle, icon, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private void OnCollapseAll(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _explorer.CollapseAll();
    }

    private void OnDisconnectServer(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedServer is { CanDisconnect: true } node)
        {
            node.Disconnect();
        }
    }

    private void OnRemoveServer(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedServer is not { } node)
        {
            return;
        }

        var answer = VsShellUtilities.ShowMessageBox(
            this,
            Strings.Format(Strings.RemoveServerConfirmation, node.Label),
            Strings.ToolWindowTitle,
            OLEMSGICON.OLEMSGICON_QUERY,
            OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
        if (answer != (int)VSConstants.MessageBoxResult.IDYES)
        {
            return;
        }

        _explorer.RemoveServer(node);
        ExtensionLog.Logger.Info($"Server '{node.Label}' removed.");
        SaveConnections();
    }

    private void OnServerProperties(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_explorer.SelectedServer is not { } node)
        {
            return;
        }

        try
        {
            var viewModel = new ConnectionEditorViewModel(new MySqlConnectionTester(ExtensionLog.Logger), node.Definition);
            var dialog = new ConnectionDialog(viewModel);
            if (dialog.ShowModal() == true && dialog.Result is { } definition)
            {
                var oldLabel = node.Label;
                _explorer.UpdateServer(node, definition);
                ExtensionLog.Logger.Info(oldLabel == node.Label ? $"Server '{node.Label}' changed." : $"Server '{oldLabel}' changed and renamed to '{node.Label}'.");
                SaveConnections();
            }
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error("Changing a server failed.", ex);
            VsShellUtilities.ShowMessageBox(
                this,
                Strings.EditServerFailed,
                Strings.ToolWindowTitle,
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private void SaveConnections()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            _store.Save(_explorer.Servers.Select(server => server.Definition));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            ExtensionLog.Logger.Error($"Saving the connections to '{_store.FilePath}' failed.", ex);
            VsShellUtilities.ShowMessageBox(
                this,
                Strings.Format(Strings.SaveConnectionsFailed, _store.FilePath),
                Strings.ToolWindowTitle,
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private void OnAddServer(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var dialog = new ConnectionDialog(new ConnectionEditorViewModel(new MySqlConnectionTester(ExtensionLog.Logger)));
            if (dialog.ShowModal() == true && dialog.Result is { } definition)
            {
                _explorer.AddServer(definition);
                ExtensionLog.Logger.Info($"Server '{definition.Label}' added.");
                SaveConnections();
            }
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error("Adding a server failed.", ex);
            VsShellUtilities.ShowMessageBox(
                this,
                Strings.AddServerFailed,
                Strings.ToolWindowTitle,
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
