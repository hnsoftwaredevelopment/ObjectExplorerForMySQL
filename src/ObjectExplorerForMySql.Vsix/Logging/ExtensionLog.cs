using System;
using System.Threading;
using ObjectExplorerForMySql.Core.Logging;

namespace ObjectExplorerForMySql.Logging;

/// <summary>
/// Shared logger for the whole extension. Writes to the Output window pane "Object Explorer for MySQL".
/// Used by the VSSDK package and by VisualStudio.Extensibility commands alike.
/// </summary>
internal static class ExtensionLog
{
    private static readonly Lazy<ExtensionLogger> _logger =
        new(() => new ExtensionLogger(new OutputWindowLogSink(), LogLevel.Info), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the shared logger.</summary>
    public static IExtensionLogger Logger => _logger.Value;

    /// <summary>Switches Debug entries on or off, following the "Debug logging" option.</summary>
    public static void SetDebugEnabled(bool enabled)
    {
        var level = enabled ? LogLevel.Debug : LogLevel.Info;
        if (_logger.Value.MinimumLevel == level)
        {
            return;
        }

        _logger.Value.MinimumLevel = level;
        _logger.Value.Info(enabled ? "Debug logging enabled." : "Debug logging disabled.");
    }
}
