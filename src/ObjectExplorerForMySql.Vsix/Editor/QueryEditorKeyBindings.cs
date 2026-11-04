using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.Editor;

/// <summary>
/// Gives every window of the MySQL Query Editor the Text Editor key bindings on top of its own scope (ADR-013).
/// The editor factory reports its own GUID as command UI context, so Ctrl+Shift+E and Alt+Break are bound only there;
/// without this inheritance the window would lose the normal editing shortcuts.
/// </summary>
internal sealed class QueryEditorKeyBindings : IVsRunningDocTableEvents
{
    /// <summary>Starts following the document windows. Call on the UI thread.</summary>
    public static void Start(IVsRunningDocumentTable table)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ErrorHandler.Failed(table.AdviseRunningDocTableEvents(new QueryEditorKeyBindings(), out _)))
        {
            ExtensionLog.Logger.Warning("Following the document windows failed; the MySQL Query Editor may miss the Text Editor shortcuts.");
        }
    }

    /// <inheritdoc />
    public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (fFirstShow == 0 || pFrame is null
            || ErrorHandler.Failed(pFrame.GetGuidProperty((int)__VSFPROPID.VSFPROPID_guidEditorType, out var editorType))
            || editorType != MySqlEditorFactory.Guid)
        {
            return VSConstants.S_OK;
        }

        var textEditor = VSConstants.GUID_TextEditorFactory;
        var hr = pFrame.SetGuidProperty((int)__VSFPROPID.VSFPROPID_InheritKeyBindings, ref textEditor);
        if (ErrorHandler.Failed(hr))
        {
            ExtensionLog.Logger.Warning($"Inheriting the Text Editor key bindings failed (0x{hr:X8}).");
        }

        return VSConstants.S_OK;
    }

    /// <inheritdoc />
    public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnAfterSave(uint docCookie) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) => VSConstants.S_OK;
}
