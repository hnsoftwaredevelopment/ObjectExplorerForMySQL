using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.Settings;

/// <summary>
/// Follows the Drag &amp; Drop options (US-0504) and keeps <see cref="Current"/> up to date, so a change applies to the
/// next drag without restarting Visual Studio. Until the options are read, the defaults apply.
/// </summary>
internal static class DragDropSettingsMonitor
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private static int _started;
    private static DropFormatOptions _current = DropFormatOptions.Default;

    /// <summary>Gets the current options.</summary>
    public static DropFormatOptions Current => Volatile.Read(ref _current);

    /// <summary>Starts following the options. A second call after a successful start does nothing. Never throws.</summary>
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

            // Each subscription calls its handler at least once with the current value.
            await WithTimeoutAsync(() => settings.SubscribeAsync(ExtensionSettings.SqlQuoting, CancellationToken.None, v =>
                Update(o => o with { SqlQuoting = ParseQuoteMode(v.ValueOrDefault(defaultValue: "whenNeeded")) })), cancellationToken);
            await WithTimeoutAsync(() => settings.SubscribeAsync(ExtensionSettings.OtherQuoting, CancellationToken.None, v =>
                Update(o => o with { OtherQuoting = ParseQuoteMode(v.ValueOrDefault(defaultValue: "never")) })), cancellationToken);
            await WithTimeoutAsync(() => settings.SubscribeAsync(ExtensionSettings.DatabasePrefixForTables, CancellationToken.None, v =>
                Update(o => o with { DatabasePrefixForTables = v.ValueOrDefault(defaultValue: false) })), cancellationToken);
            await WithTimeoutAsync(() => settings.SubscribeAsync(ExtensionSettings.DatabasePrefixForRoutines, CancellationToken.None, v =>
                Update(o => o with { DatabasePrefixForRoutines = v.ValueOrDefault(defaultValue: true) })), cancellationToken);
            await WithTimeoutAsync(() => settings.SubscribeAsync(ExtensionSettings.ColumnSeparator, CancellationToken.None, v =>
                Update(o => o with { ColumnSeparator = v.ValueOrDefault(defaultValue: "commaSpace") == "commaNewLine" ? ColumnSeparator.CommaNewLine : ColumnSeparator.CommaSpace })), cancellationToken);

            ExtensionLog.Logger.Debug($"Following the Drag & Drop options: {Current}.");
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref _started, 0);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _started, 0);
            ExtensionLog.Logger.Warning("Could not read the Drag & Drop options; the defaults apply.", ex);
        }
#pragma warning restore VSEXTPREVIEW_SETTINGS
    }

    /// <summary>Maps the stored option value to a <see cref="QuoteMode"/>; unknown values give the default.</summary>
    internal static QuoteMode ParseQuoteMode(string? value) => value switch
    {
        "always" => QuoteMode.Always,
        "never" => QuoteMode.Never,
        _ => QuoteMode.WhenNeeded,
    };

    private static void Update(Func<DropFormatOptions, DropFormatOptions> change)
    {
        DropFormatOptions before, after;
        do
        {
            before = Volatile.Read(ref _current);
            after = change(before);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _current, after, before), before));
    }

    private static async Task WithTimeoutAsync(Func<Task<IDisposable>> start, CancellationToken cancellationToken)
    {
        var subscribe = start();
        if (await Task.WhenAny(subscribe, Task.Delay(_timeout, cancellationToken)) != subscribe)
        {
            throw new TimeoutException($"Subscribing to a Drag & Drop option did not finish within {_timeout.TotalSeconds:0} seconds.");
        }

        // The subscription lives as long as Visual Studio; it is not disposed.
        _ = await subscribe;
    }
}
