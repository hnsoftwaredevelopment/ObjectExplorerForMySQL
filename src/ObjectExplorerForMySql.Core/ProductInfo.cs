namespace ObjectExplorerForMySql.Core;

/// <summary>
/// Fixed product facts shared by the extension and its tests.
/// </summary>
public static class ProductInfo
{
    /// <summary>Lowest MySQL server version the extension supports (ADR-008).</summary>
    public static readonly System.Version MinimumServerVersion = new(8, 0);

    /// <summary>
    /// Returns true when a server version string, as reported by <c>SELECT VERSION()</c>,
    /// is at or above <see cref="MinimumServerVersion"/>.
    /// </summary>
    /// <param name="serverVersion">For example "8.0.36", "8.4.2-log" or "10.11.6-MariaDB".</param>
    public static bool IsSupportedServerVersion(string? serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion))
        {
            return false;
        }

        var numericPart = serverVersion!.Trim();
        var end = 0;
        while (end < numericPart.Length && (char.IsDigit(numericPart[end]) || numericPart[end] == '.'))
        {
            end++;
        }

        if (!System.Version.TryParse(numericPart.Substring(0, end).TrimEnd('.'), out var version))
        {
            return false;
        }

        // MariaDB reports 10.x and later; ADR-008 supports MariaDB 10.6+ on a best-effort basis.
        if (serverVersion.IndexOf("MariaDB", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return version >= new System.Version(10, 6);
        }

        return version >= MinimumServerVersion;
    }
}
