using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Metadata;

public class ColumnLabelFormatterTests
{
    [Theory]
    [InlineData("id", "int", false, true, false, "id (PK, int, not null)")]
    [InlineData("customer_id", "int unsigned", false, false, true, "customer_id (FK, int unsigned, not null)")]
    [InlineData("order_id", "bigint", false, true, true, "order_id (PK, FK, bigint, not null)")]
    [InlineData("optname", "varchar(128)", true, false, false, "optname (varchar(128), null)")]
    [InlineData("price", "decimal(10,2)", false, false, false, "price (decimal(10,2), not null)")]
    public void Format_FollowsSqlServerObjectExplorerStyle(string name, string type, bool nullable, bool pk, bool fk, string expected)
    {
        Assert.Equal(expected, new ColumnInfo(name, type, nullable, pk, fk, string.Empty).Label);
    }

    [Fact]
    public void Format_ShortensLongTypes()
    {
        var longEnum = "enum('" + new string('a', 100) + "')";

        var label = new ColumnInfo("state", longEnum, true, false, false, string.Empty).Label;

        Assert.StartsWith("state (enum('aaa", label);
        Assert.EndsWith("..., null)", label);
        Assert.True(label.Length < 80, label);
    }

    [Theory]
    [InlineData("VIRTUAL GENERATED", true)]
    [InlineData("STORED GENERATED", true)]
    [InlineData("auto_increment", false)]
    [InlineData("", false)]
    public void IsGenerated_ReadsExtra(string extra, bool expected)
    {
        Assert.Equal(expected, new ColumnInfo("c", "int", true, false, false, extra).IsGenerated);
    }
}
