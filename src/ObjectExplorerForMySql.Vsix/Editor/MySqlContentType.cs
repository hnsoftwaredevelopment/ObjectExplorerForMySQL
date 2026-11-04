using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Logging;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Editor;

/// <summary>
/// Content type of documents in the MySQL query editor (US-0701). It is not linked to the .sql extension, so other
/// .sql files keep their own editor (for example the T-SQL editor of SQL Server Data Tools). Only buffers created by
/// <see cref="MySqlEditorFactory"/> get it.
/// </summary>
internal static class MySqlContentType
{
    /// <summary>Name of the content type.</summary>
    public const string Name = "MySql";

#pragma warning disable CS0649, IDE0044 // Assigned by MEF.
    [Export]
    [Name(Name)]
    [BaseDefinition("code")]
    internal static ContentTypeDefinition? Definition;
#pragma warning restore CS0649, IDE0044
}

/// <summary>
/// Services of the package that MEF parts of the editor need. Set by <see cref="ObjectExplorerPackage"/> before it
/// registers the editor factory, so every query document finds them.
/// </summary>
internal static class QueryEditorServices
{
    private static QueryTexts? _texts;
    private static PlanTexts? _planTexts;

    /// <summary>Gets or sets the explorer with the servers and the document links; null until the package loaded.</summary>
    public static ExplorerViewModel? Explorer { get; set; }

    /// <summary>Gets or sets the metadata cache of the completion list (US-1401); null until the package loaded.</summary>
    public static Core.Completion.SchemaCache? Schema { get; set; }

    /// <summary>
    /// Returns the connection and database of the document in <paramref name="documentBuffer"/> for IntelliSense
    /// (US-1401, US-1402). Only with a known password, so IntelliSense never shows the password dialog. UI thread.
    /// </summary>
    public static (Core.Connections.ConnectionDefinition? Definition, string? Database) ConnectionOf(ITextBuffer documentBuffer)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        if (Explorer is not { } explorer
            || !documentBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document)
            || explorer.Documents.Find(document.FilePath) is not { ConnectionId: { } id } link
            || System.Linq.Enumerable.FirstOrDefault(explorer.Servers, s => s.Definition.Id == id) is not { } server
            || server.Definition.Password is null)
        {
            return (null, null);
        }

        return (server.Definition, link.Database ?? server.Definition.DefaultDatabase);
    }

    /// <summary>Gets the texts of quick info (US-1402).</summary>
    public static Core.Completion.IntelliSenseTexts IntelliSenseTexts { get; } = new()
    {
        Column = Strings.QuickInfoColumn,
        Table = Strings.QuickInfoTable,
        View = Strings.QuickInfoView,
        Database = Strings.QuickInfoDatabase,
        PrimaryKey = Strings.QuickInfoPrimaryKey,
        ForeignKey = Strings.QuickInfoForeignKey,
    };

    /// <summary>Returns true when <paramref name="view"/> shows a document of the MySQL Query Editor.</summary>
    public static bool IsQueryView(ITextView? view) => view?.TextDataModel.DocumentBuffer.ContentType.IsOfType(MySqlContentType.Name) == true;

    /// <summary>
    /// Returns the runner of the document in <paramref name="buffer"/>, created on first use (US-0703). All views of a
    /// document (Window > Split) share it. Null when the package is not loaded or the buffer has no file.
    /// </summary>
    public static QueryRunner? GetRunner(ITextBuffer buffer)
    {
        if (Explorer is not { } explorer || !buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
        {
            return null;
        }

        return buffer.Properties.GetOrCreateSingletonProperty(
            typeof(QueryRunner),
            () => new QueryRunner(explorer, () => document.FilePath, d => new MySqlQuerySession(d, ExtensionLog.Logger), Texts, ExtensionLog.Logger)
            {
                PlanTexts = PlanTexts,
            });
    }

    /// <summary>Closes the session of the document and forgets its runner, when the document closes.</summary>
    public static void CloseRunner(ITextBuffer buffer)
    {
        if (buffer.Properties.TryGetProperty(typeof(QueryRunner), out QueryRunner runner))
        {
            buffer.Properties.RemoveProperty(typeof(QueryRunner));
            runner.Dispose();
        }
    }

    /// <summary>Runs the selection, or the whole document when nothing is selected (US-0703).</summary>
    public static void Execute(IWpfTextView view)
    {
        if (!IsQueryView(view) || GetRunner(view.TextDataModel.DocumentBuffer) is not { } runner || runner.IsRunning)
        {
            return;
        }

        string script;
        int firstLine;
        if (view.Selection.IsEmpty)
        {
            script = view.TextSnapshot.GetText();
            firstLine = 0;
        }
        else
        {
            var span = view.Selection.StreamSelectionSpan.SnapshotSpan;
            script = span.GetText();
            firstLine = span.Start.GetContainingLine().LineNumber;
        }

#pragma warning disable VSTHRD110 // Fire and forget: the runner reports errors in the Messages tab.
        _ = RunAndKeepFocusAsync(view, () => runner.ExecuteAsync(script, firstLine));
#pragma warning restore VSTHRD110
    }

    /// <summary>
    /// Shows the estimated plan (US-1301) of the selection, or of the statement at the caret when nothing is selected:
    /// the last statement that starts on or before the caret line. With <paramref name="analyze"/> the statement runs
    /// and the plan shows the actual rows and times (US-1302).
    /// </summary>
    public static void Explain(IWpfTextView view, bool analyze = false)
    {
        if (!IsQueryView(view) || GetRunner(view.TextDataModel.DocumentBuffer) is not { } runner || runner.IsRunning)
        {
            return;
        }

        string script;
        int firstLine;
        if (view.Selection.IsEmpty)
        {
            var caretLine = view.Caret.Position.BufferPosition.GetContainingLine().LineNumber;
            var statement = ExplainStatement.AtLine(view.TextSnapshot.GetText(), caretLine);
            script = statement?.Text ?? string.Empty;
            firstLine = statement?.Line ?? 0;
        }
        else
        {
            var span = view.Selection.StreamSelectionSpan.SnapshotSpan;
            script = span.GetText();
            firstLine = span.Start.GetContainingLine().LineNumber;
        }

#pragma warning disable VSTHRD110 // Fire and forget: the runner reports errors in the Messages tab.
        _ = RunAndKeepFocusAsync(view, () => analyze ? runner.ExplainAnalyzeAsync(script, firstLine) : runner.ExplainAsync(script, firstLine));
#pragma warning restore VSTHRD110
    }

    // The editor keeps the keyboard during and after a run, so the arrow keys work right away. Only when the user
    // clicked into the results pane in the meantime, the focus stays there.
    private static async System.Threading.Tasks.Task RunAndKeepFocusAsync(IWpfTextView view, System.Func<System.Threading.Tasks.Task> start)
    {
        var run = start();
        KeepFocus(view);
        await run;
        KeepFocus(view);
    }

    private static void KeepFocus(IWpfTextView view)
    {
        var inResults = view.Properties.TryGetProperty(typeof(QueryResultsPane), out QueryResultsPane pane) && pane.IsKeyboardFocusWithin;
        if (!view.IsClosed && !inResults && !view.HasAggregateFocus)
        {
            view.VisualElement.Focus();
        }
    }

    private static QueryTexts Texts => _texts ??= new QueryTexts
    {
        RowsReturned = Strings.QueryRowsReturned,
        RowsTruncated = Strings.QueryRowsTruncated,
        RowsAffected = Strings.QueryRowsAffected,
        Warning = Strings.QueryWarning,
        Error = Strings.QueryError,
        ConnectFailed = Strings.QueryConnectFailed,
        Completed = Strings.QueryCompleted,
        Failed = Strings.QueryFailed,
        Cancelled = Strings.QueryCancelled,
        NoConnection = Strings.QueryNoConnection,
        NothingToRun = Strings.QueryNothingToRun,
        NotExplainable = Strings.QueryNotExplainable,
        PlanReady = Strings.QueryPlanReady,
        PlanUnreadable = Strings.QueryPlanUnreadable,
        NotAnalyzable = Strings.QueryNotAnalyzable,
        AnalyzeNotSupported = Strings.QueryAnalyzeNotSupported,
        PlanAnalyzed = Strings.QueryPlanAnalyzed,
    };

    private static PlanTexts PlanTexts => _planTexts ??= new PlanTexts
    {
        QueryBlock = Strings.PlanQueryBlock,
        Table = Strings.PlanTable,
        NestedLoop = Strings.PlanNestedLoop,
        OrderBy = Strings.PlanOrderBy,
        GroupBy = Strings.PlanGroupBy,
        Distinct = Strings.PlanDistinct,
        Windowing = Strings.PlanWindowing,
        Union = Strings.PlanUnion,
        Subquery = Strings.PlanSubquery,
        Materialized = Strings.PlanMaterialized,
        Filesort = Strings.PlanFilesort,
        TemporaryTable = Strings.PlanTemporaryTable,
        Cost = Strings.PlanCost,
        Key = Strings.PlanKey,
        Rows = Strings.PlanRows,
        FullScanWarning = Strings.PlanFullScanWarning,
        FilesortWarning = Strings.PlanFilesortWarning,
        TemporaryWarning = Strings.PlanTemporaryWarning,
        ActualRows = Strings.PlanActualRows,
        ActualTime = Strings.PlanActualTime,
        Loops = Strings.PlanLoops,
        NeverExecuted = Strings.PlanNeverExecuted,
        EstimateWarning = Strings.PlanEstimateWarning,
    };
}
