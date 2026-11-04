using System;
using ObjectExplorerForMySql.Core.DragDrop;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>Kind of object Rename can change (US-0902).</summary>
public enum RenameObjectKind
{
    /// <summary>Base table: <c>RENAME TABLE</c>.</summary>
    Table,

    /// <summary>View: <c>RENAME TABLE</c> (works for views in the same database).</summary>
    View,

    /// <summary>Column of a table: <c>ALTER TABLE ... RENAME COLUMN</c>.</summary>
    Column,
}

/// <summary>Why a typed name cannot be used.</summary>
public enum NameProblem
{
    /// <summary>The name can be used.</summary>
    None,

    /// <summary>The name is the current name: nothing to do.</summary>
    Unchanged,

    /// <summary>The name is empty.</summary>
    Empty,

    /// <summary>The name is longer than 64 characters.</summary>
    TooLong,

    /// <summary>The name ends with a space, which MySQL does not allow.</summary>
    EndsWithSpace,

    /// <summary>An object with this name already exists.</summary>
    Exists,
}

/// <summary>Rules MySQL has for the name of a database, table, view or column.</summary>
public static class NameRules
{
    /// <summary>Maximum length of a name in MySQL.</summary>
    public const int MaxLength = 64;

    /// <summary>Checks a typed name: empty (or only spaces), too long, or ending with a space. Leading spaces are allowed.</summary>
    public static NameProblem Check(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameProblem.Empty;
        }

        if (name!.Length > MaxLength)
        {
            return NameProblem.TooLong;
        }

        return name.EndsWith(" ", StringComparison.Ordinal) ? NameProblem.EndsWithSpace : NameProblem.None;
    }
}

/// <summary>An object to rename and the statement that renames it (US-0902).</summary>
public sealed class RenameTarget
{
    /// <summary>Maximum length of a table, view or column name in MySQL.</summary>
    public const int MaxNameLength = NameRules.MaxLength;

    /// <summary>Initializes a new instance of the <see cref="RenameTarget"/> class.</summary>
    /// <param name="kind">Object kind.</param>
    /// <param name="database">Database.</param>
    /// <param name="name">Current name.</param>
    /// <param name="table">Table of a column; required for a column.</param>
    public RenameTarget(RenameObjectKind kind, string database, string name, string? table = null)
    {
        if (string.IsNullOrEmpty(database))
        {
            throw new ArgumentException("The database is required.", nameof(database));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The name is required.", nameof(name));
        }

        if (kind == RenameObjectKind.Column && string.IsNullOrEmpty(table))
        {
            throw new ArgumentException("A column needs its table.", nameof(table));
        }

        Kind = kind;
        Database = database;
        Name = name;
        Table = kind == RenameObjectKind.Column ? table : null;
    }

    /// <summary>Gets the object kind.</summary>
    public RenameObjectKind Kind { get; }

    /// <summary>Gets the database.</summary>
    public string Database { get; }

    /// <summary>Gets the current name.</summary>
    public string Name { get; }

    /// <summary>Gets the table of a column, otherwise null.</summary>
    public string? Table { get; }

    /// <summary>Gets the full name as shown to the user: <c>shop.orders</c> or <c>shop.orders.note</c>.</summary>
    public string DisplayName => Kind == RenameObjectKind.Column ? $"{Database}.{Table}.{Name}" : $"{Database}.{Name}";

    /// <summary>Checks a typed name. Leading spaces are kept: MySQL allows them in a quoted name.</summary>
    public NameProblem Check(string? newName)
    {
        var problem = NameRules.Check(newName);
        return problem == NameProblem.None && string.Equals(newName, Name, StringComparison.Ordinal) ? NameProblem.Unchanged : problem;
    }

    /// <summary>Returns the statement that renames the object to <paramref name="newName"/>; every name is quoted.</summary>
    public string BuildSql(string newName)
    {
        var problem = Check(newName);
        if (problem != NameProblem.None)
        {
            throw new ArgumentException($"The name cannot be used: {problem}.", nameof(newName));
        }

        static string Q(string name) => MySqlIdentifier.Quote(name);
        return Kind == RenameObjectKind.Column
            ? $"ALTER TABLE {Q(Database)}.{Q(Table!)} RENAME COLUMN {Q(Name)} TO {Q(newName)};"
            : $"RENAME TABLE {Q(Database)}.{Q(Name)} TO {Q(Database)}.{Q(newName)};";
    }
}
