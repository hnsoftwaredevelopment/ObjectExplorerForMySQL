using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>Asks before a statement runs and shows it (US-1003 Revoke). Enter and Escape cancel.</summary>
public partial class ConfirmSqlDialog : DialogWindow
{
    /// <summary>Initializes a new instance of the <see cref="ConfirmSqlDialog"/> class.</summary>
    public ConfirmSqlDialog(string title, string prompt, string sql, string okText)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        SqlBox.Text = sql;
        OkButton.Content = okText;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
