using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ObjectExplorerForMySql.Core.Query;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

// US-0705: which statements run again, and rows written as they come.
public class FullResultExportTests
{
    [Theory]
    [InlineData("SELECT * FROM rental")]
    [InlineData("  -- all rentals\n  select * from rental")]
    [InlineData("(SELECT 1) UNION (SELECT 2)")]
    [InlineData("TABLE rental")]
    [InlineData("VALUES ROW(1), ROW(2)")]
    [InlineData("SHOW PROCESSLIST")]
    [InlineData("DESCRIBE rental")]
    [InlineData("DESC rental")]
    [InlineData("EXPLAIN SELECT * FROM rental")]
    [InlineData("WITH r AS (SELECT * FROM rental) SELECT * FROM r")]
    [InlineData("SELECT 'INSERT INTO t' AS text, `update` FROM t")]
    [InlineData("SELECT * FROM t WHERE id IN (SELECT id FROM u) FOR UPDATE")]
    public void CanRunAgain_StatementsWithoutSideEffects(string statement) =>
        Assert.True(ExportStatement.CanRunAgain(statement));

    [Theory]
    [InlineData("")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("INSERT INTO t SELECT * FROM u")]
    [InlineData("CALL film_in_stock(1, 1, @n)")]
    [InlineData("SELECT * INTO OUTFILE '/tmp/r.csv' FROM rental")]
    [InlineData("SELECT COUNT(*) INTO @n FROM rental")]
    [InlineData("WITH r AS (SELECT 1) DELETE FROM t WHERE id IN (SELECT * FROM r)")]
    [InlineData("WITH r AS (SELECT 1) UPDATE t SET a = 1")]
    [InlineData("EXPLAIN ANALYZE SELECT * FROM rental")]
    [InlineData("ANALYZE SELECT * FROM rental")]
    [InlineData("ANALYZE TABLE rental")]
    [InlineData("SET @n = 1")]
    public void CanRunAgain_NotForStatementsThatChangeSomething(string statement) =>
        Assert.False(ExportStatement.CanRunAgain(statement));

    // Hands out rows one at a time and records how many the writer asked for before each write.
    private static IEnumerable<object?[]> Rows(int count, List<int> pulled)
    {
        for (var i = 1; i <= count; i++)
        {
            pulled.Add(i);
            yield return [i, "name " + i];
        }
    }

    [Fact]
    public void WriteCsv_RowsAsTheyCome_SameFormatAsTheResultSet()
    {
        var pulled = new List<int>();
        using var stream = new StringWriter();
        using var shown = new StringWriter();

        var count = QueryResultExporter.WriteCsv(["id", "name"], Rows(3, pulled), stream);
        QueryResultExporter.WriteCsv(new QueryResultSet(["id", "name"], Rows(3, []).ToList(), 3), shown);

        Assert.Equal(3, count);
        Assert.Equal([1, 2, 3], pulled);
        Assert.Equal("id,name\r\n1,name 1\r\n2,name 2\r\n3,name 3\r\n", stream.ToString());
        Assert.Equal(shown.ToString(), stream.ToString());
    }

    [Fact]
    public void WriteJson_RowsAsTheyCome_SameFormatAsTheResultSet()
    {
        using var stream = new StringWriter();
        using var shown = new StringWriter();

        var count = QueryResultExporter.WriteJson(["id", "id"], Rows(2, []), stream);
        QueryResultExporter.WriteJson(new QueryResultSet(["id", "id"], Rows(2, []).ToList(), 2), shown);

        Assert.Equal(2, count);
        Assert.Equal("[\n  {\"id\": 1, \"id_2\": \"name 1\"},\n  {\"id\": 2, \"id_2\": \"name 2\"}\n]\n", stream.ToString());
        Assert.Equal(shown.ToString(), stream.ToString());
    }

    [Fact]
    public void WriteJson_NoRows_IsAnEmptyArray()
    {
        using var writer = new StringWriter();

        Assert.Equal(0, QueryResultExporter.WriteJson(["id"], [], writer));
        Assert.Equal("[]\n", writer.ToString());
    }

    [Fact]
    public void Counted_ReportsEveryThousandRows()
    {
        var reports = new List<long>();

        var count = FullResultExporter.Counted(Rows(2500, []), new SyncProgress(reports.Add), CancellationToken.None).Count();

        Assert.Equal(2500, count);
        Assert.Equal([1000L, 2000L], reports);
    }

    [Fact]
    public void Counted_StopsWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var pulled = new List<int>();
        var rows = FullResultExporter.Counted(Rows(10, pulled), null, cancellation.Token);
        using var writer = new StringWriter();

        Assert.Throws<OperationCanceledException>(() => QueryResultExporter.WriteCsv(["id", "name"], rows.Select((r, i) =>
        {
            if (i == 2)
            {
                cancellation.Cancel();
            }

            return r;
        }), writer));
        Assert.Equal(4, pulled.Count);
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
