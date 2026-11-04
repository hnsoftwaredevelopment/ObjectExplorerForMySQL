using System;
using System.Linq;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Explorer;

public class ExplorerViewModelTests
{
    private static ConnectionDefinition Named(string name) => new() { DisplayName = name, Host = "h", UserName = "u" };

    [Fact]
    public void AddServer_KeepsServersSortedCaseInsensitive()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());

        explorer.AddServer(new ConnectionDefinition { DisplayName = "test" });
        explorer.AddServer(new ConnectionDefinition { DisplayName = "Acceptance" });
        explorer.AddServer(new ConnectionDefinition { DisplayName = "production" });
        explorer.AddServer(new ConnectionDefinition { Host = "localhost", UserName = "root" });

        Assert.Equal(["Acceptance", "localhost:3306 (root)", "production", "test"], explorer.Servers.Select(s => s.Label));
    }

    [Fact]
    public void IsEmpty_ChangesAfterFirstServer()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());
        var raised = false;
        explorer.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(ExplorerViewModel.IsEmpty);

        Assert.True(explorer.IsEmpty);
        explorer.AddServer(new ConnectionDefinition { Host = "h", UserName = "u" });

        Assert.False(explorer.IsEmpty);
        Assert.True(raised);
    }

    [Theory]
    [InlineData("A", "B", "C", "A", "Z", new[] { "B", "C", "Z" })]
    [InlineData("A", "B", "C", "C", "0", new[] { "0", "A", "B" })]
    [InlineData("A", "B", "C", "B", "Bb", new[] { "A", "Bb", "C" })]
    [InlineData("A", "B", "C", "A", "Ba", new[] { "B", "Ba", "C" })]
    public void UpdateServer_MovesNodeToSortedPosition(string first, string second, string third, string renamed, string newName, string[] expected)
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());
        explorer.AddServer(Named(first));
        explorer.AddServer(Named(second));
        explorer.AddServer(Named(third));
        var node = explorer.Servers.Single(s => s.Label == renamed);

        explorer.UpdateServer(node, Named(newName));

        Assert.Equal(expected, explorer.Servers.Select(s => s.Label));
        Assert.Contains(node, explorer.Servers);
    }

    [Fact]
    public void UpdateServer_KeepsIdAndRaisesLabelChanged()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());
        var node = explorer.AddServer(Named("Old"));
        var id = node.Definition.Id;
        var labelChanged = false;
        node.PropertyChanged += (_, e) => labelChanged |= e.PropertyName == nameof(ServerNodeViewModel.Label);

        explorer.UpdateServer(node, Named("New"));

        Assert.Equal(id, node.Definition.Id);
        Assert.Equal("New", node.Label);
        Assert.True(labelChanged);
    }

    [Fact]
    public void UpdateServer_RejectsUnknownNode()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());

        Assert.Throws<ArgumentException>(() => explorer.UpdateServer(new ServerNodeViewModel(TestExplorer.Context(), Named("X")), Named("Y")));
    }

    [Fact]
    public void RemoveServer_RemovesNodeClearsSelectionAndUpdatesIsEmpty()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());
        var node = explorer.AddServer(Named("A"));
        explorer.SelectedNode = node;

        Assert.True(explorer.RemoveServer(node));

        Assert.Empty(explorer.Servers);
        Assert.Null(explorer.SelectedServer);
        Assert.True(explorer.IsEmpty);
        Assert.False(explorer.RemoveServer(node));
    }

    [Fact]
    public void RemoveServer_KeepsSelectionOfOtherNode()
    {
        var explorer = new ExplorerViewModel(TestExplorer.Context());
        var a = explorer.AddServer(Named("A"));
        var b = explorer.AddServer(Named("B"));
        explorer.SelectedNode = b;

        explorer.RemoveServer(a);

        Assert.Same(b, explorer.SelectedServer);
    }
}
