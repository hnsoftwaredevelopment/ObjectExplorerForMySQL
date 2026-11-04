using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>Asks for the password of a connection on the first expand when none is available.</summary>
public partial class PasswordDialog : DialogWindow
{
    /// <summary>Initializes a new instance of the <see cref="PasswordDialog"/> class.</summary>
    public PasswordDialog(ConnectionDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        InitializeComponent();
        PromptText.Text = Strings.Format(Strings.PasswordPromptText, definition.Label);
        UserBox.Text = definition.UserName;
        SavePasswordBox.IsChecked = definition.SavePassword;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>Gets the answer after the dialog was closed with Connect.</summary>
    public PasswordPromptResult? Result { get; private set; }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        Result = new PasswordPromptResult(PasswordBox.Password, SavePasswordBox.IsChecked == true);
        DialogResult = true;
    }
}

/// <summary><see cref="IPasswordPrompt"/> that shows <see cref="PasswordDialog"/> on the UI thread.</summary>
internal sealed class PasswordPromptService : IPasswordPrompt
{
    /// <inheritdoc />
    public async Task<PasswordPromptResult?> PromptAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dialog = new PasswordDialog(definition);
        return dialog.ShowModal() == true ? dialog.Result : null;
    }
}
