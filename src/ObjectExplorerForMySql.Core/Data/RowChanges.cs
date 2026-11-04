using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Data;

/// <summary>
/// Builds the statements of Edit Data (US-0802): UPDATE, INSERT and DELETE of one row, found by its primary key, and the
/// SELECT that reads the row back after a change. All values are parameters.
/// </summary>
public static class RowChangeBuilder
{
    private static readonly HashSet<string> _notEditableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "binary", "varbinary", "tinyblob", "blob", "mediumblob", "longblob",
        "geometry", "point", "linestring", "polygon", "multipoint", "multilinestring", "multipolygon", "geometrycollection",
    };

    /// <summary>
    /// Returns true when the grid can edit <paramref name="column"/>: not generated, not binary and not spatial (their
    /// values are shown as hex or not at all, so the text in the grid is not the value).
    /// </summary>
    public static bool IsEditable(ColumnInfo column)
    {
        if (column is null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        var type = column.DataType ?? column.ColumnType.Split(' ', '(')[0];
        return !column.IsGenerated && !_notEditableTypes.Contains(type);
    }

    /// <summary>Returns true when rows of a table with these columns can be changed: it has a primary key.</summary>
    public static bool HasPrimaryKey(IReadOnlyList<ColumnInfo> columns) => columns?.Any(c => c.IsPrimaryKey) == true;

    /// <summary>UPDATE of the changed columns of the row with the old key values.</summary>
    public static SqlCommandText Update(string database, string table, IReadOnlyList<ColumnInfo> columns, object?[] oldValues, IReadOnlyDictionary<int, object?> changes)
    {
        if (changes is null || changes.Count == 0)
        {
            throw new ArgumentException("At least one change is required.", nameof(changes));
        }

        var parameters = new List<KeyValuePair<string, object?>>();
        var set = changes.OrderBy(c => c.Key).Select(c => MySqlIdentifier.Quote(columns[c.Key].Name) + " = " + Add(parameters, c.Value));
        var sql = "UPDATE " + Name(database, table) + " SET " + string.Join(", ", set) + KeyWhere(columns, oldValues, parameters);
        return new SqlCommandText(sql, parameters);
    }

    /// <summary>
    /// INSERT of the columns with a value. Columns left empty (null) get their default, so an AUTO_INCREMENT key can be
    /// left empty.
    /// </summary>
    public static SqlCommandText Insert(string database, string table, IReadOnlyList<ColumnInfo> columns, IReadOnlyDictionary<int, object?> values)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        var given = (values ?? new Dictionary<int, object?>()).Where(v => v.Value is not null).OrderBy(v => v.Key).ToList();
        if (given.Count == 0)
        {
            return new SqlCommandText("INSERT INTO " + Name(database, table) + " () VALUES ()", parameters);
        }

        var names = string.Join(", ", given.Select(v => MySqlIdentifier.Quote(columns[v.Key].Name)));
        var placeholders = string.Join(", ", given.Select(v => Add(parameters, v.Value)));
        return new SqlCommandText("INSERT INTO " + Name(database, table) + " (" + names + ") VALUES (" + placeholders + ")", parameters);
    }

    /// <summary>DELETE of the row with these key values.</summary>
    public static SqlCommandText Delete(string database, string table, IReadOnlyList<ColumnInfo> columns, object?[] keyValues)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        return new SqlCommandText("DELETE FROM " + Name(database, table) + KeyWhere(columns, keyValues, parameters), parameters);
    }

    /// <summary>SELECT of the row with these key values, to show the stored values after a change.</summary>
    public static SqlCommandText SelectRow(string database, string table, IReadOnlyList<ColumnInfo> columns, object?[] keyValues)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        var sql = "SELECT " + string.Join(", ", columns.Select(c => MySqlIdentifier.Quote(c.Name))) + " FROM " + Name(database, table)
            + KeyWhere(columns, keyValues, parameters);
        return new SqlCommandText(sql, parameters);
    }

    private static string KeyWhere(IReadOnlyList<ColumnInfo> columns, object?[] values, List<KeyValuePair<string, object?>> parameters)
    {
        var keys = Enumerable.Range(0, columns.Count).Where(i => columns[i].IsPrimaryKey).ToList();
        if (keys.Count == 0)
        {
            throw new InvalidOperationException("The table has no primary key.");
        }

        return " WHERE " + string.Join(" AND ", keys.Select(i => MySqlIdentifier.Quote(columns[i].Name) + " = " + Add(parameters, values[i])));
    }

    private static string Add(List<KeyValuePair<string, object?>> parameters, object? value)
    {
        var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        parameters.Add(new KeyValuePair<string, object?>(name, value));
        return name;
    }

    private static string Name(string database, string table) => MySqlIdentifier.Quote(database) + "." + MySqlIdentifier.Quote(table);

    private static readonly System.Text.RegularExpressions.Regex _columnInMessage = new(
        @"\b(?:column|field)\s+((?:[`'][^`']+[`']\.?)+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the column a server error is about, for example <c>name</c> in "Data too long for column 'name' at row 1",
    /// "Column 'name' cannot be null", "Field 'name' doesn't have a default value" or "for column `db`.`t`.`name`".
    /// Null when the message names no column of <paramref name="columns"/> (for example a duplicate key).
    /// </summary>
    public static string? ErrorColumn(string? message, IReadOnlyList<ColumnInfo> columns)
    {
        if (string.IsNullOrEmpty(message) || columns is null)
        {
            return null;
        }

        var match = _columnInMessage.Match(message);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups[1].Value.Split('.').Last().Trim('`', '\'');
        return columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;
    }
}

/// <summary>Why View Data cannot edit the rows (US-0802).</summary>
public enum EditBlock
{
    /// <summary>The rows can be edited.</summary>
    None,

    /// <summary>A view: read-only.</summary>
    View,

    /// <summary>A table without primary key: a changed row cannot be found again reliably.</summary>
    NoPrimaryKey,
}

/// <summary>State of a row in Edit Data.</summary>
public enum RowState
{
    /// <summary>As stored.</summary>
    Unchanged,

    /// <summary>Changed in the grid, not saved yet (or saving failed).</summary>
    Modified,

    /// <summary>Added in the grid, not saved yet (or saving failed).</summary>
    New,
}

/// <summary>
/// A row of View Data that the grid can edit (US-0802). The grid binds to the indexer: it shows the stored value and
/// sets text. Text <c>NULL</c> (any case) means NULL, as in SSMS. A value equal to the shown text is not a change.
/// </summary>
public sealed class EditableRow : ObservableObject
{
    private object?[] _original;
    private readonly object?[] _current;
    private readonly bool[] _changed;
    private string? _error;
    private string? _errorColumn;
    private RowState _state;

    /// <summary>Initializes a new instance of the <see cref="EditableRow"/> class with stored values.</summary>
    public EditableRow(object?[] values)
    {
        _original = values ?? throw new ArgumentNullException(nameof(values));
        _current = (object?[])values.Clone();
        _changed = new bool[values.Length];
    }

    /// <summary>Creates an empty row that is not stored yet.</summary>
    public static EditableRow CreateNew(int columnCount) => new(new object?[columnCount]) { _state = RowState.New };

    /// <summary>Gets the state.</summary>
    public RowState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    /// <summary>Gets or sets the error of the last save (password masked), or null. Clearing it also clears <see cref="ErrorColumn"/>.</summary>
    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(Marker));
                if (value is null)
                {
                    ErrorColumn = null;
                }
            }
        }
    }

    /// <summary>Gets or sets the column the error is about (from the server message), or null; the grid marks that cell.</summary>
    public string? ErrorColumn
    {
        get => _errorColumn;
        set => SetProperty(ref _errorColumn, value);
    }

    /// <summary>Gets a value indicating whether <see cref="Error"/> is set.</summary>
    public bool HasError => _error is not null;

    /// <summary>Gets the text in the row header: ! for an error, * for unsaved changes, + for a new row.</summary>
    public string Marker => _error is not null ? "!" : _state switch
    {
        RowState.New => "+",
        RowState.Modified => "*",
        _ => string.Empty,
    };

    /// <summary>Gets the stored values (for a new row: empty).</summary>
    public object?[] Original => _original;

    /// <summary>Gets the values as shown and edited.</summary>
    public object?[] Current => _current;

    /// <summary>Gets or sets a value. Set by the grid with the text of the cell.</summary>
    public object? this[int index]
    {
        get => _current[index];
        set
        {
            var newValue = value is string text && string.Equals(text, QueryValueFormatter.NullText, StringComparison.OrdinalIgnoreCase) ? null : value;
            var same = newValue is null ? _original[index] is null : newValue is string s && s == QueryValueFormatter.Format(_original[index]) && _original[index] is not null;
            _current[index] = same ? _original[index] : newValue;
            _changed[index] = !same;
            if (_state != RowState.New)
            {
                State = _changed.Any(c => c) ? RowState.Modified : RowState.Unchanged;

                // Back at the stored values: nothing is saved, so the error of the last save no longer applies.
                if (_state == RowState.Unchanged)
                {
                    Error = null;
                }
            }

            OnPropertyChanged("Item[]");
        }
    }

    /// <summary>Returns the changed columns and their new values.</summary>
    public IReadOnlyDictionary<int, object?> Changes() =>
        Enumerable.Range(0, _current.Length).Where(i => _changed[i]).ToDictionary(i => i, i => _current[i]);

    /// <summary>
    /// Takes the values read back from the server as the stored values. With <paramref name="sent"/> (the values when the
    /// save started), a value the user changed during the save stays as a new change instead of being overwritten.
    /// </summary>
    public void AcceptChanges(object?[] stored, object?[]? sent = null)
    {
        if (stored is null || stored.Length != _current.Length)
        {
            throw new ArgumentException("The stored row must have the same columns.", nameof(stored));
        }

        _original = stored;
        for (var i = 0; i < stored.Length; i++)
        {
            var editedMeanwhile = sent is not null && !Equals(_current[i], sent[i]);
            _changed[i] = editedMeanwhile;
            if (!editedMeanwhile)
            {
                _current[i] = stored[i];
            }
        }

        Error = null;
        State = _changed.Any(c => c) ? RowState.Modified : RowState.Unchanged;
        OnPropertyChanged("Item[]");
    }

    /// <summary>Puts the stored values back (Undo changes).</summary>
    public void RejectChanges()
    {
        Array.Copy(_original, _current, _current.Length);
        Array.Clear(_changed, 0, _changed.Length);
        Error = null;
        if (_state != RowState.New)
        {
            State = RowState.Unchanged;
        }

        OnPropertyChanged("Item[]");
    }
}
