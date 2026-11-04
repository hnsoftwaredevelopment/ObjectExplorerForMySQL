using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Core.Security;
using ObjectExplorerForMySql.Logging;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Editor;

/// <summary>Results pane below a query document (US-0703): one grid per result set and the messages of the last run.</summary>
public partial class QueryResultsPane : UserControl
{
    private const double MinimumHeight = 80;
    private QueryRunner? _runner;

    /// <summary>Initializes a new instance of the <see cref="QueryResultsPane"/> class.</summary>
    public QueryResultsPane()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the close button is clicked.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets or sets the function that returns the maximum height, so the editor keeps some lines.</summary>
    internal Func<double>? MaximumHeight { get; set; }

    /// <summary>Shows the results and messages of <paramref name="runner"/>.</summary>
    internal void Attach(QueryRunner runner)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _runner = runner;
        DataContext = runner;
        runner.ResultSets.CollectionChanged += OnResultSetsChanged;
        runner.Messages.CollectionChanged += OnMessagesChanged;
        runner.Plan.CollectionChanged += OnPlanChanged;
        runner.PropertyChanged += OnRunnerPropertyChanged;
        PlanTab.Visibility = runner.Plan.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildResults();
        UpdateMessages();
    }

    /// <summary>Stops following the runner.</summary>
    internal void Detach()
    {
        if (_runner is { } runner)
        {
            runner.ResultSets.CollectionChanged -= OnResultSetsChanged;
            runner.Messages.CollectionChanged -= OnMessagesChanged;
            runner.Plan.CollectionChanged -= OnPlanChanged;
            runner.PropertyChanged -= OnRunnerPropertyChanged;
            _runner = null;
        }
    }

    private void OnResultSetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        RebuildResults();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateMessages();

    // A new run clears the plan; some runs end without a busy phase (no connection, nothing to explain).
    private void OnPlanChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_runner is { Plan.Count: 0 })
        {
            if (PlanTab.IsChecked == true)
            {
                MessagesTab.IsChecked = true;
            }

            PlanTab.Visibility = Visibility.Collapsed;
        }
    }

    // After a run: the plan after Explain (US-1301), the results when there are any and no error, otherwise the
    // messages (as SSMS does). The Plan tab shows only while there is a plan.
    private void OnRunnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QueryRunner.IsRunning) || _runner is not { IsRunning: false } runner)
        {
            return;
        }

        var hasPlan = runner.Plan.Count > 0;
        PlanTab.Visibility = hasPlan ? Visibility.Visible : Visibility.Collapsed;
        if (hasPlan)
        {
            PlanTab.IsChecked = true;
            return;
        }

        var showResults = runner.ResultSets.Count > 0 && !runner.Messages.Any(m => m.Kind == QueryMessageKind.Error);
        ResultsTab.IsChecked = showResults;
        MessagesTab.IsChecked = !showResults;
    }

    private void UpdateMessages()
    {
        MessagesBox.Text = _runner is null ? string.Empty : string.Join(Environment.NewLine, _runner.Messages.Select(m => m.Text));
    }

    // One result set fills the pane; several are stacked, each with its own scroll bar.
    private void RebuildResults()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var sets = _runner?.ResultSets.ToList() ?? [];
        if (sets.Count == 0)
        {
            ResultsHost.Content = null;
            return;
        }

        if (sets.Count == 1)
        {
            ResultsHost.Content = CreateGrid(sets[0]);
            return;
        }

        var stack = new StackPanel();
        foreach (var set in sets)
        {
            var grid = CreateGrid(set);
            grid.Height = Math.Max(MinimumHeight, Math.Min(240, 30 + (22 * set.Rows.Count)));
            grid.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(grid);
        }

        ResultsHost.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    private DataGrid CreateGrid(QueryResultSet set)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            HeadersVisibility = DataGridHeadersVisibility.All,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            BorderThickness = new Thickness(0),
            ColumnHeaderStyle = (Style)FindResource("GridHeader"),
            RowHeaderStyle = (Style)FindResource("GridRowHeader"),
            CellStyle = (Style)FindResource("GridCell"),
            ItemsSource = set.Rows,
        };
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        grid.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, HeaderColors.SeparatorLineBrushKey);
        grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, HeaderColors.SeparatorLineBrushKey);

        for (var i = 0; i < set.Columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                // A TextBlock, so an underscore in a column name is not read as an access key.
                Header = new TextBlock { Text = set.Columns[i] },
                Binding = new Binding($"[{i}]") { Converter = ValueConverter.Instance, Mode = BindingMode.OneTime },
                MaxWidth = 400,
            });
        }

        var copy = new MenuItem { Header = Strings.QueryCopy };
        copy.Click += (_, _) => Copy(grid, withHeaders: false);
        var copyWithHeaders = new MenuItem { Header = Strings.QueryCopyWithHeaders };
        copyWithHeaders.Click += (_, _) => Copy(grid, withHeaders: true);
        var saveAs = new MenuItem { Header = Strings.QuerySaveAs };
        saveAs.Click += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SaveAs(set);
        };
        grid.ContextMenu = new ContextMenu { Items = { copy, copyWithHeaders, new Separator(), saveAs } };
        return grid;
    }

    // US-0704: CSV (UTF-8 with BOM, so Excel reads the characters right) or JSON (UTF-8 without BOM).
    // US-0705: a result that was cut off can run again on its own connection and stream all rows to the file.
    private static void SaveAs(QueryResultSet set)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var all = false;
        if (set.IsTruncated && set.Source is not null)
        {
            var answer = VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, Strings.Format(Strings.QuerySaveRunAgain, set.TotalRows, set.Rows.Count),
                Strings.QuerySaveTitle, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            if (answer == (int)VSConstants.MessageBoxResult.IDCANCEL)
            {
                return;
            }

            all = answer == (int)VSConstants.MessageBoxResult.IDYES;
        }
        else if (set.IsTruncated && !Confirm(Strings.Format(Strings.QuerySaveTruncated, set.TotalRows, set.Rows.Count)))
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.QuerySaveTitle,
            Filter = Strings.QuerySaveFilter,
            FileName = "Results.csv",
            DefaultExt = ".csv",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FileName;
        var json = string.Equals(System.IO.Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
        if (all)
        {
#pragma warning disable VSSDK007 // Fire and forget on purpose: the wait dialog shows the progress; errors are reported inside.
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(() => SaveAllAsync(set.Source!, path, json));
#pragma warning restore VSSDK007
            return;
        }

        try
        {
            using var writer = OpenWriter(path, json);
            if (json)
            {
                QueryResultExporter.WriteJson(set, writer);
            }
            else
            {
                QueryResultExporter.WriteCsv(set, writer);
            }

            ExtensionLog.Logger.Info($"Saved {set.Rows.Count} row(s) to '{path}'.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ExtensionLog.Logger.Error($"Saving the results to '{path}' failed.", ex);
            ShowMessage(Strings.Format(Strings.QuerySaveFailed, ex.Message), Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private static System.IO.StreamWriter OpenWriter(string path, bool json) =>
        new(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: !json));

    // Runs the statement again behind the wait dialog with the row count and Cancel; a cancelled or failed export
    // leaves no file.
    private static async System.Threading.Tasks.Task SaveAllAsync(ResultSource source, string path, bool json)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var factory = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(Microsoft.VisualStudio.Shell.Interop.SVsThreadedWaitDialogFactory))
            as Microsoft.VisualStudio.Shell.Interop.IVsThreadedWaitDialogFactory;
        var session = factory?.StartWaitDialog(Strings.QuerySaveTitle, new ThreadedWaitDialogProgressData(Strings.QuerySaveAllWait, isCancelable: true));
        var token = session?.UserCancellationToken ?? System.Threading.CancellationToken.None;
        var progress = new RowProgress(rows => session?.Progress.Report(new ThreadedWaitDialogProgressData(
            Strings.QuerySaveAllWait, Strings.Format(Strings.QuerySaveAllProgress, rows.ToString("N0", CultureInfo.CurrentCulture)), null, true)));

        long count = 0;
        var cancelled = false;
        Exception? failure = null;
        try
        {
            await System.Threading.Tasks.TaskScheduler.Default;
            try
            {
                using var writer = OpenWriter(path, json);
                count = await FullResultExporter.ExportAsync(source, json, writer, progress, token);
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            session?.Dispose();
        }

        if (cancelled)
        {
            ExtensionLog.Logger.Info($"Saving all rows to '{path}' cancelled.");
        }
        else if (failure is MySqlConnector.MySqlException)
        {
            ExtensionLog.Logger.Info($"Running the statement again for a full export failed: {SensitiveDataMasker.MaskText(failure.Message)}");
            ShowMessage(Strings.Format(Strings.QuerySaveAllFailed, SensitiveDataMasker.MaskText(failure.Message)), Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL);
        }
        else if (failure is not null)
        {
            ExtensionLog.Logger.Error($"Saving all rows to '{path}' failed.", failure);
            ShowMessage(Strings.Format(Strings.QuerySaveFailed, SensitiveDataMasker.MaskText(failure.Message)), Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_CRITICAL);
        }
        else
        {
            ExtensionLog.Logger.Info($"Saved all {count} row(s) to '{path}'.");
            ShowMessage(Strings.Format(Strings.QuerySaveAllDone, count.ToString("N0", CultureInfo.CurrentCulture), path), Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ExtensionLog.Logger.Warning($"Removing the incomplete file '{path}' failed: {ex.Message}");
        }
    }

    private static void ShowMessage(string message, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON icon)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, Strings.QuerySaveTitle, icon,
            Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK, Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    // Reports on the calling thread; the wait dialog accepts updates from any thread.
    private sealed class RowProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    private static bool Confirm(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, Strings.QuerySaveTitle,
            Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
            Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST) == (int)VSConstants.MessageBoxResult.IDYES;
    }

    private static void Copy(DataGrid grid, bool withHeaders)
    {
        if (grid.SelectedCells.Count == 0)
        {
            grid.SelectAllCells();
        }

        grid.ClipboardCopyMode = withHeaders ? DataGridClipboardCopyMode.IncludeHeader : DataGridClipboardCopyMode.ExcludeHeader;
        try
        {
            ApplicationCommands.Copy.Execute(null, grid);
        }
        finally
        {
            grid.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
        }
    }

    private void OnSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        var maximum = Math.Max(MinimumHeight, MaximumHeight?.Invoke() ?? double.MaxValue);
        Height = Math.Max(MinimumHeight, Math.Min(maximum, Height - e.VerticalChange));
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    // Grid text and clipboard text: NULL, invariant numbers, ISO dates, hex for binary data. Also used by View Data.
    internal sealed class ValueConverter : IValueConverter
    {
        public static readonly ValueConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => QueryValueFormatter.Format(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

/// <summary>Adds <see cref="QueryResultsMargin"/> below the main view of every query document.</summary>
[Export(typeof(IWpfTextViewMarginProvider))]
[Name(QueryResultsMargin.MarginName)]
[MarginContainer(PredefinedMarginNames.Bottom)]
[Order(After = PredefinedMarginNames.BottomControl)]
[ContentType(MySqlContentType.Name)]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
internal sealed class QueryResultsMarginProvider : IWpfTextViewMarginProvider
{
    /// <inheritdoc />
    public IWpfTextViewMargin? CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer) =>
        QueryEditorServices.GetRunner(wpfTextViewHost.TextView.TextDataModel.DocumentBuffer) is { } runner
            ? new QueryResultsMargin(wpfTextViewHost.TextView, runner)
            : null;
}

/// <summary>Editor margin that hosts <see cref="QueryResultsPane"/>. Hidden until the first run.</summary>
internal sealed class QueryResultsMargin : IWpfTextViewMargin
{
    /// <summary>Name of the margin.</summary>
    public const string MarginName = "MySqlQueryResults";

    private readonly IWpfTextView _view;
    private readonly QueryRunner _runner;
    private readonly QueryResultsPane _pane = new();
    private bool _disposed;

    public QueryResultsMargin(IWpfTextView view, QueryRunner runner)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _view = view;
        _runner = runner;
        _pane.Attach(runner);
        _pane.Visibility = runner.HasRun ? Visibility.Visible : Visibility.Collapsed;
        _pane.MaximumHeight = () => _pane.ActualHeight + _view.ViewportHeight - 60;
        _pane.CloseRequested += OnCloseRequested;
        _runner.RunStarted += OnRunStarted;
        _view.Closed += OnViewClosed;
        _view.Properties[typeof(QueryResultsPane)] = _pane;
    }

    /// <inheritdoc />
    public FrameworkElement VisualElement => _pane;

    /// <inheritdoc />
    public double MarginSize => _pane.ActualHeight;

    /// <inheritdoc />
    public bool Enabled => true;

    /// <inheritdoc />
    public ITextViewMargin? GetTextViewMargin(string marginName) =>
        string.Equals(marginName, MarginName, StringComparison.OrdinalIgnoreCase) ? this : null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pane.CloseRequested -= OnCloseRequested;
        _runner.RunStarted -= OnRunStarted;
        _view.Closed -= OnViewClosed;
        _view.Properties.RemoveProperty(typeof(QueryResultsPane));
        _pane.Detach();
    }

    private void OnRunStarted(object? sender, EventArgs e) => _pane.Visibility = Visibility.Visible;

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        _pane.Visibility = Visibility.Collapsed;
        _view.VisualElement.Focus();
    }

    // The main view closes with the document: close the server session.
    private void OnViewClosed(object? sender, EventArgs e)
    {
        Dispose();
        QueryEditorServices.CloseRunner(_view.TextDataModel.DocumentBuffer);
    }
}

/// <summary>Installs <see cref="ResultsCommandFilter"/> on the main view of every query document.</summary>
[Export(typeof(IVsTextViewCreationListener))]
[ContentType(MySqlContentType.Name)]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
internal sealed class ResultsCommandFilterInstaller : IVsTextViewCreationListener
{
#pragma warning disable CS0649 // Assigned by MEF.
    [Import]
    private IVsEditorAdaptersFactoryService? _adapters;
#pragma warning restore CS0649

    /// <inheritdoc />
    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_adapters?.GetWpfTextView(textViewAdapter) is not { } view)
        {
            return;
        }

        var filter = new ResultsCommandFilter(view);
        if (ErrorHandler.Succeeded(textViewAdapter.AddCommandFilter(filter, out var next)))
        {
            filter.Next = next;
        }
    }
}

/// <summary>
/// While the keyboard is in the results pane, editor commands (arrow keys, Copy, Select All, Delete) must reach the grid
/// and not the text. The filter reports them as not supported then, so Visual Studio passes the keys on to WPF.
/// </summary>
internal sealed class ResultsCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _view;

    public ResultsCommandFilter(IWpfTextView view)
    {
        _view = view;
    }

    public IOleCommandTarget? Next { get; set; }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (cCmds > 0 && IsForResults(pguidCmdGroup, prgCmds[0].cmdID))
        {
            return (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        return Next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (IsForResults(pguidCmdGroup, nCmdID))
        {
            return (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        return Next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private bool IsForResults(Guid group, uint id)
    {
        if (!_view.Properties.TryGetProperty(typeof(QueryResultsPane), out QueryResultsPane pane) || !pane.IsKeyboardFocusWithin)
        {
            return false;
        }

        return group == VSConstants.VSStd2K
            || (group == VSConstants.GUID_VSStandardCommandSet97 && (VSConstants.VSStd97CmdID)id is
                VSConstants.VSStd97CmdID.Copy or VSConstants.VSStd97CmdID.Cut or VSConstants.VSStd97CmdID.Paste
                or VSConstants.VSStd97CmdID.SelectAll or VSConstants.VSStd97CmdID.Delete
                or VSConstants.VSStd97CmdID.Undo or VSConstants.VSStd97CmdID.Redo);
    }
}
