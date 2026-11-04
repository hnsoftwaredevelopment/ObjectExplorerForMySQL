using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Completion;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Completion;

public class IntelliSenseTests
{
    private static (string Text, int Caret) Split(string textWithCaret)
    {
        var caret = textWithCaret.IndexOf('|');
        return (textWithCaret.Remove(caret, 1), caret);
    }

    private static SignatureContext? SignatureAt(string textWithCaret)
    {
        var (text, caret) = Split(textWithCaret);
        return SqlCompletionAnalyzer.SignatureAt(text, caret);
    }

    private static SymbolContext? SymbolAt(string textWithCaret)
    {
        var (text, caret) = Split(textWithCaret);
        return SqlCompletionAnalyzer.SymbolAt(text, caret);
    }

    [Fact]
    public void SignatureAt_NameAndArgumentIndex()
    {
        Assert.Equal(new SignatureContext(null, "film_in_stock", 0, 18), SignatureAt("CALL film_in_stock(|"));
        Assert.Equal(new SignatureContext("sakila", "film_in_stock", 2, 25), SignatureAt("CALL sakila.film_in_stock(1, 'a,b', |)"));
        Assert.Equal(1, SignatureAt("SELECT f(g(1, 2), |")!.ArgumentIndex);
        Assert.Equal("g", SignatureAt("SELECT f(g(1, |")!.Name);
    }

    [Theory]
    [InlineData("CALL p(1)|")]
    [InlineData("SELECT 1|")]
    [InlineData("SELECT 'p(|")]
    [InlineData("SELECT (|")]
    public void SignatureAt_NoCall(string text) => Assert.Null(SignatureAt(text));

    [Fact]
    public void SymbolAt_NameWithQualifier()
    {
        var symbol = SymbolAt("SELECT f.ti|tle FROM sakila.film f");

        Assert.Equal("title", symbol!.Name);
        Assert.Equal(["f"], symbol.Qualifier);
        Assert.Equal(9, symbol.Start);
        Assert.Equal(new TableReference("sakila", "film", "f"), Assert.Single(symbol.Tables));
        Assert.Equal(["sakila"], SymbolAt("SELECT * FROM sakila.fi|lm")!.Qualifier);
        Assert.Null(SymbolAt("SELECT 1 |+ 2"));
    }

    [Fact]
    public void Signature_ProcedureWithModes_FunctionWithReturns()
    {
        var parameters = new List<ParameterInfo> { new("p_film_id", ParameterMode.In, "int", 1), new("p_count", ParameterMode.Out, "int", 2) };

        var procedure = IntelliSenseInfoBuilder.Signature(new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, "Stock"), parameters, 1);
        var function = IntelliSenseInfoBuilder.Signature(new RoutineInfo("sakila", "f", RoutineType.Function, "tinyint(1)", null), parameters.Take(1).ToList(), 0);

        Assert.Equal("film_in_stock(IN p_film_id int, OUT p_count int)", procedure.Content);
        Assert.Equal("OUT p_count int", procedure.Content.Substring(procedure.Parameters[1].Start, procedure.Parameters[1].Length));
        Assert.Equal("Stock", procedure.Documentation);
        Assert.Equal(1, procedure.CurrentParameter);
        Assert.Equal("f(p_film_id int) RETURNS tinyint(1)", function.Content);
    }

    private static SchemaCache Cache()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", null, null)]),
            Tables = db => Task.FromResult<IReadOnlyList<TableInfo>>([new TableInfo(db, "film", "InnoDB", "All films")]),
            Routines = [new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, null, null)],
            Parameters = [new ParameterInfo("p_film_id", ParameterMode.In, "int", 1)],
            Columns = (_, table) => Task.FromResult<IReadOnlyList<ColumnInfo>>(
                table == "film" ? [new ColumnInfo("film_id", "smallint unsigned", false, true, false, "auto_increment"), new ColumnInfo("title", "varchar(128)", true, false, false, "") { Comment = "Name" }] : []),
        };
        return new SchemaCache(metadata, new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));
    }

    private static Task<string?> QuickInfoAsync(string textWithCaret, SchemaCache cache) =>
        IntelliSenseInfoBuilder.QuickInfoAsync(SymbolAt(textWithCaret)!, cache, TestExplorer.Definition(), "sakila", new IntelliSenseTexts(), CancellationToken.None);

    [Fact]
    public async Task QuickInfo_ColumnsTablesRoutinesAndDatabases()
    {
        var cache = Cache();
        var nl = System.Environment.NewLine;

        Assert.Equal($"film.film_id (column){nl}smallint unsigned NOT NULL, primary key, auto_increment", await QuickInfoAsync("SELECT f.fi|lm_id FROM film f", cache));
        Assert.Equal($"film.title (column){nl}varchar(128) NULL{nl}Name", await QuickInfoAsync("SELECT ti|tle FROM film", cache));
        Assert.Equal($"film.title (column){nl}varchar(128) NULL{nl}Name", await QuickInfoAsync("SELECT sakila.film.ti|tle", cache));
        Assert.Equal($"sakila.film (table){nl}InnoDB{nl}All films", await QuickInfoAsync("SELECT * FROM fi|lm", cache));
        Assert.Equal("film_in_stock(IN p_film_id int)", await QuickInfoAsync("CALL film_in_st|ock(1)", cache));
        Assert.Equal("sakila (database)", await QuickInfoAsync("USE sak|ila", cache));
        Assert.Null(await QuickInfoAsync("SEL|ECT 1", cache));
    }

    [Fact]
    public async Task SignatureAsync_KnownRoutineOnly()
    {
        var cache = Cache();

        var signature = await IntelliSenseInfoBuilder.SignatureAsync(SignatureAt("CALL film_in_stock(|")!, cache, TestExplorer.Definition(), "sakila", CancellationToken.None);
        var unknown = await IntelliSenseInfoBuilder.SignatureAsync(SignatureAt("SELECT COUNT(|")!, cache, TestExplorer.Definition(), "sakila", CancellationToken.None);

        Assert.Equal("film_in_stock(IN p_film_id int)", signature!.Content);
        Assert.Null(unknown);
    }
}
