using System;
using System.Globalization;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Logging;

/// <summary>
/// Default <see cref="IExtensionLogger"/>: filters on level, formats one entry per call,
/// masks passwords and forwards the result to an <see cref="ILogSink"/>.
/// </summary>
public sealed class ExtensionLogger : IExtensionLogger
{
    private readonly ILogSink _sink;
    private readonly Func<DateTimeOffset> _clock;
    private volatile LogLevel _minimumLevel;

    /// <summary>Initializes a new instance of the <see cref="ExtensionLogger"/> class.</summary>
    /// <param name="sink">Destination of the formatted entries.</param>
    /// <param name="minimumLevel">Most detailed level that is written. Default <see cref="LogLevel.Info"/>.</param>
    /// <param name="clock">Time source; defaults to the local time. Tests pass a fixed time.</param>
    public ExtensionLogger(ILogSink sink, LogLevel minimumLevel = LogLevel.Info, Func<DateTimeOffset>? clock = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _minimumLevel = minimumLevel;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Gets or sets the most detailed level that is written. Thread-safe.</summary>
    public LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => level <= _minimumLevel;

    /// <inheritdoc />
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        try
        {
            _sink.Write(Format(_clock(), level, message, exception));
        }
        catch (Exception)
        {
            // Swallowed on purpose: a failing log destination must not break the extension.
        }
    }

    /// <summary>
    /// Formats one entry as <c>[HH:mm:ss.fff] LEVEL  message</c>, followed by the exception on new lines.
    /// Passwords in the message and the exception text are masked.
    /// </summary>
    internal static string Format(DateTimeOffset time, LogLevel level, string? message, Exception? exception)
    {
        var text = string.Format(
            CultureInfo.InvariantCulture,
            "[{0:HH:mm:ss.fff}] {1,-7}{2}",
            time,
            LevelText(level),
            SensitiveDataMasker.MaskText(message));

        if (exception is not null)
        {
            text += Environment.NewLine + SensitiveDataMasker.MaskText(exception.ToString());
        }

        return text;
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warning => "WARN",
        LogLevel.Info => "INFO",
        LogLevel.Debug => "DEBUG",
        _ => level.ToString().ToUpperInvariant(),
    };
}
