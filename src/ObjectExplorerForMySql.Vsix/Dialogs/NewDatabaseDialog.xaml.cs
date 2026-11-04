using System;
using System.ComponentModel;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Management;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>New Database (US-0903): name, character set and collation; shows the statement that runs.</summary>
public partial class NewDatabaseDialog : DialogWindow
{
    private readonly NewDatabaseViewModel _model;

    /// <summary>Initializes a new instance of the <see cref="NewDatabaseDialog"/> class.</summary>
    public NewDatabaseDialog(CharacterSetCatalog catalog)
    {
        InitializeComponent();
        _model = new NewDatabaseViewModel(catalog ?? throw new ArgumentNullException(nameof(catalog)));
        _model.PropertyChanged += OnModelPropertyChanged;
        DataContext = _model;
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Gets the name to create, after the dialog was closed with Create.</summary>
    public string DatabaseName => _model.Name;

    /// <summary>Gets the statement to run, after the dialog was closed with Create.</summary>
    public string Sql => _model.Sql;

    // An empty name only disables Create; the other problems get a line under the name.
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NewDatabaseViewModel.Problem))
        {
            return;
        }

        var text = _model.Problem switch
        {
            NameProblem.Exists => Strings.NewDatabaseExists,
            NameProblem.TooLong => Strings.Format(Strings.RenameNameTooLong, NameRules.MaxLength),
            NameProblem.EndsWithSpace => Strings.RenameNameEndsWithSpace,
            _ => null,
        };
        ProblemText.Text = text ?? string.Empty;
        ProblemText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (_model.CanCreate)
        {
            DialogResult = true;
        }
    }
}
