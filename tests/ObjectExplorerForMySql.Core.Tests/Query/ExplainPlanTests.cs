using System;
using System.Linq;
using ObjectExplorerForMySql.Core.Query;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public class ExplainPlanTests
{
    // MySQL 8.0: SELECT ... FROM film f JOIN film_actor fa ... ORDER BY f.title
    private const string MySqlJoin = """
        {
          "query_block": {
            "select_id": 1,
            "cost_info": { "query_cost": "1561.25" },
            "ordering_operation": {
              "using_temporary_table": true,
              "using_filesort": true,
              "nested_loop": [
                { "table": { "table_name": "f", "access_type": "ALL", "possible_keys": [ "PRIMARY" ],
                    "rows_examined_per_scan": 1000, "filtered": "100.00",
                    "cost_info": { "read_cost": "1.25", "prefix_cost": "103.00" }, "used_columns": [ "film_id", "title" ] } },
                { "table": { "table_name": "fa", "access_type": "ref", "possible_keys": [ "idx_fk_film_id" ],
                    "key": "idx_fk_film_id", "ref": [ "sakila.f.film_id" ], "rows_examined_per_scan": 5,
                    "attached_condition": "(`sakila`.`fa`.`actor_id` > 3)" } }
              ]
            }
          }
        }
        """;

    // MariaDB 10.11: SELECT * FROM t WHERE a IN (SELECT b FROM u) ORDER BY c
    private const string MariaDb = """
        {
          "query_block": {
            "select_id": 1,
            "read_sorted_file": {
              "filesort": {
                "sort_key": "t.c",
                "table": { "table_name": "t", "access_type": "ALL", "rows": 10, "filtered": 100, "attached_condition": "t.a is not null" }
              }
            },
            "subqueries": [
              { "query_block": { "select_id": 2, "table": { "table_name": "u", "access_type": "index", "key": "ix_b", "rows": 4 } } }
            ]
          }
        }
        """;

    private static PlanNode Single(string json) => Assert.Single(ExplainPlanParser.Parse(json, new PlanTexts()));

    [Fact]
    public void MySql_QueryBlock_ShowsCostAndSteps()
    {
        var block = Single(MySqlJoin);

        Assert.Equal("Query block 1", block.Title);
        Assert.Equal("cost 1561.25", block.Summary);
        var order = Assert.Single(block.Children);
        Assert.Equal("Order by", order.Title);
        Assert.Equal(PlanWarning.Filesort | PlanWarning.TemporaryTable, order.Warnings);
        Assert.Equal("Sorts with a filesort. Builds a temporary table.", order.WarningText);
        var join = Assert.Single(order.Children);
        Assert.Equal("Nested loop join", join.Title);
        Assert.Equal(["Table f", "Table fa"], join.Children.Select(c => c.Title));
    }

    [Fact]
    public void MySql_Table_MarksFullScan_AndKeepsAllValues()
    {
        var tables = Single(MySqlJoin).Children[0].Children[0].Children;

        Assert.Equal("ALL, 1000 rows", tables[0].Summary);
        Assert.Equal(PlanWarning.FullScan, tables[0].Warnings);
        Assert.Contains(new PlanDetail("possible_keys", "PRIMARY"), tables[0].Details);
        Assert.Contains(new PlanDetail("prefix_cost", "103.00"), tables[0].Details);
        Assert.Contains(new PlanDetail("used_columns", "film_id, title"), tables[0].Details);
        Assert.StartsWith("Full table scan", tables[0].DetailText, StringComparison.Ordinal);

        Assert.Equal("ref, key idx_fk_film_id, 5 rows", tables[1].Summary);
        Assert.False(tables[1].HasWarning);
        Assert.Contains(new PlanDetail("attached_condition", "(`sakila`.`fa`.`actor_id` > 3)"), tables[1].Details);
    }

    [Fact]
    public void MariaDb_FilesortAndSubqueries()
    {
        var block = Single(MariaDb);

        Assert.Equal(["Filesort", "Subquery"], block.Children.Select(c => c.Title));
        var filesort = block.Children[0];
        Assert.Equal(PlanWarning.Filesort, filesort.Warnings);
        Assert.Contains(new PlanDetail("sort_key", "t.c"), filesort.Details);
        var table = Assert.Single(filesort.Children);
        Assert.Equal("ALL, 10 rows", table.Summary);
        Assert.True(table.HasWarning);

        var sub = Assert.Single(block.Children[1].Children);
        Assert.Equal("Query block 2", sub.Title);
        Assert.Equal("index, key ix_b, 4 rows", Assert.Single(sub.Children).Summary);
    }

    [Fact]
    public void Union_And_DerivedTable()
    {
        const string json = """
            {"query_block":{"union_result":{"using_temporary_table":true,"table_name":"<union1,2>","access_type":"ALL",
              "query_specifications":[
                {"dependent":false,"cacheable":true,"query_block":{"select_id":1,"table":{"table_name":"d","access_type":"ALL",
                  "materialized_from_subquery":{"using_temporary_table":true,"query_block":{"select_id":3,"message":"No tables used"}}}}},
                {"dependent":false,"cacheable":true,"query_block":{"select_id":2,"table":{"table_name":"t","access_type":"const","key":"PRIMARY"}}}]}}}
            """;

        var union = Assert.Single(Single(json).Children);

        Assert.Equal("Union", union.Title);
        Assert.Equal(PlanWarning.TemporaryTable, union.Warnings);
        Assert.Equal(["Query block 1", "Query block 2"], union.Children.Select(c => c.Title));
        var derived = Assert.Single(union.Children[0].Children);
        var materialized = Assert.Single(derived.Children);
        Assert.Equal("Materialized subquery", materialized.Title);
        Assert.Contains(new PlanDetail("message", "No tables used"), Assert.Single(materialized.Children).Details);
    }

    [Fact]
    public void InsertTarget_IsNotAFullScan()
    {
        var table = Assert.Single(Single("""{"query_block":{"select_id":1,"table":{"insert":true,"table_name":"t","access_type":"ALL"}}}""").Children);

        Assert.False(table.HasWarning);
    }

    [Fact]
    public void MySqlJsonVersion2_ShowsTheIterators()
    {
        const string json = """
            {"query":"/* select#1 */ select ...","query_plan":{"operation":"Sort: f.title","estimated_rows":1000.0,"estimated_total_cost":"103.0",
              "inputs":[{"operation":"Table scan on f","access_type":"table","table_name":"film","estimated_rows":1000.0}]},
             "query_type":"select","json_schema_version":"2.0"}
            """;

        var sort = Single(json);

        Assert.Equal("Sort: f.title", sort.Title);
        Assert.Equal("cost 103, 1000 rows", sort.Summary);
        Assert.Equal(PlanWarning.Filesort, sort.Warnings);
        var scan = Assert.Single(sort.Children);
        Assert.Equal("Table scan on f", scan.Title);
        Assert.Equal(PlanWarning.FullScan, scan.Warnings);
        Assert.Contains(new PlanDetail("table_name", "film"), scan.Details);
    }

    [Fact]
    public void UnknownSteps_AreSearchedForTables()
    {
        var block = Single("""{"query_block":{"select_id":1,"some_new_step":{"table":{"table_name":"x","access_type":"range"}}}}""");

        Assert.Equal("Table x", Assert.Single(block.Children).Title);
    }

    [Theory]
    [InlineData("{\"a\":}")]
    [InlineData("{\"a\":1")]
    [InlineData("")]
    [InlineData("{} x")]
    public void InvalidJson_Throws(string json) =>
        Assert.Throws<FormatException>(() => ExplainPlanParser.Parse(json, new PlanTexts()));

    [Fact]
    public void Json_ReadsEscapesAndNumbers()
    {
        var value = Assert.IsType<PlanJsonObject>(PlanJson.Parse("{\"s\":\"a\\\"b\\u0041\\n\",\"n\":-1.5e3,\"b\":false,\"z\":null,\"l\":[]}"));

        Assert.Equal("a\"bA\n", value["s"]);
        Assert.Equal("-1.5e3", value["n"]);
        Assert.False(Assert.IsType<bool>(value["b"]));
        Assert.Null(value["z"]);
        Assert.Empty(Assert.IsType<System.Collections.Generic.List<object?>>(value["l"]));
    }

    [Theory]
    [InlineData("SELECT 1", true)]
    [InlineData("  (select a from t) union (select b from u)", true)]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x", true)]
    [InlineData("update t set a = 1", true)]
    [InlineData("DELETE FROM t", true)]
    [InlineData("INSERT INTO t VALUES (1)", true)]
    [InlineData("REPLACE INTO t VALUES (1)", true)]
    [InlineData("TABLE t", true)]
    [InlineData("CREATE TABLE t (a INT)", false)]
    [InlineData("SET @a = 1", false)]
    [InlineData("SELECTED", false)]
    public void IsExplainable(string sql, bool expected) => Assert.Equal(expected, ExplainStatement.IsExplainable(sql));

    [Theory]
    [InlineData(0, "SELECT 1")]
    [InlineData(1, "SELECT 1")]
    [InlineData(2, "SELECT 2\nFROM t")]
    [InlineData(3, "SELECT 2\nFROM t")]
    [InlineData(9, "SELECT 2\nFROM t")]
    public void AtLine_TakesTheStatementThatStartsOnOrBefore(int line, string expected)
    {
        const string document = "-- header\nSELECT 1;\nSELECT 2\nFROM t;\n";

        Assert.Equal(expected, ExplainStatement.AtLine(document, line)?.Text);
    }

    [Fact]
    public void AtLine_EmptyDocument_IsNull() => Assert.Null(ExplainStatement.AtLine("-- only a comment", 0));

    [Theory]
    [InlineData("5780.574485667104", "5780.57")]
    [InlineData("1000.0", "1000")]
    [InlineData("2.5", "2.5")]
    [InlineData("0.006", "0.006")]
    [InlineData("0.0004236", "0.00042")]
    [InlineData("1e3", "1000")]
    [InlineData("n/a", "n/a")]
    public void Figure_AtMostTwoDecimals_SmallValuesKeepTheirDigits(string value, string expected) =>
        Assert.Equal(expected, ExplainPlanParser.Figure(value));
}
