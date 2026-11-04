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

namespace ObjectExplorerForMySql.Core.Activity;

/// <summary>Texts of the activity monitor; the VSIX fills them from its resources.</summary>
public sealed class ActivityTexts
{
    /// <summary>Gets or sets the count line; {0} = sessions shown, {1} = active, {2} = time of the last refresh.</summary>
    public string Summary { get; set; } = "{0} sessions, {1} active. Refreshed at {2}.";

    /// <summary>Gets or sets the line when reading failed; {0} = error.</summary>
    public string LoadFailed { get; set; } = "Reading the sessions failed: {0}";

    /// <summary>Gets or sets the line when KILL failed; {0} = session id, {1} = error.</summary>
    public string KillFailed { get; set; } = "Stopping session {0} failed: {1}";
}

/// <summary>
/// The activity monitor of one server (US-1501): the sessions of the server, active ones first and the longest
/// running on top, with Kill Query and Kill Connection. Refreshing is by hand or by the timer of the window.
/// </summary>
public sealed class ActivityMonitorViewModel : ObservableObject, IDisposable
{
    private readonly ServerNodeViewModel _server;
    private readonly IActivitySource _source;
    private readonly ActivityTexts _texts;
    private readonly IExtensionLogger _logger;
    private IReadOnlyList<SessionInfo> _all = [];
    private bool _isLoading;
    private bool _hideSleeping;
    private bool _autoRefresh = true;
    private string? _error;
    private string _summary = string.Empty;
    private DateTime? _refreshedAt;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ActivityMonitorViewModel"/> class.</summary>
    public ActivityMonitorViewModel(ServerNodeViewModel server, IActivitySource source, ActivityTexts texts, IExtensionLogger logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the name of the connection, for the caption.</summary>
    public string ServerLabel => _server.Label;

    /// <summary>Gets the sessions shown.</summary>
    public ObservableCollection<SessionInfo> Sessions { get; } = [];

    /// <summary>Gets or sets a value indicating whether sessions in Sleep are left out.</summary>
    public bool HideSleeping
    {
        get => _hideSleeping;
        set
        {
            if (SetProperty(ref _hideSleeping, value))
            {
                Apply();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the window refreshes by itself.</summary>
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set => SetProperty(ref _autoRefresh, value);
    }

    /// <summary>Gets a value indicating whether a refresh is busy.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Gets the last error, or null.</summary>
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

    /// <summary>Gets the count line.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Reads the sessions again. Does nothing while a refresh is busy or when the password prompt is cancelled.</summary>
    public async Task RefreshAsync()
    {
        if (_isLoading || _disposed)
        {
            return;
        }

        IsLoading = true;
        try
        {
            if (!await _server.EnsurePasswordAsync(CancellationToken.None))
            {
                AutoRefresh = false;
                return;
            }

            var sessions = await _source.LoadAsync(_server.Definition, CancellationToken.None);
            _all = sessions;
            _refreshedAt = DateTime.Now;
            Error = null;
            Apply();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A refresh that keeps failing would show the same error every few seconds; the timer stops.
            AutoRefresh = false;
            Error = string.Format(CultureInfo.CurrentCulture, _texts.LoadFailed, SensitiveDataMasker.MaskText(ex.Message));
            _logger.Warning($"Reading the sessions of '{_server.Label}' failed.", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Stops the statement or the whole session of <paramref name="session"/>, then refreshes.</summary>
    public async Task KillAsync(SessionInfo session, KillKind kind)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        try
        {
            if (!await _server.EnsurePasswordAsync(CancellationToken.None))
            {
                return;
            }

            await _source.KillAsync(_server.Definition, session.Id, kind, CancellationToken.None);
            _logger.Info($"{(kind == KillKind.Query ? "KILL QUERY" : "KILL CONNECTION")} {session.Id} ({session.User}) on '{_server.Label}'.");
            Error = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = string.Format(CultureInfo.CurrentCulture, _texts.KillFailed, session.Id, SensitiveDataMasker.MaskText(ex.Message));
            _logger.Warning($"Stopping session {session.Id} on '{_server.Label}' failed.", ex);
            return;
        }

        await RefreshAsync();
    }

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    /// <summary>Returns the sessions in the order of the window: active first, then the longest running first, then by id.</summary>
    internal static IEnumerable<SessionInfo> Order(IEnumerable<SessionInfo> sessions, bool hideSleeping) =>
        sessions.Where(s => !hideSleeping || !s.IsSleeping)
            .OrderBy(s => s.IsSleeping)
            .ThenByDescending(s => s.Time)
            .ThenBy(s => s.Id);

    private void Apply()
    {
        Sessions.Clear();
        foreach (var session in Order(_all, _hideSleeping))
        {
            Sessions.Add(session);
        }

        if (_refreshedAt is { } at)
        {
            Summary = string.Format(CultureInfo.CurrentCulture, _texts.Summary, Sessions.Count, _all.Count(s => !s.IsSleeping), at.ToString("T", CultureInfo.CurrentCulture));
        }
    }
}
