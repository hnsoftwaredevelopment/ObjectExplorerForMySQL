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

public class EditDataTests
{
    private static readonly IReadOnlyList<ColumnInfo> _columns =
    [
        new ColumnInfo("actor_id", "smallint unsigned", false, true, false, "auto_increment") { DataType = "smallint" },
        new ColumnInfo("first_name", "varchar(45)", false, false, false, string.Empty) { DataType = "varchar" },
        new ColumnInfo("note", "varchar(45)", true, false, false, string.Empty) { DataType = "varchar" },
        new ColumnInfo("picture", "blob", true, false, false, string.Empty) { DataType = "blob" },
        new ColumnInfo("upper_name", "varchar(45)", true, false, false, "VIRTUAL GENERATED") { DataType = "varchar" },
    ];

    [Fact]
    public void Update_SetsTheChangesOnTheOldKey()
    {
        var command = RowChangeBuilder.Update("sakila", "actor", _columns, [5, "PENELOPE", null, null, null], new Dictionary<int, object?> { [2] = null, [1] = "Pen" });

        Assert.Equal("UPDATE `sakila`.`actor` SET `first_name` = @p0, `note` = @p1 WHERE `actor_id` = @p2", command.Sql);
        Assert.Equal(["Pen", null, 5], command.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void Insert_LeavesEmptyColumnsToTheirDefault()
    {
        var command = RowChangeBuilder.Insert("sakila", "actor", _columns, new Dictionary<int, object?> { [0] = null, [1] = "NEW" });

        Assert.Equal("INSERT INTO `sakila`.`actor` (`first_name`) VALUES (@p0)", command.Sql);
        Assert.Equal("INSERT INTO `sakila`.`actor` () VALUES ()", RowChangeBuilder.Insert("sakila", "actor", _columns, new Dictionary<int, object?>()).Sql);
    }

    [Fact]
    public void DeleteAndSelectRow_UseAllKeyColumns()
    {
        var columns = new List<ColumnInfo>
        {
            new("film_id", "int", false, true, false, string.Empty),
            new("actor_id", "int", false, true, false, string.Empty),
            new("note", "text", true, false, false, string.Empty),
        };

        var delete = RowChangeBuilder.Delete("sakila", "film_actor", columns, [1, 2, "x"]);
        var select = RowChangeBuilder.SelectRow("sakila", "film_actor", columns, [1, 2, "x"]);

        Assert.Equal("DELETE FROM `sakila`.`film_actor` WHERE `film_id` = @p0 AND `actor_id` = @p1", delete.Sql);
        Assert.EndsWith("FROM `sakila`.`film_actor` WHERE `film_id` = @p0 AND `actor_id` = @p1", select.Sql, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => RowChangeBuilder.Delete("s", "t", [columns[2]], ["x"]));
    }

    [Fact]
    public void Editable_NotForGeneratedBinaryOrSpatialColumns()
    {
        Assert.Equal([true, true, true, false, false], _columns.Select(RowChangeBuilder.IsEditable));
        Assert.False(RowChangeBuilder.IsEditable(new ColumnInfo("g", "point", true, false, false, string.Empty)));
    }

    [Theory]
    [InlineData("Data too long for column 'first_name' at row 1", "first_name")]
    [InlineData("Column 'note' cannot be null", "note")]
    [InlineData("Field 'first_name' doesn't have a default value", "first_name")]
    [InlineData("Incorrect integer value: 'abc' for column `sakila`.`actor`.`actor_id` at row 1", "actor_id")]
    [InlineData("Incorrect integer value: 'abc' for column 'ACTOR_ID' at row 1", "actor_id")]
    [InlineData("Duplicate entry '5' for key 'actor.PRIMARY'", null)]
    [InlineData("Data too long for column 'unknown' at row 1", null)]
    [InlineData(null, null)]
    public void ErrorColumn_IsFoundInTheServerMessage(string? message, string? expected)
    {
        Assert.Equal(expected, RowChangeBuilder.ErrorColumn(message, _columns));
    }

    [Fact]
    public async Task Save_Error_MarksTheColumn_AndAcceptClearsIt()
    {
        var (model, source) = await CreateAsync();
        source.Fail = new InvalidOperationException("Data too long for column 'first_name' at row 1");
        var row = model.Rows[0];
        row[1] = new string('x', 50);

        await model.SaveRowAsync(row);
        Assert.Equal("first_name", row.ErrorColumn);

        source.Fail = null;
        row[1] = "short";
        await model.SaveRowAsync(row);
        Assert.Null(row.ErrorColumn);
        Assert.False(row.HasError);
    }

    [Fact]
    public async Task Save_NullOnANotNullColumn_ThenAValue_ClearsTheError()
    {
        var (model, source) = await CreateAsync();
        var row = model.Rows[0];

        source.Fail = new InvalidOperationException("Column 'first_name' cannot be null");
        row[1] = "null";
        Assert.False(await model.SaveRowAsync(row));
        Assert.Equal("first_name", row.ErrorColumn);
        Assert.Null(row[1]);

        source.Fail = null;
        row[1] = "five";
        Assert.Equal(new Dictionary<int, object?> { [1] = "five" }, row.Changes());
        Assert.True(await model.SaveRowAsync(row));

        Assert.False(row.HasError);
        Assert.Null(row.ErrorColumn);
        Assert.Equal(RowState.Unchanged, row.State);
    }

    [Fact]
    public async Task Save_ErrorThenBackToTheStoredValue_ClearsTheError_WithoutSaving()
    {
        var (model, source) = await CreateAsync();
        var row = model.Rows[0];
        var stored = QueryValueFormatter.Format(row[1]);

        source.Fail = new InvalidOperationException("Column 'first_name' cannot be null");
        row[1] = "NULL";
        Assert.False(await model.SaveRowAsync(row));
        Assert.True(row.HasError);
        var executed = source.Executed.Count;

        row[1] = stored;

        Assert.False(row.HasError);
        Assert.Null(row.ErrorColumn);
        Assert.Equal(RowState.Unchanged, row.State);
        Assert.Equal(string.Empty, row.Marker);
        Assert.True(await model.SaveRowAsync(row));
        Assert.Equal(executed, source.Executed.Count);
    }

    [Fact]
    public async Task Save_DuringASave_RunsAgainWithTheNewestValues()
    {
        var (model, source) = await CreateAsync();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Block = gate.Task;
        var row = model.Rows[0];
        row[1] = "first";

        var first = model.SaveRowAsync(row);
        row[1] = "second";
        Assert.False(await model.SaveRowAsync(row));
        source.Block = null;
        gate.SetResult(true);

        Assert.True(await first);
        Assert.Equal(2, source.Executed.Count);
        Assert.Equal("second", source.LastParameters![0].Value);
    }

    [Fact]
    public void EditableRow_TracksChanges_NullText_AndUndo()
    {
        var row = new EditableRow([5, "PENELOPE", null, null, null]);

        row[1] = "PENELOPE";
        Assert.Equal(RowState.Unchanged, row.State);
        Assert.Equal(string.Empty, row.Marker);

        row[1] = "Pen";
        row[2] = "null";
        Assert.Equal(RowState.Modified, row.State);
        Assert.Equal("*", row.Marker);
        Assert.Equal(new Dictionary<int, object?> { [1] = "Pen" }, row.Changes());

        row[2] = "remark";
        Assert.Equal(2, row.Changes().Count);

        row.RejectChanges();
        Assert.Equal("PENELOPE", row[1]);
        Assert.Equal(RowState.Unchanged, row.State);
    }

    [Fact]
    public void EditableRow_Error_IsShownInTheMarker_AndClearedByAccept()
    {
        var row = EditableRow.CreateNew(2);
        Assert.Equal("+", row.Marker);

        row.Error = "Duplicate entry";
        Assert.Equal("!", row.Marker);

        row.AcceptChanges([7, "x"]);
        Assert.False(row.HasError);
        Assert.Equal(RowState.Unchanged, row.State);
        Assert.Equal(7, row[0]);
    }

    [Fact]
    public async Task Save_Update_ReadsTheRowBack()
    {
        var (model, source) = await CreateAsync();
        var row = model.Rows[0];
        row[1] = "Pen";

        Assert.True(await model.SaveRowAsync(row));

        Assert.StartsWith("UPDATE `sakila`.`actor` SET `first_name` = @p0 WHERE `actor_id` = @p1", source.Executed.Single(), StringComparison.Ordinal);
        Assert.Equal("PEN", row[1]);
        Assert.Equal(RowState.Unchanged, row.State);
    }

    [Fact]
    public async Task Save_UpdateOfARowThatIsGone_KeepsTheEdit()
    {
        var (model, source) = await CreateAsync();
        source.Affected = 0;
        var row = model.Rows[0];
        row[1] = "Pen";

        Assert.False(await model.SaveRowAsync(row));

        Assert.True(row.HasError);
        Assert.Equal("Pen", row[1]);
        Assert.Equal(RowState.Modified, row.State);
    }

    [Fact]
    public async Task Save_ServerError_IsShownOnTheRow_Masked()
    {
        var (model, source) = await CreateAsync();
        source.Fail = new InvalidOperationException("Data too long for column 'first_name'; Password=secret");
        var row = model.Rows[0];
        row[1] = new string('x', 50);

        Assert.False(await model.SaveRowAsync(row));

        Assert.Contains("Data too long", row.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", row.Error, StringComparison.Ordinal);
        Assert.Equal("!", row.Marker);
    }

    [Fact]
    public async Task Save_NewRow_UsesTheAutoIncrementValueToReadItBack()
    {
        var (model, source) = await CreateAsync();
        source.LastInsertId = 201;
        var row = model.AddRow();
        row[1] = "NEW";

        Assert.True(await model.SaveRowAsync(row));

        Assert.StartsWith("INSERT INTO `sakila`.`actor` (`first_name`) VALUES (@p0)", source.Executed.Single(), StringComparison.Ordinal);
        Assert.Equal(201L, source.LastSelectKey);
        Assert.Equal(RowState.Unchanged, row.State);
        Assert.Equal(4, model.TotalRows);
    }

    [Fact]
    public async Task Delete_RemovesRows_AndANewRowWithoutStatement()
    {
        var (model, source) = await CreateAsync();
        var added = model.AddRow();

        var removed = await model.DeleteRowsAsync([model.Rows[0], added]);

        Assert.Equal(2, removed);
        Assert.Single(source.Executed);
        Assert.StartsWith("DELETE FROM `sakila`.`actor` WHERE `actor_id` = @p0", source.Executed[0], StringComparison.Ordinal);
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(2, model.TotalRows);
    }

    [Fact]
    public async Task ViewsAndTablesWithoutKey_AreReadOnly()
    {
        var (view, _) = await CreateAsync(isView: true);
        var (noKey, _) = await CreateAsync(columns: [new ColumnInfo("a", "int", true, false, false, string.Empty)]);
        var (table, _) = await CreateAsync();

        Assert.Equal(EditBlock.View, view.EditBlock);
        Assert.Equal(EditBlock.NoPrimaryKey, noKey.EditBlock);
        Assert.True(table.CanEdit);
        Assert.False(view.CanEdit);
    }

    private static async Task<(TableDataViewModel Model, EditSource Source)> CreateAsync(bool isView = false, IReadOnlyList<ColumnInfo>? columns = null)
    {
        var metadata = new FakeMetadataProvider { Columns = (_, _) => Task.FromResult(columns ?? _columns) };
        var explorer = new ExplorerViewModel(TestExplorer.Context(metadata));
        var server = explorer.AddServer(TestExplorer.Definition("Prod"));
        var source = new EditSource();
        var model = new TableDataViewModel(server, metadata, source, "sakila", "actor", isView, new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));
        await model.LoadAsync();
        return (model, source);
    }

    private sealed class EditSource : ITableDataSource
    {
        public List<string> Executed { get; } = [];

        public long Affected { get; set; } = 1;

        public long LastInsertId { get; set; }

        public Exception? Fail { get; set; }

        public object? LastSelectKey { get; private set; }

        public Task<QueryResultSet> ReadAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken)
        {
            if (command.Sql.Contains("LIMIT", StringComparison.Ordinal))
            {
                return Task.FromResult(new QueryResultSet(["actor_id", "first_name", "note", "picture", "upper_name"], [[1, "PENELOPE", null, null, "PENELOPE"], [2, "NICK", null, null, "NICK"], [3, "ED", null, null, "ED"]], 3));
            }

            // Read back after a change: the server stores names in upper case.
            LastSelectKey = command.Parameters[0].Value;
            return Task.FromResult(new QueryResultSet(["actor_id"], [[LastSelectKey, "PEN", null, null, "PEN"]], 1));
        }

        public Task<long> CountAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken) => Task.FromResult(3L);

        public Task<long?> EstimateRowsAsync(ConnectionDefinition definition, string database, string table, CancellationToken cancellationToken) => Task.FromResult<long?>(null);

        public Task? Block { get; set; }

        public IReadOnlyList<KeyValuePair<string, object?>>? LastParameters { get; private set; }

        public async Task<RowChangeResult> ExecuteAsync(ConnectionDefinition definition, SqlCommandText command, CancellationToken cancellationToken)
        {
            if (Block is { } block)
            {
                await block;
            }

            if (Fail is { } fail)
            {
                throw fail;
            }

            Executed.Add(command.Sql);
            LastParameters = command.Parameters;
            return new RowChangeResult(Affected, LastInsertId);
        }
    }
}
