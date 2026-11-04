using System;
using System.ComponentModel;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Management;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>New Role (US-1004): name and, on MySQL, host; shows the CREATE ROLE statement.</summary>
public partial class RoleDialog : DialogWindow
{
    private readonly RoleDialogViewModel _model;

    /// <summary>Initializes a new instance of the <see cref="RoleDialog"/> class.</summary>
    public RoleDialog(RoleDialogViewModel model)
    {
        InitializeComponent();
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _model.PropertyChanged += OnModelPropertyChanged;
        DataContext = _model;
        if (_model.IsMariaDb)
        {
            HostLabel.Visibility = HostPanel.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Gets the view model with the statement, after the dialog was closed with Create.</summary>
    public RoleDialogViewModel Model => _model;

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RoleDialogViewModel.Problem))
        {
            ProblemText.Visibility = _model.Problem == UserProblem.Exists ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (_model.CanRun)
        {
            DialogResult = true;
        }
    }
}
