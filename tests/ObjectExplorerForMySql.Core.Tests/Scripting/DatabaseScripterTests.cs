using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Scripting;

public class DatabaseScripterTests
{
    private static FakeMetadataProvider Sakila()
    {
        var metadata = new FakeMetadataProvider
        {
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>(
                [new TableInfo(db, "rental", "InnoDB", null), new TableInfo(db, "customer", "InnoDB", null), new TableInfo(db, "actor", "InnoDB", null)]),
            TableDependencies = [("rental", "customer")],
            Columns = (_, table) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
            [
                new ColumnInfo("id", "int", false, true, false, "auto_increment") { DataType = "int" },
                new ColumnInfo("name", "varchar(45)", true, false, false, string.Empty) { DataType = "varchar" },
                new ColumnInfo("upper_name", "varchar(45)", true, false, false, "VIRTUAL GENERATED") { DataType = "varchar" },
            ]),
            Views = [new TableInfo("sakila", "b_view", null, null, isView: true), new TableInfo("sakila", "a_view", null, null, isView: true)],
            Routines =
            [
                new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, null),
                new RoutineInfo("sakila", "get_balance", RoutineType.Function, "decimal(5,2)", null),
            ],
            Events = [new EventInfo("nightly", "ENABLED", null, "1", "DAY", null, null)],
            TriggersByTable = table => table == "customer" ? [new TriggerInfo("customer_create_date", "BEFORE", "INSERT")] : [],
            CreateDatabaseStatement = "CREATE DATABASE /*!32312 IF NOT EXISTS*/ `sakila` /*!40100 DEFAULT CHARACTER SET utf8mb4 */",
        };

        // a_view selects from b_view, so b_view must come first although a < b.
        metadata.CreateStatement = o => o.Kind switch
        {
            CodeObjectKind.View when o.Name == "a_view" => "CREATE VIEW `a_view` AS select `x` from `sakila`.`b_view`",
            CodeObjectKind.View => "CREATE VIEW `b_view` AS select `x` from `sakila`.`actor`",
            _ => $"CREATE {o.Keyword} `{o.Name}` (x)",
        };
        return metadata;
    }

    private static async Task<(string Script, DatabaseScriptResult Result)> ScriptAsync(FakeMetadataProvider metadata, DatabaseScriptOptions options)
    {
        using var writer = new StringWriter();
        var result = await new DatabaseScripter(metadata).WriteAsync(TestExplorer.Definition(), "sakila", writer, options, null, TestContext.Current.CancellationToken);
        return (writer.ToString(), result);
    }

    private static void AssertInOrder(string script, params string[] parts)
    {
        var position = -1;
        foreach (var part in parts)
        {
            var next = script.IndexOf(part, StringComparison.Ordinal);
            Assert.True(next >= 0, $"Missing: {part}");
            Assert.True(next > position, $"Out of order: {part}");
            position = next;
        }
    }

    [Fact]
    public async Task Script_HasCreateDatabaseUseAndObjectsInOrder()
    {
        var (script, result) = await ScriptAsync(Sakila(), new DatabaseScriptOptions { ConnectionLabel = "Local" });

        AssertInOrder(
            script,
            "-- Database sakila on Local",
            "CREATE DATABASE /*!32312 IF NOT EXISTS*/ sakila /*!40100 DEFAULT CHARACTER SET utf8mb4 */;",
            "USE sakila;",
            "FOREIGN_KEY_CHECKS = 0;",
            "CREATE TABLE actor (x);",
            "CREATE TABLE customer (x);",
            "CREATE TABLE rental (x);",
            "SET FOREIGN_KEY_CHECKS = IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1);",
            "SET SQL_MODE = IFNULL(@OLD_SQL_MODE, @@GLOBAL.SQL_MODE);",
            "SET TIME_ZONE = IFNULL(@OLD_TIME_ZONE, @@GLOBAL.TIME_ZONE);",
            "CREATE FUNCTION get_balance (x)$$",
            "CREATE PROCEDURE film_in_stock (x)$$",
            "CREATE OR REPLACE VIEW b_view",
            "CREATE OR REPLACE VIEW a_view",
            "CREATE TRIGGER customer_create_date (x)$$",
            "CREATE EVENT nightly (x)$$");
        Assert.Equal(9, result.ObjectCount);
        Assert.DoesNotContain("INSERT", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_WithoutCreateDatabase_HasNoUse()
    {
        var (script, _) = await ScriptAsync(Sakila(), new DatabaseScriptOptions { IncludeCreateDatabase = false });

        Assert.DoesNotContain("CREATE DATABASE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("USE ", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE actor (x);", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_QuoteAlways_KeepsBackticks()
    {
        var options = new DatabaseScriptOptions { Quoting = new Core.DragDrop.DropFormatOptions { SqlQuoting = Core.DragDrop.QuoteMode.Always } };
        var (script, _) = await ScriptAsync(Sakila(), options);

        Assert.Contains("USE `sakila`;", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE `actor` (x);", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_SkipsGeneratedColumns_BatchesRows_AndComesBeforeTriggers()
    {
        var metadata = Sakila();
        metadata.Rows["actor"] = Enumerable.Range(1, 250).Select(i => new object?[] { i, i == 2 ? null : "it's" }).ToList();
        metadata.Rows["customer"] = [[1, "x"]];

        var (script, result) = await ScriptAsync(metadata, new DatabaseScriptOptions { IncludeData = true });

        Assert.Equal(251, result.RowCount);
        Assert.Equal(3, CountOf(script, "INSERT INTO actor (id, name) VALUES"));
        Assert.Contains("(1, 'it\\'s'),", script, StringComparison.Ordinal);
        Assert.Contains("(2, NULL),", script, StringComparison.Ordinal);
        Assert.Contains("(250, 'it\\'s');", script, StringComparison.Ordinal);
        Assert.DoesNotContain("upper_name", script, StringComparison.Ordinal);
        AssertInOrder(script, "CREATE TABLE rental", "INSERT INTO actor", "INSERT INTO customer", "SET SQL_MODE = IFNULL(@OLD_SQL_MODE, @@GLOBAL.SQL_MODE);", "CREATE TRIGGER");
        Assert.DoesNotContain("INSERT INTO rental", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSource_IsWrittenAsCommentAndReported()
    {
        var metadata = Sakila();
        var inner = metadata.CreateStatement;
        metadata.CreateStatement = o => o.Kind == CodeObjectKind.Procedure ? null : inner(o);

        var (script, result) = await ScriptAsync(metadata, new DatabaseScriptOptions());

        Assert.Contains("-- Procedure film_in_stock: the server returned no source", script, StringComparison.Ordinal);
        Assert.Equal(["Procedure film_in_stock"], result.MissingSource);
    }

    [Fact]
    public async Task Cancel_StopsTheScript()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var writer = new StringWriter();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DatabaseScripter(Sakila()).WriteAsync(TestExplorer.Definition(), "sakila", writer, new DatabaseScriptOptions(), null, cancellation.Token));
    }

    [Fact]
    public async Task Progress_ReportsEveryStep()
    {
        var metadata = Sakila();
        metadata.Rows["actor"] = [[1, "a"]];
        var steps = new List<DatabaseScriptProgress>();
        using var writer = new StringWriter();

        await new DatabaseScripter(metadata).WriteAsync(TestExplorer.Definition(), "sakila", writer, new DatabaseScriptOptions { IncludeData = true }, new SyncProgress(steps.Add), TestContext.Current.CancellationToken);

        Assert.Equal(
            [DatabaseScriptStep.Tables, DatabaseScriptStep.Data, DatabaseScriptStep.Functions, DatabaseScriptStep.Procedures, DatabaseScriptStep.Views, DatabaseScriptStep.Triggers, DatabaseScriptStep.Events],
            steps.Select(s => s.Step).Distinct());
        Assert.Contains(steps, s => s.Step == DatabaseScriptStep.Tables && s.Name == "rental" && s.Current == 3 && s.Total == 3);
    }

    [Theory]
    [InlineData("CREATE ALGORITHM=UNDEFINED DEFINER=root@`%` SQL SECURITY DEFINER VIEW v AS select 1", "CREATE OR REPLACE ALGORITHM=UNDEFINED DEFINER=root@`%` SQL SECURITY DEFINER VIEW v AS select 1")]
    [InlineData("create view v as select 1", "create OR REPLACE view v as select 1")]
    [InlineData("CREATE OR REPLACE VIEW v AS select 1", "CREATE OR REPLACE VIEW v AS select 1")]
    [InlineData("select 1", "select 1")]
    public void OrReplace_AddsOrReplaceOnce(string statement, string expected)
    {
        Assert.Equal(expected, DatabaseScripter.OrReplace(statement));
    }

    [Fact]
    public async Task Footer_FallsBackWhenOldValuesAreMissing()
    {
        var (script, _) = await ScriptAsync(Sakila(), new DatabaseScriptOptions());

        Assert.DoesNotContain("= @OLD_", script, StringComparison.Ordinal);
        Assert.Contains("SET FOREIGN_KEY_CHECKS = IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1);", script, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class SyncProgress(Action<DatabaseScriptProgress> report) : IProgress<DatabaseScriptProgress>
    {
        public void Report(DatabaseScriptProgress value) => report(value);
    }
}
