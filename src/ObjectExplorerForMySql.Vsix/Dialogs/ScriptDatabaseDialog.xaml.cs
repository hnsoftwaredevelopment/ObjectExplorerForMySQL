using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>Options of Script Database (US-0603): CREATE DATABASE and USE, with or without data (D-7), window or file.</summary>
public partial class ScriptDatabaseDialog : DialogWindow
{
    /// <summary>Initializes a new instance of the <see cref="ScriptDatabaseDialog"/> class.</summary>
    public ScriptDatabaseDialog(string database, string connectionLabel)
    {
        InitializeComponent();
        PromptText.Text = Strings.Format(Strings.ScriptDatabasePrompt, database, connectionLabel);
        Loaded += (_, _) => IncludeCreateBox.Focus();
    }

    /// <summary>Gets a value indicating whether the script starts with CREATE DATABASE and USE.</summary>
    public bool IncludeCreateDatabase { get; private set; }

    /// <summary>Gets a value indicating whether the script contains the data.</summary>
    public bool IncludeData { get; private set; }

    /// <summary>Gets a value indicating whether the script goes to a file instead of a new query window.</summary>
    public bool ToFile { get; private set; }

    private void OnScriptClick(object sender, RoutedEventArgs e)
    {
        IncludeCreateDatabase = IncludeCreateBox.IsChecked == true;
        IncludeData = IncludeDataBox.IsChecked == true;
        ToFile = ToFileButton.IsChecked == true;
        DialogResult = true;
    }
}
