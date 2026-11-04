using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace ObjectExplorerForMySql.Editor;

/// <summary>
/// "MySQL Query Editor" (US-0701): the standard Visual Studio code editor with the <see cref="MySqlContentType"/>
/// content type. The connection bar and, later, execution and coloring attach to that content type. Listed in
/// Open With for .sql files with a low priority, so it never takes over .sql files by itself.
/// </summary>
[Guid(GuidString)]
internal sealed class MySqlEditorFactory : IVsEditorFactory
{
    /// <summary>GUID of the editor factory.</summary>
    public const string GuidString = "4cc04b32-5bc1-44de-9b86-ba9fbc616303";

    /// <summary>GUID of the editor factory.</summary>
    public static readonly Guid Guid = new(GuidString);

    private IOleServiceProvider? _site;

    /// <inheritdoc />
    public int SetSite(IOleServiceProvider psp)
    {
        _site = psp;
        return VSConstants.S_OK;
    }

    /// <inheritdoc />
    public int Close()
    {
        _site = null;
        return VSConstants.S_OK;
    }

    /// <inheritdoc />
    public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
    {
        pbstrPhysicalView = null!;
        return rguidLogicalView == VSConstants.LOGVIEWID_Primary
            || rguidLogicalView == VSConstants.LOGVIEWID_TextView
            || rguidLogicalView == VSConstants.LOGVIEWID_Code
            || rguidLogicalView == VSConstants.LOGVIEWID_Debugging
            ? VSConstants.S_OK
            : VSConstants.E_NOTIMPL;
    }

    /// <inheritdoc />
    public int CreateEditorInstance(
        uint grfCreateDoc,
        string pszMkDocument,
        string pszPhysicalView,
        IVsHierarchy pvHier,
        uint itemid,
        IntPtr punkDocDataExisting,
        out IntPtr ppunkDocView,
        out IntPtr ppunkDocData,
        out string pbstrEditorCaption,
        out Guid pguidCmdUI,
        out int pgrfCDW)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ppunkDocView = IntPtr.Zero;
        ppunkDocData = IntPtr.Zero;
        pbstrEditorCaption = string.Empty;
        pguidCmdUI = Guid.Empty;
        pgrfCDW = 0;

        if ((grfCreateDoc & (VSConstants.CEF_OPENFILE | VSConstants.CEF_SILENT)) == 0)
        {
            return VSConstants.E_INVALIDARG;
        }

        // The file is already open in another editor (for example the T-SQL editor). Visual Studio then offers to
        // close that one first; sharing its buffer would change the content type under the other editor.
        if (punkDocDataExisting != IntPtr.Zero)
        {
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }

        if (_site is null || Package.GetGlobalService(typeof(SComponentModel)) is not IComponentModel componentModel)
        {
            return VSConstants.E_UNEXPECTED;
        }

        var adapters = componentModel.GetService<IVsEditorAdaptersFactoryService>();
        var contentType = componentModel.GetService<IContentTypeRegistryService>().GetContentType(MySqlContentType.Name);
        var buffer = adapters.CreateVsTextBufferAdapter(_site, contentType);

        // Keep the MySQL content type: without this the buffer takes the language of the .sql extension on load.
        if (buffer is IVsUserData userData)
        {
            var detectLanguage = VSConstants.VsTextBufferUserDataGuid.VsBufferDetectLangSID_guid;
            ErrorHandler.ThrowOnFailure(userData.SetData(ref detectLanguage, false));
        }

        var codeWindow = adapters.CreateVsCodeWindowAdapter(_site);
        ErrorHandler.ThrowOnFailure(codeWindow.SetBuffer((IVsTextLines)buffer));

        ppunkDocView = Marshal.GetIUnknownForObject(codeWindow);
        ppunkDocData = Marshal.GetIUnknownForObject(buffer);
        // Own command UI context: the key binding scope "MySQL Query Editor" with Ctrl+Shift+E. The window inherits the
        // Text Editor bindings through QueryEditorKeyBindings (ADR-013).
        pguidCmdUI = Guid;
        return VSConstants.S_OK;
    }
}
