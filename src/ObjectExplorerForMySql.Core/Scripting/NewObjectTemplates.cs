using System;
using System.Text;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Scripting;

/// <summary>Kind of object a New ... template creates (US-0904).</summary>
public enum NewObjectKind
{
    /// <summary>CREATE TABLE, on the Tables folder.</summary>
    Table,

    /// <summary>CREATE VIEW, on the Views folder.</summary>
    View,

    /// <summary>CREATE PROCEDURE, on the Stored Procedures folder.</summary>
    Procedure,

    /// <summary>CREATE FUNCTION, on the Functions folder.</summary>
    Function,

    /// <summary>CREATE EVENT, on the Events folder.</summary>
    Event,

    /// <summary>ALTER TABLE ... ADD COLUMN, on the Columns folder of a table.</summary>
    Column,

    /// <summary>CREATE TRIGGER, on the Triggers folder of a table.</summary>
    Trigger,

    /// <summary>CREATE INDEX, on the Indexes folder of a table.</summary>
    Index,
}

/// <summary>What a New ... command creates and where: the kind, the database and, under a table, the table.</summary>
public sealed record NewObjectRequest(NewObjectKind Kind, string Database, string? Table = null)
{
    /// <summary>Gets the placeholder name of the new object: <c>new_table</c>, <c>ix_orders_column</c>, ...</summary>
    public string PlaceholderName => Kind switch
    {
        NewObjectKind.Trigger => $"{Table}_before_insert",
        NewObjectKind.Index => $"ix_{Table}_column_name",
        _ => "new_" + Kind.ToString().ToLowerInvariant(),
    };

    /// <summary>Gets the file name of the query document: <c>shop.new_table.sql</c> or <c>shop.orders.new_column.sql</c>.</summary>
    public string FileName =>
        new CodeObject(CodeObjectKind.Table, Table is null ? Database : $"{Database}.{Table}", PlaceholderName).FileName;
}

/// <summary>Templates of New Table, View, Procedure, Function, Event, Column, Trigger and Index (US-0904).</summary>
public static class NewObjectTemplates
{
    private const string Indent = "    ";

    /// <summary>
    /// Returns what New ... creates for <paramref name="node"/>, or null: only on the Tables, Views, Stored Procedures,
    /// Functions and Events folders of a user database, and on the Columns, Triggers and Indexes folders of a table.
    /// </summary>
    public static NewObjectRequest? TargetOf(TreeNodeViewModel? node)
    {
        if (node is not FolderNodeViewModel { NewObjectKind: { } kind } folder)
        {
            return null;
        }

        var request = folder.Parent switch
        {
            DatabaseNodeViewModel database => new NewObjectRequest(kind, database.Database.Name),
            TableNodeViewModel table => new NewObjectRequest(kind, table.Table.Database, table.Table.Name),
            _ => null,
        };
        return request is null || SystemDatabases.IsSystem(request.Database) ? null : request;
    }

    /// <summary>Returns the text of the query document: a comment with what to do, then the statement.</summary>
    public static string Generate(NewObjectRequest request, string? connectionLabel, DropFormatOptions? options = null)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var o = options ?? DropFormatOptions.Default;
        string Q(string name) => o.QuoteForSql(name);
        var nl = Environment.NewLine;
        var name = Q(request.Database) + "." + Q(request.PlaceholderName);
        var table = request.Table is null ? string.Empty : Q(request.Database) + "." + Q(request.Table);

        var text = new StringBuilder();
        text.Append("-- New ").Append(request.Kind.ToString().ToLowerInvariant()).Append(" in ")
            .Append(request.Table is null ? request.Database : $"{request.Database}.{request.Table}")
            .Append(string.IsNullOrEmpty(connectionLabel) ? string.Empty : " on " + connectionLabel).Append(nl);
        text.Append("-- Replace the example names and columns, run the script (Ctrl+Shift+E), then refresh the folder (F5).").Append(nl);
        if (request.Kind == NewObjectKind.Event)
        {
            text.Append("-- Events only run while the event scheduler is on: SET GLOBAL event_scheduler = ON;").Append(nl);
        }

        text.Append(nl);
        text.Append(request.Kind switch
        {
            NewObjectKind.Table =>
                $"CREATE TABLE {name} ({nl}" +
                $"{Indent}{Q("id")} INT UNSIGNED NOT NULL AUTO_INCREMENT,{nl}" +
                $"{Indent}{Q("name")} VARCHAR(100) NOT NULL,{nl}" +
                $"{Indent}{Q("created_at")} DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,{nl}" +
                $"{Indent}PRIMARY KEY ({Q("id")}){nl}" +
                $");{nl}",
            NewObjectKind.View =>
                $"CREATE VIEW {name} AS{nl}" +
                $"SELECT{nl}" +
                $"{Indent}t.{Q("id")}{nl}" +
                $"FROM {Q(request.Database)}.{Q("table_name")} AS t;{nl}",
            NewObjectKind.Procedure => Delimited(
                $"CREATE PROCEDURE {name}(IN p_id INT){nl}" +
                $"BEGIN{nl}" +
                $"{Indent}SELECT p_id;{nl}" +
                "END"),
            NewObjectKind.Function => Delimited(
                $"CREATE FUNCTION {name}(p_value INT){nl}" +
                $"RETURNS INT{nl}" +
                $"DETERMINISTIC{nl}" +
                $"BEGIN{nl}" +
                $"{Indent}RETURN p_value * 2;{nl}" +
                "END"),
            NewObjectKind.Event => Delimited(
                $"CREATE EVENT {name}{nl}" +
                $"ON SCHEDULE EVERY 1 DAY{nl}" +
                $"STARTS CURRENT_TIMESTAMP + INTERVAL 1 DAY{nl}" +
                $"DO{nl}" +
                $"BEGIN{nl}" +
                $"{Indent}-- The statements to run, for example:{nl}" +
                $"{Indent}-- DELETE FROM {Q(request.Database)}.{Q("table_name")} WHERE {Q("created_at")} < NOW() - INTERVAL 30 DAY;{nl}" +
                "END"),
            NewObjectKind.Column =>
                $"ALTER TABLE {table}{nl}" +
                $"{Indent}ADD COLUMN {Q(request.PlaceholderName)} VARCHAR(100) NULL;{nl}",
            NewObjectKind.Trigger => Delimited(
                $"CREATE TRIGGER {name}{nl}" +
                $"BEFORE INSERT ON {table}{nl}" +
                $"FOR EACH ROW{nl}" +
                $"BEGIN{nl}" +
                $"{Indent}-- NEW is the row being inserted, for example:{nl}" +
                $"{Indent}-- SET NEW.{Q("created_at")} = NOW();{nl}" +
                "END"),
            NewObjectKind.Index =>
                $"CREATE INDEX {Q(request.PlaceholderName)}{nl}" +
                $"{Indent}ON {table} ({Q("column_name")});{nl}",
            _ => throw new InvalidOperationException($"Unknown kind {request.Kind}."),
        });
        return text.ToString();
    }

    // Routines, triggers and events contain semicolons, so they run between DELIMITER $$ and DELIMITER ;, like View Code.
    private static string Delimited(string statement)
    {
        var nl = Environment.NewLine;
        return $"DELIMITER {CodeDocument.Delimiter}{nl}{nl}{statement}{CodeDocument.Delimiter}{nl}{nl}DELIMITER ;{nl}";
    }
}
