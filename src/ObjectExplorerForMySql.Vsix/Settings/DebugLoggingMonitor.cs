using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.Settings;

/// <summary>
/// Reads the "Debug logging" option once and follows its changes while Visual Studio runs.
/// Started from VisualStudio.Extensibility parts, which receive <see cref="VisualStudioExtensibility"/>
/// through dependency injection. (A VSSDK package cannot query it: the service is not proffered to packages.)
/// </summary>
internal static class DebugLoggingMonitor
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private static int _started;
    private static IDisposable? _subscription;

    /// <summary>
    /// Starts following the option. Calling it again after a successful start does nothing;
    /// after a failure or timeout the next call tries again. Never throws.
    /// </summary>
    public static async Task StartAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

#pragma warning disable VSEXTPREVIEW_SETTINGS // The settings API is marked preview in VisualStudio.Extensibility 17.14.
        try
        {
            var settings = extensibility.Settings();

            var readTask = settings.ReadEffectiveValueAsync(ExtensionSettings.DebugLogging, cancellationToken);
            if (await Task.WhenAny(readTask, Task.Delay(_timeout, cancellationToken)) != readTask)
            {
                Interlocked.Exchange(ref _started, 0);
                ExtensionLog.Logger.Warning($"Reading the 'Debug logging' option did not finish within {_timeout.TotalSeconds:0} seconds; Debug entries stay off.");
                return;
            }

            var current = await readTask;
            if (!current.Succeeded)
            {
                throw new InvalidOperationException("Reading the 'Debug logging' option failed.", current.Error);
            }

            var enabled = current.ValueOrDefault(defaultValue: false);
            ExtensionLog.Logger.Info($"Option 'Debug logging' is {(enabled ? "on" : "off")}.");
            ExtensionLog.SetDebugEnabled(enabled);

            var subscribeTask = settings.SubscribeAsync(
                ExtensionSettings.DebugLogging,
                CancellationToken.None,
                value => ExtensionLog.SetDebugEnabled(value.ValueOrDefault(defaultValue: false)));
            if (await Task.WhenAny(subscribeTask, Task.Delay(_timeout, cancellationToken)) != subscribeTask)
            {
                ExtensionLog.Logger.Warning("Subscribing to changes of the 'Debug logging' option timed out; changes apply after a restart.");
                return;
            }

            _subscription = await subscribeTask;
            ExtensionLog.Logger.Debug("Following changes of the 'Debug logging' option.");
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref _started, 0);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _started, 0);
            ExtensionLog.Logger.Warning("Could not read the 'Debug logging' option; Debug entries stay off.", ex);
        }
#pragma warning restore VSEXTPREVIEW_SETTINGS
    }
}
