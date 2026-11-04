using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Data;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Data;

public class TableDataTests
{
    private static readonly IReadOnlyList<ColumnInfo> _columns =
    [
        new ColumnInfo("rental_id", "int", false, true, false, "auto_increment") { DataType = "int" },
        new ColumnInfo("customer_name", "varchar(45)", true, false, false, string.Empty) { DataType = "varchar" },
        new ColumnInfo("rental_date", "datetime", false, false, false, string.Empty) { DataType = "datetime" },
    ];

    private static Dictionary<string, string> Filters(params (string Column, string Text)[] filters) =>
        filters.ToDictionary(f => f.Column, f => f.Text);

    [Fact]
    public void Select_DefaultOrderIsThePrimaryKey_WithLimitAndOffset()
    {
        var command = TableDataQuery.Select("sakila", "rental", _columns, Filters(), null, 2000, 1000);

        Assert.Equal("SELECT rental_id, customer_name, rental_date FROM sakila.rental ORDER BY rental_id LIMIT 1000 OFFSET 2000", Unquote(command.Sql));
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void Select_Sort_AndAViewWithoutKeyHasNoOrder()
    {
        Assert.EndsWith("ORDER BY `rental_date` DESC LIMIT 10 OFFSET 0", TableDataQuery.Select("s", "t", _columns, Filters(), new TableSort("rental_date", true), 0, 10).Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY", TableDataQuery.Select("s", "v", [_columns[1]], Filters(), null, 0, 10).Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY `gone`", TableDataQuery.Select("s", "t", _columns, Filters(), new TableSort("gone", false), 0, 10).Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Filters_AreParameters_ContainsForTextEqualsForNumbersAndDates()
    {
        var command = TableDataQuery.Select("s", "t", _columns, Filters(("customer_name", "o'Br_10%"), ("rental_id", "42"), ("rental_date", "2005-05-24 22:53:30")), null, 0, 10);

        Assert.Contains("WHERE `rental_id` = @f0 AND `customer_name` LIKE @f1 AND `rental_date` = @f2", command.Sql, StringComparison.Ordinal);
        Assert.Equal(["42", "%o'Br\\_10\\%%", "2005-05-24 22:53:30"], command.Parameters.Select(p => p.Value));
        Assert.DoesNotContain("o'Br", command.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Filters_NullNotNullEqualsSignAndEmpty()
    {
        var command = TableDataQuery.Select("s", "t", _columns, Filters(("customer_name", "=Mary"), ("rental_date", "null"), ("rental_id", "  ")), null, 0, 10);

        Assert.Contains("WHERE `customer_name` = @f0 AND `rental_date` IS NULL ORDER BY", command.Sql, StringComparison.Ordinal);
        Assert.Equal(["Mary"], command.Parameters.Select(p => p.Value));
        Assert.Contains("`customer_name` IS NOT NULL", TableDataQuery.Count("s", "t", _columns, Filters(("customer_name", "NOT NULL"))).Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_UsesTheSameFilters()
    {
        var command = TableDataQuery.Count("sakila", "rental", _columns, Filters(("rental_id", "5")));

        Assert.Equal("SELECT COUNT(*) FROM `sakila`.`rental` WHERE `rental_id` = @f0", command.Sql);
    }

    [Fact]
    public async Task FirstPage_NotFull_GivesTheExactCountWithoutCounting()
    {
        var (model, source) = Create(rows: 200);

        await model.LoadAsync();

        Assert.Equal(200, model.Rows.Count);
        Assert.Equal(200, model.TotalRows);
        Assert.Equal(1, model.PageCount);
        Assert.Equal(0, source.Counts);
        Assert.False(model.CanGoForward);
    }

    [Fact]
    public async Task LargeTable_IsCounted_AndPagesNavigate()
    {
        var (model, source) = Create(rows: 2500);

        await model.LoadAsync();
        await model.PendingCount;

        Assert.Equal(2500, model.TotalRows);
        Assert.Equal(3, model.PageCount);
        Assert.False(model.IsEstimated);

        await model.NextPageAsync();
        Assert.Equal(2, model.PageNumber);
        Assert.Contains("OFFSET 1000", source.LastSql, StringComparison.Ordinal);

        await model.LastPageAsync();
        Assert.Equal(3, model.PageNumber);
        Assert.Equal(500, model.Rows.Count);
        Assert.False(model.CanGoForward);
        Assert.True(model.CanGoBack);
    }

    [Fact]
    public async Task VeryLargeTable_UsesTheEstimate()
    {
        var (model, source) = Create(rows: 5000, estimate: 3_000_000);

        await model.LoadAsync();
        await model.PendingCount;

        Assert.Equal(3_000_000, model.TotalRows);
        Assert.True(model.IsEstimated);
        Assert.Equal(0, source.Counts);
    }

    [Fact]
    public async Task Filter_CountsExactly_AndStartsAtPage1()
    {
        var (model, source) = Create(rows: 5000, estimate: 3_000_000);
        await model.LoadAsync();
        await model.NextPageAsync();

        await model.ApplyFiltersAsync(new Dictionary<string, string> { ["customer_name"] = "mary", ["rental_id"] = string.Empty });
        await model.PendingCount;

        Assert.Equal(1, model.PageNumber);
        Assert.Single(model.Filters);
        Assert.Equal(1, source.Counts);
        Assert.False(model.IsEstimated);
        Assert.Contains("LIKE @f0", source.LastSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sort_TogglesDirection()
    {
        var (model, source) = Create(rows: 10);
        await model.LoadAsync();

        await model.SortByAsync("customer_name");
        Assert.Contains("ORDER BY `customer_name` LIMIT", source.LastSql, StringComparison.Ordinal);

        await model.SortByAsync("customer_name");
        Assert.Contains("ORDER BY `customer_name` DESC", source.LastSql, StringComparison.Ordinal);
        Assert.True(model.Sort!.Descending);
    }

    [Fact]
    public async Task Error_IsShownMasked()
    {
        var (model, source) = Create(rows: 10);
        source.Fail = new InvalidOperationException("Table is locked; Password=secret");

        await model.LoadAsync();

        Assert.True(model.HasError);
        Assert.DoesNotContain("secret", model.Error, StringComparison.Ordinal);
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task CancelledPassword_LoadsNothing()
    {
        var prompt = new FakePasswordPrompt();
        var (model, source) = Create(rows: 10, prompt: prompt, password: null);

        await model.LoadAsync();

        Assert.Equal(1, prompt.Calls);
        Assert.Empty(model.Rows);
        Assert.Equal(0, source.Reads);
    }

    private static string Unquote(string sql) => sql.Replace("`", string.Empty);

    private static (TableDataViewModel Model, FakeSource Source) Create(int rows, long? estimate = null, FakePasswordPrompt? prompt = null, string? password = "pw")
    {
        var metadata = new FakeMetadataProvider { Columns = (_, _) => Task.FromResult(_columns) };
        var explorer = new ExplorerViewModel(TestExplorer.Context(metadata, prompt));
        var server = explorer.AddServer(TestExplorer.Definition("Prod", password));
        var source = new FakeSource(rows, estimate);
        var model = new TableDataViewModel(server, metadata, source, "sakila", "rental", isView: false, new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));
        return (model, source);
    }

    private sealed class FakeSource(int rows, long? estimate) : ITableDataSource
    {
        public string LastSql { get; private set; } = string.Empty;

        public int Counts { get; private set; }

        public int Reads { get; private set; }

        public Exception? Fail { get; set; }

        public Task<QueryResultSet> ReadAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken)
        {
            Reads++;
            LastSql = command.Sql;
            if (Fail is { } fail)
            {
                return Task.FromException<QueryResultSet>(fail);
            }

            var parts = command.Sql.Split(' ');
            var limit = int.Parse(parts[Array.IndexOf(parts, "LIMIT") + 1], System.Globalization.CultureInfo.InvariantCulture);
            var offset = int.Parse(parts[Array.IndexOf(parts, "OFFSET") + 1], System.Globalization.CultureInfo.InvariantCulture);
            var count = Math.Max(0, Math.Min(limit, rows - offset));
            IReadOnlyList<object?[]> page = Enumerable.Range(offset + 1, count).Select(i => new object?[] { i, "name", DateTime.Today }).ToList();
            return Task.FromResult(new QueryResultSet(["rental_id", "customer_name", "rental_date"], page, count));
        }

        public Task<long> CountAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken)
        {
            Counts++;
            return Task.FromResult((long)rows);
        }

        public Task<long?> EstimateRowsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) =>
            Task.FromResult(estimate);

        public Task<RowChangeResult> ExecuteAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken) =>
            Task.FromResult(new RowChangeResult(1, 0));
    }
}
