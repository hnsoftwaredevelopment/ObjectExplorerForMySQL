using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>Grant Role (US-1005): a role for a user, optionally as default role, with the exact SQL.</summary>
public partial class GrantRoleDialog : DialogWindow
{
    private readonly GrantRoleDialogViewModel _model;

    /// <summary>Initializes a new instance of the <see cref="GrantRoleDialog"/> class.</summary>
    public GrantRoleDialog(GrantRoleDialogViewModel model)
    {
        InitializeComponent();
        _model = model ?? throw new ArgumentNullException(nameof(model));
        DataContext = _model;
        PromptText.Text = Strings.Format(Strings.GrantRolePrompt, _model.User.Label);
        UnknownText.Visibility = _model.DefaultRolesUnknown ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => RoleBox.Focus();
    }

    /// <summary>Gets the statements to run, after the dialog was closed with Grant.</summary>
    public IReadOnlyList<string> Statements => _model.Statements;

    private void OnGrantClick(object sender, RoutedEventArgs e)
    {
        if (_model.CanGrant)
        {
            DialogResult = true;
        }
    }
}
