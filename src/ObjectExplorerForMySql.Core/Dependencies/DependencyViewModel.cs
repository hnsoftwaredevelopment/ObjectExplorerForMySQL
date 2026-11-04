using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Dependencies;

/// <summary>Texts of the dependency window. The extension fills them from its resources (ADR-005); the defaults are English.</summary>
public sealed class DependencyTexts
{
    /// <summary>Gets or sets the title; {0} = kind, {1} = database, {2} = name.</summary>
    public string Title { get; set; } = "{0} {1}.{2}";

    /// <summary>Gets or sets the group of objects that use the subject; {0} = count.</summary>
    public string UsedBy { get; set; } = "Used by ({0})";

    /// <summary>Gets or sets the group of objects the subject uses; {0} = count.</summary>
    public string Uses { get; set; } = "Uses ({0})";

    /// <summary>Gets or sets the line of an empty group.</summary>
    public string None { get; set; } = "(none found)";

    /// <summary>Gets or sets the note of a foreign key; {0} = constraint.</summary>
    public string ForeignKey { get; set; } = "foreign key {0}";

    /// <summary>Gets or sets the note of a trigger of the table.</summary>
    public string Trigger { get; set; } = "trigger on the table";

    /// <summary>Gets or sets the note of a text match.</summary>
    public string SourceText { get; set; } = "text match";

    /// <summary>Gets or sets the note when sources could not be read.</summary>
    public string Incomplete { get; set; } = "Some sources could not be read with this account, so text matches can be missing.";

    /// <summary>Gets or sets the line while loading.</summary>
    public string Loading { get; set; } = "Reading the dependencies...";

    /// <summary>Gets or sets the error line; {0} = message.</summary>
    public string Failed { get; set; } = "Reading the dependencies failed: {0}";

    /// <summary>Gets or sets the names of the kinds, in the order of <see cref="DependencyKind"/>.</summary>
    public IReadOnlyList<string> Kinds { get; set; } = ["Table", "View", "Procedure", "Function", "Trigger", "Event"];
}

/// <summary>One object in the dependency window.</summary>
public sealed class DependencyItemViewModel
{
    internal DependencyItemViewModel(Dependency dependency, string label, string note)
    {
        Dependency = dependency;
        Label = label;
        Note = note;
    }

    /// <summary>Gets the dependency.</summary>
    public Dependency Dependency { get; }

    /// <summary>Gets the name, with the database when it is another database.</summary>
    public string Label { get; }

    /// <summary>Gets why it is listed, for example "foreign key fk_x" or "text match".</summary>
    public string Note { get; }

    /// <summary>Gets a value indicating whether the line is a text match.</summary>
    public bool IsTextMatch => Dependency.Reason == DependencyReason.SourceText;

    /// <summary>Gets the icon.</summary>
    public NodeIcon Icon => Dependency.Object.Kind switch
    {
        DependencyKind.Table => NodeIcon.Table,
        DependencyKind.View => NodeIcon.View,
        DependencyKind.Procedure => NodeIcon.StoredProcedure,
        DependencyKind.Function => NodeIcon.Function,
        DependencyKind.Trigger => NodeIcon.Trigger,
        _ => NodeIcon.Event,
    };
}

/// <summary>"Used by" or "Uses" with its objects.</summary>
public sealed class DependencyGroupViewModel : ObservableObject
{
    private bool _isExpanded = true;

    internal DependencyGroupViewModel(string title, IReadOnlyList<DependencyItemViewModel> items, string empty)
    {
        Title = title;
        Items = items;
        EmptyText = items.Count == 0 ? empty : null;
    }

    /// <summary>Gets the title with the count.</summary>
    public string Title { get; }

    /// <summary>Gets the objects.</summary>
    public IReadOnlyList<DependencyItemViewModel> Items { get; }

    /// <summary>Gets the text shown when the group is empty, otherwise null.</summary>
    public string? EmptyText { get; }

    /// <summary>Gets or sets a value indicating whether the group is expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>Content of the dependency window (US-1303): what uses an object and what it uses.</summary>
public sealed class DependencyViewModel : ObservableObject
{
    private readonly IDependencySource _source;
    private readonly DependencyTexts _texts;
    private readonly IExtensionLogger _logger;
    private string _title = string.Empty;
    private string? _status;
    private string? _note;
    private CancellationTokenSource? _cancellation;

    /// <summary>Initializes a new instance of the <see cref="DependencyViewModel"/> class.</summary>
    public DependencyViewModel(IDependencySource source, DependencyTexts texts, IExtensionLogger logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the server of the shown object, or null before the first load.</summary>
    public ServerNodeViewModel? Server { get; private set; }

    /// <summary>Gets the title: kind and name of the object.</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>Gets the loading or error line, or null.</summary>
    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Gets the note about sources that could not be read, or null.</summary>
    public string? Note
    {
        get => _note;
        private set => SetProperty(ref _note, value);
    }

    /// <summary>Gets the groups "Used by" and "Uses".</summary>
    public ObservableCollection<DependencyGroupViewModel> Groups { get; } = [];

    /// <summary>Reads and shows the dependencies of <paramref name="subject"/>; a new call stops the previous one.</summary>
    public async Task LoadAsync(ServerNodeViewModel server, SchemaObject subject)
    {
        if (server is null)
        {
            throw new ArgumentNullException(nameof(server));
        }

        if (subject is null)
        {
            throw new ArgumentNullException(nameof(subject));
        }

        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        Server = server;
        Title = string.Format(CultureInfo.CurrentCulture, _texts.Title, KindName(subject.Kind), subject.Database, subject.Name);
        Groups.Clear();
        Note = null;
        Status = _texts.Loading;
        try
        {
            if (!await server.EnsurePasswordAsync(cancellation.Token))
            {
                Status = null;
                return;
            }

            var catalog = await _source.LoadAsync(server.Definition, subject.Database, server.ServerVersion, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            var result = DependencyFinder.Find(catalog, subject);
            Groups.Add(Group(_texts.UsedBy, result.UsedBy, subject));
            Groups.Add(Group(_texts.Uses, result.Uses, subject));
            Note = result.SourcesIncomplete ? _texts.Incomplete : null;
            Status = null;
            _logger.Info($"Dependencies of {subject.Database}.{subject.Name} on '{server.Label}': used by {result.UsedBy.Count}, uses {result.Uses.Count}.");
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            // A newer request replaced this one.
        }
        catch (Exception ex)
        {
            Status = string.Format(CultureInfo.CurrentCulture, _texts.Failed, SensitiveDataMasker.MaskText(ex.Message));
            _logger.Warning($"Reading the dependencies of {subject.Database}.{subject.Name} failed.", ex);
        }
    }

    private DependencyGroupViewModel Group(string title, IReadOnlyList<Dependency> dependencies, SchemaObject subject) =>
        new(
            string.Format(CultureInfo.CurrentCulture, title, dependencies.Count),
            dependencies.Select(d => new DependencyItemViewModel(d, Label(d.Object, subject), NoteOf(d))).ToList(),
            _texts.None);

    private static string Label(SchemaObject item, SchemaObject subject) =>
        string.Equals(item.Database, subject.Database, StringComparison.OrdinalIgnoreCase) ? item.Name : item.Database + "." + item.Name;

    private string NoteOf(Dependency dependency) => dependency.Reason switch
    {
        DependencyReason.ForeignKey => string.Format(CultureInfo.CurrentCulture, _texts.ForeignKey, dependency.Detail),
        DependencyReason.Trigger => _texts.Trigger,
        DependencyReason.SourceText => _texts.SourceText,
        _ => string.Empty,
    };

    private string KindName(DependencyKind kind) => (int)kind < _texts.Kinds.Count ? _texts.Kinds[(int)kind] : kind.ToString();
}
