using System;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>Grant on Database (US-1003): database, privileges and grant option, with the exact SQL.</summary>
public partial class GrantDialog : DialogWindow
{
    private readonly GrantDialogViewModel _model;

    /// <summary>Initializes a new instance of the <see cref="GrantDialog"/> class.</summary>
    public GrantDialog(GrantDialogViewModel model)
    {
        InitializeComponent();
        _model = model ?? throw new ArgumentNullException(nameof(model));
        DataContext = _model;
        PromptText.Text = Strings.Format(Strings.GrantPrompt, _model.Account.Label);
        Loaded += (_, _) => DatabaseBox.Focus();
    }

    /// <summary>Gets the GRANT statement, after the dialog was closed with Grant.</summary>
    public string Sql => _model.Sql;

    private void OnGrantClick(object sender, RoutedEventArgs e)
    {
        if (_model.CanGrant)
        {
            DialogResult = true;
        }
    }
}
