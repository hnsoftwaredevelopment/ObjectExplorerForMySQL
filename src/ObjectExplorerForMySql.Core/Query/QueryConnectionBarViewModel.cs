using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// The bar above a query document (US-0701): the connection and database the document runs on. A choice is stored in
/// <see cref="DocumentConnections"/> right away. The full database list loads when a server is picked or the database
/// list is opened, so opening a document never asks for a password by itself.
/// </summary>
public sealed class QueryConnectionBarViewModel : ObservableObject, IDisposable
{
    private readonly ExplorerViewModel _explorer;
    private readonly DocumentConnections _documents;
    private readonly IExtensionLogger _logger;
    private string _filePath;
    private ServerNodeViewModel? _selectedServer;
    private string? _selectedDatabase;
    private bool _isLoading;
    private string? _error;
    private bool _databasesLoaded;
    private bool _updating;
    private bool _disposed;
    private CancellationTokenSource? _loadCancellation;

    /// <summary>Initializes a new instance of the <see cref="QueryConnectionBarViewModel"/> class.</summary>
    public QueryConnectionBarViewModel(ExplorerViewModel explorer, DocumentConnections documents, string filePath, IExtensionLogger logger)
    {
        _explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _filePath = string.IsNullOrEmpty(filePath) ? throw new ArgumentException("A file path is required.", nameof(filePath)) : filePath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _explorer.Servers.CollectionChanged += OnServersChanged;
        _documents.Changed += OnDocumentChanged;
        ApplyLink();
    }

    /// <summary>Gets the servers of the explorer.</summary>
    public ObservableCollection<ServerNodeViewModel> Servers => _explorer.Servers;

    /// <summary>Gets the databases of the selected server; before the list is loaded only the linked database.</summary>
    public ObservableCollection<string> Databases { get; } = [];

    /// <summary>Gets the path of the document.</summary>
    public string FilePath => _filePath;

    /// <summary>Gets a value indicating whether a server is selected.</summary>
    public bool HasServer => _selectedServer is not null;

    /// <summary>Gets a value indicating whether no server is selected; shows the hint in the bar.</summary>
    public bool HasNoServer => _selectedServer is null;

    /// <summary>Gets a value indicating whether the database list is loading.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Gets the error of the last database list load (password masked), or null.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>Gets a value indicating whether <see cref="Error"/> is set.</summary>
    public bool HasError => _error is not null;

    /// <summary>Gets or sets the server. Picking one links the document and loads its databases.</summary>
    public ServerNodeViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (_updating || _disposed || ReferenceEquals(_selectedServer, value))
            {
                return;
            }

            SetServer(value);
            var database = value?.Definition.DefaultDatabase;
            SetDatabases(string.IsNullOrEmpty(database) ? [] : [database!], database);
            SaveLink();
            if (value is not null)
            {
                _ = LoadDatabasesAsync();
            }
        }
    }

    /// <summary>Gets or sets the database. Changing it updates the link.</summary>
    public string? SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (_updating || _disposed || _selectedDatabase == value)
            {
                return;
            }

            _selectedDatabase = value;
            OnPropertyChanged();
            SaveLink();
        }
    }

    /// <summary>Gets the running load, for tests.</summary>
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    /// <summary>Loads the database list when it was not loaded for the selected server yet. Called when the list opens.</summary>
    public Task EnsureDatabasesLoadedAsync() =>
        _selectedServer is null || _databasesLoaded || _isLoading ? PendingLoad : LoadDatabasesAsync();

    /// <summary>Moves the link after Save As or a rename of the document.</summary>
    public void Rename(string newPath)
    {
        if (string.IsNullOrEmpty(newPath) || string.Equals(newPath, _filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _updating = true;
        try
        {
            _documents.Rename(_filePath, newPath);
        }
        finally
        {
            _updating = false;
        }

        _filePath = newPath;
        OnPropertyChanged(nameof(FilePath));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _explorer.Servers.CollectionChanged -= OnServersChanged;
        _documents.Changed -= OnDocumentChanged;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
    }

    private Task LoadDatabasesAsync()
    {
        PendingLoad = LoadDatabasesCoreAsync();
        return PendingLoad;
    }

    private async Task LoadDatabasesCoreAsync()
    {
        var server = _selectedServer;
        if (server is null)
        {
            return;
        }

        // Not disposed: a load that is still running may use its token. A cancelled source holds no resources.
        _loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        Error = null;
        try
        {
            if (!await server.EnsurePasswordAsync(cancellation.Token))
            {
                return;
            }

            IsLoading = true;
            var databases = await _explorer.Metadata.GetDatabasesAsync(server.Definition, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(server, _selectedServer))
            {
                return;
            }

            var names = databases.Select(d => d.Name).ToList();
            var previous = _selectedDatabase;
            var keep = previous is not null && names.Contains(previous) ? previous : null;
            SetDatabases(names, keep);
            _databasesLoaded = true;
            if (keep != previous)
            {
                SaveLink();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Another server was picked or the document closed.
        }
        catch (Exception ex)
        {
            Error = SensitiveDataMasker.MaskText(ex.Message);
            _logger.Warning($"Loading the databases of '{server.Label}' for the query document '{_filePath}' failed.", ex);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                IsLoading = false;
            }
        }
    }

    private void SetServer(ServerNodeViewModel? server)
    {
        _loadCancellation?.Cancel();
        _selectedServer = server;
        _databasesLoaded = false;
        Error = null;
        IsLoading = false;
        OnPropertyChanged(nameof(SelectedServer));
        OnPropertyChanged(nameof(HasServer));
        OnPropertyChanged(nameof(HasNoServer));
    }

    // While the list changes the combo box may push null into SelectedDatabase; _updating ignores that, and the
    // property change at the end puts the right value back.
    private void SetDatabases(System.Collections.Generic.IReadOnlyList<string> names, string? selected)
    {
        _updating = true;
        try
        {
            Databases.Clear();
            foreach (var name in names)
            {
                Databases.Add(name);
            }

            _selectedDatabase = selected;
        }
        finally
        {
            _updating = false;
        }

        OnPropertyChanged(nameof(SelectedDatabase));
    }

    private void SaveLink()
    {
        _updating = true;
        try
        {
            _documents.Register(_filePath, _selectedServer?.Definition.Id, _selectedDatabase);
        }
        finally
        {
            _updating = false;
        }
    }

    // Shows the stored link: at start, and when the link changes elsewhere or its server is added or removed.
    private void ApplyLink()
    {
        var link = _documents.Find(_filePath);
        var server = link?.ConnectionId is { } id ? _explorer.Servers.FirstOrDefault(s => s.Definition.Id == id) : null;
        if (!ReferenceEquals(server, _selectedServer))
        {
            SetServer(server);
        }

        var database = server is null ? null : link?.Database;
        var listIsRight = server is null ? Databases.Count == 0 : database is null || Databases.Contains(database);
        if (database == _selectedDatabase && listIsRight)
        {
            return;
        }

        var names = new System.Collections.Generic.List<string>();
        if (server is not null && _databasesLoaded)
        {
            names.AddRange(Databases);
        }

        if (database is not null && !names.Contains(database))
        {
            names.Add(database);
        }

        SetDatabases(names, database);
    }

    private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_disposed)
        {
            ApplyLink();
        }
    }

    private void OnDocumentChanged(object? sender, string path)
    {
        if (!_updating && !_disposed && string.Equals(path, _filePath, StringComparison.OrdinalIgnoreCase))
        {
            ApplyLink();
        }
    }
}
