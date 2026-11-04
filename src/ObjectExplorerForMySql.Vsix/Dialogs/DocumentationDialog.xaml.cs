using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using ObjectExplorerForMySql.Core.Documentation;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>The format (US-1202) and what Generate Documentation includes (US-1201); the file is chosen next.</summary>
public partial class DocumentationDialog : DialogWindow
{
    // The format chosen last time, for the rest of the Visual Studio session.
    private static bool _lastPdf;

    /// <summary>Initializes a new instance of the <see cref="DocumentationDialog"/> class.</summary>
    public DocumentationDialog(string database, string connectionLabel)
    {
        InitializeComponent();
        PromptText.Text = Strings.Format(Strings.DocumentationPrompt, database, connectionLabel);
        foreach (var box in new[] { TablesBox, ViewsBox, RoutinesBox, EventsBox })
        {
            box.Click += (_, _) => UpdateButton();
        }

        MarkdownButton.IsChecked = !_lastPdf;
        PdfButton.IsChecked = _lastPdf;
        Loaded += (_, _) => (_lastPdf ? PdfButton : MarkdownButton).Focus();
    }

    /// <summary>Gets the chosen parts, or null before Generate.</summary>
    public DocumentOptions? Options { get; private set; }

    /// <summary>Gets a value indicating whether a PDF is chosen instead of Markdown.</summary>
    public bool IsPdf { get; private set; }

    // Nothing chosen: nothing to generate.
    private void UpdateButton() =>
        GenerateButton.IsEnabled = Checked(TablesBox) || Checked(ViewsBox) || Checked(RoutinesBox) || Checked(EventsBox);

    private static bool Checked(CheckBox box) => box.IsChecked == true;

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        Options = new DocumentOptions
        {
            IncludeTables = Checked(TablesBox),
            IncludeViews = Checked(ViewsBox),
            IncludeRoutines = Checked(RoutinesBox),
            IncludeEvents = Checked(EventsBox),
            IncludeDependencies = Checked(DependenciesBox),
            IncludeDiagrams = Checked(DependenciesBox) && Checked(DiagramsBox),
        };
        IsPdf = PdfButton.IsChecked == true;
        _lastPdf = IsPdf;
        DialogResult = true;
    }
}
