using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Data;

/// <summary>Sort of View Data (US-0801): one column, ascending or descending.</summary>
public sealed record TableSort(string Column, bool Descending);

/// <summary>A SQL statement with its parameters (names with @).</summary>
public sealed record SqlCommandText(string Sql, IReadOnlyList<KeyValuePair<string, object?>> Parameters);

/// <summary>How the filter text of a column is used.</summary>
public enum ColumnFilterKind
{
    /// <summary>Text columns: the value contains the text (LIKE '%text%'); "=text" means equal.</summary>
    Contains,

    /// <summary>Numbers, dates and times: the value equals the text.</summary>
    Equals,
}

/// <summary>
/// Builds the statements of View Data (US-0801): a page with LIMIT and OFFSET, the row count, the server-side sort and
/// the column filters. Names are quoted; filter values are always parameters.
/// </summary>
public static class TableDataQuery
{
    private static readonly HashSet<string> _equalsTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tinyint", "smallint", "mediumint", "int", "integer", "bigint", "decimal", "numeric", "float", "double", "real",
        "bit", "year", "date", "datetime", "timestamp", "time", "bool", "boolean",
    };

    /// <summary>Returns how a filter on <paramref name="column"/> works.</summary>
    public static ColumnFilterKind FilterKind(ColumnInfo column)
    {
        if (column is null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        var type = column.DataType ?? column.ColumnType.Split(' ', '(')[0];
        return _equalsTypes.Contains(type) ? ColumnFilterKind.Equals : ColumnFilterKind.Contains;
    }

    /// <summary>
    /// Returns the SELECT of one page. Without a sort the rows are ordered by the primary key, so the pages do not
    /// overlap; a view or a table without primary key has no order then.
    /// </summary>
    public static SqlCommandText Select(
        string database,
        string table,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyDictionary<string, string> filters,
        TableSort? sort,
        long offset,
        int limit)
    {
        if (columns is null || columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        var parameters = new List<KeyValuePair<string, object?>>();
        var sql = new StringBuilder("SELECT ");
        sql.Append(string.Join(", ", columns.Select(c => MySqlIdentifier.Quote(c.Name))));
        sql.Append(" FROM ").Append(Name(database, table));
        sql.Append(Where(columns, filters, parameters));

        var order = sort is not null && columns.Any(c => c.Name == sort.Column)
            ? MySqlIdentifier.Quote(sort.Column) + (sort.Descending ? " DESC" : string.Empty)
            : string.Join(", ", columns.Where(c => c.IsPrimaryKey).Select(c => MySqlIdentifier.Quote(c.Name)));
        if (order.Length > 0)
        {
            sql.Append(" ORDER BY ").Append(order);
        }

        sql.Append(" LIMIT ").Append(limit.ToString(CultureInfo.InvariantCulture));
        sql.Append(" OFFSET ").Append(Math.Max(0, offset).ToString(CultureInfo.InvariantCulture));
        return new SqlCommandText(sql.ToString(), parameters);
    }

    /// <summary>Returns the COUNT(*) with the same filters.</summary>
    public static SqlCommandText Count(string database, string table, IReadOnlyList<ColumnInfo> columns, IReadOnlyDictionary<string, string> filters)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        var sql = "SELECT COUNT(*) FROM " + Name(database, table) + Where(columns ?? [], filters, parameters);
        return new SqlCommandText(sql, parameters);
    }

    /// <summary>Returns true when at least one filter has text.</summary>
    public static bool HasFilters(IReadOnlyDictionary<string, string>? filters) =>
        filters is not null && filters.Values.Any(v => !string.IsNullOrWhiteSpace(v));

    // Filters of all columns, combined with AND. NULL and NOT NULL (any case) test for NULL.
    private static string Where(IReadOnlyList<ColumnInfo> columns, IReadOnlyDictionary<string, string>? filters, List<KeyValuePair<string, object?>> parameters)
    {
        if (filters is null || !HasFilters(filters))
        {
            return string.Empty;
        }

        var conditions = new List<string>();
        foreach (var column in columns)
        {
            if (!filters.TryGetValue(column.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var text = raw.Trim();
            var name = MySqlIdentifier.Quote(column.Name);
            if (string.Equals(text, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add(name + " IS NULL");
                continue;
            }

            if (string.Equals(text, "NOT NULL", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add(name + " IS NOT NULL");
                continue;
            }

            var parameter = "@f" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            if (FilterKind(column) == ColumnFilterKind.Equals || (text.Length > 1 && text[0] == '='))
            {
                var value = text[0] == '=' ? text.Substring(1).Trim() : text;
                conditions.Add(name + " = " + parameter);
                parameters.Add(new KeyValuePair<string, object?>(parameter, value));
            }
            else
            {
                conditions.Add(name + " LIKE " + parameter);
                parameters.Add(new KeyValuePair<string, object?>(parameter, "%" + EscapeLike(text) + "%"));
            }
        }

        return conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
    }

    private static string Name(string database, string table) => MySqlIdentifier.Quote(database) + "." + MySqlIdentifier.Quote(table);

    // LIKE wildcards in the filter text are searched for as characters; the default escape character is a backslash.
    private static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
