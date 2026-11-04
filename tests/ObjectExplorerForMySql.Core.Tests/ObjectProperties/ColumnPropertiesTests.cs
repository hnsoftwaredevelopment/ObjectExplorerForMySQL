using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.ObjectProperties;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.ObjectProperties;

public class ColumnPropertiesTests
{
    private static readonly ConnectionDefinition Connection = new() { DisplayName = "Local", Host = "db.local", Port = 3307, UserName = "app" };

    private static readonly TableInfo Table = new("shop", "orders", "InnoDB", null);

    private static ColumnProperties Price() => new(
        new ColumnInfo("price", "decimal(10,2) unsigned", false, false, false, string.Empty)
        {
            DataType = "decimal",
            Default = "0.00",
            NumericPrecision = 10,
            NumericScale = 2,
            Comment = "Unit price",
            OrdinalPosition = 4,
        },
        Table,
        Connection);

    private static PropertyDescriptor Property(object component, string displayName) =>
        TypeDescriptor.GetProperties(component).Cast<PropertyDescriptor>().Single(p => p.DisplayName == displayName);

    private static object? Value(object component, string displayName) => Property(component, displayName).GetValue(component);

    [Fact]
    public void Header_ShowsColumnNameAndType()
    {
        var properties = Price();

        Assert.Equal("price", TypeDescriptor.GetComponentName(properties));
        Assert.Equal("Column", TypeDescriptor.GetClassName(properties));
    }

    [Fact]
    public void Properties_AreGroupedInTheFourCategories()
    {
        var byCategory = TypeDescriptor.GetProperties(Price()).Cast<PropertyDescriptor>()
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Select(p => p.DisplayName).OrderBy(n => n, StringComparer.Ordinal).ToList());

        Assert.Equal(["Connection", "Data Type", "Description", "General"], byCategory.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(["Auto Increment", "Default", "Foreign Key", "Generation Expression", "Is Generated", "Nullable", "Primary Key"], byCategory["General"]);
        Assert.Equal(["Character Set", "Collation", "Data Type", "Length", "Precision", "Scale", "Unsigned"], byCategory["Data Type"]);
        Assert.Equal(["Comment", "Database", "Name", "Ordinal Position", "Parent", "Parent Type"], byCategory["Description"]);
        Assert.Equal(["Connection Name", "Port", "Server", "User"], byCategory["Connection"]);
    }

    [Fact]
    public void Properties_AreAllReadOnly()
    {
        Assert.All(TypeDescriptor.GetProperties(Price()).Cast<PropertyDescriptor>(), p => Assert.True(p.IsReadOnly, p.Name));
    }

    [Fact]
    public void NumericColumn_MapsValues()
    {
        var properties = Price();

        Assert.Equal("decimal(10,2) unsigned", Value(properties, "Data Type"));
        Assert.Equal(10L, Value(properties, "Precision"));
        Assert.Equal(2L, Value(properties, "Scale"));
        Assert.Null(Value(properties, "Length"));
        Assert.Equal(true, Value(properties, "Unsigned"));
        Assert.Equal(false, Value(properties, "Nullable"));
        Assert.Equal("0.00", Value(properties, "Default"));
        Assert.Equal(string.Empty, Value(properties, "Character Set"));
        Assert.Equal("Unit price", Value(properties, "Comment"));
        Assert.Equal(4, Value(properties, "Ordinal Position"));
        Assert.Equal("orders", Value(properties, "Parent"));
        Assert.Equal("Table", Value(properties, "Parent Type"));
        Assert.Equal("shop", Value(properties, "Database"));
    }

    [Fact]
    public void ConnectionCategory_ShowsServerOfTheConnection()
    {
        var properties = Price();

        Assert.Equal("Local", Value(properties, "Connection Name"));
        Assert.Equal("db.local", Value(properties, "Server"));
        Assert.Equal(3307, Value(properties, "Port"));
        Assert.Equal("app", Value(properties, "User"));
    }

    [Fact]
    public void CharacterAndGeneratedColumns_MapValues()
    {
        var view = new TableInfo("shop", "customer_list", null, null, isView: true);
        var name = new ColumnProperties(
            new ColumnInfo("full_name", "varchar(91)", true, false, false, "VIRTUAL GENERATED")
            {
                CharacterMaximumLength = 91,
                CharacterSet = "utf8mb4",
                Collation = "utf8mb4_0900_ai_ci",
                GenerationExpression = "concat(`first_name`,_utf8mb4' ',`last_name`)",
            },
            view,
            null);

        Assert.Equal(91L, Value(name, "Length"));
        Assert.Equal("utf8mb4", Value(name, "Character Set"));
        Assert.Equal("utf8mb4_0900_ai_ci", Value(name, "Collation"));
        Assert.Equal(true, Value(name, "Is Generated"));
        Assert.Equal("concat(`first_name`,_utf8mb4' ',`last_name`)", Value(name, "Generation Expression"));
        Assert.Equal("View", Value(name, "Parent Type"));
        Assert.Equal(string.Empty, Value(name, "Server"));
        Assert.Null(Value(name, "Port"));
    }

    [Fact]
    public void AutoIncrementColumn_IsDetectedFromExtra()
    {
        var id = new ColumnProperties(new ColumnInfo("id", "int unsigned", false, true, false, "auto_increment"), Table, Connection);

        Assert.Equal(true, Value(id, "Auto Increment"));
        Assert.Equal(true, Value(id, "Primary Key"));
        Assert.Equal(false, Value(id, "Is Generated"));
    }

    [Fact]
    public void EveryResourceKeyOfEveryPropertyObjectExists()
    {
        var types = typeof(ObjectPropertiesBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ObjectPropertiesBase).IsAssignableFrom(t))
            .ToList();
        Assert.NotEmpty(types);

        var missing = new List<string>();
        foreach (var property in types.SelectMany(t => t.GetProperties()))
        {
            var category = property.GetCustomAttribute<LocalizedCategoryAttribute>();
            var displayName = property.GetCustomAttribute<LocalizedDisplayNameAttribute>();
            if (category is null || displayName is null)
            {
                missing.Add($"{property.DeclaringType!.Name}.{property.Name}: category or display name attribute missing");
                continue;
            }

            foreach (var key in new[] { category.ResourceKey, displayName.ResourceKey })
            {
                if (!PropertyTexts.Exists(key))
                {
                    missing.Add($"{property.DeclaringType!.Name}.{property.Name}: resource '{key}' missing");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task ColumnNode_CreatesPropertiesWithItsServerConnection()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "orders", "InnoDB", null)]),
            Columns = (_, _) => Task.FromResult<IReadOnlyList<ColumnInfo>>([new ColumnInfo("id", "int", false, true, false, string.Empty)]),
        };
        var definition = TestExplorer.Definition("Prod");
        var server = new ServerNodeViewModel(TestExplorer.Context(metadata), definition);
        TreeNodeViewModel node = server;
        foreach (var key in new[] { "Databases", "shop", "Tables", "orders", "Columns" })
        {
            node.IsExpanded = true;
            await node.WaitForLoadAsync();
            node = node.Children.Single(c => c.Key == key);
        }

        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        var column = Assert.IsType<ColumnNodeViewModel>(Assert.Single(node.Children));

        var properties = Assert.IsType<ColumnProperties>(column.CreateProperties());
        Assert.Same(server, column.OwningServer);
        Assert.Equal("Prod", properties.ConnectionName);
        Assert.Equal("orders", properties.Parent);
        Assert.Null(server.Children[0].CreateProperties());
    }
}
