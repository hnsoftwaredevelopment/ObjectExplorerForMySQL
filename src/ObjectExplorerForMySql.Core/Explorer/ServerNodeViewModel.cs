using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Metadata;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Explorer;

/// <summary>Connection state of a server node (US-0204).</summary>
public enum ConnectionState
{
    /// <summary>Not connected.</summary>
    NotConnected,

    /// <summary>Connecting.</summary>
    Connecting,

    /// <summary>Connected.</summary>
    Connected,

    /// <summary>The last connection attempt failed.</summary>
    Error,
}

/// <summary>Node for one MySQL server. Expanding it connects and shows the "Databases" folder.</summary>
public sealed class ServerNodeViewModel : TreeNodeViewModel
{
    private ConnectionDefinition _definition;
    private ConnectionState _state;
    private string? _serverVersion;
    private string? _currentUser;
    private string? _lastError;
    private ServerInfo? _info;
    private AccountPrivileges? _privileges;

    /// <summary>Initializes a new instance of the <see cref="ServerNodeViewModel"/> class.</summary>
    public ServerNodeViewModel(ExplorerContext context, ConnectionDefinition definition)
        : base(context, canHaveChildren: true)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>Raised when the connection definition changed through the password prompt and must be saved.</summary>
    public event EventHandler? DefinitionChanged;

    /// <summary>Gets the connection of this server. Replaced through <see cref="ExplorerViewModel.UpdateServer"/>; that disconnects.</summary>
    public ConnectionDefinition Definition
    {
        get => _definition;
        internal set
        {
            var newValue = value ?? throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(_definition, newValue))
            {
                return;
            }

            Disconnect();
            _definition = newValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(UserName));
        }
    }

    /// <inheritdoc />
    public override string Label => Definition.Label;

    /// <summary>Gets the user name of the connection.</summary>
    public string UserName => Definition.UserName;

    /// <summary>Gets the connection state.</summary>
    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(CanDisconnect));
                OnPropertyChanged(nameof(CanRefresh));
            }
        }
    }

    /// <summary>Gets the server version of the last successful connection.</summary>
    public string? ServerVersion
    {
        get => _serverVersion;
        private set => SetProperty(ref _serverVersion, value);
    }

    /// <summary>Gets the account the server authenticated, for example "app@%".</summary>
    public string? CurrentUser
    {
        get => _currentUser;
        private set => SetProperty(ref _currentUser, value);
    }

    /// <summary>Gets the error of the last failed connection attempt (password masked), or null.</summary>
    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (SetProperty(ref _lastError, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>Gets the server information of the current connection, or null when not connected (US-0402).</summary>
    public ServerInfo? Info
    {
        get => _info;
        private set => SetProperty(ref _info, value);
    }

    /// <summary>
    /// Gets what the account may do with users (US-1001), decided after the last connect; null when not connected or
    /// when no check is configured.
    /// </summary>
    public AccountPrivileges? Privileges
    {
        get => _privileges;
        private set => SetProperty(ref _privileges, value);
    }

    /// <inheritdoc />
    public override object? CreateProperties() => new ObjectProperties.ServerProperties(this);

    /// <summary>Gets a value indicating whether <see cref="LastError"/> is set.</summary>
    public bool HasError => LastError is not null;

    /// <summary>Gets a value indicating whether Disconnect is possible.</summary>
    public bool CanDisconnect => State is ConnectionState.Connected or ConnectionState.Error;

    /// <summary>Gets a value indicating whether Refresh is possible: only when connected.</summary>
    public override bool CanRefresh => State == ConnectionState.Connected;

    /// <inheritdoc />
    public override NodeIcon Icon => State switch
    {
        ConnectionState.Connecting => NodeIcon.ServerConnecting,
        ConnectionState.Connected => NodeIcon.ServerConnected,
        ConnectionState.Error => NodeIcon.ServerError,
        _ => NodeIcon.ServerDisconnected,
    };

    /// <summary>
    /// Closes the connections, removes the loaded children and collapses the node. A password that is
    /// not saved is forgotten, so the next expand asks for it again (decision 2026-10-27, US-0204).
    /// </summary>
    public void Disconnect()
    {
        if (!Definition.SavePassword)
        {
            Definition.Password = null;
        }

        var wasConnected = State != ConnectionState.NotConnected;
        Collapse();
        if (wasConnected)
        {
            try
            {
                Context.Metadata.Disconnect(Definition);
            }
            catch (Exception ex)
            {
                Context.Logger.Warning($"Closing the connections of '{Label}' failed.", ex);
            }
        }

        State = ConnectionState.NotConnected;
        LastError = null;
        Info = null;
        Privileges = null;
    }

    /// <summary>
    /// Asks for the password when it is not known yet (not saved, or forgotten after Disconnect or a wrong password).
    /// Returns false when the user cancelled the prompt. Used by the tree and the query editor (US-0701).
    /// </summary>
    public async Task<bool> EnsurePasswordAsync(CancellationToken cancellationToken)
    {
        if (Definition.Password is not null)
        {
            return true;
        }

        var answer = await Context.PasswordPrompt.PromptAsync(Definition, cancellationToken);
        if (answer is null)
        {
            return false;
        }

        Definition.Password = answer.Password;
        var saveChanged = Definition.SavePassword != answer.SavePassword;
        Definition.SavePassword = answer.SavePassword;
        if (answer.SavePassword || saveChanged)
        {
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<TreeNodeViewModel>> LoadChildrenAsync(CancellationToken cancellationToken)
    {
        if (!await EnsurePasswordAsync(cancellationToken))
        {
            State = ConnectionState.NotConnected;
            throw new LoadAbortedException();
        }

        State = ConnectionState.Connecting;
        LastError = null;
        try
        {
            var info = await Context.Metadata.ConnectAsync(Definition, cancellationToken);

            // US-1001: what the account may do with users; never fails the connect.
            Privileges = Context.PrivilegeChecker is { } checker
                ? await checker.CheckAsync(Definition, info.Version, cancellationToken)
                : null;
            if (Privileges is { } privileges)
            {
                Context.Logger.Info($"Account {info.CurrentUser} on '{Label}': read users {(privileges.CanReadUsers ? "yes" : "no")}, manage users {(privileges.CanManageUsers ? "yes" : "no")} (decided by {privileges.Source}).");
            }

            Info = info;
            ServerVersion = info.Version;
            CurrentUser = info.CurrentUser;
            State = ConnectionState.Connected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = ConnectionState.NotConnected;
            throw;
        }

        // US-1002: Security after Databases, as in SQL Server Object Explorer, only when the account may read users.
        return SecurityFolder.Create(Context, this) is { } security
            ? [FolderNodeViewModel.CreateDatabasesFolder(Context, this), security]
            : [FolderNodeViewModel.CreateDatabasesFolder(Context, this)];
    }

    /// <inheritdoc />
    protected override void OnLoadFailed(Exception exception)
    {
        State = ConnectionState.Error;
        LastError = SensitiveDataMasker.MaskText(exception.Message);

        // Wrong password: forget it, so the next expand asks for it again.
        if (exception is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.AccessDenied })
        {
            Definition.Password = null;
            Context.Logger.Info($"Access denied for '{Label}'; the password will be asked on the next expand.");
        }
    }
}
