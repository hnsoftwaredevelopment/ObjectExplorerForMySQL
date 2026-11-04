using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Logging;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>"Add MySQL Server" dialog. Logic lives in <see cref="ConnectionEditorViewModel"/>.</summary>
public partial class ConnectionDialog : DialogWindow
{
    private readonly ConnectionEditorViewModel _viewModel;
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Initializes a new instance of the <see cref="ConnectionDialog"/> class.</summary>
    public ConnectionDialog(ConnectionEditorViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        InitializeComponent();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_viewModel.IsEditing)
        {
            Title = Strings.ConnectionPropertiesDialogTitle;
        }

        KeepPasswordHint.Visibility = _viewModel.HasExistingPassword ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => DisplayNameBox.Focus();
        Closed += OnClosed;
    }

    /// <summary>Gets the connection entered by the user after the dialog was closed with OK.</summary>
    public ConnectionDefinition? Result { get; private set; }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = PasswordBox.Password;

    private void OnTestConnectionClick(object sender, RoutedEventArgs e) => _ = TestConnectionAsync();

    private async Task TestConnectionAsync()
    {
        try
        {
            await _viewModel.TestConnectionAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Dialog was closed during the test.
        }
        catch (Exception ex)
        {
            ExtensionLog.Logger.Error("Connection test failed unexpectedly.", ex);
            ShowStatus(KnownMonikers.StatusError, Strings.Format(Strings.TestConnectionFailed, ex.Message));
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TryCreateDefinition(out var definition))
        {
            Result = definition;
            DialogResult = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionEditorViewModel.IsTesting)
            or nameof(ConnectionEditorViewModel.LastTestResult)
            or nameof(ConnectionEditorViewModel.ValidationErrors))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        TestButton.IsEnabled = !_viewModel.IsTesting;
        OkButton.IsEnabled = !_viewModel.IsTesting;

        if (_viewModel.IsTesting)
        {
            ShowStatus(KnownMonikers.StatusInformation, Strings.TestConnectionRunning);
        }
        else if (_viewModel.ValidationErrors.Count > 0)
        {
            ShowStatus(KnownMonikers.StatusError, string.Join(Environment.NewLine, _viewModel.ValidationErrors.Select(ValidationText)));
        }
        else if (_viewModel.LastTestResult is { } result)
        {
            if (!result.Succeeded)
            {
                ShowStatus(KnownMonikers.StatusError, Strings.Format(Strings.TestConnectionFailed, result.ErrorMessage));
            }
            else if (!result.IsSupportedVersion)
            {
                ShowStatus(KnownMonikers.StatusWarning, Strings.Format(Strings.TestConnectionUnsupportedVersion, result.ServerVersion));
            }
            else
            {
                ShowStatus(KnownMonikers.StatusOK, Strings.Format(Strings.TestConnectionSucceeded, result.ServerVersion));
            }
        }
        else
        {
            StatusPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowStatus(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker moniker, string text)
    {
        StatusImage.Moniker = moniker;
        StatusText.Text = text;
        StatusPanel.Visibility = Visibility.Visible;
    }

    private static string ValidationText(ConnectionValidationError error) => error switch
    {
        ConnectionValidationError.HostRequired => Strings.ValidationHostRequired,
        ConnectionValidationError.PortInvalid => Strings.ValidationPortInvalid,
        ConnectionValidationError.UserRequired => Strings.ValidationUserRequired,
        ConnectionValidationError.TimeoutInvalid => Strings.ValidationTimeoutInvalid,
        _ => error.ToString(),
    };

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
