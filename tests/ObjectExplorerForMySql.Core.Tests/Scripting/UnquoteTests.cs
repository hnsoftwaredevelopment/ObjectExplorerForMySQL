using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Scripting;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Scripting;

public class UnquoteTests
{
    [Theory]
    [InlineData("CREATE TABLE `actor` (`actor_id` int)", "CREATE TABLE actor (actor_id int)")]
    [InlineData("KEY `idx_name` (`last_name`,`first_name`)", "KEY idx_name (last_name,first_name)")]
    [InlineData("`sakila`.`film`", "sakila.film")]
    [InlineData("`order` int, `first name` text, `2024` int", "`order` int, `first name` text, `2024` int")]
    [InlineData("`a``b` int", "`a``b` int")]
    [InlineData("REFERENCES `language` (`language_id`) ON DELETE RESTRICT", "REFERENCES language (language_id) ON DELETE RESTRICT")]
    public void Identifiers_LoseBackticksOnlyWhenSafe(string sql, string expected) =>
        Assert.Equal(expected, MySqlIdentifier.UnquoteWhereSafe(sql));

    [Theory]
    [InlineData("COMMENT 'use `actor` here'", "COMMENT 'use `actor` here'")]
    [InlineData("DEFAULT 'it''s `x`'", "DEFAULT 'it''s `x`'")]
    [InlineData("DEFAULT 'a\\'`b`'", "DEFAULT 'a\\'`b`'")]
    [InlineData("SET x = \"`y`\"", "SET x = \"`y`\"")]
    [InlineData("-- `keep` this\n`go`", "-- `keep` this\ngo")]
    [InlineData("# `keep`\n`go`", "# `keep`\ngo")]
    [InlineData("/* `keep` */ `go`", "/* `keep` */ go")]
    [InlineData("/*!50100 PARTITION BY HASH (`id`) */", "/*!50100 PARTITION BY HASH (`id`) */")]
    [InlineData("x--`y`", "x--y")]
    public void StringsAndComments_AreCopiedUnchanged(string sql, string expected) =>
        Assert.Equal(expected, MySqlIdentifier.UnquoteWhereSafe(sql));

    [Theory]
    [InlineData("abc`def`", "abc`def`")]
    [InlineData("`def`ghi", "`def`ghi")]
    [InlineData("`unclosed", "`unclosed")]
    [InlineData("", "")]
    public void EdgeCases_KeepTheText(string sql, string expected) =>
        Assert.Equal(expected, MySqlIdentifier.UnquoteWhereSafe(sql));

    [Fact]
    public void Routine_BodyKeepsStringsAndQuotesOnlyWhereNeeded()
    {
        var sql = "CREATE DEFINER=`root`@`localhost` PROCEDURE `film_in_stock`(IN p_film_id INT)\nBEGIN\n  SELECT `inventory_id` FROM `inventory` WHERE `order` = 'a`b';\nEND";

        Assert.Equal(
            "CREATE DEFINER=root@localhost PROCEDURE film_in_stock(IN p_film_id INT)\nBEGIN\n  SELECT inventory_id FROM inventory WHERE `order` = 'a`b';\nEND",
            MySqlIdentifier.UnquoteWhereSafe(sql));
    }

    [Fact]
    public void CreateScript_FollowsTheSqlQuotingOption()
    {
        var code = new CodeObject(CodeObjectKind.Table, "sakila", "actor");
        var create = "CREATE TABLE `actor` (\n  `actor_id` smallint unsigned NOT NULL,\n  `order` int\n)";

        var whenNeeded = ScriptGenerator.Generate(new ScriptRequest(ScriptKind.Create, code) { CreateStatement = create });
        var always = ScriptGenerator.Generate(new ScriptRequest(ScriptKind.Create, code) { CreateStatement = create, Options = DropFormatOptions.Default with { SqlQuoting = QuoteMode.Always } });

        Assert.StartsWith("CREATE TABLE actor (\n  actor_id smallint unsigned NOT NULL,\n  `order` int\n)", whenNeeded);
        Assert.StartsWith(create, always);
    }
}
