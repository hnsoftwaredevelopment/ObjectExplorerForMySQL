using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public class QueryConnectionBarTests
{
    private const string Path = @"C:\Temp\Query1.sql";

    private readonly FakeMetadataProvider _metadata = new()
    {
        Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>(
            [new DatabaseInfo("modelbuilder", null, null), new DatabaseInfo("sakila", null, null), new DatabaseInfo("world", null, null)]),
    };

    private readonly FakePasswordPrompt _prompt = new();
    private readonly ExplorerViewModel _explorer;
    private readonly DocumentConnections _documents = new();
    private readonly ServerNodeViewModel _prod;
    private readonly ServerNodeViewModel _test;

    public QueryConnectionBarTests()
    {
        _explorer = new ExplorerViewModel(TestExplorer.Context(_metadata, _prompt));
        _prod = _explorer.AddServer(TestExplorer.Definition("Prod"));
        _prod.Definition.DefaultDatabase = "sakila";
        _test = _explorer.AddServer(TestExplorer.Definition("Test", password: null));
    }

    private QueryConnectionBarViewModel Bar() => new(_explorer, _documents, Path, new Core.Logging.ExtensionLogger(new ListSink(), Core.Logging.LogLevel.Debug));

    [Fact]
    public void Open_ShowsTheLinkWithoutLoadingOrPrompting()
    {
        _documents.Register(Path, _test.Definition.Id, "world");

        using var bar = Bar();

        Assert.Same(_test, bar.SelectedServer);
        Assert.Equal("world", bar.SelectedDatabase);
        Assert.Equal(["world"], bar.Databases);
        Assert.Equal(0, _prompt.Calls);
    }

    [Fact]
    public void Open_WithoutLink_HasNoServer()
    {
        using var bar = Bar();

        Assert.Null(bar.SelectedServer);
        Assert.False(bar.HasServer);
        Assert.Empty(bar.Databases);
    }

    [Fact]
    public async Task PickServer_LinksWithDefaultDatabase_AndLoadsTheList()
    {
        using var bar = Bar();

        bar.SelectedServer = _prod;
        Assert.Equal(new DocumentLink(_prod.Definition.Id, "sakila"), _documents.Find(Path));
        await bar.PendingLoad;

        Assert.Equal(["modelbuilder", "sakila", "world"], bar.Databases);
        Assert.Equal("sakila", bar.SelectedDatabase);
        Assert.False(bar.IsLoading);
    }

    [Fact]
    public async Task PickServer_PasswordCancelled_KeepsTheLinkWithoutList()
    {
        using var bar = Bar();

        bar.SelectedServer = _test;
        await bar.PendingLoad;

        Assert.Equal(1, _prompt.Calls);
        Assert.Empty(bar.Databases);
        Assert.Equal(new DocumentLink(_test.Definition.Id, null), _documents.Find(Path));
    }

    [Fact]
    public async Task PickDatabase_UpdatesTheLink()
    {
        using var bar = Bar();
        bar.SelectedServer = _prod;
        await bar.PendingLoad;

        bar.SelectedDatabase = "world";

        Assert.Equal(new DocumentLink(_prod.Definition.Id, "world"), _documents.Find(Path));
    }

    [Fact]
    public async Task Load_DropsADatabaseThatNoLongerExists()
    {
        _documents.Register(Path, _prod.Definition.Id, "gone");
        using var bar = Bar();

        await bar.EnsureDatabasesLoadedAsync();

        Assert.Null(bar.SelectedDatabase);
        Assert.Equal(new DocumentLink(_prod.Definition.Id, null), _documents.Find(Path));
    }

    [Fact]
    public async Task EnsureDatabasesLoaded_LoadsOnce()
    {
        var calls = 0;
        _metadata.Databases = _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sakila", null, null)]);
        };
        _documents.Register(Path, _prod.Definition.Id, "sakila");
        using var bar = Bar();

        await bar.EnsureDatabasesLoadedAsync();
        await bar.EnsureDatabasesLoadedAsync();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LoadError_IsShownMasked()
    {
        _metadata.Databases = _ => Task.FromException<IReadOnlyList<DatabaseInfo>>(new InvalidOperationException("Server down; Password=secret"));
        _documents.Register(Path, _prod.Definition.Id, "sakila");
        using var bar = Bar();

        await bar.EnsureDatabasesLoadedAsync();

        Assert.True(bar.HasError);
        Assert.DoesNotContain("secret", bar.Error, StringComparison.Ordinal);
        Assert.Equal("sakila", bar.SelectedDatabase);
    }

    [Fact]
    public void RemovingTheServer_ClearsTheSelection_AndAddingItBackRestoresIt()
    {
        _documents.Register(Path, _prod.Definition.Id, "sakila");
        using var bar = Bar();
        var definition = _prod.Definition;

        _explorer.RemoveServer(_prod);
        Assert.Null(bar.SelectedServer);
        Assert.Empty(bar.Databases);

        var again = _explorer.AddServer(definition);
        Assert.Same(again, bar.SelectedServer);
        Assert.Equal("sakila", bar.SelectedDatabase);
    }

    [Fact]
    public void LinkChangedElsewhere_IsShown()
    {
        using var bar = Bar();

        _documents.Register(Path, _test.Definition.Id, "world");

        Assert.Same(_test, bar.SelectedServer);
        Assert.Equal("world", bar.SelectedDatabase);
    }

    [Fact]
    public void Rename_MovesTheLink()
    {
        _documents.Register(Path, _prod.Definition.Id, "sakila");
        using var bar = Bar();

        bar.Rename(@"C:\Project\report.sql");

        Assert.Null(_documents.Find(Path));
        Assert.Equal(new DocumentLink(_prod.Definition.Id, "sakila"), _documents.Find(@"C:\Project\report.sql"));
        Assert.Equal(@"C:\Project\report.sql", bar.FilePath);
    }

    [Fact]
    public void Dispose_StopsFollowingTheLink()
    {
        var bar = Bar();
        bar.Dispose();

        _documents.Register(Path, _test.Definition.Id, "world");

        Assert.Null(bar.SelectedServer);
    }

    [Fact]
    public void DocumentConnections_RaisesChangedOnlyForRealChanges()
    {
        var changes = new List<string>();
        _documents.Changed += (_, path) => changes.Add(path);
        var id = Guid.NewGuid();

        _documents.Register(Path, id, "a");
        _documents.Register(Path, id, "a");
        _documents.Register(Path, id, string.Empty);
        _documents.Rename(Path, @"C:\Other.sql");
        _documents.Remove(@"C:\Other.sql");
        _documents.Remove(@"C:\Other.sql");

        Assert.Equal([Path, Path, @"C:\Other.sql", @"C:\Other.sql"], changes);
    }

    [Fact]
    public async Task EnsurePassword_UsesThePromptOnce()
    {
        _prompt.Answer = new PasswordPromptResult("pw", savePassword: false);

        Assert.True(await _test.EnsurePasswordAsync(TestContext.Current.CancellationToken));
        Assert.True(await _test.EnsurePasswordAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, _prompt.Calls);
        Assert.Equal("pw", _test.Definition.Password);
    }
}
