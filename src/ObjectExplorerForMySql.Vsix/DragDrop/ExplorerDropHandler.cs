using System;
using System.ComponentModel.Composition;
using System.IO;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.DragDrop;
using Microsoft.VisualStudio.Utilities;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.DragDrop;

/// <summary>
/// Registers <see cref="ExplorerDropHandler"/> for objects dragged from the explorer into any text editor (US-0501).
/// It runs before the standard text handlers, so it can pick the SQL or the plain text.
/// </summary>
[Export(typeof(IDropHandlerProvider))]
[DropFormat(DragData.SqlTextFormat)]
[DropFormat(DragData.ConnectionStringFormat)]
[ContentType("text")]
[Name("ObjectExplorerForMySqlDropHandler")]
[Order(Before = "LanguageServiceTextDropHandler")]
[Order(Before = "DefaultTextDropHandler")]
internal sealed class ExplorerDropHandlerProvider : IDropHandlerProvider
{
    /// <inheritdoc />
    public IDropHandler GetAssociatedDropHandler(IWpfTextView wpfTextView) =>
        wpfTextView.Properties.GetOrCreateSingletonProperty(() => new ExplorerDropHandler(wpfTextView));
}

/// <summary>Inserts the dragged text at the drop position: quoted SQL in .sql files, the plain name elsewhere.</summary>
internal sealed class ExplorerDropHandler : IDropHandler
{
    private readonly IWpfTextView _view;

    public ExplorerDropHandler(IWpfTextView view)
    {
        _view = view;
    }

    /// <summary>Returns true when the editor shows a SQL file: extension .sql or a SQL content type.</summary>
    internal static bool IsSqlFile(string? filePath, string? contentType) =>
        string.Equals(Path.GetExtension(filePath ?? string.Empty), ".sql", StringComparison.OrdinalIgnoreCase)
        || (contentType ?? string.Empty).IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0;

    public DragDropPointerEffects HandleDragStarted(DragDropInfo dragDropInfo) => DragDropPointerEffects.Copy | DragDropPointerEffects.Track;

    public DragDropPointerEffects HandleDraggingOver(DragDropInfo dragDropInfo) => DragDropPointerEffects.Copy | DragDropPointerEffects.Track;

    public DragDropPointerEffects HandleDataDropped(DragDropInfo dragDropInfo)
    {
        try
        {
            var text = SelectText(dragDropInfo);
            if (string.IsNullOrEmpty(text))
            {
                return DragDropPointerEffects.None;
            }

            // Fill virtual space (after the end of a line) with spaces, as the standard text drop does.
            var position = dragDropInfo.VirtualBufferPosition;
            var insert = new string(' ', position.VirtualSpaces) + text;
            var snapshot = _view.TextBuffer.Insert(position.Position.Position, insert);

            var end = new SnapshotPoint(snapshot, position.Position.Position + insert.Length);
            _view.Caret.MoveTo(end);
            _view.Selection.Clear();
            return DragDropPointerEffects.Copy;
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error("Dropping into the editor failed.", ex);
            return DragDropPointerEffects.None;
        }
    }

    public bool IsDropEnabled(DragDropInfo dragDropInfo) =>
        (dragDropInfo.Data.GetDataPresent(DragData.SqlTextFormat) || dragDropInfo.Data.GetDataPresent(DragData.ConnectionStringFormat))
        && !_view.Options.GetOptionValue(DefaultTextViewOptions.ViewProhibitUserInputId);

    public void HandleDragCanceled()
    {
    }

    private string? SelectText(DragDropInfo dragDropInfo)
    {
        var data = dragDropInfo.Data;
        _view.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument? document);

        // US-0505: a connection string in the form of the file: appsettings.json entry, C# or VB string, config element.
        if (data.GetData(DragData.ConnectionStringFormat) is string connectionString)
        {
            var name = data.GetData(DragData.ConnectionNameFormat) as string ?? string.Empty;
            return Core.DragDrop.ConnectionStringText.Format(connectionString, name, Core.DragDrop.ConnectionStringText.TargetFor(document?.FilePath));
        }

        if (IsSqlFile(document?.FilePath, _view.TextBuffer.ContentType.TypeName))
        {
            // Alt while dropping: only the name instead of a CALL/SELECT for a routine (US-0503).
            var nameOnly = (dragDropInfo.KeyStates & System.Windows.DragDropKeyStates.AltKey) != 0;
            return (nameOnly ? data.GetData(DragData.SqlNameFormat) : null) as string ?? data.GetData(DragData.SqlTextFormat) as string;
        }

        return data.GetData(System.Windows.DataFormats.UnicodeText) as string;
    }
}
