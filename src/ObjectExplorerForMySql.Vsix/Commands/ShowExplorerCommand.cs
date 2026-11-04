using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Extensibility.Shell;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;
using ObjectExplorerForMySql.Resources;
using ObjectExplorerForMySql.Settings;
using ObjectExplorerForMySql.ToolWindows;

namespace ObjectExplorerForMySql.Commands;

/// <summary>
/// View menu command that opens the explorer tool window.
/// </summary>
[VisualStudioContribution]
internal sealed class ShowExplorerCommand : Command
{
    // View menu group that also holds Server Explorer and SQL Server Object Explorer
    // (guidSHLMainMenu / IDG_VS_VIEW_DEV_WINDOWS in vsshlids.h).
    private static readonly Guid _shellMainMenuGuid = new("d309f791-903f-11d0-9efc-00a0c911004f");
    private const uint ViewDevWindowsGroupId = 0x0723;
    private const ushort Priority = 0x0300;

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%ObjectExplorerForMySql.ShowExplorerCommand.DisplayName%")
    {
        Placements = [CommandPlacement.VsctParent(_shellMainMenuGuid, ViewDevWindowsGroupId, Priority)],
        Icon = new(ImageMoniker.KnownValues.Database, IconSettings.IconAndText),
    };

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        // Not awaited: opening the window must not wait for the settings service. StartAsync never throws,
        // is a no-op once started and retries after a failure.
        _ = DebugLoggingMonitor.StartAsync(Extensibility, CancellationToken.None);
        _ = DragDropSettingsMonitor.StartAsync(Extensibility, CancellationToken.None);

        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var uiShell = await AsyncServiceProvider.GlobalProvider.GetServiceAsync<SVsUIShell, IVsUIShell>();
            var toolWindowGuid = ExplorerToolWindowPane.ToolWindowGuid;

            // FTW_fForceCreate loads the package on demand and creates the window when needed.
            var hr = uiShell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref toolWindowGuid, out var frame);
            ErrorHandler.ThrowOnFailure(hr);
            if (frame is null)
            {
                throw new InvalidOperationException("Tool window frame could not be created.");
            }

            ErrorHandler.ThrowOnFailure(frame.Show());
            ExtensionLog.Logger.Debug("Explorer tool window shown.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExtensionLog.Logger.Error("Opening the explorer tool window failed.", ex);
            await Extensibility.Shell().ShowPromptAsync(Strings.OpenExplorerFailed, PromptOptions.OK, cancellationToken);
        }
    }
}
