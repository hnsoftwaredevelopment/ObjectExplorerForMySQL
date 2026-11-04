using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Explorer;

public class ServerNodeViewModelTests
{
    private static IReadOnlyList<DatabaseInfo> SampleDatabases() =>
    [
        new("shop", "utf8mb4", "utf8mb4_0900_ai_ci"),
        new("sys", null, null),
        new("Analytics", null, null),
        new("mysql", null, null),
        new("information_schema", null, null),
        new("performance_schema", null, null),
    ];

    [Fact]
    public void NewServer_IsNotConnectedCollapsedWithPlaceholder()
    {
        var node = new ServerNodeViewModel(TestExplorer.Context(), TestExplorer.Definition());

        Assert.Equal(ConnectionState.NotConnected, node.State);
        Assert.Equal(NodeIcon.ServerDisconnected, node.Icon);
        Assert.False(node.IsExpanded);
        Assert.Equal(LoadState.NotLoaded, node.LoadState);
        Assert.Equal(NodeIcon.Loading, Assert.Single(node.Children).Icon);
        Assert.False(node.CanDisconnect);
    }

    [Fact]
    public async Task Expand_ConnectsAndShowsDatabasesFolder()
    {
        var metadata = new FakeMetadataProvider();
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());

        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal(ConnectionState.Connected, node.State);
        Assert.Equal(NodeIcon.ServerConnected, node.Icon);
        Assert.Equal("8.0.36", node.ServerVersion);
        Assert.Equal("app@%", node.CurrentUser);
        Assert.True(node.CanDisconnect);
        Assert.True(node.CanRefresh);
        var folder = Assert.IsType<FolderNodeViewModel>(Assert.Single(node.Children));
        Assert.Equal("Databases", folder.Label);
        Assert.Equal(1, metadata.ConnectCalls);
    }

    [Fact]
    public async Task ExpandDatabasesFolder_GroupsSystemDatabasesFirstAndSortsByName()
    {
        var metadata = new FakeMetadataProvider { Databases = _ => Task.FromResult(SampleDatabases()) };
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        var databasesFolder = (FolderNodeViewModel)node.Children[0];

        databasesFolder.IsExpanded = true;
        await databasesFolder.WaitForLoadAsync();

        Assert.Equal(["System Databases", "Analytics", "shop"], databasesFolder.Children.Select(c => c.Label));
        var systemFolder = (FolderNodeViewModel)databasesFolder.Children[0];
        systemFolder.IsExpanded = true;
        await systemFolder.WaitForLoadAsync();
        Assert.Equal(["information_schema", "mysql", "performance_schema", "sys"], systemFolder.Children.Select(c => c.Label));
        Assert.All(systemFolder.Children, c => Assert.Equal(NodeIcon.Database, c.Icon));
        Assert.Equal(NodeIcon.FolderOpened, systemFolder.Icon);
    }

    [Fact]
    public async Task Expand_ShowsLoadingPlaceholderWhileConnecting()
    {
        var gate = new TaskCompletionSource<ServerInfo>();
        var metadata = new FakeMetadataProvider();
        metadata.ConnectResults.Enqueue(_ => gate.Task);
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());

        node.IsExpanded = true;

        Assert.Equal(ConnectionState.Connecting, node.State);
        Assert.Equal(NodeIcon.ServerConnecting, node.Icon);
        Assert.Equal(LoadState.Loading, node.LoadState);
        Assert.Equal("Loading...", Assert.Single(node.Children).Label);

        gate.SetResult(new ServerInfo("8.4.2", "app@%"));
        await node.WaitForLoadAsync();
        Assert.Equal(ConnectionState.Connected, node.State);
    }

    [Fact]
    public async Task ConnectFailure_ShowsErrorNodeAndStateAndRetriesOnNextExpand()
    {
        var metadata = new FakeMetadataProvider();
        metadata.ConnectResults.Enqueue(_ => Task.FromException<ServerInfo>(new InvalidOperationException("Unable to connect. Password=Secret123")));
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());

        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal(ConnectionState.Error, node.State);
        Assert.Equal(NodeIcon.ServerError, node.Icon);
        Assert.Equal(LoadState.Failed, node.LoadState);
        Assert.True(node.HasError);
        Assert.DoesNotContain("Secret123", node.LastError);
        var error = Assert.IsType<PlaceholderNodeViewModel>(Assert.Single(node.Children));
        Assert.True(error.IsError);
        Assert.DoesNotContain("Secret123", error.Label);

        node.IsExpanded = false;
        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal(ConnectionState.Connected, node.State);
        Assert.Null(node.LastError);
        Assert.Equal(2, metadata.ConnectCalls);
    }

    [Fact]
    public async Task MissingPassword_PromptCancelled_CollapsesWithoutError()
    {
        var metadata = new FakeMetadataProvider();
        var prompt = new FakePasswordPrompt { Answer = null };
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata, prompt), TestExplorer.Definition(password: null));

        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal(1, prompt.Calls);
        Assert.Equal(0, metadata.ConnectCalls);
        Assert.False(node.IsExpanded);
        Assert.Equal(ConnectionState.NotConnected, node.State);
        Assert.Equal(LoadState.NotLoaded, node.LoadState);
        Assert.False(node.HasError);
    }

    [Fact]
    public async Task MissingPassword_PromptWithSave_UsesPasswordAndRaisesDefinitionChanged()
    {
        var metadata = new FakeMetadataProvider();
        var prompt = new FakePasswordPrompt { Answer = new PasswordPromptResult("typed", savePassword: true) };
        var explorer = new ExplorerViewModel(TestExplorer.Context(metadata, prompt));
        var node = explorer.AddServer(TestExplorer.Definition(password: null));
        var saved = 0;
        explorer.ConnectionsChanged += (_, _) => saved++;

        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal("typed", metadata.PasswordAtConnect);
        Assert.Equal("typed", node.Definition.Password);
        Assert.True(node.Definition.SavePassword);
        Assert.Equal(1, saved);
        Assert.Equal(ConnectionState.Connected, node.State);

        node.Collapse();
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        Assert.Equal(1, prompt.Calls);
    }

    [Fact]
    public async Task MissingPassword_PromptWithoutSave_DoesNotRequestSaving()
    {
        var prompt = new FakePasswordPrompt { Answer = new PasswordPromptResult("typed", savePassword: false) };
        var explorer = new ExplorerViewModel(TestExplorer.Context(prompt: prompt));
        var definition = TestExplorer.Definition(password: null);
        definition.SavePassword = false;
        var node = explorer.AddServer(definition);
        var saved = 0;
        explorer.ConnectionsChanged += (_, _) => saved++;

        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal(0, saved);
        Assert.Equal("typed", node.Definition.Password);
    }

    [Fact]
    public async Task Disconnect_ClosesConnectionsCollapsesAndResetsChildren()
    {
        var metadata = new FakeMetadataProvider();
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());
        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        node.Disconnect();

        Assert.Equal(1, metadata.DisconnectCalls);
        Assert.Equal(ConnectionState.NotConnected, node.State);
        Assert.False(node.IsExpanded);
        Assert.Equal(LoadState.NotLoaded, node.LoadState);
        Assert.Equal(NodeIcon.Loading, Assert.Single(node.Children).Icon);
        Assert.Equal("8.0.36", node.ServerVersion);
    }

    [Fact]
    public async Task Disconnect_ForgetsUnsavedPasswordSoTheNextExpandPrompts()
    {
        var prompt = new FakePasswordPrompt { Answer = new PasswordPromptResult("typed", savePassword: false) };
        var definition = TestExplorer.Definition(password: null);
        definition.SavePassword = false;
        var node = new ServerNodeViewModel(TestExplorer.Context(prompt: prompt), definition);
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        Assert.Equal("typed", node.Definition.Password);

        node.Disconnect();
        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal(2, prompt.Calls);
        Assert.Equal(ConnectionState.Connected, node.State);
    }

    [Fact]
    public async Task Disconnect_KeepsSavedPassword()
    {
        var prompt = new FakePasswordPrompt();
        var node = new ServerNodeViewModel(TestExplorer.Context(prompt: prompt), TestExplorer.Definition(password: "saved"));
        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        node.Disconnect();
        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        Assert.Equal("saved", node.Definition.Password);
        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public void Disconnect_WhenNotConnected_DoesNotCallProvider()
    {
        var metadata = new FakeMetadataProvider();
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());

        node.Disconnect();

        Assert.Equal(0, metadata.DisconnectCalls);
    }

    [Fact]
    public async Task Refresh_ReloadsExpandedServer()
    {
        var metadata = new FakeMetadataProvider();
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        var firstFolder = node.Children[0];

        await node.RefreshAsync();

        Assert.Equal(2, metadata.ConnectCalls);
        Assert.NotSame(firstFolder, node.Children[0]);
        Assert.True(node.IsExpanded);
    }

    [Fact]
    public async Task UpdateServer_DisconnectsConnectedServer()
    {
        var metadata = new FakeMetadataProvider();
        var explorer = new ExplorerViewModel(TestExplorer.Context(metadata));
        var node = explorer.AddServer(TestExplorer.Definition("A"));
        node.IsExpanded = true;
        await node.WaitForLoadAsync();

        explorer.UpdateServer(node, TestExplorer.Definition("B"));

        Assert.Equal(ConnectionState.NotConnected, node.State);
        Assert.False(node.IsExpanded);
        Assert.Equal(1, metadata.DisconnectCalls);
    }

    [Fact]
    public async Task DatabasesFolder_QueryFailure_KeepsServerConnected()
    {
        var metadata = new FakeMetadataProvider
        {
            Databases = _ => Task.FromException<IReadOnlyList<DatabaseInfo>>(new InvalidOperationException("SELECT command denied")),
        };
        var node = new ServerNodeViewModel(TestExplorer.Context(metadata), TestExplorer.Definition());
        node.IsExpanded = true;
        await node.WaitForLoadAsync();
        var folder = (FolderNodeViewModel)node.Children[0];

        folder.IsExpanded = true;
        await folder.WaitForLoadAsync();

        Assert.Equal(ConnectionState.Connected, node.State);
        Assert.Equal(LoadState.Failed, folder.LoadState);
        Assert.Equal("SELECT command denied", Assert.Single(folder.Children).Label);
    }

    [Theory]
    [InlineData("mysql", true)]
    [InlineData("INFORMATION_SCHEMA", true)]
    [InlineData("performance_schema", true)]
    [InlineData("sys", true)]
    [InlineData("shop", false)]
    [InlineData("mysql_backup", false)]
    [InlineData(null, false)]
    public void SystemDatabases_AreRecognized(string? name, bool expected)
    {
        Assert.Equal(expected, SystemDatabases.IsSystem(name));
    }
}
