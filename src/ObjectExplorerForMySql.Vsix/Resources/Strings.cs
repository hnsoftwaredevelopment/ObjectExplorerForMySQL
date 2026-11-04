using System.Globalization;
using System.Resources;

namespace ObjectExplorerForMySql.Resources;

/// <summary>
/// Typed access to <c>Strings.resx</c>. The current UI culture of Visual Studio selects the language (ADR-005, ADR-010).
/// </summary>
public static class Strings
{
    private static readonly ResourceManager _resourceManager =
        new("ObjectExplorerForMySql.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Gets or sets an explicit culture; null means the current UI culture.</summary>
    public static CultureInfo? Culture { get; set; }

    /// <summary>Title of the tool window.</summary>
    public static string ToolWindowTitle => Get(nameof(ToolWindowTitle));

    /// <summary>Text shown when no server connection exists yet.</summary>
    public static string EmptyExplorerHint => Get(nameof(EmptyExplorerHint));

    /// <summary>Name of the Output window pane.</summary>
    public static string OutputPaneTitle => Get(nameof(OutputPaneTitle));

    /// <summary>Message shown when the explorer tool window cannot be opened.</summary>
    public static string OpenExplorerFailed => Get(nameof(OpenExplorerFailed));

    /// <summary>Message shown when the Add MySQL Server command fails unexpectedly.</summary>
    public static string AddServerFailed => Get(nameof(AddServerFailed));

    /// <summary>Connections could not be saved; {0} = file path.</summary>
    public static string SaveConnectionsFailed => Get(nameof(SaveConnectionsFailed));

    /// <summary>Confirmation before removing a server; {0} = label.</summary>
    public static string RemoveServerConfirmation => Get(nameof(RemoveServerConfirmation));

    /// <summary>Message shown when changing a server fails unexpectedly.</summary>
    public static string EditServerFailed => Get(nameof(EditServerFailed));

    /// <summary>Title of the connection dialog when editing.</summary>
    public static string ConnectionPropertiesDialogTitle => Get(nameof(ConnectionPropertiesDialogTitle));

    /// <summary>Hint below the password field when editing a connection with a saved password.</summary>
    public static string ConnectionKeepPasswordHint => Get(nameof(ConnectionKeepPasswordHint));

    /// <summary>Title of the connection dialog.</summary>
    public static string ConnectionDialogTitle => Get(nameof(ConnectionDialogTitle));

    /// <summary>Label of the connection name field.</summary>
    public static string ConnectionName => Get(nameof(ConnectionName));

    /// <summary>Header of the log-on group.</summary>
    public static string ConnectionLogOnGroup => Get(nameof(ConnectionLogOnGroup));

    /// <summary>Label of the host field.</summary>
    public static string ConnectionHost => Get(nameof(ConnectionHost));

    /// <summary>Label of the port field.</summary>
    public static string ConnectionPort => Get(nameof(ConnectionPort));

    /// <summary>Label of the user name field.</summary>
    public static string ConnectionUserName => Get(nameof(ConnectionUserName));

    /// <summary>Label of the password field.</summary>
    public static string ConnectionPassword => Get(nameof(ConnectionPassword));

    /// <summary>Label of the save password check box.</summary>
    public static string ConnectionSavePassword => Get(nameof(ConnectionSavePassword));

    /// <summary>Label of the default database field.</summary>
    public static string ConnectionDefaultDatabase => Get(nameof(ConnectionDefaultDatabase));

    /// <summary>Header of the advanced section.</summary>
    public static string ConnectionAdvanced => Get(nameof(ConnectionAdvanced));

    /// <summary>Label of the SSL mode field.</summary>
    public static string ConnectionSslMode => Get(nameof(ConnectionSslMode));

    /// <summary>Label of the connection timeout field.</summary>
    public static string ConnectionTimeout => Get(nameof(ConnectionTimeout));

    /// <summary>Label of the allow public key retrieval check box.</summary>
    public static string ConnectionAllowPublicKeyRetrieval => Get(nameof(ConnectionAllowPublicKeyRetrieval));

    /// <summary>Tooltip explaining public key retrieval.</summary>
    public static string ConnectionAllowPublicKeyRetrievalHint => Get(nameof(ConnectionAllowPublicKeyRetrievalHint));

    /// <summary>Text of the Test Connection button.</summary>
    public static string ButtonTestConnection => Get(nameof(ButtonTestConnection));

    /// <summary>Text of the OK button.</summary>
    public static string ButtonOk => Get(nameof(ButtonOk));

    /// <summary>Text of the Cancel button.</summary>
    public static string ButtonCancel => Get(nameof(ButtonCancel));

    /// <summary>Shown while a connection test runs.</summary>
    public static string TestConnectionRunning => Get(nameof(TestConnectionRunning));

    /// <summary>Connection test succeeded; {0} = server version.</summary>
    public static string TestConnectionSucceeded => Get(nameof(TestConnectionSucceeded));

    /// <summary>Connection test succeeded on an unsupported version; {0} = server version.</summary>
    public static string TestConnectionUnsupportedVersion => Get(nameof(TestConnectionUnsupportedVersion));

    /// <summary>Connection test failed; {0} = error message.</summary>
    public static string TestConnectionFailed => Get(nameof(TestConnectionFailed));

    /// <summary>Validation: host is empty.</summary>
    public static string ValidationHostRequired => Get(nameof(ValidationHostRequired));

    /// <summary>Validation: port is not valid.</summary>
    public static string ValidationPortInvalid => Get(nameof(ValidationPortInvalid));

    /// <summary>Validation: user name is empty.</summary>
    public static string ValidationUserRequired => Get(nameof(ValidationUserRequired));

    /// <summary>Validation: timeout is not valid.</summary>
    public static string ValidationTimeoutInvalid => Get(nameof(ValidationTimeoutInvalid));

    /// <summary>SSL mode None.</summary>
    public static string SslModeNone => Get(nameof(SslModeNone));

    /// <summary>SSL mode Preferred.</summary>
    public static string SslModePreferred => Get(nameof(SslModePreferred));

    /// <summary>SSL mode Required.</summary>
    public static string SslModeRequired => Get(nameof(SslModeRequired));

    /// <summary>SSL mode Verify CA.</summary>
    public static string SslModeVerifyCA => Get(nameof(SslModeVerifyCA));

    /// <summary>SSL mode Verify full.</summary>
    public static string SslModeVerifyFull => Get(nameof(SslModeVerifyFull));

    /// <summary>Connection state: not connected.</summary>
    public static string StateNotConnected => Get(nameof(StateNotConnected));

    /// <summary>Connection state: connecting.</summary>
    public static string StateConnecting => Get(nameof(StateConnecting));

    /// <summary>Connection state: connected.</summary>
    public static string StateConnected => Get(nameof(StateConnected));

    /// <summary>Connection state: last attempt failed.</summary>
    public static string StateError => Get(nameof(StateError));

    /// <summary>Tooltip label: server.</summary>
    public static string TooltipServer => Get(nameof(TooltipServer));

    /// <summary>Tooltip label: user.</summary>
    public static string TooltipUser => Get(nameof(TooltipUser));

    /// <summary>Tooltip label: state.</summary>
    public static string TooltipState => Get(nameof(TooltipState));

    /// <summary>Tooltip label: server version.</summary>
    public static string TooltipVersion => Get(nameof(TooltipVersion));

    /// <summary>Tooltip label: last error.</summary>
    public static string TooltipLastError => Get(nameof(TooltipLastError));

    /// <summary>Title of the password prompt.</summary>
    public static string PasswordDialogTitle => Get(nameof(PasswordDialogTitle));

    /// <summary>Password prompt text; {0} = connection label.</summary>
    public static string PasswordPromptText => Get(nameof(PasswordPromptText));

    /// <summary>Text of the Connect button.</summary>
    public static string ButtonConnect => Get(nameof(ButtonConnect));

    /// <summary>Placeholder node while children load.</summary>
    public static string NodeLoading => Get(nameof(NodeLoading));

    /// <summary>Folder with all databases.</summary>
    public static string NodeDatabases => Get(nameof(NodeDatabases));

    /// <summary>Folder with the server's own databases.</summary>
    public static string NodeSystemDatabases => Get(nameof(NodeSystemDatabases));

    /// <summary>Folder with the tables of a database.</summary>
    public static string NodeTables => Get(nameof(NodeTables));

    /// <summary>Folder with the columns of a table.</summary>
    public static string NodeColumns => Get(nameof(NodeColumns));

    /// <summary>Folder with the keys of a table.</summary>
    public static string NodeKeys => Get(nameof(NodeKeys));

    /// <summary>Folder with the check constraints of a table.</summary>
    public static string NodeConstraints => Get(nameof(NodeConstraints));

    /// <summary>Folder with the triggers of a table.</summary>
    public static string NodeTriggers => Get(nameof(NodeTriggers));

    /// <summary>Folder with the indexes of a table.</summary>
    public static string NodeIndexes => Get(nameof(NodeIndexes));

    /// <summary>Folder with the views of a database.</summary>
    public static string NodeViews => Get(nameof(NodeViews));

    /// <summary>Folder with the stored procedures of a database.</summary>
    public static string NodeStoredProcedures => Get(nameof(NodeStoredProcedures));

    /// <summary>Folder with the functions of a database.</summary>
    public static string NodeFunctions => Get(nameof(NodeFunctions));

    /// <summary>Folder with the events of a database.</summary>
    public static string NodeEvents => Get(nameof(NodeEvents));

    /// <summary>Folder with the parameters of a routine.</summary>
    public static string NodeParameters => Get(nameof(NodeParameters));

    /// <summary>Folder label while the name filter is active; {0} is the folder name.</summary>
    public static string NodeFiltered => Get(nameof(NodeFiltered));

    /// <summary>Name of the Security folder under a server (US-1002).</summary>
    public static string NodeSecurity => Get(nameof(NodeSecurity));

    /// <summary>Name of the Users folder.</summary>
    public static string NodeUsers => Get(nameof(NodeUsers));

    /// <summary>Name of the Roles folder.</summary>
    public static string NodeRoles => Get(nameof(NodeRoles));

    /// <summary>Suffix of a grant node with WITH GRANT OPTION.</summary>
    public static string NodeWithGrantOption => Get(nameof(NodeWithGrantOption));

    /// <summary>Watermark of the search box in the tool window.</summary>
    public static string SearchWatermark => Get(nameof(SearchWatermark));

    /// <summary>View Code failed; {0} = object, {1} = error.</summary>
    public static string ViewCodeFailed => Get(nameof(ViewCodeFailed));

    /// <summary>The server returned no source; {0} = object.</summary>
    public static string ViewCodeNoSource => Get(nameof(ViewCodeNoSource));

    /// <summary>Script As failed; {0} = statement, {1} = object, {2} = error.</summary>
    public static string ScriptFailed => Get(nameof(ScriptFailed));

    /// <summary>Title of the Script Database dialog and wait dialog (US-0603).</summary>
    public static string ScriptDatabaseTitle => Get(nameof(ScriptDatabaseTitle));

    /// <summary>Text at the top of the Script Database dialog; {0} = database, {1} = connection.</summary>
    public static string ScriptDatabasePrompt => Get(nameof(ScriptDatabasePrompt));

    /// <summary>Check box: CREATE DATABASE and USE.</summary>
    public static string ScriptDatabaseIncludeCreate => Get(nameof(ScriptDatabaseIncludeCreate));

    /// <summary>Check box: data.</summary>
    public static string ScriptDatabaseIncludeData => Get(nameof(ScriptDatabaseIncludeData));

    /// <summary>Hint below the data check box.</summary>
    public static string ScriptDatabaseDataHint => Get(nameof(ScriptDatabaseDataHint));

    /// <summary>Label of the output choice.</summary>
    public static string ScriptDatabaseOutput => Get(nameof(ScriptDatabaseOutput));

    /// <summary>Output choice: new query window.</summary>
    public static string ScriptDatabaseToWindow => Get(nameof(ScriptDatabaseToWindow));

    /// <summary>Output choice: file.</summary>
    public static string ScriptDatabaseToFile => Get(nameof(ScriptDatabaseToFile));

    /// <summary>Button that starts the script.</summary>
    public static string ButtonScript => Get(nameof(ButtonScript));

    /// <summary>Wait dialog text; {0} = database.</summary>
    public static string ScriptDatabaseWaitMessage => Get(nameof(ScriptDatabaseWaitMessage));

    /// <summary>Wait dialog progress; {0} = step, {1} = object, {2} = number, {3} = total.</summary>
    public static string ScriptDatabaseProgress => Get(nameof(ScriptDatabaseProgress));

    /// <summary>Name of the data step in the wait dialog.</summary>
    public static string ScriptDatabaseStepData => Get(nameof(ScriptDatabaseStepData));

    /// <summary>Filter of the save file dialog.</summary>
    public static string ScriptDatabaseFileFilter => Get(nameof(ScriptDatabaseFileFilter));

    /// <summary>The script was saved; {0} = database, {1} = path.</summary>
    public static string ScriptDatabaseSaved => Get(nameof(ScriptDatabaseSaved));

    /// <summary>Objects without source; {0} = database, {1} = objects.</summary>
    public static string ScriptDatabaseMissingSource => Get(nameof(ScriptDatabaseMissingSource));

    /// <summary>Script Database failed; {0} = database, {1} = error.</summary>
    public static string ScriptDatabaseFailed => Get(nameof(ScriptDatabaseFailed));

    /// <summary>Label of the connection list in the query editor bar (US-0701).</summary>
    public static string QueryBarConnection => Get(nameof(QueryBarConnection));

    /// <summary>Label of the database list in the query editor bar.</summary>
    public static string QueryBarDatabase => Get(nameof(QueryBarDatabase));

    /// <summary>Hint in the query editor bar when no connection is chosen.</summary>
    public static string QueryBarNoConnection => Get(nameof(QueryBarNoConnection));

    /// <summary>Shown in the query editor bar while the database list loads.</summary>
    public static string QueryBarLoading => Get(nameof(QueryBarLoading));

    /// <summary>New Query failed; {0} = error.</summary>
    public static string NewQueryFailed => Get(nameof(NewQueryFailed));

    /// <summary>Tooltip of the Execute button in the query editor bar (US-0703).</summary>
    public static string QueryExecuteToolTip => Get(nameof(QueryExecuteToolTip));

    /// <summary>Tooltip of the Explain button (US-1301).</summary>
    public static string QueryExplainToolTip => Get(nameof(QueryExplainToolTip));

    /// <summary>Tooltip of the Cancel button in the query editor bar.</summary>
    public static string QueryCancelToolTip => Get(nameof(QueryCancelToolTip));

    /// <summary>Tab with the result grids.</summary>
    public static string QueryResultsTab => Get(nameof(QueryResultsTab));

    /// <summary>Tab with the messages of a run.</summary>
    public static string QueryMessagesTab => Get(nameof(QueryMessagesTab));

    /// <summary>Shown in the results pane while a query runs.</summary>
    public static string QueryRunning => Get(nameof(QueryRunning));

    /// <summary>Tooltip of the close button of the results pane.</summary>
    public static string QueryClosePane => Get(nameof(QueryClosePane));

    /// <summary>Context menu of a result grid.</summary>
    public static string QueryCopy => Get(nameof(QueryCopy));

    /// <summary>Context menu of a result grid.</summary>
    public static string QueryCopyWithHeaders => Get(nameof(QueryCopyWithHeaders));

    /// <summary>Rows returned; {0} = count.</summary>
    public static string QueryRowsReturned => Get(nameof(QueryRowsReturned));

    /// <summary>Result over the row limit; {0} = count, {1} = limit.</summary>
    public static string QueryRowsTruncated => Get(nameof(QueryRowsTruncated));

    /// <summary>Rows changed; {0} = count.</summary>
    public static string QueryRowsAffected => Get(nameof(QueryRowsAffected));

    /// <summary>Server warning; {0} = level, {1} = code, {2} = message.</summary>
    public static string QueryWarning => Get(nameof(QueryWarning));

    /// <summary>Statement error; {0} = code, {1} = line, {2} = message.</summary>
    public static string QueryError => Get(nameof(QueryError));

    /// <summary>Connection error; {0} = message.</summary>
    public static string QueryConnectFailed => Get(nameof(QueryConnectFailed));

    /// <summary>Run finished; {0} = statements, {1} = duration.</summary>
    public static string QueryCompleted => Get(nameof(QueryCompleted));

    /// <summary>Run stopped by an error; {0} = duration.</summary>
    public static string QueryFailed => Get(nameof(QueryFailed));

    /// <summary>Run cancelled; {0} = duration.</summary>
    public static string QueryCancelled => Get(nameof(QueryCancelled));

    /// <summary>Run without a connection.</summary>
    public static string QueryNoConnection => Get(nameof(QueryNoConnection));

    /// <summary>Run of an empty selection or document.</summary>
    public static string QueryNothingToRun => Get(nameof(QueryNothingToRun));

    /// <summary>Explain of a statement that EXPLAIN does not support; {0} = line.</summary>
    public static string QueryNotExplainable => Get(nameof(QueryNotExplainable));

    /// <summary>Message after Explain; {0} = line, {1} = duration.</summary>
    public static string QueryPlanReady => Get(nameof(QueryPlanReady));

    /// <summary>The plan JSON could not be read; {0} = message.</summary>
    public static string QueryPlanUnreadable => Get(nameof(QueryPlanUnreadable));

    /// <summary>Tab with the estimated plan (US-1301).</summary>
    public static string QueryPlanTab => Get(nameof(QueryPlanTab));

    /// <summary>Plan step: query block; {0} = select id.</summary>
    public static string PlanQueryBlock => Get(nameof(PlanQueryBlock));

    /// <summary>Plan step: table; {0} = table.</summary>
    public static string PlanTable => Get(nameof(PlanTable));

    /// <summary>Plan step: nested loop join.</summary>
    public static string PlanNestedLoop => Get(nameof(PlanNestedLoop));

    /// <summary>Plan step: ORDER BY.</summary>
    public static string PlanOrderBy => Get(nameof(PlanOrderBy));

    /// <summary>Plan step: GROUP BY.</summary>
    public static string PlanGroupBy => Get(nameof(PlanGroupBy));

    /// <summary>Plan step: DISTINCT.</summary>
    public static string PlanDistinct => Get(nameof(PlanDistinct));

    /// <summary>Plan step: window functions.</summary>
    public static string PlanWindowing => Get(nameof(PlanWindowing));

    /// <summary>Plan step: UNION.</summary>
    public static string PlanUnion => Get(nameof(PlanUnion));

    /// <summary>Plan step: subquery.</summary>
    public static string PlanSubquery => Get(nameof(PlanSubquery));

    /// <summary>Plan step: materialized subquery.</summary>
    public static string PlanMaterialized => Get(nameof(PlanMaterialized));

    /// <summary>Plan step: filesort (MariaDB).</summary>
    public static string PlanFilesort => Get(nameof(PlanFilesort));

    /// <summary>Plan step: temporary table (MariaDB).</summary>
    public static string PlanTemporaryTable => Get(nameof(PlanTemporaryTable));

    /// <summary>Plan summary: cost; {0} = cost.</summary>
    public static string PlanCost => Get(nameof(PlanCost));

    /// <summary>Plan summary: key; {0} = key.</summary>
    public static string PlanKey => Get(nameof(PlanKey));

    /// <summary>Plan summary: rows; {0} = rows.</summary>
    public static string PlanRows => Get(nameof(PlanRows));

    /// <summary>Plan warning: full table scan.</summary>
    public static string PlanFullScanWarning => Get(nameof(PlanFullScanWarning));

    /// <summary>Plan warning: filesort.</summary>
    public static string PlanFilesortWarning => Get(nameof(PlanFilesortWarning));

    /// <summary>Plan warning: temporary table.</summary>
    public static string PlanTemporaryWarning => Get(nameof(PlanTemporaryWarning));

    /// <summary>Explain Analyze of a statement that is not a SELECT; {0} = line.</summary>
    public static string QueryNotAnalyzable => Get(nameof(QueryNotAnalyzable));

    /// <summary>Explain Analyze on a server without it; {0} = version.</summary>
    public static string QueryAnalyzeNotSupported => Get(nameof(QueryAnalyzeNotSupported));

    /// <summary>Message after Explain Analyze; {0} = line, {1} = duration.</summary>
    public static string QueryPlanAnalyzed => Get(nameof(QueryPlanAnalyzed));

    /// <summary>Tooltip of the Explain Analyze button (US-1302).</summary>
    public static string QueryExplainAnalyzeToolTip => Get(nameof(QueryExplainAnalyzeToolTip));

    /// <summary>Plan summary: actual rows; {0} = rows.</summary>
    public static string PlanActualRows => Get(nameof(PlanActualRows));

    /// <summary>Plan summary: actual time; {0} = milliseconds.</summary>
    public static string PlanActualTime => Get(nameof(PlanActualTime));

    /// <summary>Plan summary: loops; {0} = loops.</summary>
    public static string PlanLoops => Get(nameof(PlanLoops));

    /// <summary>Plan summary: a step that did not run.</summary>
    public static string PlanNeverExecuted => Get(nameof(PlanNeverExecuted));

    /// <summary>Plan warning: estimate far off; {0} = estimated rows, {1} = actual rows.</summary>
    public static string PlanEstimateWarning => Get(nameof(PlanEstimateWarning));

    /// <summary>Context menu of a result grid (US-0704).</summary>
    public static string QuerySaveAs => Get(nameof(QuerySaveAs));

    /// <summary>Title of the save dialog and messages of Save Results As.</summary>
    public static string QuerySaveTitle => Get(nameof(QuerySaveTitle));

    /// <summary>Filter of the save dialog.</summary>
    public static string QuerySaveFilter => Get(nameof(QuerySaveFilter));

    /// <summary>Confirmation when the result was over the row limit; {0} = total, {1} = rows shown.</summary>
    public static string QuerySaveTruncated => Get(nameof(QuerySaveTruncated));

    /// <summary>Save Results As failed; {0} = error.</summary>
    public static string QuerySaveFailed => Get(nameof(QuerySaveFailed));

    /// <summary>Run again or save the rows shown; {0} = total rows, {1} = rows shown.</summary>
    public static string QuerySaveRunAgain => Get(nameof(QuerySaveRunAgain));

    /// <summary>Wait dialog text of a full export.</summary>
    public static string QuerySaveAllWait => Get(nameof(QuerySaveAllWait));

    /// <summary>Wait dialog progress; {0} = rows saved so far.</summary>
    public static string QuerySaveAllProgress => Get(nameof(QuerySaveAllProgress));

    /// <summary>Full export done; {0} = rows, {1} = file.</summary>
    public static string QuerySaveAllDone => Get(nameof(QuerySaveAllDone));

    /// <summary>Full export failed; {0} = error.</summary>
    public static string QuerySaveAllFailed => Get(nameof(QuerySaveAllFailed));

    /// <summary>Activity monitor (US-1501); Title of messages of the activity monitor.</summary>
    public static string ActivityTitle => Get(nameof(ActivityTitle));

    /// <summary>Activity monitor (US-1501); {0} = connection.</summary>
    public static string ActivityCaption => Get(nameof(ActivityCaption));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityRefresh => Get(nameof(ActivityRefresh));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityKillQuery => Get(nameof(ActivityKillQuery));

    /// <summary>Activity monitor (US-1501); Toolbar button, without access key.</summary>
    public static string ActivityKillQueryPlain => Get(nameof(ActivityKillQueryPlain));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityKillConnection => Get(nameof(ActivityKillConnection));

    /// <summary>Activity monitor (US-1501); Toolbar button, without access key.</summary>
    public static string ActivityKillConnectionPlain => Get(nameof(ActivityKillConnectionPlain));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityAutoRefresh => Get(nameof(ActivityAutoRefresh));

    /// <summary>Activity monitor (US-1501); Sessions with command Sleep.</summary>
    public static string ActivityHideSleeping => Get(nameof(ActivityHideSleeping));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityCopyStatement => Get(nameof(ActivityCopyStatement));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnId => Get(nameof(ActivityColumnId));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnUser => Get(nameof(ActivityColumnUser));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnHost => Get(nameof(ActivityColumnHost));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnDatabase => Get(nameof(ActivityColumnDatabase));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnCommand => Get(nameof(ActivityColumnCommand));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnTime => Get(nameof(ActivityColumnTime));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnState => Get(nameof(ActivityColumnState));

    /// <summary>Activity monitor (US-1501).</summary>
    public static string ActivityColumnStatement => Get(nameof(ActivityColumnStatement));

    /// <summary>Activity monitor (US-1501); {0} = id, {1} = user, {2} = connection.</summary>
    public static string ActivityKillQueryConfirm => Get(nameof(ActivityKillQueryConfirm));

    /// <summary>Activity monitor (US-1501); {0} = id, {1} = user, {2} = connection.</summary>
    public static string ActivityKillConnectionConfirm => Get(nameof(ActivityKillConnectionConfirm));

    /// <summary>Activity monitor (US-1501); {0} = sessions shown, {1} = active, {2} = time.</summary>
    public static string ActivitySummary => Get(nameof(ActivitySummary));

    /// <summary>Activity monitor (US-1501); {0} = error.</summary>
    public static string ActivityLoadFailed => Get(nameof(ActivityLoadFailed));

    /// <summary>Activity monitor (US-1501); {0} = id, {1} = error.</summary>
    public static string ActivityKillFailed => Get(nameof(ActivityKillFailed));

    /// <summary>Activity monitor (US-1501); {0} = connection, {1} = error.</summary>
    public static string ActivityOpenFailed => Get(nameof(ActivityOpenFailed));

    /// <summary>Caption of the View Data window (US-0801); {0} = database, {1} = table.</summary>
    public static string ViewDataCaption => Get(nameof(ViewDataCaption));

    /// <summary>Tooltip of the Refresh button in View Data.</summary>
    public static string ViewDataRefresh => Get(nameof(ViewDataRefresh));

    /// <summary>Tooltip in View Data.</summary>
    public static string ViewDataFirstPage => Get(nameof(ViewDataFirstPage));

    /// <summary>Tooltip in View Data.</summary>
    public static string ViewDataPreviousPage => Get(nameof(ViewDataPreviousPage));

    /// <summary>Tooltip in View Data.</summary>
    public static string ViewDataNextPage => Get(nameof(ViewDataNextPage));

    /// <summary>Tooltip in View Data.</summary>
    public static string ViewDataLastPage => Get(nameof(ViewDataLastPage));

    /// <summary>Tooltip in View Data.</summary>
    public static string ViewDataClearFilters => Get(nameof(ViewDataClearFilters));

    /// <summary>Page number with the number of pages; {0} = page, {1} = pages.</summary>
    public static string ViewDataPage => Get(nameof(ViewDataPage));

    /// <summary>Page number while the number of pages is not known; {0} = page.</summary>
    public static string ViewDataPageUnknown => Get(nameof(ViewDataPageUnknown));

    /// <summary>Row count; {0} = rows.</summary>
    public static string ViewDataRows => Get(nameof(ViewDataRows));

    /// <summary>Estimated row count of a large table; {0} = rows.</summary>
    public static string ViewDataRowsEstimated => Get(nameof(ViewDataRowsEstimated));

    /// <summary>Shown while the rows are counted.</summary>
    public static string ViewDataCounting => Get(nameof(ViewDataCounting));

    /// <summary>Shown while a page loads.</summary>
    public static string ViewDataLoading => Get(nameof(ViewDataLoading));

    /// <summary>Tooltip of the filter boxes below the column headers.</summary>
    public static string ViewDataFilterToolTip => Get(nameof(ViewDataFilterToolTip));

    /// <summary>View Data could not open; {0} = object, {1} = error.</summary>
    public static string ViewDataFailed => Get(nameof(ViewDataFailed));

    /// <summary>Caption of the dependency window (US-1303).</summary>
    public static string DependenciesCaption => Get(nameof(DependenciesCaption));

    /// <summary>Opening the dependency window failed; {0} = database.object, {1} = error.</summary>
    public static string DependenciesFailed => Get(nameof(DependenciesFailed));

    /// <summary>Title in the dependency window; {0} = kind, {1} = database, {2} = name.</summary>
    public static string DependenciesTitle => Get(nameof(DependenciesTitle));

    /// <summary>Group of objects that use the object; {0} = count.</summary>
    public static string DependenciesUsedBy => Get(nameof(DependenciesUsedBy));

    /// <summary>Group of objects the object uses; {0} = count.</summary>
    public static string DependenciesUses => Get(nameof(DependenciesUses));

    /// <summary>Empty group.</summary>
    public static string DependenciesNone => Get(nameof(DependenciesNone));

    /// <summary>Note of a foreign key; {0} = constraint.</summary>
    public static string DependenciesForeignKey => Get(nameof(DependenciesForeignKey));

    /// <summary>Note of a trigger of the table.</summary>
    public static string DependenciesTrigger => Get(nameof(DependenciesTrigger));

    /// <summary>Note of a text match.</summary>
    public static string DependenciesSourceText => Get(nameof(DependenciesSourceText));

    /// <summary>Note when sources could not be read.</summary>
    public static string DependenciesIncomplete => Get(nameof(DependenciesIncomplete));

    /// <summary>Line while the dependencies load.</summary>
    public static string DependenciesLoading => Get(nameof(DependenciesLoading));

    /// <summary>Line when reading failed; {0} = message.</summary>
    public static string DependenciesReadFailed => Get(nameof(DependenciesReadFailed));

    /// <summary>Title of Generate Documentation (US-1201).</summary>
    public static string DocumentationTitle => Get(nameof(DocumentationTitle));

    /// <summary>Text at the top of the dialog; {0} = database, {1} = connection.</summary>
    public static string DocumentationPrompt => Get(nameof(DocumentationPrompt));

    /// <summary>Label above the parts.</summary>
    public static string DocumentationInclude => Get(nameof(DocumentationInclude));

    /// <summary>Part: tables.</summary>
    public static string DocumentationTables => Get(nameof(DocumentationTables));

    /// <summary>Part: views.</summary>
    public static string DocumentationViews => Get(nameof(DocumentationViews));

    /// <summary>Part: procedures and functions.</summary>
    public static string DocumentationRoutines => Get(nameof(DocumentationRoutines));

    /// <summary>Part: events.</summary>
    public static string DocumentationEvents => Get(nameof(DocumentationEvents));

    /// <summary>Option: dependencies per object.</summary>
    public static string DocumentationDependencies => Get(nameof(DocumentationDependencies));

    /// <summary>Option: a diagram per object.</summary>
    public static string DocumentationDiagrams => Get(nameof(DocumentationDiagrams));

    /// <summary>Progress step: dependencies.</summary>
    public static string DocumentationStepDependencies => Get(nameof(DocumentationStepDependencies));

    /// <summary>Hint below the parts.</summary>
    public static string DocumentationHint => Get(nameof(DocumentationHint));

    /// <summary>Button of the dialog.</summary>
    public static string ButtonGenerate => Get(nameof(ButtonGenerate));

    /// <summary>Filter of the save dialog.</summary>
    public static string DocumentationFileFilter => Get(nameof(DocumentationFileFilter));

    /// <summary>Filter of the save dialog for a PDF.</summary>
    public static string DocumentationPdfFileFilter => Get(nameof(DocumentationPdfFileFilter));

    /// <summary>Caption above the formats.</summary>
    public static string DocumentationFormat => Get(nameof(DocumentationFormat));

    /// <summary>Format Markdown.</summary>
    public static string DocumentationMarkdown => Get(nameof(DocumentationMarkdown));

    /// <summary>Format PDF.</summary>
    public static string DocumentationPdf => Get(nameof(DocumentationPdf));

    /// <summary>Opening the PDF failed; {0} = file, {1} = error.</summary>
    public static string DocumentationOpenFailed => Get(nameof(DocumentationOpenFailed));

    /// <summary>Wait dialog text; {0} = database.</summary>
    public static string DocumentationWaitMessage => Get(nameof(DocumentationWaitMessage));

    /// <summary>Wait dialog progress; {0} = part, {1} = object, {2} = current, {3} = total.</summary>
    public static string DocumentationProgress => Get(nameof(DocumentationProgress));

    /// <summary>Done; {0} = database, {1} = file. Asks to open it.</summary>
    public static string DocumentationSaved => Get(nameof(DocumentationSaved));

    /// <summary>Failed; {0} = database, {1} = error.</summary>
    public static string DocumentationFailed => Get(nameof(DocumentationFailed));

    /// <summary>Quick info: kind of a column (US-1402).</summary>
    public static string QuickInfoColumn => Get(nameof(QuickInfoColumn));

    /// <summary>Quick info: kind of a table.</summary>
    public static string QuickInfoTable => Get(nameof(QuickInfoTable));

    /// <summary>Quick info: kind of a view.</summary>
    public static string QuickInfoView => Get(nameof(QuickInfoView));

    /// <summary>Quick info: kind of a database.</summary>
    public static string QuickInfoDatabase => Get(nameof(QuickInfoDatabase));

    /// <summary>Quick info: a primary key column.</summary>
    public static string QuickInfoPrimaryKey => Get(nameof(QuickInfoPrimaryKey));

    /// <summary>Quick info: a foreign key column.</summary>
    public static string QuickInfoForeignKey => Get(nameof(QuickInfoForeignKey));

    /// <summary>Kind "Table" in the dependency window.</summary>
    public static string KindTable => Get(nameof(KindTable));

    /// <summary>Kind "View" in the dependency window.</summary>
    public static string KindView => Get(nameof(KindView));

    /// <summary>Kind "Procedure" in the dependency window.</summary>
    public static string KindProcedure => Get(nameof(KindProcedure));

    /// <summary>Kind "Function" in the dependency window.</summary>
    public static string KindFunction => Get(nameof(KindFunction));

    /// <summary>Kind "Trigger" in the dependency window.</summary>
    public static string KindTrigger => Get(nameof(KindTrigger));

    /// <summary>Kind "Event" in the dependency window.</summary>
    public static string KindEvent => Get(nameof(KindEvent));

    /// <summary>Tooltip of the Add row button in View Data (US-0802).</summary>
    public static string ViewDataAddRow => Get(nameof(ViewDataAddRow));

    /// <summary>Tooltip of the Delete button in View Data.</summary>
    public static string ViewDataDeleteRows => Get(nameof(ViewDataDeleteRows));

    /// <summary>Context menu of the View Data grid.</summary>
    public static string ViewDataDeleteRowsMenu => Get(nameof(ViewDataDeleteRowsMenu));

    /// <summary>Context menu of the View Data grid: put back the stored values of the row.</summary>
    public static string ViewDataUndoRow => Get(nameof(ViewDataUndoRow));

    /// <summary>Confirmation before deleting rows; {0} = number of rows, {1} = table.</summary>
    public static string ViewDataDeleteConfirm => Get(nameof(ViewDataDeleteConfirm));

    /// <summary>Notice in View Data for a view.</summary>
    public static string ViewDataReadOnlyView => Get(nameof(ViewDataReadOnlyView));

    /// <summary>Notice in View Data for a table without primary key.</summary>
    public static string ViewDataReadOnlyNoKey => Get(nameof(ViewDataReadOnlyNoKey));

    /// <summary>Error on a row whose UPDATE found no row.</summary>
    public static string ViewDataRowNotFound => Get(nameof(ViewDataRowNotFound));

    /// <summary>Question before paging, sorting, filtering or refreshing with unsaved rows.</summary>
    public static string ViewDataDiscardChanges => Get(nameof(ViewDataDiscardChanges));

    /// <summary>Title of the Delete dialog; {0} = object kind.</summary>
    public static string DeleteDialogTitle => Get(nameof(DeleteDialogTitle));

    /// <summary>Text above the SQL in the Delete dialog; {0} = object kind, {1} = full name.</summary>
    public static string DeletePrompt => Get(nameof(DeletePrompt));

    /// <summary>Label of the name box when a database is deleted; {0} = database name.</summary>
    public static string DeleteTypeName => Get(nameof(DeleteTypeName));

    /// <summary>Text of the Delete button.</summary>
    public static string ButtonDelete => Get(nameof(ButtonDelete));

    /// <summary>Text of the Copy button for the SQL in the Delete dialog.</summary>
    public static string ButtonCopySql => Get(nameof(ButtonCopySql));

    /// <summary>Error after a failed delete; {0} = object kind, {1} = full name, {2} = server message.</summary>
    public static string DeleteFailed => Get(nameof(DeleteFailed));

    /// <summary>Object kind "database" in Delete texts.</summary>
    public static string DropKindDatabase => Get(nameof(DropKindDatabase));

    /// <summary>Object kind "table" in Delete texts.</summary>
    public static string DropKindTable => Get(nameof(DropKindTable));

    /// <summary>Object kind "view" in Delete texts.</summary>
    public static string DropKindView => Get(nameof(DropKindView));

    /// <summary>Object kind "stored procedure" in Delete texts.</summary>
    public static string DropKindProcedure => Get(nameof(DropKindProcedure));

    /// <summary>Object kind "function" in Delete texts.</summary>
    public static string DropKindFunction => Get(nameof(DropKindFunction));

    /// <summary>Object kind "trigger" in Delete texts.</summary>
    public static string DropKindTrigger => Get(nameof(DropKindTrigger));

    /// <summary>Object kind "event" in Delete texts.</summary>
    public static string DropKindEvent => Get(nameof(DropKindEvent));

    /// <summary>Object kind "index" in Delete texts.</summary>
    public static string DropKindIndex => Get(nameof(DropKindIndex));

    /// <summary>Object kind "column" in Delete texts.</summary>
    public static string DropKindColumn => Get(nameof(DropKindColumn));

    /// <summary>Object kind "user" in Delete texts (US-1003).</summary>
    public static string DropKindUser => Get(nameof(DropKindUser));

    /// <summary>Object kind "role" in Delete texts (US-1003).</summary>
    public static string DropKindRole => Get(nameof(DropKindRole));

    /// <summary>Title of the New User dialog.</summary>
    public static string NewUserTitle => Get(nameof(NewUserTitle));

    /// <summary>Title of the Change Password dialog.</summary>
    public static string ChangePasswordTitle => Get(nameof(ChangePasswordTitle));

    /// <summary>Text at the top of the Change Password dialog; {0} = account.</summary>
    public static string ChangePasswordPrompt => Get(nameof(ChangePasswordPrompt));

    /// <summary>Label of the user name box.</summary>
    public static string UserName => Get(nameof(UserName));

    /// <summary>Label of the host box.</summary>
    public static string UserHost => Get(nameof(UserHost));

    /// <summary>Hint next to the host box.</summary>
    public static string UserHostHint => Get(nameof(UserHostHint));

    /// <summary>Label of the password box.</summary>
    public static string UserPassword => Get(nameof(UserPassword));

    /// <summary>Label of the confirm password box.</summary>
    public static string UserConfirmPassword => Get(nameof(UserConfirmPassword));

    /// <summary>Message when the account exists.</summary>
    public static string UserExists => Get(nameof(UserExists));

    /// <summary>Message when the passwords differ.</summary>
    public static string UserPasswordMismatch => Get(nameof(UserPasswordMismatch));

    /// <summary>Text of the Change button.</summary>
    public static string ButtonChange => Get(nameof(ButtonChange));

    /// <summary>Error after a failed CREATE USER; {0} = account, {1} = server message.</summary>
    public static string NewUserFailed => Get(nameof(NewUserFailed));

    /// <summary>Error after a failed ALTER USER; {0} = account, {1} = server message.</summary>
    public static string ChangePasswordFailed => Get(nameof(ChangePasswordFailed));

    /// <summary>Title of the Grant on Database dialog.</summary>
    public static string GrantTitle => Get(nameof(GrantTitle));

    /// <summary>Text at the top of the Grant dialog; {0} = account.</summary>
    public static string GrantPrompt => Get(nameof(GrantPrompt));

    /// <summary>Label of the database list.</summary>
    public static string GrantDatabase => Get(nameof(GrantDatabase));

    /// <summary>Check box ALL PRIVILEGES.</summary>
    public static string GrantAll => Get(nameof(GrantAll));

    /// <summary>Check box WITH GRANT OPTION.</summary>
    public static string GrantWithGrantOption => Get(nameof(GrantWithGrantOption));

    /// <summary>Text of the Grant button.</summary>
    public static string ButtonGrant => Get(nameof(ButtonGrant));

    /// <summary>Error after a failed GRANT; {0} = account, {1} = server message.</summary>
    public static string GrantFailed => Get(nameof(GrantFailed));

    /// <summary>Error when the databases cannot be read for the Grant dialog; {0} = message.</summary>
    public static string GrantLoadFailed => Get(nameof(GrantLoadFailed));

    /// <summary>Title of the New Role dialog (US-1004).</summary>
    public static string NewRoleTitle => Get(nameof(NewRoleTitle));

    /// <summary>Label of the role name box.</summary>
    public static string RoleName => Get(nameof(RoleName));

    /// <summary>Hint below the host of a role.</summary>
    public static string RoleHostHint => Get(nameof(RoleHostHint));

    /// <summary>Message when the role name exists.</summary>
    public static string RoleExists => Get(nameof(RoleExists));

    /// <summary>Error after a failed CREATE ROLE; {0} = role, {1} = server message.</summary>
    public static string NewRoleFailed => Get(nameof(NewRoleFailed));

    /// <summary>Title of the Grant Role dialog (US-1005).</summary>
    public static string GrantRoleTitle => Get(nameof(GrantRoleTitle));

    /// <summary>Text at the top of the Grant Role dialog; {0} = user.</summary>
    public static string GrantRolePrompt => Get(nameof(GrantRolePrompt));

    /// <summary>Label of the role list.</summary>
    public static string GrantRoleRole => Get(nameof(GrantRoleRole));

    /// <summary>Check box: make it a default role.</summary>
    public static string GrantRoleMakeDefault => Get(nameof(GrantRoleMakeDefault));

    /// <summary>Note when the current default roles could not be read.</summary>
    public static string GrantRoleDefaultsUnknown => Get(nameof(GrantRoleDefaultsUnknown));

    /// <summary>Message when the user already has every role; {0} = user.</summary>
    public static string GrantRoleNone => Get(nameof(GrantRoleNone));

    /// <summary>Error after a failed GRANT role; {0} = user, {1} = server message.</summary>
    public static string GrantRoleFailed => Get(nameof(GrantRoleFailed));

    /// <summary>Title of the Revoke dialog.</summary>
    public static string RevokeTitle => Get(nameof(RevokeTitle));

    /// <summary>Text above the SQL in the Revoke dialog; {0} = account.</summary>
    public static string RevokePrompt => Get(nameof(RevokePrompt));

    /// <summary>Text of the Revoke button.</summary>
    public static string ButtonRevoke => Get(nameof(ButtonRevoke));

    /// <summary>Error after a failed REVOKE; {0} = account, {1} = server message.</summary>
    public static string RevokeFailed => Get(nameof(RevokeFailed));

    /// <summary>Returns the text of an object kind for the Delete texts.</summary>
    public static string DropKind(Core.Management.DropObjectKind kind) => kind switch
    {
        Core.Management.DropObjectKind.User => DropKindUser,
        Core.Management.DropObjectKind.Role => DropKindRole,
        Core.Management.DropObjectKind.Database => DropKindDatabase,
        Core.Management.DropObjectKind.Table => DropKindTable,
        Core.Management.DropObjectKind.View => DropKindView,
        Core.Management.DropObjectKind.Procedure => DropKindProcedure,
        Core.Management.DropObjectKind.Function => DropKindFunction,
        Core.Management.DropObjectKind.Trigger => DropKindTrigger,
        Core.Management.DropObjectKind.Event => DropKindEvent,
        Core.Management.DropObjectKind.Index => DropKindIndex,
        _ => DropKindColumn,
    };

    /// <summary>Message when a new name is longer than 64 characters; {0} = maximum length.</summary>
    public static string RenameNameTooLong => Get(nameof(RenameNameTooLong));

    /// <summary>Message when a new name ends with a space.</summary>
    public static string RenameNameEndsWithSpace => Get(nameof(RenameNameEndsWithSpace));

    /// <summary>Error after a failed rename; {0} = object kind, {1} = full name, {2} = new name, {3} = server message.</summary>
    public static string RenameFailed => Get(nameof(RenameFailed));

    /// <summary>Title of the New Database dialog.</summary>
    public static string NewDatabaseTitle => Get(nameof(NewDatabaseTitle));

    /// <summary>Label of the name box in New Database.</summary>
    public static string NewDatabaseName => Get(nameof(NewDatabaseName));

    /// <summary>Label of the character set list in New Database.</summary>
    public static string NewDatabaseCharacterSet => Get(nameof(NewDatabaseCharacterSet));

    /// <summary>Label of the collation list in New Database.</summary>
    public static string NewDatabaseCollation => Get(nameof(NewDatabaseCollation));

    /// <summary>Label above the SQL in New Database.</summary>
    public static string NewDatabaseSql => Get(nameof(NewDatabaseSql));

    /// <summary>Message in New Database when the name exists.</summary>
    public static string NewDatabaseExists => Get(nameof(NewDatabaseExists));

    /// <summary>Text of the Create button.</summary>
    public static string ButtonCreate => Get(nameof(ButtonCreate));

    /// <summary>Error after a failed CREATE DATABASE; {0} = name, {1} = server message.</summary>
    public static string NewDatabaseFailed => Get(nameof(NewDatabaseFailed));

    /// <summary>Error when the character sets cannot be read; {0} = server label, {1} = message.</summary>
    public static string NewDatabaseLoadFailed => Get(nameof(NewDatabaseLoadFailed));

    /// <summary>Menu text on the Tables folder.</summary>
    public static string NewObjectTable => Get(nameof(NewObjectTable));

    /// <summary>Menu text on the Views folder.</summary>
    public static string NewObjectView => Get(nameof(NewObjectView));

    /// <summary>Menu text on the Stored Procedures folder.</summary>
    public static string NewObjectProcedure => Get(nameof(NewObjectProcedure));

    /// <summary>Menu text on the Functions folder.</summary>
    public static string NewObjectFunction => Get(nameof(NewObjectFunction));

    /// <summary>Menu text on the Events folder.</summary>
    public static string NewObjectEvent => Get(nameof(NewObjectEvent));

    /// <summary>Menu text on the Columns folder of a table.</summary>
    public static string NewObjectColumn => Get(nameof(NewObjectColumn));

    /// <summary>Menu text on the Triggers folder of a table.</summary>
    public static string NewObjectTrigger => Get(nameof(NewObjectTrigger));

    /// <summary>Menu text on the Indexes folder of a table.</summary>
    public static string NewObjectIndex => Get(nameof(NewObjectIndex));

    /// <summary>Error when the template document cannot be opened; {0} = message.</summary>
    public static string NewObjectFailed => Get(nameof(NewObjectFailed));

    /// <summary>Returns the menu text of New ... for a folder (US-0904).</summary>
    public static string NewObjectText(Core.Scripting.NewObjectKind kind) => kind switch
    {
        Core.Scripting.NewObjectKind.Table => NewObjectTable,
        Core.Scripting.NewObjectKind.View => NewObjectView,
        Core.Scripting.NewObjectKind.Procedure => NewObjectProcedure,
        Core.Scripting.NewObjectKind.Function => NewObjectFunction,
        Core.Scripting.NewObjectKind.Event => NewObjectEvent,
        Core.Scripting.NewObjectKind.Column => NewObjectColumn,
        Core.Scripting.NewObjectKind.Trigger => NewObjectTrigger,
        _ => NewObjectIndex,
    };

    /// <summary>Returns the text of an object kind for the Rename texts.</summary>
    public static string RenameKind(Core.Management.RenameObjectKind kind) => kind switch
    {
        Core.Management.RenameObjectKind.Table => DropKindTable,
        Core.Management.RenameObjectKind.View => DropKindView,
        _ => DropKindColumn,
    };

    /// <summary>Formats a resource with arguments, using the current UI culture.</summary>
    public static string Format(string format, params object?[] args) =>
        string.Format(Culture ?? CultureInfo.CurrentUICulture, format, args);

    private static string Get(string key) => _resourceManager.GetString(key, Culture) ?? key;
}
