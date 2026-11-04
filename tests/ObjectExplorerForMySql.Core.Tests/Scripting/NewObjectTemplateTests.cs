using System;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Tests.Management;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Scripting;

public class NewObjectTemplateTests
{
    private static readonly string NL = Environment.NewLine;

    private static readonly DropFormatOptions Always = DropFormatOptions.Default with { SqlQuoting = QuoteMode.Always };

    [Fact]
    public async Task Targets_TheFoldersOfAUserDatabaseAndATable()
    {
        var tree = await TestTree.OpenAsync();

        Assert.Equal(new NewObjectRequest(NewObjectKind.Table, "shop"), NewObjectTemplates.TargetOf(tree.Folder(tree.Database, "Tables")));
        Assert.Equal(new NewObjectRequest(NewObjectKind.View, "shop"), NewObjectTemplates.TargetOf(tree.Folder(tree.Database, "Views")));
        Assert.Equal(NewObjectKind.Procedure, NewObjectTemplates.TargetOf(tree.Folder(tree.Database, "Stored Procedures"))?.Kind);
        Assert.Equal(NewObjectKind.Function, NewObjectTemplates.TargetOf(tree.Folder(tree.Database, "Functions"))?.Kind);
        Assert.Equal(NewObjectKind.Event, NewObjectTemplates.TargetOf(tree.Folder(tree.Database, "Events"))?.Kind);
        Assert.Equal(new NewObjectRequest(NewObjectKind.Column, "shop", "orders"), NewObjectTemplates.TargetOf(tree.Folder(tree.Table, "Columns")));
        Assert.Equal(new NewObjectRequest(NewObjectKind.Trigger, "shop", "orders"), NewObjectTemplates.TargetOf(tree.Folder(tree.Table, "Triggers")));
        Assert.Equal(new NewObjectRequest(NewObjectKind.Index, "shop", "orders"), NewObjectTemplates.TargetOf(tree.Folder(tree.Table, "Indexes")));
    }

    [Fact]
    public async Task NoTarget_ElsewhereOrInASystemDatabase()
    {
        var tree = await TestTree.OpenAsync();
        var view = await TestTree.ExpandAsync((ViewNodeViewModel)(await TestTree.ExpandAsync(tree.Folder(tree.Database, "Views"))).Children[0]);

        Assert.Null(NewObjectTemplates.TargetOf(tree.Folder(tree.Table, "Keys")));
        Assert.Null(NewObjectTemplates.TargetOf(view.Children[0]));
        Assert.Null(NewObjectTemplates.TargetOf(tree.Databases));
        Assert.Null(NewObjectTemplates.TargetOf(tree.Table));
        Assert.Null(NewObjectTemplates.TargetOf(tree.Database));
        Assert.Null(NewObjectTemplates.TargetOf(null));

        var system = await TestTree.OpenAsync(databaseName: "mysql");
        Assert.Null(NewObjectTemplates.TargetOf(system.Folder(system.Database, "Tables")));
        Assert.Null(NewObjectTemplates.TargetOf(system.Folder(system.Table, "Columns")));
    }

    [Theory]
    [InlineData(NewObjectKind.Table, null, "new_table", "shop.new_table.sql")]
    [InlineData(NewObjectKind.Procedure, null, "new_procedure", "shop.new_procedure.sql")]
    [InlineData(NewObjectKind.Column, "orders", "new_column", "shop.orders.new_column.sql")]
    [InlineData(NewObjectKind.Trigger, "orders", "orders_before_insert", "shop.orders.orders_before_insert.sql")]
    [InlineData(NewObjectKind.Index, "orders", "ix_orders_column_name", "shop.orders.ix_orders_column_name.sql")]
    public void PlaceholderAndFileName(NewObjectKind kind, string? table, string placeholder, string fileName)
    {
        var request = new NewObjectRequest(kind, "shop", table);

        Assert.Equal(placeholder, request.PlaceholderName);
        Assert.Equal(fileName, request.FileName);
    }

    [Fact]
    public void Table_Template()
    {
        var text = NewObjectTemplates.Generate(new NewObjectRequest(NewObjectKind.Table, "shop"), "Local");

        Assert.Equal(
            "-- New table in shop on Local" + NL +
            "-- Replace the example names and columns, run the script (Ctrl+Shift+E), then refresh the folder (F5)." + NL +
            NL +
            "CREATE TABLE shop.new_table (" + NL +
            "    id INT UNSIGNED NOT NULL AUTO_INCREMENT," + NL +
            "    name VARCHAR(100) NOT NULL," + NL +
            "    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP," + NL +
            "    PRIMARY KEY (id)" + NL +
            ");" + NL,
            text);
    }

    [Fact]
    public void Quoting_FollowsTheSetting()
    {
        var text = NewObjectTemplates.Generate(new NewObjectRequest(NewObjectKind.Column, "my shop", "orders"), null, Always);

        Assert.Contains("ALTER TABLE `my shop`.`orders`" + NL + "    ADD COLUMN `new_column` VARCHAR(100) NULL;", text);
        Assert.StartsWith("-- New column in my shop.orders" + NL, text);
    }

    [Theory]
    [InlineData(NewObjectKind.Procedure, "CREATE PROCEDURE shop.new_procedure(IN p_id INT)")]
    [InlineData(NewObjectKind.Function, "CREATE FUNCTION shop.new_function(p_value INT)")]
    [InlineData(NewObjectKind.Event, "CREATE EVENT shop.new_event")]
    [InlineData(NewObjectKind.Trigger, "CREATE TRIGGER shop.orders_before_insert")]
    public void Routines_TriggersAndEvents_UseDelimiter(NewObjectKind kind, string start)
    {
        var text = NewObjectTemplates.Generate(new NewObjectRequest(kind, "shop", kind == NewObjectKind.Trigger ? "orders" : null), "Local");

        Assert.Contains("DELIMITER $$" + NL + NL + start + NL, text);
        Assert.EndsWith("END$$" + NL + NL + "DELIMITER ;" + NL, text);
    }

    [Theory]
    [InlineData(NewObjectKind.Table, 1)]
    [InlineData(NewObjectKind.View, 1)]
    [InlineData(NewObjectKind.Procedure, 1)]
    [InlineData(NewObjectKind.Function, 1)]
    [InlineData(NewObjectKind.Event, 1)]
    [InlineData(NewObjectKind.Column, 1)]
    [InlineData(NewObjectKind.Trigger, 1)]
    [InlineData(NewObjectKind.Index, 1)]
    public void EveryTemplate_IsOneStatement_ForTheQueryEditor(NewObjectKind kind, int statements)
    {
        var text = NewObjectTemplates.Generate(new NewObjectRequest(kind, "shop", "orders"), "Local");

        Assert.Equal(statements, SqlScriptSplitter.Split(text).Count);
    }

    [Fact]
    public void Event_MentionsTheEventScheduler()
    {
        Assert.Contains("event_scheduler", NewObjectTemplates.Generate(new NewObjectRequest(NewObjectKind.Event, "shop"), null));
    }

    [Fact]
    public void Trigger_AndIndex_NameTheTable()
    {
        Assert.Contains("BEFORE INSERT ON shop.orders", NewObjectTemplates.Generate(new NewObjectRequest(NewObjectKind.Trigger, "shop", "orders"), null));
        Assert.Contains("    ON shop.orders (column_name);", NewObjectTemplates.Generate(new NewObjectRequest(NewObjectKind.Index, "shop", "orders"), null));
    }
}
