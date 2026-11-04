using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.ObjectProperties;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.ObjectProperties;

public class ObjectPropertiesTests
{
    private static object? Value(object component, string displayName) =>
        TypeDescriptor.GetProperties(component).Cast<PropertyDescriptor>().Single(p => p.DisplayName == displayName).GetValue(component);

    private static IEnumerable<string> Categories(object component) =>
        TypeDescriptor.GetProperties(component).Cast<PropertyDescriptor>().Select(p => p.Category).Distinct().OrderBy(c => c, StringComparer.Ordinal);

    private static async Task<T> ExpandAsync<T>(T node)
        where T : TreeNodeViewModel
    {
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        return node;
    }

    private static T Child<T>(TreeNodeViewModel parent, string key)
        where T : TreeNodeViewModel =>
        Assert.IsType<T>(parent.Children.Single(c => c.Key == key));

    private static FakeMetadataProvider Metadata(string version = "8.0.36")
    {
        var metadata = CreateMetadata();
        metadata.ConnectResults.Enqueue(_ => Task.FromResult(new ServerInfo(version, "app@%") { CharacterSet = "utf8mb4", Collation = "utf8mb4_0900_ai_ci", TimeZone = "SYSTEM (CET)", SslCipher = "TLS_AES_256_GCM_SHA384" }));
        return metadata;
    }

    private static FakeMetadataProvider CreateMetadata() => new()
    {
        Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", "utf8mb4", "utf8mb4_0900_ai_ci")]),
        Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>(
        [
            new TableInfo(db, "orders", "InnoDB", "All orders")
            {
                RowFormat = "Dynamic", EstimatedRows = 1200, DataLength = 1572864, IndexLength = 16384, AutoIncrement = 1201,
                Created = new DateTime(2026, 1, 2, 3, 4, 5), Collation = "utf8mb4_0900_ai_ci",
            },
            new TableInfo(db, "customers", "InnoDB", null),
        ]),
        Views =
        [
            new TableInfo("shop", "order_list", null, "VIEW", isView: true) { IsUpdatable = false, SecurityType = "DEFINER", Definer = "root@localhost", CheckOption = "NONE" },
        ],
        Keys =
        [
            new KeyInfo("PRIMARY", KeyType.Primary, ["id"]),
            new KeyInfo("fk_order_customer", KeyType.Foreign, ["customer_id"], "crm", "customers", ["id"]) { UpdateRule = "CASCADE", DeleteRule = "RESTRICT" },
        ],
        CheckConstraints = [new CheckConstraintInfo("chk_total", "(`total` >= 0)", false)],
        Indexes = [new IndexInfo("ix_date", false, "BTREE", ["order_date", "customer_id"]) { IsVisible = false, Comment = "Reports" }],
        Triggers = [new TriggerInfo("trg_orders_bi", "BEFORE", "INSERT") { Definer = "root@localhost", ActionOrder = 1, Created = new DateTime(2026, 2, 1) }],
        Routines =
        [
            new RoutineInfo("shop", "close_order", RoutineType.Procedure, null, "Closes an order") { Definer = "root@localhost", SecurityType = "INVOKER", SqlDataAccess = "MODIFIES SQL DATA" },
            new RoutineInfo("shop", "order_total", RoutineType.Function, "decimal(10,2)", null) { IsDeterministic = true, SqlDataAccess = "READS SQL DATA" },
        ],
        Parameters = [new ParameterInfo("p_order_id", ParameterMode.In, "int", 1), new ParameterInfo("p_result", ParameterMode.Out, "varchar(20)", 2)],
        Events = [new EventInfo("purge", "ENABLED", null, "1", "DAY", new DateTime(2026, 1, 1), null) { OnCompletion = "PRESERVE", Comment = "Nightly", LastExecuted = new DateTime(2026, 10, 27, 2, 0, 0) }],
    };

    private static async Task<(ServerNodeViewModel Server, DatabaseNodeViewModel Shop, FakeMetadataProvider Metadata)> OpenAsync(string version = "8.0.36")
    {
        var metadata = Metadata(version);
        var server = await ExpandAsync(new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition("Prod")));
        var databases = await ExpandAsync(Child<FolderNodeViewModel>(server, "Databases"));
        var shop = await ExpandAsync(Child<DatabaseNodeViewModel>(databases, "shop"));
        return (server, shop, metadata);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(512L, "512 bytes")]
    [InlineData(16384L, "16 KB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(5368709120L, "5 GB")]
    public void Bytes_FormatsSizes(long? bytes, string expected) => Assert.Equal(expected, PropertyFormat.Bytes(bytes));

    [Theory]
    [InlineData("SYSTEM", "CET", "SYSTEM (CET)")]
    [InlineData("system", "", "system")]
    [InlineData("+01:00", "CET", "+01:00")]
    [InlineData(null, "CET", null)]
    public void FormatTimeZone_ExplainsSystem(string? timeZone, string? systemTimeZone, string? expected) =>
        Assert.Equal(expected, ServerInfo.FormatTimeZone(timeZone, systemTimeZone));

    [Fact]
    public async Task Server_ShowsConfiguredValuesBeforeAndLiveValuesAfterConnecting()
    {
        var metadata = Metadata();
        var server = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition("Prod"));

        var before = Assert.IsType<ServerProperties>(server.CreateProperties());
        Assert.Equal("Prod", TypeDescriptor.GetComponentName(before));
        Assert.Equal("MySQL Server", TypeDescriptor.GetClassName(before));
        Assert.Equal(string.Empty, Value(before, "Version"));
        Assert.Equal("Not connected", Value(before, "State"));
        Assert.Null(Value(before, "SSL In Use"));
        Assert.Equal("db.local", Value(before, "Server"));
        Assert.Equal("Preferred", Value(before, "SSL Mode"));

        await ExpandAsync(server);

        Assert.Equal("8.0.36", Value(before, "Version"));
        Assert.Equal("Connected", Value(before, "State"));
        Assert.Equal("utf8mb4", Value(before, "Character Set"));
        Assert.Equal("SYSTEM (CET)", Value(before, "Time Zone"));
        Assert.Equal(true, Value(before, "SSL In Use"));
        Assert.Equal("TLS_AES_256_GCM_SHA384", Value(before, "SSL Cipher"));
        Assert.Equal("app@%", Value(before, "Authenticated As"));
        Assert.Equal(["Connection", "General"], Categories(before));
    }

    [Fact]
    public async Task Server_DisconnectClearsLiveValues()
    {
        var (server, _, _) = await OpenAsync();

        server.Disconnect();

        Assert.Null(server.Info);
        Assert.Equal(string.Empty, Value(server.CreateProperties()!, "Version"));
    }

    [Fact]
    public async Task Database_ShowsTableCountOnlyWhenTablesAreLoaded()
    {
        var (_, shop, _) = await OpenAsync();

        var before = Assert.IsType<DatabaseProperties>(shop.CreateProperties());
        Assert.Null(Value(before, "Tables"));
        Assert.Equal("utf8mb4", Value(before, "Character Set"));
        Assert.Equal(false, Value(before, "System Database"));
        Assert.Equal("Database", TypeDescriptor.GetClassName(before));

        await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));

        Assert.Equal(2, Value(shop.CreateProperties()!, "Tables"));
    }

    [Fact]
    public async Task Table_MapsStorageAndDescription()
    {
        var (_, shop, _) = await OpenAsync();
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));

        var properties = Assert.IsType<TableProperties>(Child<TableNodeViewModel>(tables, "orders").CreateProperties());

        Assert.Equal("InnoDB", Value(properties, "Engine"));
        Assert.Equal("Dynamic", Value(properties, "Row Format"));
        Assert.Equal(1201L, Value(properties, "Next Auto Increment"));
        Assert.Equal(1200L, Value(properties, "Estimated Rows"));
        Assert.Equal("1.5 MB", Value(properties, "Data Size"));
        Assert.Equal("16 KB", Value(properties, "Index Size"));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), Value(properties, "Created"));
        Assert.Null(Value(properties, "Last Data Change"));
        Assert.Equal("All orders", Value(properties, "Comment"));
        Assert.Equal("Prod", Value(properties, "Connection Name"));
        Assert.Equal(["Connection", "Description", "General", "Storage"], Categories(properties));
    }

    [Fact]
    public async Task View_MapsViewDefinition()
    {
        var (_, shop, _) = await OpenAsync();
        var views = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Views"));

        var properties = Assert.IsType<ViewProperties>(Child<ViewNodeViewModel>(views, "order_list").CreateProperties());

        Assert.Equal(false, Value(properties, "Is Updatable"));
        Assert.Equal("DEFINER", Value(properties, "Security Type"));
        Assert.Equal("root@localhost", Value(properties, "Definer"));
        Assert.Equal("NONE", Value(properties, "Check Option"));
        Assert.Equal("View", TypeDescriptor.GetClassName(properties));
    }

    [Fact]
    public void SystemView_HasEmptyViewDefinition()
    {
        var properties = new ViewProperties(new TableInfo("information_schema", "TABLES", null, null, isView: true), null);

        Assert.Null(Value(properties, "Is Updatable"));
        Assert.Equal(string.Empty, Value(properties, "Definer"));
    }

    [Fact]
    public async Task TableObjects_MapKeysConstraintsIndexesAndTriggers()
    {
        var (_, shop, metadata) = await OpenAsync();
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        var orders = await ExpandAsync(Child<TableNodeViewModel>(tables, "orders"));
        var keys = await ExpandAsync(Child<FolderNodeViewModel>(orders, "Keys"));
        var constraints = await ExpandAsync(Child<FolderNodeViewModel>(orders, "Constraints"));
        var indexes = await ExpandAsync(Child<FolderNodeViewModel>(orders, "Indexes"));
        var triggers = await ExpandAsync(Child<FolderNodeViewModel>(orders, "Triggers"));

        var primary = Assert.IsType<KeyProperties>(keys.Children[0].CreateProperties());
        Assert.Equal("Primary Key", TypeDescriptor.GetClassName(primary));
        Assert.Equal("id", Value(primary, "Columns"));
        Assert.Equal(string.Empty, Value(primary, "On Delete"));

        var foreign = Assert.IsType<KeyProperties>(keys.Children[1].CreateProperties());
        Assert.Equal("Foreign Key", Value(foreign, "Key Type"));
        Assert.Equal("crm.customers", Value(foreign, "Referenced Table"));
        Assert.Equal("id", Value(foreign, "Referenced Columns"));
        Assert.Equal("CASCADE", Value(foreign, "On Update"));
        Assert.Equal("RESTRICT", Value(foreign, "On Delete"));
        Assert.Equal("orders", Value(foreign, "Table"));
        Assert.Equal("shop", Value(foreign, "Database"));

        var check = Assert.IsType<CheckConstraintProperties>(Assert.Single(constraints.Children).CreateProperties());
        Assert.Equal("(`total` >= 0)", Value(check, "Expression"));
        Assert.Equal(false, Value(check, "Enforced"));

        var index = Assert.IsType<IndexProperties>(Assert.Single(indexes.Children).CreateProperties());
        Assert.Equal("order_date, customer_id", Value(index, "Columns"));
        Assert.Equal(false, Value(index, "Visible"));
        Assert.Equal(false, Value(index, "Unique"));
        Assert.Equal("BTREE", Value(index, "Index Type"));
        Assert.Equal("Reports", Value(index, "Comment"));
        Assert.Equal(false, metadata.IndexesRequestedForMariaDb);

        var trigger = Assert.IsType<TriggerProperties>(Assert.Single(triggers.Children).CreateProperties());
        Assert.Equal("BEFORE", Value(trigger, "Timing"));
        Assert.Equal("INSERT", Value(trigger, "Event"));
        Assert.Equal(1, Value(trigger, "Action Order"));
        Assert.Equal("root@localhost", Value(trigger, "Definer"));
    }

    [Fact]
    public async Task Indexes_UseMariaDbQueryOnMariaDb()
    {
        var (_, shop, metadata) = await OpenAsync("10.11.6-MariaDB");
        var tables = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Tables"));
        var orders = await ExpandAsync(Child<TableNodeViewModel>(tables, "orders"));

        await ExpandAsync(Child<FolderNodeViewModel>(orders, "Indexes"));

        Assert.Equal(true, metadata.IndexesRequestedForMariaDb);
    }

    [Fact]
    public async Task Routines_ParametersAndEvents_Map()
    {
        var (_, shop, _) = await OpenAsync();
        var procedures = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Stored Procedures"));
        var functions = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Functions"));
        var events = await ExpandAsync(Child<FolderNodeViewModel>(shop, "Events"));

        var procedureNode = await ExpandAsync(Child<RoutineNodeViewModel>(procedures, "close_order"));
        var procedure = Assert.IsType<RoutineProperties>(procedureNode.CreateProperties());
        Assert.Equal("Stored Procedure", TypeDescriptor.GetClassName(procedure));
        Assert.Equal(string.Empty, Value(procedure, "Returns"));
        Assert.Equal("INVOKER", Value(procedure, "Security Type"));
        Assert.Equal("MODIFIES SQL DATA", Value(procedure, "SQL Data Access"));
        Assert.Equal("Closes an order", Value(procedure, "Comment"));

        var function = Assert.IsType<RoutineProperties>(Child<RoutineNodeViewModel>(functions, "order_total").CreateProperties());
        Assert.Equal("Function", TypeDescriptor.GetClassName(function));
        Assert.Equal("decimal(10,2)", Value(function, "Returns"));
        Assert.Equal(true, Value(function, "Deterministic"));

        var parameters = await ExpandAsync(Child<FolderNodeViewModel>(procedureNode, "Parameters"));
        var output = Assert.IsType<ParameterProperties>(parameters.Children[1].CreateProperties());
        Assert.Equal("OUT", Value(output, "Mode"));
        Assert.Equal("varchar(20)", Value(output, "Data Type"));
        Assert.Equal(2, Value(output, "Ordinal Position"));
        Assert.Equal("close_order", Value(output, "Routine"));
        Assert.Equal("shop", Value(output, "Database"));

        var purge = Assert.IsType<EventProperties>(Assert.Single(events.Children).CreateProperties());
        Assert.Equal("ENABLED", Value(purge, "Status"));
        Assert.Equal("EVERY 1 DAY STARTS '2026-01-01 00:00:00'", Value(purge, "Schedule"));
        Assert.Equal("PRESERVE", Value(purge, "On Completion"));
        Assert.Equal(new DateTime(2026, 10, 27, 2, 0, 0), Value(purge, "Last Executed"));
        Assert.Equal("shop", Value(purge, "Database"));
        Assert.Equal("Nightly", Value(purge, "Comment"));
    }

    [Fact]
    public async Task FoldersAndPlaceholders_HaveNoProperties()
    {
        var (server, shop, _) = await OpenAsync();

        Assert.Null(Child<FolderNodeViewModel>(server, "Databases").CreateProperties());
        Assert.Null(Child<FolderNodeViewModel>(shop, "Tables").CreateProperties());
    }

    [Fact]
    public void AllPropertyObjects_AreReadOnly_AndAllStateTextsExist()
    {
        var types = typeof(ObjectPropertiesBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ObjectPropertiesBase).IsAssignableFrom(t));
        foreach (var type in types)
        {
            Assert.All(type.GetProperties().Where(p => p.GetCustomAttributes(typeof(LocalizedDisplayNameAttribute), true).Length > 0), p => Assert.False(p.CanWrite, $"{type.Name}.{p.Name}"));
        }

        foreach (var state in Enum.GetValues(typeof(ConnectionState)))
        {
            Assert.True(PropertyTexts.Exists("State" + state), $"State{state}");
        }

        foreach (var key in new[] { "TypeServer", "TypeDatabase", "TypeTable", "TypeView", "TypeColumn", "TypeIndex", "TypePrimaryKey", "TypeUniqueKey", "TypeForeignKey", "TypeCheckConstraint", "TypeTrigger", "TypeStoredProcedure", "TypeFunction", "TypeParameter", "TypeEvent" })
        {
            Assert.True(PropertyTexts.Exists(key), key);
        }
    }

    [Fact]
    public void KeyRows_CarryRulesForForeignKeysOnly()
    {
        var keys = KeyInfo.FromRows(
        [
            new KeyInfo.Row("PRIMARY", "PRIMARY KEY", "id", 1, null, null, null, "X", "Y"),
            new KeyInfo.Row("fk_a", "FOREIGN KEY", "a_id", 1, "shop", "a", "id", "SET NULL", "CASCADE"),
        ]);

        Assert.Null(keys[0].UpdateRule);
        Assert.Equal("SET NULL", keys[1].UpdateRule);
        Assert.Equal("CASCADE", keys[1].DeleteRule);
    }

    [Fact]
    public void IndexRows_CarryVisibilityAndComment()
    {
        var indexes = IndexInfo.FromRows(
        [
            new IndexInfo.Row("ix_a", true, "a", 1, "BTREE", null, false, "note"),
            new IndexInfo.Row("ix_b", true, "b", 1, "BTREE", null, true, ""),
        ]);

        Assert.False(indexes[0].IsVisible);
        Assert.Equal("note", indexes[0].Comment);
        Assert.True(indexes[1].IsVisible);
        Assert.Null(indexes[1].Comment);
    }
}
