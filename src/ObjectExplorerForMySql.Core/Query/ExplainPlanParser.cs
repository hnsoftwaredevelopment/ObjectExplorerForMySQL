using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// Turns the output of <c>EXPLAIN FORMAT=JSON</c> into <see cref="PlanNode"/> steps (US-1301). It knows the step names of
/// MySQL 5.7+ and MariaDB; an unknown object is searched for known steps, so a newer server still shows its tables.
/// </summary>
public static class ExplainPlanParser
{
    private static readonly HashSet<string> SubqueryLists = new(StringComparer.Ordinal)
    {
        "attached_subqueries", "optimized_away_subqueries", "select_list_subqueries", "having_subqueries",
        "order_by_subqueries", "group_by_subqueries", "update_value_subqueries", "subqueries",
    };

    /// <summary>Parses <paramref name="json"/>; throws <see cref="FormatException"/> when it is not valid JSON.</summary>
    public static IReadOnlyList<PlanNode> Parse(string json, PlanTexts texts)
    {
        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }

        return Children(PlanJson.Parse(json), texts);
    }

    private static List<PlanNode> Children(object? value, PlanTexts texts)
    {
        var result = new List<PlanNode>();
        switch (value)
        {
            case PlanJsonObject json when json["operation"] is string:
                result.Add(Operation(json, texts));
                break;
            case PlanJsonObject json:
                foreach (var pair in json)
                {
                    result.AddRange(Steps(pair.Key, pair.Value, texts));
                }

                break;
            case List<object?> list:
                foreach (var item in list)
                {
                    result.AddRange(Children(item, texts));
                }

                break;
        }

        return result;
    }

    private static IEnumerable<PlanNode> Steps(string key, object? value, PlanTexts texts)
    {
        switch (key)
        {
            case "query_block":
                var block = value as PlanJsonObject;
                return [Step(Format(texts.QueryBlock, Text(block?["select_id"]) ?? "1"), block, PlanWarning.None, texts)];
            case "table":
                return [Table(value as PlanJsonObject, texts)];
            case "nested_loop":
                return [new PlanNode(texts.NestedLoop, string.Empty, PlanWarning.None, [], Children(value, texts))];
            case "ordering_operation":
                return [Step(texts.OrderBy, value as PlanJsonObject, PlanWarning.None, texts)];
            case "grouping_operation":
                return [Step(texts.GroupBy, value as PlanJsonObject, PlanWarning.None, texts)];
            case "duplicates_removal":
                return [Step(texts.Distinct, value, PlanWarning.None, texts)];
            case "windowing":
                return [Step(texts.Windowing, value as PlanJsonObject, PlanWarning.None, texts)];
            case "union_result":
                return [Step(texts.Union, value as PlanJsonObject, PlanWarning.None, texts)];
            case "materialized_from_subquery":
            case "materialized":
                return [Step(texts.Materialized, value as PlanJsonObject, PlanWarning.None, texts)];
            case "filesort":
                return [Step(texts.Filesort, value as PlanJsonObject, PlanWarning.Filesort, texts)];
            case "temporary_table":
                return [Step(texts.TemporaryTable, value as PlanJsonObject, PlanWarning.TemporaryTable, texts)];
            default:
                if (SubqueryLists.Contains(key) && value is List<object?> subqueries)
                {
                    return subqueries.Select(item => Step(texts.Subquery, item as PlanJsonObject, PlanWarning.None, texts)).ToList();
                }

                // query_specifications, read_sorted_file, block-nl-join and unknown objects: show the steps inside.
                return Children(value, texts);
        }
    }

    private static PlanNode Table(PlanJsonObject? json, PlanTexts texts)
    {
        var name = Text(json?["table_name"]) ?? "?";
        var access = Text(json?["access_type"]);
        var warnings = Flags(json);

        // An INSERT target shows access type ALL as well, but nothing is read from it.
        if (string.Equals(access, "ALL", StringComparison.OrdinalIgnoreCase) && json?["insert"] is not true)
        {
            warnings |= PlanWarning.FullScan;
        }

        var parts = new List<string>();
        if (access is not null)
        {
            parts.Add(access);
        }

        if (Text(json?["key"]) is { } key)
        {
            parts.Add(Format(texts.Key, key));
        }

        var estimate = Text(json?["rows_examined_per_scan"]) ?? Text(json?["rows"]);
        if (estimate is not null)
        {
            parts.Add(Format(texts.Rows, Figure(estimate)));
        }

        // MariaDB ANALYZE FORMAT=JSON (US-1302): the actual rows and time next to the estimate.
        string? actual = null;
        if (Text(json?["r_rows"]) is { } actualRows)
        {
            actual = actualRows;
            warnings |= Actual(parts, estimate, actualRows, Text(json?["r_total_time_ms"]), Text(json?["r_loops"]), texts);
        }

        return new PlanNode(Format(texts.Table, name), string.Join(", ", parts), warnings, Details(json), Children(json, texts))
        {
            WarningText = WarningText(warnings, texts, estimate, actual),
        };
    }

    /// <summary>Adds the actual rows, time and loops to <paramref name="parts"/>; returns EstimateOff when the estimate was far off.</summary>
    internal static PlanWarning Actual(List<string> parts, string? estimate, string actualRows, string? timeMs, string? loops, PlanTexts texts)
    {
        parts.Add(Format(texts.ActualRows, Figure(actualRows)));
        if (timeMs is not null)
        {
            parts.Add(Format(texts.ActualTime, Figure(timeMs)));
        }

        if (Number(loops) > 1)
        {
            parts.Add(Format(texts.Loops, loops!));
        }

        return IsEstimateOff(estimate, actualRows) ? PlanWarning.EstimateOff : PlanWarning.None;
    }

    /// <summary>
    /// Returns true when the actual rows differ from the estimate by a factor of 10 or more and by at least 100 rows;
    /// smaller differences are normal and only add noise.
    /// </summary>
    internal static bool IsEstimateOff(string? estimate, string? actual)
    {
        var e = Number(estimate);
        var a = Number(actual);
        if (e < 0 || a < 0 || Math.Abs(a - e) < 100)
        {
            return false;
        }

        return Math.Max(a, e) >= 10 * Math.Max(Math.Min(a, e), 1);
    }

    /// <summary>Returns the warning texts of <paramref name="warnings"/>.</summary>
    internal static string WarningText(PlanWarning warnings, PlanTexts texts, string? estimate = null, string? actual = null)
    {
        var parts = new List<string>();
        if ((warnings & PlanWarning.FullScan) != 0)
        {
            parts.Add(texts.FullScanWarning);
        }

        if ((warnings & PlanWarning.Filesort) != 0)
        {
            parts.Add(texts.FilesortWarning);
        }

        if ((warnings & PlanWarning.TemporaryTable) != 0)
        {
            parts.Add(texts.TemporaryWarning);
        }

        if ((warnings & PlanWarning.EstimateOff) != 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, texts.EstimateWarning, Figure(estimate), Figure(actual)));
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Returns a figure of the plan with at most two decimals (5780.574485667104 becomes 5780.57), so the Plan tab stays
    /// readable; values below 1, such as a time of 0.006 ms, keep up to five decimals. Text that is not a number is returned
    /// as it is. The tooltip keeps the exact values.
    /// </summary>
    internal static string Figure(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value) && !double.IsInfinity(value)
            ? Math.Abs(value) >= 1
                ? Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture)
                : value.ToString("0.#####", CultureInfo.InvariantCulture)
            : text ?? string.Empty;

    private static double Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : -1;

    // MySQL 8.0.32+ with explain_json_format_version = 2: a tree of iterators, each with an operation and its inputs.
    private static PlanNode Operation(PlanJsonObject json, PlanTexts texts)
    {
        var operation = (string)json["operation"]!;
        var warnings = Flags(json);
        if (string.Equals(Text(json["access_type"]), "table", StringComparison.Ordinal))
        {
            warnings |= PlanWarning.FullScan;
        }

        if (operation.StartsWith("Sort", StringComparison.Ordinal))
        {
            warnings |= PlanWarning.Filesort;
        }

        if (operation.IndexOf("temporary table", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            warnings |= PlanWarning.TemporaryTable;
        }

        var parts = new List<string>();
        if (Text(json["estimated_total_cost"]) is { } cost)
        {
            parts.Add(Format(texts.Cost, Figure(cost)));
        }

        if (Text(json["estimated_rows"]) is { } rows)
        {
            parts.Add(Format(texts.Rows, Figure(rows)));
        }

        var children = json.Where(p => p.Value is PlanJsonObject or List<object?>).SelectMany(p => Children(p.Value, texts)).ToList();
        return new PlanNode(operation, string.Join(", ", parts), warnings, Details(json), children) { WarningText = WarningText(warnings, texts) };
    }

    private static PlanNode Step(string title, object? value, PlanWarning warnings, PlanTexts texts)
    {
        var json = value as PlanJsonObject;
        var cost = json?["cost_info"] is PlanJsonObject costInfo ? Text(costInfo["query_cost"]) : Text(json?["cost"]);
        var summary = cost is null ? string.Empty : Format(texts.Cost, Figure(cost));
        if (json is null)
        {
            // MariaDB writes some steps as an array of steps.
            return new PlanNode(title, summary, warnings, [], Children(value, texts));
        }

        return Create(title, summary, warnings | Flags(json), json, texts);
    }

    private static PlanNode Create(string title, string summary, PlanWarning warnings, PlanJsonObject? json, PlanTexts texts) =>
        new(title, summary, warnings, Details(json), Children(json, texts)) { WarningText = WarningText(warnings, texts) };

    private static PlanWarning Flags(PlanJsonObject? json)
    {
        var warnings = PlanWarning.None;
        if (json?["using_filesort"] is true)
        {
            warnings |= PlanWarning.Filesort;
        }

        if (json?["using_temporary_table"] is true)
        {
            warnings |= PlanWarning.TemporaryTable;
        }

        return warnings;
    }

    // Values of the step: plain values, lists of names and the cost figures. Objects with steps become child nodes.
    private static List<PlanDetail> Details(PlanJsonObject? json)
    {
        var result = new List<PlanDetail>();
        if (json is null)
        {
            return result;
        }

        foreach (var pair in json)
        {
            switch (pair.Value)
            {
                case PlanJsonObject inner when pair.Key == "cost_info":
                    result.AddRange(inner.Where(c => Text(c.Value) is not null).Select(c => new PlanDetail(c.Key, Text(c.Value)!)));
                    break;
                case List<object?> list when list.All(item => item is not PlanJsonObject and not List<object?>):
                    result.Add(new PlanDetail(pair.Key, string.Join(", ", list.Select(item => Text(item) ?? "null"))));
                    break;
                default:
                    if (Text(pair.Value) is { } text)
                    {
                        result.Add(new PlanDetail(pair.Key, text));
                    }

                    break;
            }
        }

        return result;
    }

    private static string? Text(object? value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        _ => null,
    };

    private static string Format(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);
}

/// <summary>Finds the statement to explain and checks that EXPLAIN supports it (US-1301).</summary>
public static class ExplainStatement
{
    private static readonly HashSet<string> Explainable = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "INSERT", "UPDATE", "DELETE", "REPLACE", "TABLE",
    };

    private static readonly HashSet<string> Changing = new(StringComparer.OrdinalIgnoreCase) { "INSERT", "UPDATE", "DELETE", "REPLACE" };

    /// <summary>Returns the statement at 0-based <paramref name="line"/>: the last one that starts on or before it.</summary>
    public static SqlStatement? AtLine(string document, int line)
    {
        var statements = SqlScriptSplitter.Split(document ?? string.Empty);
        return statements.LastOrDefault(s => s.Line <= line) ?? statements.FirstOrDefault();
    }

    /// <summary>Returns true when <paramref name="statement"/> starts with a keyword that EXPLAIN accepts.</summary>
    public static bool IsExplainable(string statement) => Explainable.Contains(FirstWord(statement));

    /// <summary>
    /// Returns true when Explain Analyze may run <paramref name="statement"/> (US-1302): it really runs, so only a
    /// SELECT or TABLE, or a WITH whose main statement is not an INSERT, UPDATE, DELETE or REPLACE.
    /// </summary>
    public static bool IsAnalyzable(string statement)
    {
        var first = FirstWord(statement).ToUpperInvariant();
        return first switch
        {
            "SELECT" or "TABLE" => true,
            "WITH" => !TopLevelWords(statement).Any(Changing.Contains),
            _ => false,
        };
    }

    private static string FirstWord(string? statement)
    {
        var text = statement ?? string.Empty;
        var start = 0;
        while (start < text.Length && (char.IsWhiteSpace(text[start]) || text[start] == '('))
        {
            start++;
        }

        var end = start;
        while (end < text.Length && char.IsLetter(text[end]))
        {
            end++;
        }

        return text.Substring(start, end - start);
    }

    // Words outside parentheses, strings, quoted names and comments.
    internal static IEnumerable<string> TopLevelWords(string text)
    {
        var depth = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '\'' or '"' or '`')
            {
                i++;
                while (i < text.Length && text[i] != c)
                {
                    i += text[i] == '\\' && c != '`' ? 2 : 1;
                }

                i++;
            }
            else if (c == '#' || (c == '-' && i + 2 < text.Length && text[i + 1] == '-' && char.IsWhiteSpace(text[i + 2])))
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? text.Length : close + 2;
            }
            else if (c == '(' || c == ')')
            {
                depth += c == '(' ? 1 : -1;
                i++;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
                {
                    i++;
                }

                if (depth == 0)
                {
                    yield return text.Substring(start, i - start);
                }
            }
            else
            {
                i++;
            }
        }
    }
}
