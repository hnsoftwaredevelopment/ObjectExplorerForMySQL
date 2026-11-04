using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace ObjectExplorerForMySql.Settings;

#pragma warning disable VSEXTPREVIEW_SETTINGS // The settings API is marked preview in VisualStudio.Extensibility 17.14.

/// <summary>
/// Options shown under Tools > Options > Object Explorer for MySQL (Unified Settings).
/// </summary>
internal static class ExtensionSettings
{
    /// <summary>Settings category of the extension.</summary>
    [VisualStudioContribution]
    internal static SettingCategory Category { get; } = new("objectExplorerForMySql", "%ObjectExplorerForMySql.Settings.Category.DisplayName%")
    {
        Description = "%ObjectExplorerForMySql.Settings.Category.Description%",
    };

    /// <summary>When true, Debug entries (for example executed metadata queries) are written to the Output window.</summary>
    [VisualStudioContribution]
    internal static Setting.Boolean DebugLogging { get; } = new("debugLogging", "%ObjectExplorerForMySql.Settings.DebugLogging.DisplayName%", Category, defaultValue: false)
    {
        Description = "%ObjectExplorerForMySql.Settings.DebugLogging.Description%",
    };

    /// <summary>Subcategory "Drag &amp; Drop" (US-0504).</summary>
    [VisualStudioContribution]
    internal static SettingCategory DragDropCategory { get; } = new("dragDrop", "%ObjectExplorerForMySql.Settings.DragDrop.DisplayName%", Category)
    {
        Description = "%ObjectExplorerForMySql.Settings.DragDrop.Description%",
    };

    /// <summary>Backticks in .sql files: "whenNeeded" (default, decision D-5) or "always".</summary>
    [VisualStudioContribution]
    internal static Setting.Enum SqlQuoting { get; } = new(
        "sqlQuoting",
        "%ObjectExplorerForMySql.Settings.SqlQuoting.DisplayName%",
        DragDropCategory,
        [
            new("whenNeeded", "%ObjectExplorerForMySql.Settings.Quoting.WhenNeeded%"),
            new("always", "%ObjectExplorerForMySql.Settings.Quoting.Always%"),
        ],
        defaultValue: "whenNeeded")
    {
        Description = "%ObjectExplorerForMySql.Settings.SqlQuoting.Description%",
    };

    /// <summary>Backticks in other files and on the clipboard: "never" (default), "whenNeeded" or "always".</summary>
    [VisualStudioContribution]
    internal static Setting.Enum OtherQuoting { get; } = new(
        "otherQuoting",
        "%ObjectExplorerForMySql.Settings.OtherQuoting.DisplayName%",
        DragDropCategory,
        [
            new("never", "%ObjectExplorerForMySql.Settings.Quoting.Never%"),
            new("whenNeeded", "%ObjectExplorerForMySql.Settings.Quoting.WhenNeeded%"),
            new("always", "%ObjectExplorerForMySql.Settings.Quoting.Always%"),
        ],
        defaultValue: "never")
    {
        Description = "%ObjectExplorerForMySql.Settings.OtherQuoting.Description%",
    };

    /// <summary>Database prefix for tables and views (default off).</summary>
    [VisualStudioContribution]
    internal static Setting.Boolean DatabasePrefixForTables { get; } = new("databasePrefixForTables", "%ObjectExplorerForMySql.Settings.DatabasePrefixForTables.DisplayName%", DragDropCategory, defaultValue: false)
    {
        Description = "%ObjectExplorerForMySql.Settings.DatabasePrefixForTables.Description%",
    };

    /// <summary>Database prefix in a CALL or SELECT of a routine (default on).</summary>
    [VisualStudioContribution]
    internal static Setting.Boolean DatabasePrefixForRoutines { get; } = new("databasePrefixForRoutines", "%ObjectExplorerForMySql.Settings.DatabasePrefixForRoutines.DisplayName%", DragDropCategory, defaultValue: true)
    {
        Description = "%ObjectExplorerForMySql.Settings.DatabasePrefixForRoutines.Description%",
    };

    /// <summary>Separator of a column list: "commaSpace" (default) or "commaNewLine".</summary>
    [VisualStudioContribution]
    internal static Setting.Enum ColumnSeparator { get; } = new(
        "columnSeparator",
        "%ObjectExplorerForMySql.Settings.ColumnSeparator.DisplayName%",
        DragDropCategory,
        [
            new("commaSpace", "%ObjectExplorerForMySql.Settings.ColumnSeparator.CommaSpace%"),
            new("commaNewLine", "%ObjectExplorerForMySql.Settings.ColumnSeparator.CommaNewLine%"),
        ],
        defaultValue: "commaSpace")
    {
        Description = "%ObjectExplorerForMySql.Settings.ColumnSeparator.Description%",
    };
}

#pragma warning restore VSEXTPREVIEW_SETTINGS
