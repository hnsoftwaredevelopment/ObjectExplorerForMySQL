using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.DragDrop;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.DragDrop;

public class ConnectionStringTextTests
{
    private static ConnectionDefinition Definition() =>
        new() { DisplayName = "Local", Host = "db.local", Port = 3306, UserName = "app", Password = "secret", DefaultDatabase = "shop" };

    [Fact]
    public void Build_WithPlaceholder_NeverThePassword()
    {
        var text = ConnectionStringText.Build(Definition());

        Assert.Equal("Server=db.local;Port=3306;Database=shop;User ID=app;Password=<password>;", text);
        Assert.DoesNotContain("secret", text);
    }

    [Fact]
    public void Build_OtherDatabase_OrNone()
    {
        Assert.Equal("Server=db.local;Port=3306;Database=sakila;User ID=app;Password=<password>;", ConnectionStringText.Build(Definition(), "sakila"));

        var noDefault = Definition();
        noDefault.DefaultDatabase = null;
        Assert.Equal("Server=db.local;Port=3306;User ID=app;Password=<password>;", ConnectionStringText.Build(noDefault));
    }

    [Fact]
    public void Build_SslModeAndPublicKeyRetrieval_OnlyWhenNotDefault()
    {
        var definition = Definition();
        definition.SslMode = SslMode.Required;
        definition.AllowPublicKeyRetrieval = true;
        definition.Port = 3307;

        Assert.Equal(
            "Server=db.local;Port=3307;Database=shop;User ID=app;Password=<password>;SslMode=Required;AllowPublicKeyRetrieval=true;",
            ConnectionStringText.Build(definition));
    }

    [Fact]
    public void Build_QuotesValuesWithSpecialCharacters()
    {
        var definition = Definition();
        definition.UserName = "o;\"neil";

        Assert.Contains("User ID=\"o;\"\"neil\";", ConnectionStringText.Build(definition));
    }

    [Theory]
    [InlineData(@"C:\src\appsettings.json", ConnectionStringTarget.Json)]
    [InlineData(@"C:\src\appsettings.Development.JSON", ConnectionStringTarget.Json)]
    [InlineData(@"C:\src\Program.cs", ConnectionStringTarget.CSharp)]
    [InlineData(@"C:\src\Module1.vb", ConnectionStringTarget.VisualBasic)]
    [InlineData(@"C:\src\web.config", ConnectionStringTarget.Config)]
    [InlineData(@"C:\src\query.sql", ConnectionStringTarget.Plain)]
    [InlineData(null, ConnectionStringTarget.Plain)]
    public void TargetFor_TheFileType(string? path, ConnectionStringTarget expected)
    {
        Assert.Equal(expected, ConnectionStringText.TargetFor(path));
    }

    [Theory]
    [InlineData(ConnectionStringTarget.Plain, "Server=a;User ID=\"x\"\"y\";Password=<password>;")]
    [InlineData(ConnectionStringTarget.Json, "\"Local \\\"dev\\\"\": \"Server=a;User ID=\\\"x\\\"\\\"y\\\";Password=<password>;\"")]
    [InlineData(ConnectionStringTarget.CSharp, "\"Server=a;User ID=\\\"x\\\"\\\"y\\\";Password=<password>;\"")]
    [InlineData(ConnectionStringTarget.VisualBasic, "\"Server=a;User ID=\"\"x\"\"\"\"y\"\";Password=<password>;\"")]
    [InlineData(ConnectionStringTarget.Config, "<add name=\"Local &quot;dev&quot;\" connectionString=\"Server=a;User ID=&quot;x&quot;&quot;y&quot;;Password=&lt;password&gt;;\" providerName=\"MySqlConnector\" />")]
    public void Format_PerTarget_EscapesCorrectly(ConnectionStringTarget target, string expected)
    {
        Assert.Equal(expected, ConnectionStringText.Format("Server=a;User ID=\"x\"\"y\";Password=<password>;", "Local \"dev\"", target));
    }

    [Fact]
    public void Format_EmptyName_UsesMySql()
    {
        Assert.Equal("\"MySql\": \"x\"", ConnectionStringText.Format("x", " ", ConnectionStringTarget.Json));
    }
}
