using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Data;

/// <summary>
/// View Data of one table or view (US-0801): pages of <see cref="PageSize"/> rows, server-side sort and filters, and the
/// row count. The count is exact, except for a table without filters that the storage engine estimates at
/// <see cref="EstimateThreshold"/> rows or more: COUNT(*) on such a table can take minutes.
/// </summary>
public sealed class TableDataViewModel : ObservableObject, IDisposable
{
    /// <summary>Default rows per page.</summary>
    public const int DefaultPageSize = 1000;

    /// <summary>From this estimate on the row count is not counted.</summary>
    public const long EstimateThreshold = 1_000_000;

    private readonly ServerNodeViewModel _server;
    private readonly IMetadataProvider _metadata;
    private readonly ITableDataSource _source;
    private readonly IExtensionLogger _logger;
    private readonly Dictionary<string, string> _filters = new(StringComparer.Ordinal);
    private IReadOnlyList<ColumnInfo> _columns = [];
    private ObservableCollection<EditableRow> _rows = [];
    private readonly HashSet<EditableRow> _saving = [];
    private readonly HashSet<EditableRow> _saveAgain = [];
    private CancellationTokenSource? _load;
    private CancellationTokenSource? _count;
    private TableSort? _sort;
    private long _pageIndex;
    private long? _totalRows;
    private bool _isEstimated;
    private bool _isLoading;
    private bool _isCounting;
    private string? _error;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="TableDataViewModel"/> class.</summary>
    public TableDataViewModel(ServerNodeViewModel server, IMetadataProvider metadata, ITableDataSource source, string database, string table, bool isView, IExtensionLogger logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Database = database ?? throw new ArgumentNullException(nameof(database));
        Table = table ?? throw new ArgumentNullException(nameof(table));
        IsView = isView;
    }

    /// <summary>Raised when the columns are known (after the first load), so the view can build the grid.</summary>
    public event EventHandler? ColumnsLoaded;

    /// <summary>Gets the connection.</summary>
    public ServerNodeViewModel Server => _server;

    /// <summary>Gets the database.</summary>
    public string Database { get; }

    /// <summary>Gets the table or view.</summary>
    public string Table { get; }

    /// <summary>Gets a value indicating whether the object is a view.</summary>
    public bool IsView { get; }

    /// <summary>Gets or sets the rows per page.</summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Gets the columns.</summary>
    public IReadOnlyList<ColumnInfo> Columns => _columns;

    /// <summary>Gets the rows of the current page, plus rows added in the grid.</summary>
    public ObservableCollection<EditableRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>Gets why the rows cannot be edited (US-0802), or <see cref="EditBlock.None"/>.</summary>
    public EditBlock EditBlock => IsView ? EditBlock.View : _columns.Count > 0 && !RowChangeBuilder.HasPrimaryKey(_columns) ? EditBlock.NoPrimaryKey : EditBlock.None;

    /// <summary>Gets a value indicating whether the rows can be edited: a table with a primary key, columns loaded.</summary>
    public bool CanEdit => EditBlock == EditBlock.None && _columns.Count > 0;

    /// <summary>Gets or sets the error text when a changed row is no longer found.</summary>
    public string RowNotFoundText { get; set; } = "The row was not found. Someone else may have changed or deleted it; refresh to see the current data.";

    /// <summary>Gets the sort, or null for the default order (primary key).</summary>
    public TableSort? Sort => _sort;

    /// <summary>Gets the filter texts per column.</summary>
    public IReadOnlyDictionary<string, string> Filters => _filters;

    /// <summary>Gets the 1-based page number.</summary>
    public long PageNumber => _pageIndex + 1;

    /// <summary>Gets the number of pages, or null while the row count is not known.</summary>
    public long? PageCount => _totalRows is { } total ? Math.Max(1, (total + PageSize - 1) / PageSize) : null;

    /// <summary>Gets the number of rows (with the filters), or null while it is not known.</summary>
    public long? TotalRows => _totalRows;

    /// <summary>Gets a value indicating whether <see cref="TotalRows"/> is the estimate of the storage engine.</summary>
    public bool IsEstimated => _isEstimated;

    /// <summary>Gets a value indicating whether a page loads.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaiseNavigation();
            }
        }
    }

    /// <summary>Gets a value indicating whether the rows are being counted.</summary>
    public bool IsCounting
    {
        get => _isCounting;
        private set => SetProperty(ref _isCounting, value);
    }

    /// <summary>Gets the error of the last load (password masked), or null.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>Gets a value indicating whether <see cref="Error"/> is set.</summary>
    public bool HasError => _error is not null;

    /// <summary>Gets a value indicating whether there is a previous page.</summary>
    public bool CanGoBack => !_isLoading && _pageIndex > 0;

    /// <summary>Gets a value indicating whether there may be a next page.</summary>
    public bool CanGoForward => !_isLoading && (PageCount is { } count ? PageNumber < count : _rows.Count >= PageSize);

    /// <summary>Gets a value indicating whether the last page is known.</summary>
    public bool CanGoToLast => !_isLoading && PageCount is { } count && PageNumber < count;

    /// <summary>Gets the running load, for tests.</summary>
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the running count, for tests.</summary>
    internal Task PendingCount { get; private set; } = Task.CompletedTask;

    /// <summary>Loads the columns and the first page. Also used by Refresh: keeps sort, filters and page.</summary>
    public Task LoadAsync() => Start(reloadColumns: _columns.Count == 0, recount: true);

    /// <summary>Reloads the current page and the row count.</summary>
    public Task RefreshAsync() => Start(reloadColumns: false, recount: true);

    /// <summary>Goes to the first page.</summary>
    public Task FirstPageAsync() => GoTo(0);

    /// <summary>Goes to the previous page.</summary>
    public Task PreviousPageAsync() => GoTo(_pageIndex - 1);

    /// <summary>Goes to the next page.</summary>
    public Task NextPageAsync() => GoTo(_pageIndex + 1);

    /// <summary>Goes to the last page, when the row count is known.</summary>
    public Task LastPageAsync() => PageCount is { } count ? GoTo(count - 1) : Task.CompletedTask;

    /// <summary>Sorts on <paramref name="column"/>: ascending, then descending, then ascending again. Back to page 1.</summary>
    public Task SortByAsync(string column)
    {
        _sort = _sort is { } current && current.Column == column && !current.Descending
            ? new TableSort(column, Descending: true)
            : new TableSort(column, Descending: false);
        OnPropertyChanged(nameof(Sort));
        _pageIndex = 0;
        return Start(reloadColumns: false, recount: false);
    }

    /// <summary>Sets the filters (column name to text; empty text removes it) and loads page 1 with a new count.</summary>
    public Task ApplyFiltersAsync(IReadOnlyDictionary<string, string> filters)
    {
        _filters.Clear();
        foreach (var pair in filters ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                _filters[pair.Key] = pair.Value.Trim();
            }
        }

        OnPropertyChanged(nameof(Filters));
        _pageIndex = 0;
        return Start(reloadColumns: false, recount: true);
    }

    /// <summary>Adds an empty row at the end of the page; it is stored when <see cref="SaveRowAsync"/> runs.</summary>
    public EditableRow AddRow()
    {
        var row = EditableRow.CreateNew(_columns.Count);
        _rows.Add(row);
        return row;
    }

    /// <summary>
    /// Stores the changes of <paramref name="row"/> (US-0802): INSERT for a new row, UPDATE of the changed columns on the
    /// old key otherwise, then reads the row back so the grid shows what the server stored. On an error the row keeps its
    /// edited values and gets the message; returns false then.
    /// </summary>
    public async Task<bool> SaveRowAsync(EditableRow row)
    {
        if (row is null || row.State == RowState.Unchanged || !CanEdit)
        {
            return true;
        }

        // One save per row at a time; a request during a save runs again afterwards with the newest values.
        if (!_saving.Add(row))
        {
            _saveAgain.Add(row);
            return false;
        }

        try
        {
            bool saved;
            do
            {
                _saveAgain.Remove(row);
                saved = await SaveRowCoreAsync(row);
            }
            while (_saveAgain.Contains(row) && row.State != RowState.Unchanged);

            return saved;
        }
        finally
        {
            _saving.Remove(row);
            _saveAgain.Remove(row);
        }
    }

    private async Task<bool> SaveRowCoreAsync(EditableRow row)
    {
        _logger.Debug($"Saving a {row.State} row of {Database}.{Table}.");
        try
        {
            if (!await _server.EnsurePasswordAsync(CancellationToken.None))
            {
                return false;
            }

            var definition = _server.Definition;
            var changes = row.Changes();
            var sent = (object?[])row.Current.Clone();
            object?[] key;
            if (row.State == RowState.New)
            {
                var result = await _source.ExecuteAsync(definition, RowChangeBuilder.Insert(Database, Table, _columns, changes), CancellationToken.None);
                key = (object?[])row.Current.Clone();
                var keys = Enumerable.Range(0, _columns.Count).Where(i => _columns[i].IsPrimaryKey).ToList();
                if (keys.Count == 1 && key[keys[0]] is null && _columns[keys[0]].IsAutoIncrement)
                {
                    key[keys[0]] = result.LastInsertId;
                }

                if (_totalRows is { } total)
                {
                    SetTotal(total + 1, _isEstimated);
                }
            }
            else
            {
                if (changes.Count == 0)
                {
                    row.RejectChanges();
                    return true;
                }

                var result = await _source.ExecuteAsync(definition, RowChangeBuilder.Update(Database, Table, _columns, row.Original, changes), CancellationToken.None);
                if (result.RowsAffected == 0)
                {
                    row.Error = RowNotFoundText;
                    return false;
                }

                key = row.Current;
            }

            var stored = await _source.ReadAsync(definition, RowChangeBuilder.SelectRow(Database, Table, _columns, key), CancellationToken.None);
            row.AcceptChanges(stored.Rows.Count > 0 ? stored.Rows[0] : sent, sent);
            return true;
        }
        catch (Exception ex)
        {
            row.Error = SensitiveDataMasker.MaskText(ex.Message);
            row.ErrorColumn = RowChangeBuilder.ErrorColumn(ex.Message, _columns);
            _logger.Warning($"Saving a row of {Database}.{Table} failed.", ex);
            return false;
        }
    }

    /// <summary>
    /// Deletes <paramref name="rows"/> on the server (rows not stored yet are only removed). Stops at the first error,
    /// which is shown on that row. Returns the number of rows removed.
    /// </summary>
    public async Task<int> DeleteRowsAsync(IReadOnlyList<EditableRow> rows)
    {
        var removed = 0;
        if (rows is null || !CanEdit)
        {
            return removed;
        }

        if (!await _server.EnsurePasswordAsync(CancellationToken.None))
        {
            return removed;
        }

        foreach (var row in rows.ToList())
        {
            if (row.State != RowState.New)
            {
                try
                {
                    await _source.ExecuteAsync(_server.Definition, RowChangeBuilder.Delete(Database, Table, _columns, row.Original), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    row.Error = SensitiveDataMasker.MaskText(ex.Message);
                    _logger.Warning($"Deleting a row of {Database}.{Table} failed.", ex);
                    break;
                }

                if (_totalRows is { } total)
                {
                    SetTotal(Math.Max(0, total - 1), _isEstimated);
                }
            }

            _rows.Remove(row);
            removed++;
        }

        return removed;
    }

    /// <summary>Undoes the changes of <paramref name="row"/>; a row that is not stored yet is removed.</summary>
    public void UndoRow(EditableRow row)
    {
        if (row is null)
        {
            return;
        }

        if (row.State == RowState.New)
        {
            _rows.Remove(row);
        }
        else
        {
            row.RejectChanges();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _load?.Cancel();
        _count?.Cancel();
    }

    private Task GoTo(long pageIndex)
    {
        if (pageIndex < 0 || pageIndex == _pageIndex || (PageCount is { } count && pageIndex >= count))
        {
            return Task.CompletedTask;
        }

        _pageIndex = pageIndex;
        return Start(reloadColumns: false, recount: false);
    }

    private Task Start(bool reloadColumns, bool recount)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        PendingLoad = LoadCoreAsync(reloadColumns, recount);
        return PendingLoad;
    }

    private async Task LoadCoreAsync(bool reloadColumns, bool recount)
    {
        _load?.Cancel();
        var cancellation = new CancellationTokenSource();
        _load = cancellation;
        if (recount)
        {
            _count?.Cancel();
            _totalRows = null;
            _isEstimated = false;
            OnPropertyChanged(nameof(TotalRows));
            OnPropertyChanged(nameof(IsEstimated));
            OnPropertyChanged(nameof(PageCount));
        }

        Error = null;
        IsLoading = true;
        try
        {
            if (!await _server.EnsurePasswordAsync(cancellation.Token))
            {
                return;
            }

            var definition = _server.Definition;
            if (reloadColumns || _columns.Count == 0)
            {
                _columns = await _metadata.GetColumnsAsync(definition, Database, Table, cancellation.Token);
                ColumnsLoaded?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(Columns));
                OnPropertyChanged(nameof(EditBlock));
                OnPropertyChanged(nameof(CanEdit));
            }

            if (_columns.Count == 0)
            {
                Rows = [];
                return;
            }

            var offset = _pageIndex * PageSize;
            var page = await _source.ReadAsync(definition, TableDataQuery.Select(Database, Table, _columns, _filters, _sort, offset, PageSize), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            Rows = new ObservableCollection<EditableRow>(page.Rows.Select(values => new EditableRow(values)));

            // A page that is not full gives the exact count, without COUNT(*).
            if (page.Rows.Count < PageSize && (page.Rows.Count > 0 || _pageIndex == 0))
            {
                _count?.Cancel();
                SetTotal(offset + page.Rows.Count, estimated: false);
            }
            else if (recount)
            {
                PendingCount = CountAsync(definition);
            }
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            // A newer load replaced this one, or the window closed.
        }
        catch (Exception ex)
        {
            Error = SensitiveDataMasker.MaskText(ex.Message);
            _logger.Warning($"View Data of {Database}.{Table} on '{_server.Label}' failed.", ex);
        }
        finally
        {
            if (ReferenceEquals(_load, cancellation))
            {
                IsLoading = false;
            }

            OnPropertyChanged(nameof(PageNumber));
            RaiseNavigation();
        }
    }

    private async Task CountAsync(Connections.ConnectionDefinition definition)
    {
        _count?.Cancel();
        var cancellation = new CancellationTokenSource();
        _count = cancellation;
        IsCounting = true;
        try
        {
            if (!IsView && !TableDataQuery.HasFilters(_filters))
            {
                var estimate = await _source.EstimateRowsAsync(definition, Database, Table, cancellation.Token);
                if (estimate is >= EstimateThreshold && !cancellation.IsCancellationRequested)
                {
                    SetTotal(estimate.Value, estimated: true);
                    return;
                }
            }

            var total = await _source.CountAsync(definition, TableDataQuery.Count(Database, Table, _columns, _filters), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                SetTotal(total, estimated: false);
            }
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            // A new filter or Refresh started another count.
        }
        catch (Exception ex)
        {
            _logger.Warning($"Counting the rows of {Database}.{Table} failed.", ex);
        }
        finally
        {
            if (ReferenceEquals(_count, cancellation))
            {
                IsCounting = false;
            }
        }
    }

    private void SetTotal(long total, bool estimated)
    {
        _totalRows = total;
        _isEstimated = estimated;
        OnPropertyChanged(nameof(TotalRows));
        OnPropertyChanged(nameof(IsEstimated));
        OnPropertyChanged(nameof(PageCount));
        RaiseNavigation();
    }

    private void RaiseNavigation()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoToLast));
    }
}
