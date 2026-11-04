using System;
using System.Collections.Generic;
using ObjectExplorerForMySql.Core.Scripting;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Scripting;

public class SqlLiteralTests
{
    [Fact]
    public void Null_And_DbNull_AreNull()
    {
        Assert.Equal("NULL", SqlLiteral.Format(null));
        Assert.Equal("NULL", SqlLiteral.Format(DBNull.Value));
    }

    [Theory]
    [InlineData("abc", "'abc'")]
    [InlineData("it's", "'it\\'s'")]
    [InlineData("a\\b", "'a\\\\b'")]
    [InlineData("line1\nline2\r", "'line1\\nline2\\r'")]
    [InlineData("", "''")]
    public void Strings_UseBackslashEscapes(string value, string expected) =>
        Assert.Equal(expected, SqlLiteral.Format(value));

    [Fact]
    public void Numbers_UseInvariantCulture()
    {
        Assert.Equal("42", SqlLiteral.Format(42));
        Assert.Equal("-7", SqlLiteral.Format(-7L));
        Assert.Equal("18446744073709551615", SqlLiteral.Format(ulong.MaxValue));
        Assert.Equal("1.5", SqlLiteral.Format(1.5m));
        Assert.Equal("0.1", SqlLiteral.Format(0.1d));
        Assert.Equal("2.5", SqlLiteral.Format(2.5f));
        Assert.Equal("1", SqlLiteral.Format(true));
        Assert.Equal("0", SqlLiteral.Format(false));
    }

    [Fact]
    public void Dates_FollowTheDataType()
    {
        var value = new DateTime(2006, 2, 15, 4, 34, 33);
        Assert.Equal("'2006-02-15'", SqlLiteral.Format(value.Date, "date"));
        Assert.Equal("'2006-02-15 04:34:33'", SqlLiteral.Format(value, "datetime"));
        Assert.Equal("'2006-02-15 04:34:33.12'", SqlLiteral.Format(value.AddTicks(1_200_000), "timestamp"));
    }

    [Fact]
    public void Time_CanBeNegativeAndAbove24Hours()
    {
        Assert.Equal("'25:30:00'", SqlLiteral.Format(new TimeSpan(25, 30, 0)));
        Assert.Equal("'-01:02:03'", SqlLiteral.Format(-new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void ZeroDates_StayZero()
    {
        Assert.Equal("'0000-00-00'", SqlLiteral.Format(new MySqlConnector.MySqlDateTime(0, 0, 0, 0, 0, 0, 0), "date"));
        Assert.Equal("'0000-00-00 00:00:00'", SqlLiteral.Format(new MySqlConnector.MySqlDateTime(0, 0, 0, 0, 0, 0, 0), "datetime"));
        Assert.Equal("'2020-01-02'", SqlLiteral.Format(new MySqlConnector.MySqlDateTime(2020, 1, 2, 0, 0, 0, 0), "date"));
    }

    [Fact]
    public void Bytes_AreHex()
    {
        Assert.Equal("0x00FF10", SqlLiteral.Format(new byte[] { 0x00, 0xFF, 0x10 }));
        Assert.Equal("''", SqlLiteral.Format(Array.Empty<byte>()));
    }

    [Fact]
    public void DependencyOrder_PutsDependenciesFirst_AlphabeticalOtherwise()
    {
        var order = DependencyOrder.Sort(
            ["rental", "customer", "address", "city", "actor"],
            [("rental", "customer"), ("customer", "address"), ("address", "city"), ("actor", "actor"), ("city", "country")]);

        Assert.Equal(["actor", "city", "address", "customer", "rental"], order);
    }

    [Fact]
    public void DependencyOrder_AppendsCycleAlphabetically()
    {
        var order = DependencyOrder.Sort(["b", "a", "c"], new List<(string, string)> { ("a", "b"), ("b", "a") });

        Assert.Equal(["c", "a", "b"], order);
    }
}
