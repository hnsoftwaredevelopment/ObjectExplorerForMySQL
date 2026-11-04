using System;
using ObjectExplorerForMySql.Core.DragDrop;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>Kind of object Delete can drop (US-0901).</summary>
public enum DropObjectKind
{
    /// <summary>Database: <c>DROP DATABASE</c>.</summary>
    Database,

    /// <summary>Base table: <c>DROP TABLE</c>.</summary>
    Table,

    /// <summary>View: <c>DROP VIEW</c>.</summary>
    View,

    /// <summary>Stored procedure: <c>DROP PROCEDURE</c>.</summary>
    Procedure,

    /// <summary>Stored function: <c>DROP FUNCTION</c>.</summary>
    Function,

    /// <summary>Trigger: <c>DROP TRIGGER</c>.</summary>
    Trigger,

    /// <summary>Event: <c>DROP EVENT</c>.</summary>
    Event,

    /// <summary>Index: <c>ALTER TABLE ... DROP INDEX</c>, or <c>DROP PRIMARY KEY</c> for the index PRIMARY.</summary>
    Index,

    /// <summary>Column of a table: <c>ALTER TABLE ... DROP COLUMN</c>.</summary>
    Column,

    /// <summary>User account: <c>DROP USER</c> (US-1003).</summary>
    User,

    /// <summary>Role: <c>DROP ROLE</c> (US-1003).</summary>
    Role,
}

/// <summary>An object to delete and the exact statement that deletes it (US-0901).</summary>
public sealed class DropTarget
{
    /// <summary>Initializes a new instance of the <see cref="DropTarget"/> class.</summary>
    /// <param name="kind">Object kind.</param>
    /// <param name="database">Database (for a database: its own name).</param>
    /// <param name="name">Object name.</param>
    /// <param name="table">Table of an index or column; required for those kinds.</param>
    public DropTarget(DropObjectKind kind, string database, string name, string? table = null)
    {
        if (string.IsNullOrEmpty(database))
        {
            throw new ArgumentException("The database is required.", nameof(database));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The name is required.", nameof(name));
        }

        if (kind is DropObjectKind.Index or DropObjectKind.Column && string.IsNullOrEmpty(table))
        {
            throw new ArgumentException($"A {kind} needs its table.", nameof(table));
        }

        Kind = kind;
        Database = database;
        Name = name;
        Table = kind is DropObjectKind.Index or DropObjectKind.Column ? table : null;
        Sql = Build();
    }

    private DropTarget(DropObjectKind kind, Security.AccountInfo account)
    {
        Kind = kind;
        Database = string.Empty;
        Name = account.Label;
        Sql = $"DROP {(kind == DropObjectKind.Role ? "ROLE" : "USER")} {account.Sql};";
    }

    /// <summary>Returns the target that drops a user or role (US-1003): <c>DROP USER `app`@`%`;</c>.</summary>
    public static DropTarget ForAccount(Security.AccountInfo account) =>
        new(account?.IsRole == true ? DropObjectKind.Role : DropObjectKind.User, account ?? throw new ArgumentNullException(nameof(account)));

    /// <summary>Gets the object kind.</summary>
    public DropObjectKind Kind { get; }

    /// <summary>Gets the database.</summary>
    public string Database { get; }

    /// <summary>Gets the object name.</summary>
    public string Name { get; }

    /// <summary>Gets the table of an index or column, otherwise null.</summary>
    public string? Table { get; }

    /// <summary>Gets the statement that runs; every name is quoted, so the text is exactly what the server receives.</summary>
    public string Sql { get; }

    /// <summary>Gets the full name as shown to the user: <c>shop</c>, <c>shop.orders</c> or <c>shop.orders.ix_date</c>.</summary>
    public string DisplayName => Kind switch
    {
        DropObjectKind.Database or DropObjectKind.User or DropObjectKind.Role => Name,
        DropObjectKind.Index or DropObjectKind.Column => $"{Database}.{Table}.{Name}",
        _ => $"{Database}.{Name}",
    };

    /// <summary>Gets a value indicating whether the user must type the name to confirm (a database).</summary>
    public bool RequiresNameConfirmation => Kind == DropObjectKind.Database;

    /// <summary>
    /// Returns true when <paramref name="typed"/> confirms the delete: always for objects other than a database,
    /// for a database only the exact name (case-sensitive, no extra spaces).
    /// </summary>
    public bool IsConfirmedBy(string? typed) => !RequiresNameConfirmation || string.Equals(typed, Name, StringComparison.Ordinal);

    private string Build()
    {
        static string Q(string name) => MySqlIdentifier.Quote(name);
        return Kind switch
        {
            DropObjectKind.Database => $"DROP DATABASE {Q(Name)};",
            DropObjectKind.Index when string.Equals(Name, "PRIMARY", StringComparison.OrdinalIgnoreCase) =>
                $"ALTER TABLE {Q(Database)}.{Q(Table!)} DROP PRIMARY KEY;",
            DropObjectKind.Index => $"ALTER TABLE {Q(Database)}.{Q(Table!)} DROP INDEX {Q(Name)};",
            DropObjectKind.Column => $"ALTER TABLE {Q(Database)}.{Q(Table!)} DROP COLUMN {Q(Name)};",
            _ => $"DROP {Kind.ToString().ToUpperInvariant()} {Q(Database)}.{Q(Name)};",
        };
    }
}
