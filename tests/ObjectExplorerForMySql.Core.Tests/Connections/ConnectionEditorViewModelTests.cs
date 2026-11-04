using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Connections;

public class ConnectionEditorViewModelTests
{
    private static ConnectionEditorViewModel CreateValid(IConnectionTester? tester = null) =>
        new(tester ?? new FakeTester(ConnectionTestResult.Success("8.0.36", TimeSpan.Zero)))
        {
            Host = " localhost ",
            UserName = " root ",
            Password = "pw",
        };

    [Fact]
    public void Defaults_PortAndTimeoutText()
    {
        var viewModel = new ConnectionEditorViewModel(new FakeTester(null));

        Assert.Equal("3306", viewModel.PortText);
        Assert.Equal("15", viewModel.TimeoutText);
        Assert.Equal(SslMode.Preferred, viewModel.SslMode);
        Assert.Equal(5, viewModel.SslModes.Count);
    }

    [Fact]
    public void TryCreateDefinition_TrimsInputAndCopiesEveryField()
    {
        var viewModel = CreateValid();
        viewModel.DisplayName = "  ";
        viewModel.PortText = " 3307 ";
        viewModel.DefaultDatabase = " shop ";
        viewModel.SslMode = SslMode.Required;
        viewModel.TimeoutText = "20";
        viewModel.AllowPublicKeyRetrieval = true;
        viewModel.SavePassword = false;

        Assert.True(viewModel.TryCreateDefinition(out var definition));

        Assert.Null(definition!.DisplayName);
        Assert.Equal("localhost", definition.Host);
        Assert.Equal(3307, definition.Port);
        Assert.Equal("root", definition.UserName);
        Assert.Equal("pw", definition.Password);
        Assert.Equal("shop", definition.DefaultDatabase);
        Assert.Equal(SslMode.Required, definition.SslMode);
        Assert.Equal(20, definition.ConnectionTimeoutSeconds);
        Assert.True(definition.AllowPublicKeyRetrieval);
        Assert.False(definition.SavePassword);
        Assert.Empty(viewModel.ValidationErrors);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("70000")]
    [InlineData("")]
    public void TryCreateDefinition_RejectsInvalidPortText(string portText)
    {
        var viewModel = CreateValid();
        viewModel.PortText = portText;

        Assert.False(viewModel.TryCreateDefinition(out var definition));
        Assert.Null(definition);
        Assert.Contains(ConnectionValidationError.PortInvalid, viewModel.ValidationErrors);
    }

    [Fact]
    public async Task TestConnectionAsync_StoresResultAndResetsIsTesting()
    {
        var tester = new FakeTester(ConnectionTestResult.Success("8.4.2", TimeSpan.Zero));
        var viewModel = CreateValid(tester);
        var testingStates = new List<bool>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionEditorViewModel.IsTesting))
            {
                testingStates.Add(viewModel.IsTesting);
            }
        };

        await viewModel.TestConnectionAsync(CancellationToken.None);

        Assert.Equal("8.4.2", viewModel.LastTestResult!.ServerVersion);
        Assert.Equal([true, false], testingStates);
        Assert.Equal(1, tester.Calls);
    }

    [Fact]
    public async Task TestConnectionAsync_DoesNotCallTesterForInvalidInput()
    {
        var tester = new FakeTester(ConnectionTestResult.Success("8.4.2", TimeSpan.Zero));
        var viewModel = new ConnectionEditorViewModel(tester);

        await viewModel.TestConnectionAsync(CancellationToken.None);

        Assert.Equal(0, tester.Calls);
        Assert.Null(viewModel.LastTestResult);
        Assert.Contains(ConnectionValidationError.HostRequired, viewModel.ValidationErrors);
    }

    [Fact]
    public async Task ChangingInput_ClearsTheLastTestResult()
    {
        var viewModel = CreateValid();
        await viewModel.TestConnectionAsync(CancellationToken.None);
        Assert.NotNull(viewModel.LastTestResult);

        viewModel.Host = "other";

        Assert.Null(viewModel.LastTestResult);
    }

    [Fact]
    public async Task TestConnectionAsync_ResetsIsTestingWhenCancelled()
    {
        var viewModel = CreateValid(new CancellingTester());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.TestConnectionAsync(CancellationToken.None));

        Assert.False(viewModel.IsTesting);
    }

    private static ConnectionDefinition Existing() => new()
    {
        DisplayName = "Prod",
        Host = "db.local",
        Port = 3307,
        UserName = "app",
        Password = "OldSecret",
        SavePassword = true,
        DefaultDatabase = "shop",
        SslMode = SslMode.Required,
        ConnectionTimeoutSeconds = 30,
        AllowPublicKeyRetrieval = true,
    };

    [Fact]
    public void EditMode_FillsFieldsButNotThePassword()
    {
        var viewModel = new ConnectionEditorViewModel(new FakeTester(null), Existing());

        Assert.True(viewModel.IsEditing);
        Assert.True(viewModel.HasExistingPassword);
        Assert.Equal("Prod", viewModel.DisplayName);
        Assert.Equal("db.local", viewModel.Host);
        Assert.Equal("3307", viewModel.PortText);
        Assert.Equal("app", viewModel.UserName);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal("shop", viewModel.DefaultDatabase);
        Assert.Equal(SslMode.Required, viewModel.SslMode);
        Assert.Equal("30", viewModel.TimeoutText);
        Assert.True(viewModel.AllowPublicKeyRetrieval);
        Assert.True(viewModel.SavePassword);
    }

    [Fact]
    public void EditMode_EmptyPasswordKeepsTheCurrentPasswordAndId()
    {
        var existing = Existing();
        var viewModel = new ConnectionEditorViewModel(new FakeTester(null), existing) { DisplayName = "Renamed" };

        Assert.True(viewModel.TryCreateDefinition(out var definition));

        Assert.Equal(existing.Id, definition!.Id);
        Assert.Equal("OldSecret", definition.Password);
        Assert.Equal("Renamed", definition.DisplayName);
    }

    [Fact]
    public void EditMode_TypedPasswordReplacesTheCurrentPassword()
    {
        var viewModel = new ConnectionEditorViewModel(new FakeTester(null), Existing()) { Password = "NewSecret" };

        Assert.True(viewModel.TryCreateDefinition(out var definition));

        Assert.Equal("NewSecret", definition!.Password);
    }

    [Fact]
    public void NewMode_IsNotEditingAndCreatesNewIds()
    {
        var viewModel = CreateValid();

        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.HasExistingPassword);
        Assert.True(viewModel.TryCreateDefinition(out var first));
        Assert.True(viewModel.TryCreateDefinition(out var second));
        Assert.NotEqual(first!.Id, second!.Id);
    }

    private sealed class FakeTester(ConnectionTestResult? result) : IConnectionTester
    {
        public int Calls { get; private set; }

        public Task<ConnectionTestResult> TestAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result!);
        }
    }

    private sealed class CancellingTester : IConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(ConnectionDefinition definition, CancellationToken cancellationToken) =>
            Task.FromCanceled<ConnectionTestResult>(new CancellationToken(canceled: true));
    }
}
