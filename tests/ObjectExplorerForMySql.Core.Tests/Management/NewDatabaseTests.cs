using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Management;

public class NewDatabaseTests
{
    private static CharacterSetCatalog Catalog(string? serverSet = "utf8mb4", string? serverCollation = "utf8mb4_0900_ai_ci") => new(
        [
            new CharacterSetInfo("utf8mb4", "UTF-8 Unicode", "utf8mb4_0900_ai_ci"),
            new CharacterSetInfo("latin1", "cp1252 West European", "latin1_swedish_ci"),
        ],
        [
            new CollationInfo("utf8mb4_general_ci", "utf8mb4"),
            new CollationInfo("utf8mb4_0900_ai_ci", "utf8mb4"),
            new CollationInfo("latin1_swedish_ci", "latin1"),
            new CollationInfo("latin1_bin", "latin1"),
        ],
        serverSet,
        serverCollation,
        ["shop", "mysql"]);

    [Fact]
    public void Starts_AtTheServerDefaults_WithSortedLists()
    {
        var model = new NewDatabaseViewModel(Catalog(serverCollation: "utf8mb4_general_ci"));

        Assert.Equal(["latin1", "utf8mb4"], model.CharacterSets.Select(c => c.Name));
        Assert.Equal("utf8mb4", model.CharacterSet?.Name);
        Assert.Equal("utf8mb4_general_ci", model.Collation?.Name);
        Assert.Equal(["utf8mb4_0900_ai_ci", "utf8mb4_general_ci"], model.Collations.Select(c => c.Name));
        Assert.Equal("utf8mb4 - UTF-8 Unicode", model.CharacterSet?.Display);
    }

    [Fact]
    public void ServerCollationOfAnotherSet_FallsBackToTheDefaultCollation()
    {
        var model = new NewDatabaseViewModel(Catalog(serverSet: "utf8mb4", serverCollation: "latin1_bin"));

        Assert.Equal("utf8mb4_0900_ai_ci", model.Collation?.Name);
    }

    [Fact]
    public void UnknownServerSet_StartsAtUtf8mb4()
    {
        Assert.Equal("utf8mb4", new NewDatabaseViewModel(Catalog(serverSet: null, serverCollation: null)).CharacterSet?.Name);
    }

    [Fact]
    public void ChangingTheCharacterSet_SelectsItsDefaultCollation()
    {
        var model = new NewDatabaseViewModel(Catalog());
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.CharacterSet = model.CharacterSets.Single(c => c.Name == "latin1");

        Assert.Equal(["latin1_bin", "latin1_swedish_ci"], model.Collations.Select(c => c.Name));
        Assert.Equal("latin1_swedish_ci", model.Collation?.Name);
        Assert.Contains(nameof(NewDatabaseViewModel.Collations), changed);
        Assert.Contains(nameof(NewDatabaseViewModel.Sql), changed);
    }

    [Theory]
    [InlineData("", NameProblem.Empty)]
    [InlineData("  ", NameProblem.Empty)]
    [InlineData("shop", NameProblem.Exists)]
    [InlineData("SHOP", NameProblem.Exists)]
    [InlineData("new ", NameProblem.EndsWithSpace)]
    [InlineData("sales", NameProblem.None)]
    public void Name_IsChecked(string name, NameProblem expected)
    {
        var model = new NewDatabaseViewModel(Catalog()) { Name = name };

        Assert.Equal(expected, model.Problem);
        Assert.Equal(expected == NameProblem.None, model.CanCreate);
    }

    [Fact]
    public void Name_Of65Characters_IsTooLong()
    {
        Assert.Equal(NameProblem.TooLong, new NewDatabaseViewModel(Catalog()) { Name = new string('a', 65) }.Problem);
    }

    [Fact]
    public void Sql_ShowsTheStatement_OnlyForAUsableName()
    {
        var model = new NewDatabaseViewModel(Catalog());
        Assert.Equal(string.Empty, model.Sql);

        model.Name = "my db";
        model.Collation = model.Collations.Single(c => c.Name == "utf8mb4_general_ci");

        Assert.Equal("CREATE DATABASE `my db` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;", model.Sql);
    }

    [Fact]
    public void BuildSql_LeavesOutMissingClauses()
    {
        Assert.Equal("CREATE DATABASE `a``b`;", NewDatabaseViewModel.BuildSql("a`b", null, null));
    }

    [Fact]
    public async Task Create_RunsTheSql_RefreshesAndSelectsTheDatabase_IgnoringCase()
    {
        var tree = await TestTree.OpenAsync();
        var runner = new FakeRunner();
        tree.Metadata.Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("shop", null, null), new DatabaseInfo("sales", null, null)]);
        var creator = new DatabaseCreator(runner, new FakeSource(Catalog()), tree.Logger);

        Assert.True(DatabaseCreator.IsTarget(tree.Databases));
        await creator.CreateAsync(tree.Databases, "Sales", "CREATE DATABASE `Sales`;", CancellationToken.None);

        Assert.Equal(["CREATE DATABASE `Sales`;"], runner.Executed);
        Assert.True(tree.Databases.Children.OfType<DatabaseNodeViewModel>().Single(d => d.Key == "sales").IsSelected);
    }

    [Fact]
    public async Task Create_OnACollapsedFolder_ExpandsIt()
    {
        var tree = await TestTree.OpenAsync();

        // Collapsed by the user in the tree: the old children stay loaded.
        tree.Databases.IsExpanded = false;
        tree.Metadata.Databases = _ => Task.FromResult<IReadOnlyList<DatabaseInfo>>([new DatabaseInfo("sales", null, null)]);

        await new DatabaseCreator(new FakeRunner(), new FakeSource(Catalog()), tree.Logger).CreateAsync(tree.Databases, "sales", "CREATE DATABASE `sales`;", CancellationToken.None);

        Assert.True(tree.Databases.IsExpanded);
        Assert.True(tree.Databases.Children.OfType<DatabaseNodeViewModel>().Single().IsSelected);
    }

    [Fact]
    public async Task Target_IsOnlyTheDatabasesFolder()
    {
        var tree = await TestTree.OpenAsync();

        Assert.False(DatabaseCreator.IsTarget(tree.Server));
        Assert.False(DatabaseCreator.IsTarget(tree.Database));
        Assert.False(DatabaseCreator.IsTarget(tree.Folder(tree.Database, "Tables")));
        Assert.False(DatabaseCreator.IsTarget(tree.Databases.Children[0]));
        Assert.False(DatabaseCreator.IsTarget(null));
    }

    [Fact]
    public async Task LoadCatalog_AfterACancelledPassword_IsNull()
    {
        var tree = await TestTree.OpenAsync();
        tree.Server.Definition.Password = null;
        var creator = new DatabaseCreator(new FakeRunner(), new FakeSource(Catalog()), tree.Logger);

        Assert.Null(await creator.LoadCatalogAsync(tree.Databases, CancellationToken.None));

        tree.Server.Definition.Password = "pw";
        Assert.NotNull(await creator.LoadCatalogAsync(tree.Databases, CancellationToken.None));
    }

    private sealed class FakeSource(CharacterSetCatalog catalog) : ICharacterSetSource
    {
        public Task<CharacterSetCatalog> GetAsync(ConnectionDefinition definition, CancellationToken cancellationToken) => Task.FromResult(catalog);
    }
}
