using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.ToolWindows;

/// <summary>
/// One run of the tool window search box (US-0308). The search host starts it on a background thread
/// for every keystroke (instant search); the filter itself is set on the UI thread.
/// </summary>
internal sealed class ExplorerSearchTask : VsSearchTask
{
    private readonly ExplorerViewModel _explorer;

    /// <summary>Initializes a new instance of the <see cref="ExplorerSearchTask"/> class.</summary>
    public ExplorerSearchTask(uint cookie, IVsSearchQuery query, IVsSearchCallback callback, ExplorerViewModel explorer)
        : base(cookie, query, callback)
    {
        _explorer = explorer;
    }

    /// <inheritdoc />
    protected override void OnStartSearch()
    {
        // The search host hands the query to this background thread; reading it here is the documented pattern
        // (walkthrough "Add search to a tool window"). Only the filter itself needs the UI thread.
#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
        var text = SearchQuery.SearchString;
#pragma warning restore VSTHRD010
        ErrorCode = VSConstants.S_OK;
        try
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _explorer.FilterText = text;
            });
        }
        catch (Exception ex)
        {
            ErrorCode = VSConstants.E_FAIL;
            ExtensionLog.Logger.Error("Applying the filter failed.", ex);
        }

        base.OnStartSearch();
    }

    /// <inheritdoc />
    protected override void OnStopSearch() => SearchResults = 0;
}
