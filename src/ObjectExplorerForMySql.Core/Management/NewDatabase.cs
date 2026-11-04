using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>State of the New Database dialog (US-0903): name, character set, collation and the statement that runs.</summary>
public sealed class NewDatabaseViewModel : ObservableObject
{
    private readonly CharacterSetCatalog _catalog;
    private string _name = string.Empty;
    private CharacterSetInfo? _characterSet;
    private CollationInfo? _collation;
    private IReadOnlyList<CollationInfo> _collations = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="NewDatabaseViewModel"/> class. Character set and collation start at
    /// the server defaults; when the server collation does not belong to the character set, at its default collation.
    /// </summary>
    public NewDatabaseViewModel(CharacterSetCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        var start = Find(catalog.ServerCharacterSet) ?? Find("utf8mb4") ?? catalog.CharacterSets.FirstOrDefault();
        SetCharacterSet(start, catalog.ServerCollation);
    }

    /// <summary>Gets the character sets of the server.</summary>
    public IReadOnlyList<CharacterSetInfo> CharacterSets => _catalog.CharacterSets;

    /// <summary>Gets the collations of the selected character set.</summary>
    public IReadOnlyList<CollationInfo> Collations
    {
        get => _collations;
        private set => SetProperty(ref _collations, value);
    }

    /// <summary>Gets or sets the database name as typed.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    /// <summary>Gets or sets the character set; changing it selects its default collation.</summary>
    public CharacterSetInfo? CharacterSet
    {
        get => _characterSet;
        set
        {
            if (!Equals(value, _characterSet))
            {
                SetCharacterSet(value, value?.DefaultCollation);
            }
        }
    }

    /// <summary>Gets or sets the collation.</summary>
    public CollationInfo? Collation
    {
        get => _collation;
        set
        {
            if (SetProperty(ref _collation, value))
            {
                Changed();
            }
        }
    }

    /// <summary>Gets what is wrong with the name; <see cref="NameProblem.Exists"/> compares without case, as MySQL on Windows does.</summary>
    public NameProblem Problem
    {
        get
        {
            var problem = NameRules.Check(_name);
            return problem == NameProblem.None && _catalog.ExistingDatabases.Any(d => string.Equals(d, _name, StringComparison.OrdinalIgnoreCase))
                ? NameProblem.Exists
                : problem;
        }
    }

    /// <summary>Gets a value indicating whether Create is enabled.</summary>
    public bool CanCreate => Problem == NameProblem.None;

    /// <summary>Gets the statement that runs, or an empty string while the name cannot be used.</summary>
    public string Sql => CanCreate ? BuildSql(_name, _characterSet?.Name, _collation?.Name) : string.Empty;

    /// <summary>Builds <c>CREATE DATABASE `name` CHARACTER SET x COLLATE y;</c>; the clauses are left out when not given.</summary>
    public static string BuildSql(string name, string? characterSet, string? collation)
    {
        if (NameRules.Check(name) != NameProblem.None)
        {
            throw new ArgumentException("The name cannot be used.", nameof(name));
        }

        var sql = "CREATE DATABASE " + MySqlIdentifier.Quote(name);
        if (!string.IsNullOrEmpty(characterSet))
        {
            sql += " CHARACTER SET " + MySqlIdentifier.QuoteIfNeeded(characterSet!);
        }

        if (!string.IsNullOrEmpty(collation))
        {
            sql += " COLLATE " + MySqlIdentifier.QuoteIfNeeded(collation!);
        }

        return sql + ";";
    }

    private CharacterSetInfo? Find(string? name) =>
        _catalog.CharacterSets.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private void SetCharacterSet(CharacterSetInfo? characterSet, string? preferredCollation)
    {
        _characterSet = characterSet;
        OnPropertyChanged(nameof(CharacterSet));
        Collations = _catalog.CollationsOf(characterSet?.Name);
        _collation = Collations.FirstOrDefault(c => string.Equals(c.Name, preferredCollation, StringComparison.OrdinalIgnoreCase))
            ?? Collations.FirstOrDefault(c => string.Equals(c.Name, characterSet?.DefaultCollation, StringComparison.OrdinalIgnoreCase))
            ?? Collations.FirstOrDefault();
        OnPropertyChanged(nameof(Collation));
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(Sql));
    }
}

/// <summary>Creates a database from the Databases folder and selects it in the tree (US-0903).</summary>
public sealed class DatabaseCreator
{
    private readonly ISchemaCommandRunner _runner;
    private readonly ICharacterSetSource _source;
    private readonly IExtensionLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="DatabaseCreator"/> class.</summary>
    public DatabaseCreator(ISchemaCommandRunner runner, ICharacterSetSource source, IExtensionLogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Returns true when New Database is available on <paramref name="node"/>: the Databases folder of a server.</summary>
    public static bool IsTarget(TreeNodeViewModel? node) => node is FolderNodeViewModel { Parent: ServerNodeViewModel };

    /// <summary>Reads what the dialog needs; null when the password prompt was cancelled.</summary>
    public async Task<CharacterSetCatalog?> LoadCatalogAsync(TreeNodeViewModel folder, CancellationToken cancellationToken)
    {
        var server = ServerOf(folder);
        return await server.EnsurePasswordAsync(cancellationToken) ? await _source.GetAsync(server.Definition, cancellationToken) : null;
    }

    /// <summary>
    /// Runs <paramref name="sql"/>, refreshes the Databases folder and selects the new database. The name is matched
    /// without case: on Windows the server stores it in lower case. A server error is thrown.
    /// </summary>
    public async Task CreateAsync(TreeNodeViewModel folder, string name, string sql, CancellationToken cancellationToken)
    {
        var server = ServerOf(folder);
        await _runner.ExecuteAsync(server.Definition, sql, cancellationToken);
        _logger.Info($"Created database {name} ('{server.Label}'): {sql}");

        // A folder collapsed in the tree keeps its old children; refresh first, so expanding loads the new list.
        await folder.RefreshAsync();
        if (!folder.IsExpanded)
        {
            folder.IsExpanded = true;
            await folder.WaitForLoadAsync();
        }

        var created = folder.Children.OfType<DatabaseNodeViewModel>().FirstOrDefault(d => string.Equals(d.Key, name, StringComparison.Ordinal))
            ?? folder.Children.OfType<DatabaseNodeViewModel>().FirstOrDefault(d => string.Equals(d.Key, name, StringComparison.OrdinalIgnoreCase));
        if (created is not null)
        {
            created.IsSelected = true;
        }
    }

    private static ServerNodeViewModel ServerOf(TreeNodeViewModel folder) =>
        IsTarget(folder) ? (ServerNodeViewModel)folder.Parent! : throw new ArgumentException("Not the Databases folder of a server.", nameof(folder));
}
