using System;
using System.Linq;
using ObjectExplorerForMySql.Core.Query;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public class ExplainAnalyzeTests
{
    // MySQL 8.0.36: EXPLAIN ANALYZE SELECT ... FROM film f JOIN film_actor fa ... ORDER BY f.title
    private const string MySqlTree =
        "-> Sort: f.title  (actual time=12.4..12.9 rows=5462 loops=1)\n" +
        "    -> Stream results  (cost=1561 rows=5462) (actual time=0.11..9.8 rows=5462 loops=1)\n" +
        "        -> Nested loop inner join  (cost=1561 rows=5462) (actual time=0.1..8.1 rows=5462 loops=1)\n" +
        "            -> Table scan on f  (cost=103 rows=10) (actual time=0.06..0.6 rows=1000 loops=1)\n" +
        "            -> Covering index lookup on fa using idx_fk_film_id (film_id=f.film_id)  (cost=0.98 rows=5.46) (actual time=0.004..0.006 rows=5.46 loops=1000)\n" +
        "        -> Filter: (f.rating = 'X')  (cost=1 rows=1) (never executed)\n";

    private static PlanNode Root(string tree) => Assert.Single(AnalyzeTreeParser.Parse(tree, new PlanTexts()));

    [Fact]
    public void Tree_BuildsTheStepsByIndent()
    {
        var sort = Root(MySqlTree);

        Assert.Equal("Sort: f.title", sort.Title);
        var stream = Assert.Single(sort.Children);
        Assert.Equal(["Nested loop inner join", "Filter: (f.rating = 'X')"], stream.Children.Select(c => c.Title));
        Assert.Equal(["Table scan on f", "Covering index lookup on fa using idx_fk_film_id (film_id=f.film_id)"], stream.Children[0].Children.Select(c => c.Title));
    }

    [Fact]
    public void Tree_ShowsEstimateAndActual()
    {
        var sort = Root(MySqlTree);
        var lookup = sort.Children[0].Children[0].Children[1];

        Assert.Equal("actual 5462 rows, 12.9 ms", sort.Summary);
        Assert.Equal("5.46 rows, actual 5.46 rows, 0.006 ms, 1000 loops", lookup.Summary);
        Assert.Contains(new PlanDetail("cost", "0.98"), lookup.Details);
        Assert.Contains(new PlanDetail("actual time", "0.004..0.006 ms"), lookup.Details);
        Assert.Equal("1 rows, never executed", sort.Children[0].Children[1].Summary);
    }

    [Fact]
    public void Tree_MarksScansSortsAndWrongEstimates()
    {
        var sort = Root(MySqlTree);
        var scan = sort.Children[0].Children[0].Children[0];

        Assert.Equal(PlanWarning.Filesort, sort.Warnings);
        Assert.Equal(PlanWarning.FullScan | PlanWarning.EstimateOff, scan.Warnings);
        Assert.Equal("Full table scan: every row is read. Estimated 10 rows, actually 1000.", scan.WarningText);
        Assert.False(sort.Children[0].HasWarning);
    }

    [Fact]
    public void Tree_WithoutSteps_Throws() =>
        Assert.Throws<FormatException>(() => AnalyzeTreeParser.Parse("You have an error", new PlanTexts()));

    [Fact]
    public void MariaDb_Analyze_ShowsActualRowsAndTime()
    {
        const string json = """
            {"query_block":{"select_id":1,"r_loops":1,"r_total_time_ms":0.31,
              "table":{"table_name":"t","access_type":"ALL","r_loops":1,"rows":20,"r_rows":5000,"r_total_time_ms":0.25,"filtered":100,"r_filtered":10}}}
            """;

        var table = Assert.Single(Assert.Single(ExplainPlanParser.Parse(json, new PlanTexts())).Children);

        Assert.Equal("ALL, 20 rows, actual 5000 rows, 0.25 ms", table.Summary);
        Assert.Equal(PlanWarning.FullScan | PlanWarning.EstimateOff, table.Warnings);
        Assert.Contains(new PlanDetail("r_filtered", "10"), table.Details);
    }

    [Theory]
    [InlineData("10", "1000", true)]
    [InlineData("1000", "10", true)]
    [InlineData("0", "150", true)]
    [InlineData("10", "90", false)]
    [InlineData("1000", "5000", false)]
    [InlineData(null, "1000", false)]
    public void EstimateOff(string? estimate, string actual, bool expected) =>
        Assert.Equal(expected, ExplainPlanParser.IsEstimateOff(estimate, actual));

    [Theory]
    [InlineData("SELECT * FROM t", true)]
    [InlineData("(SELECT 1) UNION (SELECT 2)", true)]
    [InlineData("TABLE t", true)]
    [InlineData("WITH x AS (SELECT * FROM t) SELECT * FROM x", true)]
    [InlineData("WITH x AS (SELECT 'update') SELECT * FROM x -- delete", true)]
    [InlineData("WITH x AS (SELECT id FROM t) UPDATE t JOIN x USING (id) SET a = 1", false)]
    [InlineData("WITH x AS (SELECT id FROM t) DELETE t FROM t JOIN x USING (id)", false)]
    [InlineData("UPDATE t SET a = 1", false)]
    [InlineData("DELETE FROM t", false)]
    [InlineData("INSERT INTO t SELECT * FROM u", false)]
    [InlineData("CREATE TABLE t (a INT)", false)]
    public void IsAnalyzable(string sql, bool expected) => Assert.Equal(expected, ExplainStatement.IsAnalyzable(sql));
}
