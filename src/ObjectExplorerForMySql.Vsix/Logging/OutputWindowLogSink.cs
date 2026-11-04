using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Logging;

/// <summary>
/// Writes log entries to the "Object Explorer for MySQL" pane of the Output window.
/// The pane is created on the UI thread on first use; entries written before that are buffered
/// and written in their original order.
/// </summary>
internal sealed class OutputWindowLogSink : ILogSink
{
    private static readonly Guid _paneGuid = new("2b9abebf-2fab-4098-a01b-49f187f6465e");

    private readonly object _gate = new();
    private readonly List<string> _pending = [];
    private IVsOutputWindowPane? _pane;
    private bool _creatingPane;

    /// <inheritdoc />
    public void Write(string formattedEntry)
    {
        lock (_gate)
        {
            if (_pane is not null)
            {
                // OutputStringThreadSafe is documented as callable from any thread; the analyzer cannot know that.
                // Writing under the lock keeps the order of entries.
#pragma warning disable VSTHRD010
                _pane.OutputStringThreadSafe(formattedEntry + Environment.NewLine);
#pragma warning restore VSTHRD010
                return;
            }

            _pending.Add(formattedEntry);
            if (_creatingPane)
            {
                return;
            }

            _creatingPane = true;
        }

        // Fire and forget on purpose: a log call must never wait for the UI thread (that could deadlock a
        // caller that holds the UI thread). CreatePaneAsync catches its own exceptions.
#pragma warning disable VSSDK007
        ThreadHelper.JoinableTaskFactory.RunAsync(CreatePaneAsync).FileAndForget("ObjectExplorerForMySql/OutputPane/Create");
#pragma warning restore VSSDK007
    }

    private async System.Threading.Tasks.Task CreatePaneAsync()
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var outputWindow = await AsyncServiceProvider.GlobalProvider.GetServiceAsync<SVsOutputWindow, IVsOutputWindow>();
            var paneGuid = _paneGuid;
            if (outputWindow.GetPane(ref paneGuid, out var pane) != 0 || pane is null)
            {
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(
                    outputWindow.CreatePane(ref paneGuid, Strings.OutputPaneTitle, fInitVisible: 1, fClearWithSolution: 0));
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(outputWindow.GetPane(ref paneGuid, out pane));
            }

            lock (_gate)
            {
                _pane = pane;
                foreach (var entry in _pending)
                {
                    pane.OutputStringThreadSafe(entry + Environment.NewLine);
                }

                _pending.Clear();
                _creatingPane = false;
            }
        }
        catch (Exception)
        {
            lock (_gate)
            {
                // Next write tries again. Entries stay buffered so nothing is lost.
                _creatingPane = false;
            }
        }
    }
}
