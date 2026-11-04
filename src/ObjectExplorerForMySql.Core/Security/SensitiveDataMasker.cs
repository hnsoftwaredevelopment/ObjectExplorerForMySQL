using System;
using System.Text.RegularExpressions;

namespace ObjectExplorerForMySql.Core.Security;

/// <summary>
/// Removes passwords from connection strings and free text before they are logged or shown.
/// </summary>
public static class SensitiveDataMasker
{
    /// <summary>Replacement text for a masked value.</summary>
    public const string Mask = "*****";

    /// <summary>Returned when masking cannot finish in time; the original text is never returned then.</summary>
    public const string MaskingFailedText = "[text hidden: password masking timed out]";

    // Matches "<key>=<value>" where the key ends in password, passwd or pwd
    // (Password, User Password, CertificatePassword, SslKeyPassword, Pwd, ...), case-insensitive.
    // The value is a single- or double-quoted string (quotes escaped by doubling), or
    // everything up to the next ';' or line break.
    private static readonly Regex _secretAssignment = new(
        @"(?<key>\b[a-z _]*?(?:password|passwd|pwd)\s*=\s*)(?<value>'(?:[^']|'')*'|""(?:[^""]|"""")*""|[^;\r\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Returns <paramref name="connectionString"/> with every password value replaced by <see cref="Mask"/>.
    /// Other keys and their order are kept as they are.
    /// </summary>
    public static string MaskConnectionString(string? connectionString) => MaskText(connectionString);

    /// <summary>
    /// Masks every <c>password=value</c> fragment in <paramref name="text"/>, for example a connection
    /// string inside an exception message. Returns an empty string for null.
    /// </summary>
    public static string MaskText(string? text)
    {
        if (text is null || text.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return _secretAssignment.Replace(text, match => match.Groups["key"].Value + Mask);
        }
        catch (RegexMatchTimeoutException)
        {
            return MaskingFailedText;
        }
    }
}
