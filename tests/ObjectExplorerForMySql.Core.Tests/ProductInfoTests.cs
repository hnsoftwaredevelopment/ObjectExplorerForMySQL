using ObjectExplorerForMySql.Core;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests;

public class ProductInfoTests
{
    [Theory]
    [InlineData("8.0.0", true)]
    [InlineData("8.0.36", true)]
    [InlineData("8.0.36-log", true)]
    [InlineData("8.4.2", true)]
    [InlineData("9.1.0", true)]
    [InlineData("5.7.44", false)]
    [InlineData("5.7.44-log", false)]
    [InlineData("10.6.18-MariaDB", true)]
    [InlineData("10.11.6-MariaDB-0ubuntu0.24.04.1", true)]
    [InlineData("10.5.25-MariaDB", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not a version", false)]
    public void IsSupportedServerVersion_ReturnsExpectedResult(string? version, bool expected)
    {
        Assert.Equal(expected, ProductInfo.IsSupportedServerVersion(version));
    }
}
