using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;

namespace ObjectExplorerForMySql.Settings;

/// <summary>
/// Starts following the options when the first text editor opens (US-0504). Visual Studio can restore the explorer
/// window at startup without running the View menu command, and the VSSDK package cannot reach the settings API
/// itself; every drop target is an editor, so this guarantees the options are known before the first drop.
/// </summary>
[VisualStudioContribution]
internal sealed class SettingsStarter : ExtensionPart, ITextViewOpenClosedListener
{
    /// <summary>Initializes a new instance of the <see cref="SettingsStarter"/> class.</summary>
    public SettingsStarter(Extension extension, VisualStudioExtensibility extensibility)
        : base(extension, extensibility)
    {
    }

    /// <inheritdoc />
    public TextViewExtensionConfiguration TextViewExtensionConfiguration => new()
    {
        AppliesTo = [DocumentFilter.FromDocumentType(DocumentType.KnownValues.Text)],
    };

    /// <inheritdoc />
    public Task TextViewOpenedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken)
    {
        // Both calls return at once after a successful start and never throw.
        _ = DragDropSettingsMonitor.StartAsync(Extensibility, CancellationToken.None);
        _ = DebugLoggingMonitor.StartAsync(Extensibility, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TextViewClosedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken) => Task.CompletedTask;
}
