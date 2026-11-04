using System;

namespace ObjectExplorerForMySql.Core.Explorer;

/// <summary>
/// Name filter of the explorer (US-0308). Applies to the objects in the Tables, Views, Stored Procedures,
/// Functions and Events folders: a name matches when it contains the text, ignoring case.
/// </summary>
public sealed class ExplorerFilter
{
    /// <summary>Gets the filter text, trimmed; empty when no filter is active.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether a filter is active.</summary>
    public bool IsActive => Text.Length > 0;

    /// <summary>Sets the filter text. Returns false when the (trimmed) text did not change.</summary>
    public bool SetText(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (string.Equals(trimmed, Text, StringComparison.Ordinal))
        {
            return false;
        }

        Text = trimmed;
        return true;
    }

    /// <summary>Returns true when no filter is active or <paramref name="name"/> contains the filter text (case-insensitive).</summary>
    public bool IsMatch(string name) => !IsActive || (name ?? string.Empty).IndexOf(Text, StringComparison.OrdinalIgnoreCase) >= 0;
}
