using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ObjectExplorerForMySql.Core.Data;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Data;

/// <summary>
/// Content of a View Data window (US-0801, US-0802). The columns are built when the model knows them: a header with the
/// column name and a filter box. A click on the name sorts on the server; Enter in a filter box filters on the server.
/// For a table with a primary key the cells can be edited; a row is stored when the grid leaves it.
/// </summary>
public partial class TableDataControl : UserControl
{
    private readonly Dictionary<string, TextBox> _filterBoxes = new(StringComparer.Ordinal);
    private TableDataViewModel? _model;

    /// <summary>Initializes a new instance of the <see cref="TableDataControl"/> class.</summary>
    public TableDataControl()
    {
        InitializeComponent();
    }

    /// <summary>Shows <paramref name="model"/>.</summary>
    internal void Attach(TableDataViewModel model)
    {
        _model = model;
        model.RowNotFoundText = Strings.ViewDataRowNotFound;
        DataContext = model;
        model.ColumnsLoaded += OnColumnsLoaded;
        model.PropertyChanged += OnModelPropertyChanged;
        BuildColumns();
        UpdateTexts();
    }

    /// <summary>Stops following the model.</summary>
    internal void Detach()
    {
        if (_model is { } model)
        {
            model.ColumnsLoaded -= OnColumnsLoaded;
            model.PropertyChanged -= OnModelPropertyChanged;
            _model = null;
        }
    }

    private void OnColumnsLoaded(object? sender, EventArgs e) => BuildColumns();

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TableDataViewModel.PageNumber):
            case nameof(TableDataViewModel.PageCount):
            case nameof(TableDataViewModel.TotalRows):
            case nameof(TableDataViewModel.IsEstimated):
            case nameof(TableDataViewModel.IsCounting):
                UpdateTexts();
                break;
            case nameof(TableDataViewModel.Sort):
                UpdateSortGlyphs();
                break;
        }
    }

    private void BuildColumns()
    {
        if (_model is not { } model)
        {
            return;
        }

        Grid.Columns.Clear();
        _filterBoxes.Clear();
        var editor = (Style)FindResource("CellEditor");
        for (var i = 0; i < model.Columns.Count; i++)
        {
            var column = model.Columns[i];
            var filter = new TextBox { Style = (Style)FindResource("FilterBox") };
            filter.Text = model.Filters.TryGetValue(column.Name, out var text) ? text : string.Empty;
            filter.KeyDown += OnFilterKeyDown;
            AutomationProperties.SetName(filter, column.Name);
            _filterBoxes[column.Name] = filter;

            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = column.Name, ToolTip = column.ColumnType });
            header.Children.Add(filter);

            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                SortMemberPath = column.Name,
                Binding = new Binding($"[{i}]") { Converter = EditValueConverter.Instance, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                IsReadOnly = !RowChangeBuilder.IsEditable(column),
                EditingElementStyle = editor,
                MaxWidth = 400,
            });
        }

        // US-0802: only tables with a primary key can be edited.
        Grid.IsReadOnly = !model.CanEdit;
        var editVisibility = model.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        EditSeparator.Visibility = editVisibility;
        UndoMenuItem.Visibility = editVisibility;
        DeleteMenuItem.Visibility = editVisibility;
        ReadOnlyText.Text = model.EditBlock switch
        {
            EditBlock.View => Strings.ViewDataReadOnlyView,
            EditBlock.NoPrimaryKey => Strings.ViewDataReadOnlyNoKey,
            _ => string.Empty,
        };
        UpdateSortGlyphs();
    }

    private void UpdateTexts()
    {
        if (_model is not { } model)
        {
            return;
        }

        PageText.Text = model.PageCount is { } pages
            ? Strings.Format(Strings.ViewDataPage, model.PageNumber, pages)
            : Strings.Format(Strings.ViewDataPageUnknown, model.PageNumber);
        RowsText.Text = model.TotalRows is { } total
            ? Strings.Format(model.IsEstimated ? Strings.ViewDataRowsEstimated : Strings.ViewDataRows, total)
            : model.IsCounting ? Strings.ViewDataCounting : string.Empty;
    }

    private void UpdateSortGlyphs()
    {
        var sort = _model?.Sort;
        foreach (var column in Grid.Columns)
        {
            column.SortDirection = sort is not null && column.SortMemberPath == sort.Column
                ? sort.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;
        }
    }

    // Paging, sort, filters and refresh load other rows: unsaved rows are lost then, so ask first.
    private bool ConfirmDiscard()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Grid.CancelEdit(DataGridEditingUnit.Row);
        if (_model is not { } model || !model.Rows.Any(r => r.State != RowState.Unchanged))
        {
            return true;
        }

        return Ask(Strings.ViewDataDiscardChanges);
    }

    // The grid only shows a page; the sort runs on the server over all rows.
    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        e.Handled = true;
        if (_model is { } model && e.Column.SortMemberPath is { Length: > 0 } column && ConfirmDiscard())
        {
            _ = model.SortByAsync(column);
        }
    }

    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e.Key == Key.Enter)
        {
            ApplyFilters();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && sender is TextBox box)
        {
            box.Clear();
            ApplyFilters();
            e.Handled = true;
        }
    }

    private void ApplyFilters()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_model is { } model && ConfirmDiscard())
        {
            _ = model.ApplyFiltersAsync(_filterBoxes.ToDictionary(p => p.Key, p => p.Value.Text));
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ConfirmDiscard())
        {
            _ = _model?.RefreshAsync();
        }
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (var box in _filterBoxes.Values)
        {
            box.Clear();
        }

        ApplyFilters();
    }

    private void OnFirstPage(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Navigate(m => m.FirstPageAsync());
    }

    private void OnPreviousPage(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Navigate(m => m.PreviousPageAsync());
    }

    private void OnNextPage(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Navigate(m => m.NextPageAsync());
    }

    private void OnLastPage(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Navigate(m => m.LastPageAsync());
    }

    private void Navigate(Func<TableDataViewModel, Task> go)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_model is { } model && ConfirmDiscard())
        {
            _ = go(model);
        }
    }

    // US-0802: the grid commits the row first; the save runs right after, so it sees the new values.
    private void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is EditableRow row && _model is { } model)
        {
#pragma warning disable VSSDK007 // Fire and forget: the result is shown on the row.
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                await model.SaveRowAsync(row);
            });
#pragma warning restore VSSDK007
        }
    }

    /// <summary>
    /// A row with an error is saved again as soon as a fixed cell is committed, so the
    /// marking clears without having to leave the row. The yield lets the binding
    /// write the new value first; the view model runs one save per row at a time.
    /// </summary>
    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is EditableRow { HasError: true } row && _model is { } model)
        {
#pragma warning disable VSSDK007 // Fire and forget: the result is shown on the row.
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                await model.SaveRowAsync(row);
            });
#pragma warning restore VSSDK007
        }
    }

    private void OnAddRow(object sender, RoutedEventArgs e)
    {
        if (_model is not { CanEdit: true } model)
        {
            return;
        }

        Grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        var row = model.AddRow();
        Grid.ScrollIntoView(row);
        var first = Grid.Columns.FirstOrDefault(c => !c.IsReadOnly);
        if (first is null)
        {
            return;
        }

        // After a layout pass the new row has a container and can be edited.
        Grid.UpdateLayout();
        Grid.Focus();
        Grid.SelectedCells.Clear();
        Grid.CurrentCell = new DataGridCellInfo(row, first);
        Grid.SelectedCells.Add(Grid.CurrentCell);
        Grid.BeginEdit();
    }

    private void OnDeleteRows(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_model is not { CanEdit: true } model)
        {
            return;
        }

        Grid.CancelEdit(DataGridEditingUnit.Row);
        var rows = SelectedRows();
        if (rows.Count == 0)
        {
            return;
        }

        var stored = rows.Count(r => r.State != RowState.New);
        if (stored > 0 && !Ask(Strings.Format(Strings.ViewDataDeleteConfirm, stored, $"{model.Database}.{model.Table}")))
        {
            return;
        }

        _ = model.DeleteRowsAsync(rows);
    }

    private void OnUndoRow(object sender, RoutedEventArgs e)
    {
        Grid.CancelEdit(DataGridEditingUnit.Row);
        if (_model is { } model)
        {
            foreach (var row in SelectedRows())
            {
                model.UndoRow(row);
            }
        }
    }

    // Rows with a selected cell, or the current row.
    private List<EditableRow> SelectedRows()
    {
        var rows = Grid.SelectedCells.Select(c => c.Item).OfType<EditableRow>().Distinct().ToList();
        if (rows.Count == 0 && Grid.CurrentItem is EditableRow current)
        {
            rows.Add(current);
        }

        return rows;
    }

    private void OnCopy(object sender, RoutedEventArgs e) => Copy(withHeaders: false);

    private void OnCopyWithHeaders(object sender, RoutedEventArgs e) => Copy(withHeaders: true);

    private void Copy(bool withHeaders)
    {
        if (Grid.SelectedCells.Count == 0)
        {
            Grid.SelectAllCells();
        }

        // Headers hold a filter box; copy the column names instead.
        Grid.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
        ApplicationCommands.Copy.Execute(null, Grid);
        if (!withHeaders || _model is not { } model)
        {
            return;
        }

        var cells = Clipboard.GetText();
        var shown = Grid.SelectedCells.Select(c => c.Column).Distinct().OrderBy(c => c.DisplayIndex)
            .Select(c => model.Columns[Grid.Columns.IndexOf(c)].Name);
        Clipboard.SetText(string.Join("\t", shown) + Environment.NewLine + cells);
    }

    private static bool Ask(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, Strings.ToolWindowTitle,
            OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == (int)VSConstants.MessageBoxResult.IDYES;
    }

    // Shows values as in the query results; an edited cell gives its text back, the row turns "NULL" into NULL.
    private sealed class EditValueConverter : IValueConverter
    {
        public static readonly EditValueConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => QueryValueFormatter.Format(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
    }
}

/// <summary>
/// True when a cell belongs to the column of the row's save error (US-0802). Values: <see cref="EditableRow.ErrorColumn"/>
/// and the column name of the cell (<see cref="DataGridColumn.SortMemberPath"/>).
/// </summary>
public sealed class ErrorCellConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is { Length: 2 } && values[0] is string errorColumn && values[1] is string column
        && string.Equals(errorColumn, column, StringComparison.Ordinal);

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// View Data window (US-0801): a tool window in the document well, one per table. Transient: Visual Studio does not
/// reopen it at startup, so it never asks for a password by itself.
/// </summary>
[Guid(ToolWindowGuidString)]
public sealed class TableDataToolWindow : ToolWindowPane
{
    /// <summary>GUID of the tool window type.</summary>
    public const string ToolWindowGuidString = "8d6b1f3e-2c51-4a7e-9a0f-5b3f6c1e2d47";

    private readonly TableDataControl _control = new();
    private TableDataViewModel? _model;

    /// <summary>Initializes a new instance of the <see cref="TableDataToolWindow"/> class.</summary>
    public TableDataToolWindow()
        : base(null)
    {
        Content = _control;
    }

    /// <summary>Gets the model shown, or null before <see cref="Show"/>.</summary>
    internal TableDataViewModel? Model => _model;

    /// <summary>Shows <paramref name="model"/> and starts loading.</summary>
    internal void Show(TableDataViewModel model)
    {
        _model = model;
        Caption = Strings.Format(Strings.ViewDataCaption, model.Database, model.Table);
        _control.Attach(model);
        _ = model.LoadAsync();
    }

    /// <inheritdoc />
    protected override void OnClose()
    {
        _control.Detach();
        _model?.Dispose();
        _model = null;
        base.OnClose();
    }
}
