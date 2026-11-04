using System;

namespace ObjectExplorerForMySql.Core.Logging;

/// <summary>
/// Logger used throughout the extension. Implementations never throw and always mask secrets.
/// </summary>
public interface IExtensionLogger
{
    /// <summary>Gets the most detailed level that is currently written.</summary>
    LogLevel MinimumLevel { get; }

    /// <summary>Returns true when entries of <paramref name="level"/> are written.</summary>
    bool IsEnabled(LogLevel level);

    /// <summary>Writes an entry when <paramref name="level"/> is enabled.</summary>
    void Log(LogLevel level, string message, Exception? exception = null);
}

/// <summary>Convenience methods for <see cref="IExtensionLogger"/>.</summary>
public static class ExtensionLoggerExtensions
{
    /// <summary>Writes an <see cref="LogLevel.Error"/> entry.</summary>
    public static void Error(this IExtensionLogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Error, message, exception);

    /// <summary>Writes a <see cref="LogLevel.Warning"/> entry.</summary>
    public static void Warning(this IExtensionLogger logger, string message, Exception? exception = null)
        => logger.Log(LogLevel.Warning, message, exception);

    /// <summary>Writes an <see cref="LogLevel.Info"/> entry.</summary>
    public static void Info(this IExtensionLogger logger, string message)
        => logger.Log(LogLevel.Info, message);

    /// <summary>Writes a <see cref="LogLevel.Debug"/> entry.</summary>
    public static void Debug(this IExtensionLogger logger, string message)
        => logger.Log(LogLevel.Debug, message);
}
