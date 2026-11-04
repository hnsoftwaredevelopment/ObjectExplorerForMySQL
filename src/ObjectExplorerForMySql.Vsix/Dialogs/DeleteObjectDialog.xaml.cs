using System;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>
/// Asks before an object is deleted (US-0901) and shows the exact SQL. Cancel is the default button, so Enter
/// does not delete; a database is only deleted after its name was typed.
/// </summary>
public partial class DeleteObjectDialog : DialogWindow
{
    private readonly DeleteConfirmation _model;

    /// <summary>Initializes a new instance of the <see cref="DeleteObjectDialog"/> class.</summary>
    public DeleteObjectDialog(DropTarget target)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        InitializeComponent();
        _model = new DeleteConfirmation(target);
        DataContext = _model;
        var kind = Strings.DropKind(target.Kind);
        Title = Strings.Format(Strings.DeleteDialogTitle, kind);
        PromptText.Text = Strings.Format(Strings.DeletePrompt, kind, target.DisplayName);
        if (_model.RequiresName)
        {
            NamePanel.Visibility = Visibility.Visible;
            NameLabel.Text = Strings.Format(Strings.DeleteTypeName, target.Name);
        }

        Loaded += (_, _) =>
        {
            if (_model.RequiresName)
            {
                NameBox.Focus();
            }
            else
            {
                CancelButton.Focus();
            }
        };
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => Clipboard.SetText(_model.Sql);

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_model.CanDelete)
        {
            DialogResult = true;
        }
    }
}
