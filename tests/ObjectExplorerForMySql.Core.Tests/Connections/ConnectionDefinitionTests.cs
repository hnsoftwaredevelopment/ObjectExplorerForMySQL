using System.Linq;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Connections;

public class ConnectionDefinitionTests
{
    private static ConnectionDefinition Valid() => new()
    {
        Host = "db.local",
        Port = 3307,
        UserName = "app",
        Password = "Secret;123",
    };

    [Fact]
    public void Defaults_MatchTheStory()
    {
        var definition = new ConnectionDefinition();

        Assert.Equal(3306, definition.Port);
        Assert.Equal(15, definition.ConnectionTimeoutSeconds);
        Assert.Equal(SslMode.Preferred, definition.SslMode);
        Assert.True(definition.SavePassword);
        Assert.False(definition.AllowPublicKeyRetrieval);
    }

    [Fact]
    public void Label_UsesHostPortAndUserWithoutDisplayName()
    {
        Assert.Equal("db.local:3307 (app)", Valid().Label);
    }

    [Fact]
    public void Label_IncludesDefaultDatabaseWithoutDisplayName()
    {
        var definition = Valid();
        definition.DefaultDatabase = " shop ";

        Assert.Equal("db.local:3307/shop (app)", definition.Label);
    }

    [Fact]
    public void Label_UsesTrimmedDisplayNameWhenSet()
    {
        var definition = Valid();
        definition.DisplayName = "  Production  ";

        Assert.Equal("Production", definition.Label);
    }

    [Fact]
    public void Validate_ValidDefinitionHasNoErrors()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void Validate_ReportsEveryProblem()
    {
        var definition = new ConnectionDefinition { Host = " ", Port = 0, UserName = "", ConnectionTimeoutSeconds = 301 };

        Assert.Equal(
            [
                ConnectionValidationError.HostRequired,
                ConnectionValidationError.PortInvalid,
                ConnectionValidationError.UserRequired,
                ConnectionValidationError.TimeoutInvalid,
            ],
            definition.Validate());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(65536, false)]
    public void Validate_ChecksPortRange(int port, bool valid)
    {
        var definition = Valid();
        definition.Port = port;

        Assert.Equal(valid, !definition.Validate().Contains(ConnectionValidationError.PortInvalid));
    }

    [Fact]
    public void Build_MapsAllFields()
    {
        var definition = Valid();
        definition.DefaultDatabase = " shop ";
        definition.SslMode = SslMode.VerifyFull;
        definition.ConnectionTimeoutSeconds = 30;
        definition.AllowPublicKeyRetrieval = true;

        var builder = new MySqlConnectionStringBuilder(ConnectionStringFactory.Build(definition));

        Assert.Equal("db.local", builder.Server);
        Assert.Equal(3307u, builder.Port);
        Assert.Equal("app", builder.UserID);
        Assert.Equal("Secret;123", builder.Password);
        Assert.Equal("shop", builder.Database);
        Assert.Equal(MySqlSslMode.VerifyFull, builder.SslMode);
        Assert.Equal(30u, builder.ConnectionTimeout);
        Assert.True(builder.AllowPublicKeyRetrieval);
        Assert.True(builder.Pooling);
        Assert.Equal(ConnectionStringFactory.ApplicationName, builder.ApplicationName);
    }

    [Fact]
    public void Build_WithoutDatabaseAndPooling()
    {
        var builder = new MySqlConnectionStringBuilder(ConnectionStringFactory.Build(Valid(), pooling: false));

        Assert.Equal(string.Empty, builder.Database);
        Assert.False(builder.Pooling);
    }

    [Theory]
    [InlineData(SslMode.None, MySqlSslMode.None)]
    [InlineData(SslMode.Preferred, MySqlSslMode.Preferred)]
    [InlineData(SslMode.Required, MySqlSslMode.Required)]
    [InlineData(SslMode.VerifyCA, MySqlSslMode.VerifyCA)]
    [InlineData(SslMode.VerifyFull, MySqlSslMode.VerifyFull)]
    public void ToMySqlSslMode_MapsEveryValue(SslMode mode, MySqlSslMode expected)
    {
        Assert.Equal(expected, ConnectionStringFactory.ToMySqlSslMode(mode));
    }
}
