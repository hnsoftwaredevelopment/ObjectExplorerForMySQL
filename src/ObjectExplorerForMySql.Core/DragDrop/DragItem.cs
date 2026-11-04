using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.DragDrop;

/// <summary>Kind of object that can be dragged into the editor (E05).</summary>
public enum DragObjectKind
{
    /// <summary>Database (schema).</summary>
    Database,

    /// <summary>Base table.</summary>
    Table,

    /// <summary>View.</summary>
    View,

    /// <summary>Column of a table or view.</summary>
    Column,

    /// <summary>Stored procedure.</summary>
    Procedure,

    /// <summary>Stored function.</summary>
    Function,
}

/// <summary>An object dragged or copied from the explorer (US-0501).</summary>
public sealed class DragItem
{
    /// <summary>Initializes a new instance of the <see cref="DragItem"/> class.</summary>
    /// <param name="kind">Object kind.</param>
    /// <param name="name">Object name.</param>
    /// <param name="database">Database of the object; null for a database itself.</param>
    /// <param name="parent">Table or view of a column; otherwise null.</param>
    public DragItem(DragObjectKind kind, string name, string? database = null, string? parent = null)
    {
        Kind = kind;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Database = database;
        Parent = parent;
    }

    /// <summary>Gets the object kind.</summary>
    public DragObjectKind Kind { get; }

    /// <summary>Gets the object name.</summary>
    public string Name { get; }

    /// <summary>Gets the database, or null.</summary>
    public string? Database { get; }

    /// <summary>Gets the table or view of a column, or null.</summary>
    public string? Parent { get; }

    /// <summary>
    /// Gets the parameter names of a procedure or function in order (US-0503), or null when they are not known;
    /// then only the name is dropped.
    /// </summary>
    public IReadOnlyList<string>? Parameters { get; init; }
}

/// <summary>The two texts of a drag: one for SQL editors and one for every other target.</summary>
public sealed class DropText
{
    /// <summary>Initializes a new instance of the <see cref="DropText"/> class.</summary>
    /// <param name="sql">Text for a SQL file.</param>
    /// <param name="plain">Text for other targets.</param>
    /// <param name="sqlName">Name only for a SQL file (Alt while dropping); defaults to <paramref name="sql"/>.</param>
    public DropText(string sql, string plain, string? sqlName = null)
    {
        Sql = sql;
        Plain = plain;
        SqlName = sqlName ?? sql;
    }

    /// <summary>Gets the text for a SQL file: identifiers quoted when needed; a CALL or SELECT for routines.</summary>
    public string Sql { get; }

    /// <summary>Gets the name only, quoted when needed, for a SQL file when Alt is held while dropping (US-0503).</summary>
    public string SqlName { get; }

    /// <summary>Gets the text for any other file or application: the plain name.</summary>
    public string Plain { get; }
}

/// <summary>Builds the text that a drag or copy inserts (US-0501 to US-0504).</summary>
public static class DropTextBuilder
{
    /// <summary>Returns the texts for <paramref name="item"/> with the default options.</summary>
    public static DropText Build(DragItem item) => Build(item, DropFormatOptions.Default);

    /// <summary>
    /// Returns the texts for <paramref name="item"/>: in SQL files the (quoted) name, or a CALL/SELECT for a routine with
    /// known parameters; in other files the name with the quoting chosen for other files. Tables and views get the
    /// database prefix when <see cref="DropFormatOptions.DatabasePrefixForTables"/> is on.
    /// </summary>
    public static DropText Build(DragItem item, DropFormatOptions options)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        options ??= DropFormatOptions.Default;
        var prefix = options.DatabasePrefixForTables && item.Kind is DragObjectKind.Table or DragObjectKind.View && item.Database is not null;
        var sqlName = prefix ? options.QuoteForSql(item.Database!) + "." + options.QuoteForSql(item.Name) : options.QuoteForSql(item.Name);
        var plain = prefix ? options.QuoteForOther(item.Database!) + "." + options.QuoteForOther(item.Name) : options.QuoteForOther(item.Name);

        return item.Kind is DragObjectKind.Procedure or DragObjectKind.Function && item.Parameters is { } parameters
            ? new DropText(BuildCall(item, parameters, options), plain, sqlName)
            : new DropText(sqlName, plain);
    }

    /// <summary>Builds a call with the default options; see <see cref="BuildCall(DragItem, IReadOnlyList{string}, DropFormatOptions)"/>.</summary>
    public static string BuildCall(DragItem item, IReadOnlyList<string> parameters) => BuildCall(item, parameters, DropFormatOptions.Default);

    /// <summary>
    /// Builds a call with a user variable per parameter (US-0503):
    /// <c>CALL sakila.film_in_stock(@p_film_id, @p_store_id, @p_film_count);</c> or
    /// <c>SELECT sakila.get_customer_balance(@p_customer_id, @p_effective_date);</c>.
    /// </summary>
    public static string BuildCall(DragItem item, IReadOnlyList<string> parameters, DropFormatOptions options)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        options ??= DropFormatOptions.Default;
        var target = item.Database is not null && options.DatabasePrefixForRoutines
            ? options.QuoteForSql(item.Database) + "." + options.QuoteForSql(item.Name)
            : options.QuoteForSql(item.Name);
        var arguments = string.Join(", ", System.Linq.Enumerable.Select(parameters ?? Array.Empty<string>(), UserVariable));
        var keyword = item.Kind == DragObjectKind.Function ? "SELECT" : "CALL";
        return $"{keyword} {target}({arguments});";
    }

    /// <summary>
    /// Returns a user variable for a parameter name: <c>@p_id</c>, or <c>@`p name`</c> when the name has characters
    /// a user variable cannot have unquoted (letters, digits, <c>_</c>, <c>$</c> and <c>.</c> are allowed).
    /// </summary>
    public static string UserVariable(string name)
    {
        var plain = !string.IsNullOrEmpty(name);
        foreach (var c in name ?? string.Empty)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '$' || c == '.'))
            {
                plain = false;
                break;
            }
        }

        return "@" + (plain ? name : MySqlIdentifier.Quote(name ?? string.Empty));
    }

    /// <summary>Returns the texts for several items with the default options.</summary>
    public static DropText Build(IEnumerable<DragItem> items) => Build(items, DropFormatOptions.Default);

    /// <summary>
    /// Returns the texts for several items in the given order, separated by <see cref="DropFormatOptions.SeparatorText"/>
    /// (US-0502), for example <c>actor_id, first_name, `order`</c>.
    /// </summary>
    public static DropText Build(IEnumerable<DragItem> items, DropFormatOptions options)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        options ??= DropFormatOptions.Default;
        var texts = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(items, i => Build(i, options)));
        if (texts.Count == 1)
        {
            return texts[0];
        }

        var separator = options.SeparatorText;
        return new DropText(
            string.Join(separator, System.Linq.Enumerable.Select(texts, t => t.Sql)),
            string.Join(separator, System.Linq.Enumerable.Select(texts, t => t.Plain)),
            string.Join(separator, System.Linq.Enumerable.Select(texts, t => t.SqlName)));
    }
}
