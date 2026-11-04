namespace ObjectExplorerForMySql.Core.Logging;

/// <summary>
/// Destination for formatted log lines, for example the Visual Studio Output window.
/// </summary>
public interface ILogSink
{
    /// <summary>Writes one formatted entry. Can be called from any thread.</summary>
    /// <param name="formattedEntry">The complete entry, already masked. May contain line breaks.</param>
    void Write(string formattedEntry);
}
