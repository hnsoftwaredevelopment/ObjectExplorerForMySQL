using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ObjectExplorerForMySql.Core.Connections;

/// <summary>
/// View model of the "Add MySQL Server" dialog. Holds the input as text, validates it and runs
/// the connection test. Contains no UI types, so it is unit tested in Core.
/// </summary>
public sealed class ConnectionEditorViewModel : ObservableObject
{
    private readonly IConnectionTester _tester;

    private string _displayName = string.Empty;
    private string _host = string.Empty;
    private string _portText = ConnectionDefinition.DefaultPort.ToString(CultureInfo.InvariantCulture);
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private bool _savePassword = true;
    private string _defaultDatabase = string.Empty;
    private SslMode _sslMode = SslMode.Preferred;
    private string _timeoutText = ConnectionDefinition.DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
    private bool _allowPublicKeyRetrieval;
    private bool _isTesting;
    private ConnectionTestResult? _lastTestResult;
    private IReadOnlyList<ConnectionValidationError> _validationErrors = Array.Empty<ConnectionValidationError>();

    private readonly Guid? _existingId;
    private readonly string? _existingPassword;

    /// <summary>Initializes a new instance of the <see cref="ConnectionEditorViewModel"/> class for a new connection.</summary>
    public ConnectionEditorViewModel(IConnectionTester tester)
    {
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionEditorViewModel"/> class to edit
    /// <paramref name="existing"/>. The password field starts empty; leaving it empty keeps the current password.
    /// </summary>
    public ConnectionEditorViewModel(IConnectionTester tester, ConnectionDefinition existing)
        : this(tester)
    {
        if (existing is null)
        {
            throw new ArgumentNullException(nameof(existing));
        }

        _existingId = existing.Id;
        _existingPassword = existing.Password;
        _displayName = existing.DisplayName ?? string.Empty;
        _host = existing.Host;
        _portText = existing.Port.ToString(CultureInfo.InvariantCulture);
        _userName = existing.UserName;
        _savePassword = existing.SavePassword;
        _defaultDatabase = existing.DefaultDatabase ?? string.Empty;
        _sslMode = existing.SslMode;
        _timeoutText = existing.ConnectionTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _allowPublicKeyRetrieval = existing.AllowPublicKeyRetrieval;
    }

    /// <summary>Gets a value indicating whether an existing connection is edited.</summary>
    public bool IsEditing => _existingId.HasValue;

    /// <summary>Gets a value indicating whether the edited connection has a password that is kept when the field stays empty.</summary>
    public bool HasExistingPassword => !string.IsNullOrEmpty(_existingPassword);

    /// <summary>Gets the SSL modes the user can choose from.</summary>
    public IReadOnlyList<SslMode> SslModes { get; } = (SslMode[])Enum.GetValues(typeof(SslMode));

    /// <summary>Gets or sets the optional display name.</summary>
    public string DisplayName { get => _displayName; set => SetInput(ref _displayName, value); }

    /// <summary>Gets or sets the host.</summary>
    public string Host { get => _host; set => SetInput(ref _host, value); }

    /// <summary>Gets or sets the port as typed.</summary>
    public string PortText { get => _portText; set => SetInput(ref _portText, value); }

    /// <summary>Gets or sets the user name.</summary>
    public string UserName { get => _userName; set => SetInput(ref _userName, value); }

    /// <summary>Gets or sets the password. Set from the dialog's PasswordBox; never bound to visible text.</summary>
    public string Password { get => _password; set => SetInput(ref _password, value); }

    /// <summary>Gets or sets a value indicating whether the password may be stored.</summary>
    public bool SavePassword { get => _savePassword; set => SetProperty(ref _savePassword, value); }

    /// <summary>Gets or sets the optional default database.</summary>
    public string DefaultDatabase { get => _defaultDatabase; set => SetInput(ref _defaultDatabase, value); }

    /// <summary>Gets or sets the SSL mode.</summary>
    public SslMode SslMode { get => _sslMode; set => SetInput(ref _sslMode, value); }

    /// <summary>Gets or sets the connection timeout in seconds as typed.</summary>
    public string TimeoutText { get => _timeoutText; set => SetInput(ref _timeoutText, value); }

    /// <summary>Gets or sets a value indicating whether public key retrieval is allowed.</summary>
    public bool AllowPublicKeyRetrieval { get => _allowPublicKeyRetrieval; set => SetInput(ref _allowPublicKeyRetrieval, value); }

    /// <summary>Gets a value indicating whether a connection test is running.</summary>
    public bool IsTesting { get => _isTesting; private set => SetProperty(ref _isTesting, value); }

    /// <summary>Gets the result of the last test; reset when the input changes.</summary>
    public ConnectionTestResult? LastTestResult { get => _lastTestResult; private set => SetProperty(ref _lastTestResult, value); }

    /// <summary>Gets the problems found by the last <see cref="TryCreateDefinition"/> or test.</summary>
    public IReadOnlyList<ConnectionValidationError> ValidationErrors
    {
        get => _validationErrors;
        private set => SetProperty(ref _validationErrors, value);
    }

    /// <summary>
    /// Validates the input and creates a definition. Returns false and fills <see cref="ValidationErrors"/>
    /// when the input is not valid.
    /// </summary>
    public bool TryCreateDefinition(out ConnectionDefinition? definition)
    {
        var errors = new List<ConnectionValidationError>();

        var portValid = int.TryParse(PortText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port);
        var timeoutValid = int.TryParse(TimeoutText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var timeout);

        var candidate = new ConnectionDefinition
        {
            Id = _existingId ?? Guid.NewGuid(),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
            Host = Host.Trim(),
            Port = portValid ? port : 0,
            UserName = UserName.Trim(),
            Password = string.IsNullOrEmpty(Password) ? _existingPassword : Password,
            SavePassword = SavePassword,
            DefaultDatabase = string.IsNullOrWhiteSpace(DefaultDatabase) ? null : DefaultDatabase.Trim(),
            SslMode = SslMode,
            ConnectionTimeoutSeconds = timeoutValid ? timeout : 0,
            AllowPublicKeyRetrieval = AllowPublicKeyRetrieval,
        };

        errors.AddRange(candidate.Validate());
        ValidationErrors = errors;

        definition = errors.Count == 0 ? candidate : null;
        return definition is not null;
    }

    /// <summary>
    /// Validates the input and tests the connection. Does nothing while a test is running.
    /// Throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (IsTesting)
        {
            return;
        }

        LastTestResult = null;
        if (!TryCreateDefinition(out var definition))
        {
            return;
        }

        IsTesting = true;
        try
        {
            var result = await _tester.TestAsync(definition!, cancellationToken).ConfigureAwait(true);
            LastTestResult = result;
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void SetInput<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            // A result belongs to the input it was made with.
            LastTestResult = null;
        }
    }
}
