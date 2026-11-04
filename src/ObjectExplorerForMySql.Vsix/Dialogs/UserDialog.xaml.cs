using System;
using System.ComponentModel;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>New User and Change Password (US-1003). The password only lives in the PasswordBoxes and the view model.</summary>
public partial class UserDialog : DialogWindow
{
    private readonly UserDialogViewModel _model;

    /// <summary>Initializes a new instance of the <see cref="UserDialog"/> class.</summary>
    public UserDialog(UserDialogViewModel model)
    {
        InitializeComponent();
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _model.PropertyChanged += OnModelPropertyChanged;
        DataContext = _model;
        if (_model.IsNew)
        {
            Title = Strings.NewUserTitle;
            OkButton.Content = Strings.ButtonCreate;
            Loaded += (_, _) => NameBox.Focus();
        }
        else
        {
            Title = Strings.ChangePasswordTitle;
            OkButton.Content = Strings.ButtonChange;
            PromptText.Text = Strings.Format(Strings.ChangePasswordPrompt, _model.Account!.Label);
            PromptText.Visibility = Visibility.Visible;
            NameLabel.Visibility = NameBox.Visibility = HostLabel.Visibility = HostPanel.Visibility = Visibility.Collapsed;
            Loaded += (_, _) => PasswordBox.Focus();
        }
    }

    /// <summary>Gets the view model with the statement to run, after the dialog was closed with OK.</summary>
    public UserDialogViewModel Model => _model;

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _model.Password = PasswordBox.Password;
        _model.ConfirmPassword = ConfirmBox.Password;
    }

    // Only problems the user can act on get a line; an empty name or password only disables OK.
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserDialogViewModel.Problem))
        {
            return;
        }

        var text = _model.Problem switch
        {
            UserProblem.Exists => Strings.UserExists,
            UserProblem.PasswordMismatch when ConfirmBox.Password.Length > 0 => Strings.UserPasswordMismatch,
            _ => null,
        };
        ProblemText.Text = text ?? string.Empty;
        ProblemText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (_model.CanRun)
        {
            DialogResult = true;
        }
    }
}
