using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Completion;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Completion;

public class CompletionTests
{
    // "|" marks the caret.
    private static SqlCompletionContext At(string textWithCaret)
    {
        var caret = textWithCaret.IndexOf('|');
        return SqlCompletionAnalyzer.Analyze(textWithCaret.Remove(caret, 1), caret);
    }

    [Fact]
    public void Names_WordStartIsTheTypedWord()
    {
        var context = At("SELECT * FROM fi|");

        Assert.Equal(CompletionSituation.Names, context.Situation);
        Assert.Equal(14, context.WordStart);
        Assert.Empty(context.Qualifier);
    }

    [Theory]
    [InlineData("SELECT 'fi|'")]
    [InlineData("SELECT 1 -- fi|")]
    [InlineData("SELECT /* fi|")]
    [InlineData("SET @fi|")]
    [InlineData("SELECT 12|")]
    public void Nothing_InStringsCommentsVariablesAndNumbers(string text) =>
        Assert.Equal(CompletionSituation.None, At(text).Situation);

    [Fact]
    public void LineComment_EndsAtTheLineBreak() =>
        Assert.Equal(CompletionSituation.Names, At("-- note\nSEL|").Situation);

    [Fact]
    public void Members_AfterAliasDot_WithTablesAndAliases()
    {
        var context = At("SELECT f.| FROM sakila.film AS f JOIN film_actor fa ON fa.film_id = f.film_id WHERE 1");

        Assert.Equal(CompletionSituation.Members, context.Situation);
        Assert.Equal(["f"], context.Qualifier);
        Assert.Equal(
            [new TableReference("sakila", "film", "f"), new TableReference(null, "film_actor", "fa")],
            context.Tables);
    }

    [Fact]
    public void FromList_WithCommas()
    {
        var context = At("SELECT | FROM film f, `language` l WHERE 1");

        Assert.Equal([new TableReference(null, "film", "f"), new TableReference(null, "language", "l")], context.Tables);
    }

    [Fact]
    public void Members_DatabaseAndTableQualifier()
    {
        var context = At("SELECT sakila.film.ti|");

        Assert.Equal(["sakila", "film"], context.Qualifier);
        Assert.Equal(19, context.WordStart);
    }

    [Fact]
    public void Statement_EndsAtSemicolon()
    {
        var context = At("SELECT * FROM a x; SELECT x.| FROM b y; SELECT * FROM c z");

        Assert.Equal([new TableReference(null, "b", "y")], context.Tables);
    }

    [Fact]
    public void Subquery_IsSkipped_AndClauseWordsAreNoAlias()
    {
        var context = At("SELECT | FROM (SELECT 1) AS d JOIN film WHERE 1");

        Assert.Equal([new TableReference(null, "film", null)], context.Tables);
    }

    [Fact]
    public void Backtick_WordIsQuoted()
    {
        var context = At("SELECT * FROM `fi|");

        Assert.True(context.InBackticks);
        Assert.Equal(14, context.WordStart);
    }

    private static (SchemaCache Cache, FakeMetadataProvider Metadata) Cache()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", null, null), new DatabaseInfo("my db", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>(db == "sakila" ? [new TableInfo(db, "film", "InnoDB", "Films")] : [new TableInfo(db, "notes", "InnoDB", null)]),
            Views = [new TableInfo("sakila", "film_list", null, "VIEW", isView: true)],
            Routines = [new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, null)],
            Columns = (_, table) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
                table == "film" ? [new ColumnInfo("film_id", "smallint", false, true, false, ""), new ColumnInfo("release year", "year", true, false, false, "")] : []),
        };
        return (new SchemaCache(metadata, new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug)), metadata);
    }

    private static Task<IReadOnlyList<CompletionEntry>> BuildAsync(string text, SchemaCache cache, bool connected = true) =>
        CompletionBuilder.BuildAsync(At(text), cache, connected ? TestExplorer.Definition() : null, "sakila", DropFormatOptions.Default, CancellationToken.None);

    [Fact]
    public async Task Names_KeywordsDatabasesObjectsAndStatementColumns()
    {
        var (cache, _) = Cache();

        var entries = await BuildAsync("SELECT | FROM film", cache);

        Assert.Contains(entries, e => e.Kind == CompletionKind.Keyword && e.Display == "SELECT");
        Assert.Contains(new CompletionEntry("my db", "`my db`", CompletionKind.Database), entries);
        Assert.Contains(new CompletionEntry("film", "film", CompletionKind.Table, "Films"), entries);
        Assert.Contains(entries, e => e.Kind == CompletionKind.View && e.Display == "film_list");
        Assert.Contains(entries, e => e.Kind == CompletionKind.Procedure && e.Display == "film_in_stock");
        Assert.Contains(entries, e => e.Kind == CompletionKind.Column && e.Insert == "`release year`");
    }

    [Fact]
    public async Task Members_ColumnsOfAliasInTableOrder()
    {
        var (cache, _) = Cache();

        var entries = await BuildAsync("SELECT f.| FROM film f", cache);

        Assert.Equal(["film_id", "`release year`"], entries.Select(e => e.Insert));
    }

    [Fact]
    public async Task Members_TableWithoutAliasAndDatabase()
    {
        var (cache, _) = Cache();

        Assert.Equal(2, (await BuildAsync("SELECT film.|", cache)).Count);
        // The fake returns the same views and routines for every database; the tables differ.
        var tables = (await BuildAsync("SELECT * FROM `my db`.|", cache)).Where(e => e.Kind == CompletionKind.Table).Select(e => e.Display);
        Assert.Equal(["notes"], tables);
        Assert.Empty(await BuildAsync("SELECT nothing.|", cache));
    }

    [Fact]
    public async Task InBackticks_AlwaysQuotedAndNoKeywords()
    {
        var (cache, _) = Cache();

        var entries = await BuildAsync("SELECT * FROM `fi|", cache);

        Assert.DoesNotContain(entries, e => e.Kind == CompletionKind.Keyword);
        Assert.Contains(new CompletionEntry("film", "`film`", CompletionKind.Table, "Films"), entries);
    }

    [Fact]
    public async Task WithoutConnection_OnlyKeywords()
    {
        var (cache, _) = Cache();

        var entries = await BuildAsync("SEL|", cache, connected: false);

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal(CompletionKind.Keyword, e.Kind));
        Assert.Empty(await BuildAsync("SELECT f.|", cache, connected: false));
    }

    [Fact]
    public async Task Cache_ReadsOnce_UntilCleared()
    {
        var (cache, metadata) = Cache();
        var calls = 0;
        var inner = metadata.Tables;
        metadata.Tables = db =>
        {
            calls++;
            return inner(db);
        };
        var definition = TestExplorer.Definition();

        await cache.GetDatabaseAsync(definition, "sakila", CancellationToken.None);
        await cache.GetDatabaseAsync(definition, "SAKILA", CancellationToken.None);
        Assert.Equal(1, calls);

        cache.Clear(definition.Id);
        await cache.GetDatabaseAsync(definition, "sakila", CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Cache_FailedLoad_IsTriedAgain()
    {
        var (cache, metadata) = Cache();
        var fail = true;
        var inner = metadata.Tables;
        metadata.Tables = db => fail ? throw new InvalidOperationException("down") : inner(db);
        var definition = TestExplorer.Definition();

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetDatabaseAsync(definition, "sakila", CancellationToken.None));
        fail = false;

        Assert.Contains((await cache.GetDatabaseAsync(definition, "sakila", CancellationToken.None)).Objects, o => o.Name == "film");
    }
}
