using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// Turns the output of MySQL <c>EXPLAIN ANALYZE</c> (US-1302) into <see cref="PlanNode"/> steps. The output is a text
/// tree: one line per step, <c>-&gt; </c> after four spaces per level, with the estimate <c>(cost=.. rows=..)</c> and
/// the measurement <c>(actual time=first..last rows=.. loops=..)</c> or <c>(never executed)</c>.
/// </summary>
public static class AnalyzeTreeParser
{
    private static readonly Regex Estimate = new(@"\((?:cost=(?<cost>[^ )]+) )?rows=(?<rows>[^ )]+)\)", RegexOptions.CultureInvariant);

    private static readonly Regex Measured = new(
        @"\(actual time=(?<first>[\d.]+?)\.\.(?<last>[^ )]+) rows=(?<rows>[^ )]+) loops=(?<loops>[^ )]+)\)",
        RegexOptions.CultureInvariant);

    private const string NeverExecutedText = "(never executed)";

    /// <summary>Parses <paramref name="text"/>; throws <see cref="FormatException"/> when it has no step.</summary>
    public static IReadOnlyList<PlanNode> Parse(string text, PlanTexts texts)
    {
        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }

        var steps = new List<(int Depth, string Text)>();
        foreach (var raw in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var arrow = raw.IndexOf("-> ", StringComparison.Ordinal);
            if (arrow >= 0 && raw.Substring(0, arrow).Trim().Length == 0)
            {
                steps.Add((arrow / 4, raw.Substring(arrow + 3)));
            }
            else if (steps.Count > 0 && raw.Trim().Length > 0)
            {
                // A condition with a line break in a string continues the step above.
                var last = steps[steps.Count - 1];
                steps[steps.Count - 1] = (last.Depth, last.Text + " " + raw.Trim());
            }
        }

        if (steps.Count == 0)
        {
            throw new FormatException("The EXPLAIN ANALYZE output has no steps.");
        }

        var index = 0;
        return Read(steps, ref index, steps[0].Depth, texts);
    }

    private static List<PlanNode> Read(List<(int Depth, string Text)> steps, ref int index, int depth, PlanTexts texts)
    {
        var result = new List<PlanNode>();
        while (index < steps.Count && steps[index].Depth >= depth)
        {
            var text = steps[index].Text;
            index++;
            var children = index < steps.Count && steps[index].Depth > depth
                ? Read(steps, ref index, steps[index].Depth, texts)
                : new List<PlanNode>();
            result.Add(Step(text, children, texts));
        }

        return result;
    }

    private static PlanNode Step(string text, List<PlanNode> children, PlanTexts texts)
    {
        var details = new List<PlanDetail>();
        var parts = new List<string>();
        var warnings = PlanWarning.None;
        string? estimate = null;
        string? actual = null;
        var title = text;

        var estimateMatch = Estimate.Match(text);
        if (estimateMatch.Success)
        {
            estimate = estimateMatch.Groups["rows"].Value;
            parts.Add(string.Format(CultureInfo.CurrentCulture, texts.Rows, ExplainPlanParser.Figure(estimate)));
            if (estimateMatch.Groups["cost"].Success)
            {
                details.Add(new PlanDetail("cost", estimateMatch.Groups["cost"].Value));
            }

            details.Add(new PlanDetail("rows", estimate));
            title = title.Replace(estimateMatch.Value, string.Empty);
        }

        var measured = Measured.Match(text);
        if (measured.Success)
        {
            actual = measured.Groups["rows"].Value;
            var loops = measured.Groups["loops"].Value;
            details.Add(new PlanDetail("actual time", measured.Groups["first"].Value + ".." + measured.Groups["last"].Value + " ms"));
            details.Add(new PlanDetail("actual rows", actual));
            details.Add(new PlanDetail("loops", loops));
            warnings |= ExplainPlanParser.Actual(parts, estimate, actual, measured.Groups["last"].Value, loops, texts);
            title = title.Replace(measured.Value, string.Empty);
        }
        else if (text.IndexOf(NeverExecutedText, StringComparison.Ordinal) >= 0)
        {
            parts.Add(texts.NeverExecuted);
            title = title.Replace(NeverExecutedText, string.Empty);
        }

        title = title.Trim();
        if (title.StartsWith("Table scan on ", StringComparison.Ordinal))
        {
            warnings |= PlanWarning.FullScan;
        }

        if (title.StartsWith("Sort", StringComparison.Ordinal))
        {
            warnings |= PlanWarning.Filesort;
        }

        if (title.IndexOf("temporary table", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            warnings |= PlanWarning.TemporaryTable;
        }

        return new PlanNode(title, string.Join(", ", parts), warnings, details, children)
        {
            WarningText = ExplainPlanParser.WarningText(warnings, texts, estimate, actual),
        };
    }
}
