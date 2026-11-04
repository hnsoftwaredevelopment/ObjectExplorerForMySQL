using System;

namespace ObjectExplorerForMySql.Core.DragDrop;

/// <summary>When names get backticks (US-0504).</summary>
public enum QuoteMode
{
    /// <summary>Only when MySQL requires it: reserved word, special character, leading digit.</summary>
    WhenNeeded,

    /// <summary>Always.</summary>
    Always,

    /// <summary>Never (other files only; SQL files always quote when needed, or the SQL breaks).</summary>
    Never,
}

/// <summary>Separator between the names of a column list (US-0504).</summary>
public enum ColumnSeparator
{
    /// <summary><c>a, b, c</c>.</summary>
    CommaSpace,

    /// <summary>One name per line: <c>a,</c> newline <c>b</c>.</summary>
    CommaNewLine,
}

/// <summary>Options for the text of a drag or copy (Tools > Options > Object Explorer for MySQL > Drag &amp; Drop, US-0504).</summary>
public sealed record DropFormatOptions
{
    /// <summary>Gets the defaults: quote when needed in SQL files (decision D-5), plain names elsewhere, prefix only for calls.</summary>
    public static DropFormatOptions Default { get; } = new();

    /// <summary>Gets the quoting in .sql files. <see cref="QuoteMode.Never"/> acts as <see cref="QuoteMode.WhenNeeded"/>.</summary>
    public QuoteMode SqlQuoting { get; init; } = QuoteMode.WhenNeeded;

    /// <summary>Gets the quoting in other files and on the clipboard (Ctrl+C).</summary>
    public QuoteMode OtherQuoting { get; init; } = QuoteMode.Never;

    /// <summary>Gets a value indicating whether table and view names get the database: <c>sakila.actor</c>.</summary>
    public bool DatabasePrefixForTables { get; init; }

    /// <summary>Gets a value indicating whether a CALL or SELECT of a routine gets the database: <c>CALL sakila.film_in_stock(...)</c>.</summary>
    public bool DatabasePrefixForRoutines { get; init; } = true;

    /// <summary>Gets the separator of a column list.</summary>
    public ColumnSeparator ColumnSeparator { get; init; } = ColumnSeparator.CommaSpace;

    /// <summary>Gets the separator text: <c>", "</c> or a comma and a line break.</summary>
    public string SeparatorText => ColumnSeparator == ColumnSeparator.CommaNewLine ? "," + Environment.NewLine : ", ";

    /// <summary>Quotes a name for a .sql file.</summary>
    public string QuoteForSql(string name) =>
        SqlQuoting == QuoteMode.Always ? MySqlIdentifier.Quote(name) : MySqlIdentifier.QuoteIfNeeded(name);

    /// <summary>Quotes a name for any other file or the clipboard.</summary>
    public string QuoteForOther(string name) => OtherQuoting switch
    {
        QuoteMode.Always => MySqlIdentifier.Quote(name),
        QuoteMode.WhenNeeded => MySqlIdentifier.QuoteIfNeeded(name),
        _ => name,
    };
}
