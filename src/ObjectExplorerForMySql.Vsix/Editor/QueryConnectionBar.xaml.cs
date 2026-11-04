using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.Editor;

/// <summary>Bar above a query document with the connection and database (US-0701).</summary>
public partial class QueryConnectionBar : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="QueryConnectionBar"/> class.</summary>
    public QueryConnectionBar()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the Execute button is clicked (US-0703).</summary>
    public event EventHandler? ExecuteRequested;

    /// <summary>Raised when the Explain button is clicked (US-1301).</summary>
    public event EventHandler? ExplainRequested;

    /// <summary>Raised when the Explain Analyze button is clicked (US-1302).</summary>
    public event EventHandler? ExplainAnalyzeRequested;

    /// <summary>Raised when the Cancel button is clicked.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>Connects the Execute and Cancel buttons to the runner of the document; without one they are hidden.</summary>
    internal void SetRunner(QueryRunner? runner)
    {
        RunButtons.DataContext = runner;
        RunButtons.Visibility = runner is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnExecuteClick(object sender, RoutedEventArgs e) => ExecuteRequested?.Invoke(this, EventArgs.Empty);

    private void OnExplainClick(object sender, RoutedEventArgs e) => ExplainRequested?.Invoke(this, EventArgs.Empty);

    private void OnExplainAnalyzeClick(object sender, RoutedEventArgs e) => ExplainAnalyzeRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancelClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    // The full list loads only when it is opened, so opening a document never asks for a password.
    private void OnDatabaseDropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is QueryConnectionBarViewModel bar)
        {
            _ = bar.EnsureDatabasesLoadedAsync();
        }
    }
}

/// <summary>Adds <see cref="QueryConnectionMargin"/> to every document view of the MySQL query editor.</summary>
[Export(typeof(IWpfTextViewMarginProvider))]
[Name(QueryConnectionMargin.MarginName)]
[MarginContainer(PredefinedMarginNames.Top)]
[ContentType(MySqlContentType.Name)]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class QueryConnectionMarginProvider : IWpfTextViewMarginProvider
{
#pragma warning disable CS0649 // Assigned by MEF.
    [Import]
    private ITextDocumentFactoryService? _documentFactory;
#pragma warning restore CS0649

    /// <inheritdoc />
    public IWpfTextViewMargin? CreateMargin(IWpfTextViewHost wpfTextViewHost, IWpfTextViewMargin marginContainer)
    {
        if (QueryEditorServices.Explorer is not { } explorer)
        {
            ExtensionLog.Logger.Warning("The query editor bar is not shown: the package is not loaded.");
            return null;
        }

        if (_documentFactory is null || !_documentFactory.TryGetTextDocument(wpfTextViewHost.TextView.TextDataModel.DocumentBuffer, out var document))
        {
            ExtensionLog.Logger.Warning("The query editor bar is not shown: the document has no file.");
            return null;
        }

        return new QueryConnectionMargin(wpfTextViewHost.TextView, document, explorer);
    }
}

/// <summary>Editor margin that hosts <see cref="QueryConnectionBar"/>.</summary>
internal sealed class QueryConnectionMargin : IWpfTextViewMargin
{
    /// <summary>Name of the margin.</summary>
    public const string MarginName = "MySqlQueryConnectionBar";

    private readonly IWpfTextView _view;
    private readonly ITextDocument _document;
    private readonly QueryConnectionBarViewModel _viewModel;
    private readonly QueryConnectionBar _bar;
    private readonly QueryRunner? _runner;
    private bool _disposed;

    public QueryConnectionMargin(IWpfTextView view, ITextDocument document, ExplorerViewModel explorer)
    {
        _view = view;
        _document = document;
        _viewModel = new QueryConnectionBarViewModel(explorer, explorer.Documents, document.FilePath, ExtensionLog.Logger);
        _bar = new QueryConnectionBar { DataContext = _viewModel };
        _runner = QueryEditorServices.GetRunner(view.TextDataModel.DocumentBuffer);
        _bar.SetRunner(_runner);
        _bar.ExecuteRequested += OnExecuteRequested;
        _bar.ExplainRequested += OnExplainRequested;
        _bar.ExplainAnalyzeRequested += OnExplainAnalyzeRequested;
        _bar.CancelRequested += OnCancelRequested;
        _document.FileActionOccurred += OnFileActionOccurred;
    }

    /// <inheritdoc />
    public FrameworkElement VisualElement => _bar;

    /// <inheritdoc />
    public double MarginSize => _bar.ActualHeight;

    /// <inheritdoc />
    public bool Enabled => true;

    /// <inheritdoc />
    public ITextViewMargin? GetTextViewMargin(string marginName) =>
        string.Equals(marginName, MarginName, StringComparison.OrdinalIgnoreCase) ? this : null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bar.ExecuteRequested -= OnExecuteRequested;
        _bar.ExplainRequested -= OnExplainRequested;
        _bar.ExplainAnalyzeRequested -= OnExplainAnalyzeRequested;
        _bar.CancelRequested -= OnCancelRequested;
        _document.FileActionOccurred -= OnFileActionOccurred;
        _viewModel.Dispose();
    }

    private void OnExecuteRequested(object? sender, EventArgs e) => QueryEditorServices.Execute(_view);

    private void OnExplainRequested(object? sender, EventArgs e) => QueryEditorServices.Explain(_view);

    private void OnExplainAnalyzeRequested(object? sender, EventArgs e) => QueryEditorServices.Explain(_view, analyze: true);

    private void OnCancelRequested(object? sender, EventArgs e) => _runner?.Cancel();

    // Save As or a rename in Solution Explorer: the link moves with the file.
    private void OnFileActionOccurred(object? sender, TextDocumentFileActionEventArgs e)
    {
        if ((e.FileActionType & FileActionTypes.DocumentRenamed) != 0)
        {
            _viewModel.Rename(e.FilePath);
        }
    }
}
