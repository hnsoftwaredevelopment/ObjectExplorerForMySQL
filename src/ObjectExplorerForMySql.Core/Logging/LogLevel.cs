namespace ObjectExplorerForMySql.Core.Logging;

/// <summary>
/// Severity of a log entry. A lower value is more severe; a logger writes every entry
/// whose level is at or below its minimum level.
/// </summary>
public enum LogLevel
{
    /// <summary>An operation failed.</summary>
    Error = 0,

    /// <summary>Something unexpected happened, but the operation continued.</summary>
    Warning = 1,

    /// <summary>Normal progress information, for example a connection that was opened.</summary>
    Info = 2,

    /// <summary>Detailed diagnostics such as executed metadata queries. Only written when enabled in the options.</summary>
    Debug = 3,
}
