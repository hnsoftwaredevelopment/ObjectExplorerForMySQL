using System;
using System.Linq;
using ObjectExplorerForMySql.Core.Query;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public class SqlScriptSplitterTests
{
    private static string[] Texts(string script) => SqlScriptSplitter.Split(script).Select(s => s.Text).ToArray();

    [Fact]
    public void SplitsOnSemicolons_AndKeepsTheLastStatementWithoutOne()
    {
        Assert.Equal(["SELECT 1", "SELECT 2", "SELECT 3"], Texts("SELECT 1; SELECT 2;\nSELECT 3"));
    }

    [Fact]
    public void IgnoresSemicolonsInStringsNamesAndComments()
    {
        var script = "SELECT 'a;b', \"c;d\", `e;f` -- x;y\nFROM t; /* g;h */ SELECT 2 # i;j\n;";

        Assert.Equal(["SELECT 'a;b', \"c;d\", `e;f` -- x;y\nFROM t", "SELECT 2 # i;j"], Texts(script));
    }

    [Fact]
    public void EscapedAndDoubledQuotes_DoNotEndTheString()
    {
        Assert.Equal(["SELECT 'it''s; \\'x;'", "SELECT 2"], Texts("SELECT 'it''s; \\'x;'; SELECT 2;"));
    }

    [Fact]
    public void CommentsBeforeAStatement_AreLeftOut_AndOnlyCommentsGiveNothing()
    {
        Assert.Equal(["SELECT 1"], Texts("-- header\n/* block */\n# more\nSELECT 1;\n-- trailing comment\n"));
        Assert.Empty(Texts("-- only a comment\n/* and this */"));
    }

    [Fact]
    public void ConditionalComments_AreCode()
    {
        Assert.Equal(["/*!40101 SET NAMES utf8 */", "SELECT /*+ MAX_EXECUTION_TIME(1000) */ 1"], Texts("/*!40101 SET NAMES utf8 */;\nSELECT /*+ MAX_EXECUTION_TIME(1000) */ 1;"));
    }

    [Fact]
    public void Delimiter_ChangesTheDelimiter()
    {
        var script = string.Join("\n",
            "DELIMITER $$",
            "CREATE PROCEDURE p()",
            "BEGIN",
            "  SELECT 1;",
            "  SELECT 2;",
            "END$$",
            "DELIMITER ;",
            "CALL p();");

        var statements = SqlScriptSplitter.Split(script);

        Assert.Equal(["CREATE PROCEDURE p()\nBEGIN\n  SELECT 1;\n  SELECT 2;\nEND", "CALL p()"], statements.Select(s => s.Text));
        Assert.Equal([1, 7], statements.Select(s => s.Line));
    }

    [Fact]
    public void Delimiter_IsCaseInsensitive_AndOnlyAtTheStartOfALine()
    {
        Assert.Equal(["SELECT 1", "SELECT 2"], Texts("delimiter //\nSELECT 1//\n  DELIMITER ;\nSELECT 2;"));
        Assert.Equal(["SELECT 'DELIMITER x'"], Texts("SELECT 'DELIMITER x';"));
    }

    [Fact]
    public void Lines_CountFromTheFirstCode()
    {
        var statements = SqlScriptSplitter.Split("\r\n-- c\r\nSELECT 1;\r\n\r\nSELECT\r\n 'a\r\nb';  SELECT 3;");

        Assert.Equal([2, 4, 6], statements.Select(s => s.Line));
        Assert.Equal("SELECT\r\n 'a\r\nb'", statements[1].Text);
    }

    [Fact]
    public void DoubleDashWithoutSpace_IsNoComment()
    {
        Assert.Equal(["SELECT 5--3", "SELECT 2"], Texts("SELECT 5--3; SELECT 2;"));
    }

    [Fact]
    public void EmptyScript_GivesNothing()
    {
        Assert.Empty(Texts(string.Empty));
        Assert.Empty(Texts(" ;; \n ; "));
    }

    [Theory]
    [InlineData(null, "NULL")]
    [InlineData("text", "text")]
    [InlineData(true, "1")]
    [InlineData(12.5, "12.5")]
    public void Formatter_SimpleValues(object? value, string expected)
    {
        Assert.Equal(expected, QueryValueFormatter.Format(value));
    }

    [Fact]
    public void Formatter_DatesTimesDecimalsAndBinary()
    {
        Assert.Equal("2026-10-30 14:05:00", QueryValueFormatter.Format(new DateTime(2026, 10, 30, 14, 5, 0)));
        Assert.Equal("2026-10-30 14:05:00.25", QueryValueFormatter.Format(new DateTime(2026, 10, 30, 14, 5, 0, 250)));
        Assert.Equal("838:59:59", QueryValueFormatter.Format(new TimeSpan(34, 22, 59, 59)));
        Assert.Equal("-01:30:00", QueryValueFormatter.Format(TimeSpan.FromMinutes(-90)));
        Assert.Equal("1234.50", QueryValueFormatter.Format(1234.50m));
        Assert.Equal("0x0AFF", QueryValueFormatter.Format(new byte[] { 0x0A, 0xFF }));
        Assert.EndsWith("...", QueryValueFormatter.Format(new byte[100]), StringComparison.Ordinal);
        Assert.Equal(2 + (2 * QueryValueFormatter.MaxBinaryBytes) + 3, QueryValueFormatter.Format(new byte[100]).Length);
    }
}
