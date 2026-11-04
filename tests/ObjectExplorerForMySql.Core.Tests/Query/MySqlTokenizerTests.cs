using System.Collections.Generic;
using System.Linq;
using ObjectExplorerForMySql.Core.Query;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public class MySqlTokenizerTests
{
    // Returns "Kind:text" for each token, so expectations read like the SQL.
    private static string[] Tokens(string line, MySqlLexState state = MySqlLexState.Normal) =>
        MySqlTokenizer.Tokenize(line, state, out _).Select(t => $"{t.Kind}:{line.Substring(t.Start, t.Length)}").ToArray();

    private static MySqlLexState End(string line, MySqlLexState state = MySqlLexState.Normal)
    {
        MySqlTokenizer.Tokenize(line, state, out var end);
        return end;
    }

    [Fact]
    public void Keywords_AreCaseInsensitive_OtherWordsAreNot()
    {
        Assert.Equal(["Keyword:select", "Keyword:FROM", "Keyword:Where"], Tokens("select actor_id FROM actor Where x"));
    }

    [Fact]
    public void WordAfterADot_IsNoKeyword()
    {
        Assert.Equal(["Keyword:SELECT", "Keyword:FROM"], Tokens("SELECT t.status, t.key FROM t"));
    }

    [Theory]
    [InlineData("-- comment", "Comment:-- comment")]
    [InlineData("--", "Comment:--")]
    [InlineData("# comment", "Comment:# comment")]
    [InlineData("/* c */", "Comment:/* c */")]
    [InlineData("/*!40101 SET NAMES utf8 */", "Comment:/*!40101 SET NAMES utf8 */")]
    public void Comments(string line, string expected)
    {
        Assert.Equal([expected], Tokens(line));
    }

    [Fact]
    public void DoubleDashWithoutSpace_IsNoComment()
    {
        Assert.Equal(["Keyword:SELECT", "Number:5", "Number:3"], Tokens("SELECT 5--3"));
    }

    [Fact]
    public void Strings_WithEscapesAndDoubledQuotes()
    {
        Assert.Equal(
            ["String:'it''s'", "String:'a\\'b'", "String:\"x\"\"y\""],
            Tokens("'it''s' 'a\\'b' \"x\"\"y\""));
    }

    [Fact]
    public void Backticks_AreQuotedIdentifiers_WithDoubledBackticks()
    {
        Assert.Equal(["Keyword:SELECT", "QuotedIdentifier:`a``b`", "Keyword:FROM", "QuotedIdentifier:`order`"], Tokens("SELECT `a``b` FROM `order`"));
    }

    [Theory]
    [InlineData("@total", "Variable:@total")]
    [InlineData("@@sql_mode", "Variable:@@sql_mode")]
    [InlineData("@@global.max_connections", "Variable:@@global.max_connections")]
    [InlineData("@`my var`", "Variable:@`my var`")]
    public void Variables(string line, string expected)
    {
        Assert.Equal([expected], Tokens(line));
    }

    [Fact]
    public void Numbers_AndWordsWithDigits()
    {
        Assert.Equal(["Number:12", "Number:1.5", "Number:2e10", "Number:0x1F", "Number:.5"], Tokens("12 1.5 2e10 0x1F .5 t1 1abc"));
    }

    [Fact]
    public void BlockComment_OverLines()
    {
        Assert.Equal(MySqlLexState.BlockComment, End("SELECT /* start"));
        Assert.Equal(["Comment:middle"], Tokens("middle", MySqlLexState.BlockComment));
        Assert.Equal(["Comment:end */", "Keyword:FROM"], Tokens("end */ FROM", MySqlLexState.BlockComment));
        Assert.Equal(MySqlLexState.Normal, End("end */ FROM", MySqlLexState.BlockComment));
    }

    [Fact]
    public void OpenString_AndBacktick_CarryOver()
    {
        Assert.Equal(MySqlLexState.SingleQuoted, End("SELECT 'line one"));
        Assert.Equal(["String:two'", "Keyword:AS"], Tokens("two' AS x", MySqlLexState.SingleQuoted));
        Assert.Equal(MySqlLexState.Backtick, End("SELECT `col"));
        Assert.Equal(MySqlLexState.SingleQuoted, End("whole line", MySqlLexState.SingleQuoted));
    }

    [Fact]
    public void QuotesInsideComments_DoNotOpenStrings()
    {
        Assert.Equal(MySqlLexState.Normal, End("-- it's fine"));
        Assert.Equal(MySqlLexState.Normal, End("/* it's */"));
    }

    [Fact]
    public void Delimiter_IsAKeyword()
    {
        Assert.Equal(["Keyword:DELIMITER"], Tokens("DELIMITER $$"));
    }

    [Fact]
    public void EndState_MatchesTokenize()
    {
        foreach (var line in new[] { "SELECT 'a", "/* x", "`y", "\"z", "SELECT 1 -- 'x" })
        {
            Assert.Equal(End(line), MySqlTokenizer.EndState(line, MySqlLexState.Normal));
        }
    }

    [Fact]
    public void LineStates_UpdateOnlyAsFarAsNeeded()
    {
        var lines = new List<string> { "SELECT 1;", "SELECT 2;", "SELECT 3;", "SELECT 4;" };
        var states = new MySqlLineStates();
        states.Rebuild(lines.Count, i => lines[i]);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(MySqlLexState.Normal, states.StartOf(i)));

        // Opening a block comment on line 1 changes all lines after it.
        lines[1] = "SELECT 2; /*";
        Assert.Equal(3, states.Update(1, 1, lines.Count, i => lines[i]));
        Assert.Equal(MySqlLexState.BlockComment, states.StartOf(2));
        Assert.Equal(MySqlLexState.BlockComment, states.StartOf(3));

        // Closing it on line 2 restores line 3.
        lines[2] = "*/ SELECT 3;";
        Assert.Equal(3, states.Update(2, 2, lines.Count, i => lines[i]));
        Assert.Equal(MySqlLexState.Normal, states.StartOf(3));

        // An edit that changes nothing stops at the edited line.
        lines[0] = "SELECT 10;";
        Assert.Equal(0, states.Update(0, 0, lines.Count, i => lines[i]));
    }

    [Fact]
    public void LineStates_InsertedAndRemovedLines()
    {
        var lines = new List<string> { "a", "/* b", "c */", "d" };
        var states = new MySqlLineStates();
        states.Rebuild(lines.Count, i => lines[i]);
        Assert.Equal(MySqlLexState.BlockComment, states.StartOf(2));

        lines.Insert(1, "new");
        states.Update(1, 1, lines.Count, i => lines[i]);
        Assert.Equal(5, states.Count);
        Assert.Equal(MySqlLexState.BlockComment, states.StartOf(3));
        Assert.Equal(MySqlLexState.Normal, states.StartOf(4));

        lines.RemoveAt(2);
        states.Update(1, 1, lines.Count, i => lines[i]);
        Assert.Equal(4, states.Count);
        Assert.Equal(MySqlLexState.Normal, states.StartOf(2));
        Assert.Equal(MySqlLexState.Normal, states.StartOf(3));
    }
}
