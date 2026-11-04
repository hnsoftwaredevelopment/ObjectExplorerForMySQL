using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>Costly steps that the Plan tab marks (US-1301).</summary>
[Flags]
public enum PlanWarning
{
    /// <summary>No marking.</summary>
    None = 0,

    /// <summary>The step reads every row of the table (access type ALL).</summary>
    FullScan = 1,

    /// <summary>The step sorts with a filesort.</summary>
    Filesort = 2,

    /// <summary>The step builds a temporary table.</summary>
    TemporaryTable = 4,

    /// <summary>The actual number of rows is far from the estimate (Explain Analyze, US-1302).</summary>
    EstimateOff = 8,
}

/// <summary>One value of a plan step, as EXPLAIN names it (for example <c>possible_keys</c>).</summary>
public sealed record PlanDetail(string Name, string Value);

/// <summary>A step of an execution plan, shown as a node in the Plan tab (US-1301).</summary>
public sealed class PlanNode
{
    /// <summary>Initializes a new instance of the <see cref="PlanNode"/> class.</summary>
    public PlanNode(string title, string summary, PlanWarning warnings, IReadOnlyList<PlanDetail> details, IReadOnlyList<PlanNode> children)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Summary = summary ?? string.Empty;
        Warnings = warnings;
        Details = details ?? throw new ArgumentNullException(nameof(details));
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    /// <summary>Gets the name of the step, for example "Table film".</summary>
    public string Title { get; }

    /// <summary>Gets a short line with the access type, key and rows.</summary>
    public string Summary { get; }

    /// <summary>Gets the costly parts of this step.</summary>
    public PlanWarning Warnings { get; }

    /// <summary>Gets a value indicating whether the step is marked.</summary>
    public bool HasWarning => Warnings != PlanWarning.None;

    /// <summary>Gets the warning texts, filled by the parser from <see cref="PlanTexts"/>.</summary>
    public string WarningText { get; internal set; } = string.Empty;

    /// <summary>Gets all values of the step, for the tooltip.</summary>
    public IReadOnlyList<PlanDetail> Details { get; }

    /// <summary>Gets the tooltip text: warnings and all values.</summary>
    public string DetailText =>
        string.Join(Environment.NewLine, (HasWarning ? new[] { WarningText } : Array.Empty<string>()).Concat(Details.Select(d => d.Name + ": " + d.Value)));

    /// <summary>Gets the steps inside this one.</summary>
    public IReadOnlyList<PlanNode> Children { get; }

    /// <summary>Gets or sets a value indicating whether the node is expanded; a plan is small, so it starts expanded.</summary>
    public bool IsExpanded { get; set; } = true;
}

/// <summary>Texts of the Plan tab. The extension fills them from its resources (ADR-005); the defaults are English.</summary>
public sealed class PlanTexts
{
    /// <summary>Gets or sets the title of a query block; {0} = select id.</summary>
    public string QueryBlock { get; set; } = "Query block {0}";

    /// <summary>Gets or sets the title of a table step; {0} = table.</summary>
    public string Table { get; set; } = "Table {0}";

    /// <summary>Gets or sets the title of a nested loop join.</summary>
    public string NestedLoop { get; set; } = "Nested loop join";

    /// <summary>Gets or sets the title of a sort for ORDER BY.</summary>
    public string OrderBy { get; set; } = "Order by";

    /// <summary>Gets or sets the title of a GROUP BY.</summary>
    public string GroupBy { get; set; } = "Group by";

    /// <summary>Gets or sets the title of a DISTINCT.</summary>
    public string Distinct { get; set; } = "Remove duplicates";

    /// <summary>Gets or sets the title of window functions.</summary>
    public string Windowing { get; set; } = "Window functions";

    /// <summary>Gets or sets the title of a UNION.</summary>
    public string Union { get; set; } = "Union";

    /// <summary>Gets or sets the title of a subquery.</summary>
    public string Subquery { get; set; } = "Subquery";

    /// <summary>Gets or sets the title of a materialized subquery or derived table.</summary>
    public string Materialized { get; set; } = "Materialized subquery";

    /// <summary>Gets or sets the title of a filesort step (MariaDB).</summary>
    public string Filesort { get; set; } = "Filesort";

    /// <summary>Gets or sets the title of a temporary table step (MariaDB).</summary>
    public string TemporaryTable { get; set; } = "Temporary table";

    /// <summary>Gets or sets the summary part for the query cost; {0} = cost.</summary>
    public string Cost { get; set; } = "cost {0}";

    /// <summary>Gets or sets the summary part for the key; {0} = key.</summary>
    public string Key { get; set; } = "key {0}";

    /// <summary>Gets or sets the summary part for the rows; {0} = rows.</summary>
    public string Rows { get; set; } = "{0} rows";

    /// <summary>Gets or sets the warning for a full table scan.</summary>
    public string FullScanWarning { get; set; } = "Full table scan: every row is read.";

    /// <summary>Gets or sets the warning for a filesort.</summary>
    public string FilesortWarning { get; set; } = "Sorts with a filesort.";

    /// <summary>Gets or sets the warning for a temporary table.</summary>
    public string TemporaryWarning { get; set; } = "Builds a temporary table.";

    /// <summary>Gets or sets the summary part for the actual rows (US-1302); {0} = rows.</summary>
    public string ActualRows { get; set; } = "actual {0} rows";

    /// <summary>Gets or sets the summary part for the actual time; {0} = milliseconds.</summary>
    public string ActualTime { get; set; } = "{0} ms";

    /// <summary>Gets or sets the summary part for the number of loops; {0} = loops.</summary>
    public string Loops { get; set; } = "{0} loops";

    /// <summary>Gets or sets the summary part for a step that did not run.</summary>
    public string NeverExecuted { get; set; } = "never executed";

    /// <summary>Gets or sets the warning for a wrong estimate; {0} = estimated rows, {1} = actual rows.</summary>
    public string EstimateWarning { get; set; } = "Estimated {0} rows, actually {1}.";
}
