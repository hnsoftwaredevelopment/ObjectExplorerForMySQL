using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ObjectExplorerForMySql.Core.Activity;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.ToolWindows;

/// <summary>
/// Content of an activity monitor window (US-1501): the sessions of one server with Kill Query and Kill Connection.
/// While the window is visible and Auto refresh is on, it refreshes every five seconds; a hidden window does not
/// query the server.
/// </summary>
public partial class ActivityMonitorControl : UserControl
{
    /// <summary>The interval of Auto refresh.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private ActivityMonitorViewModel? _model;
    private long? _selectedId;

    /// <summary>Initializes a new instance of the <see cref="ActivityMonitorControl"/> class.</summary>
    public ActivityMonitorControl()
    {
        InitializeComponent();
        _timer.Tick += OnTick;
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    /// <summary>Shows <paramref name="model"/>.</summary>
    internal void Attach(ActivityMonitorViewModel model)
    {
        Detach();
        _model = model;
        DataContext = model;
        model.PropertyChanged += OnModelPropertyChanged;
        model.Sessions.CollectionChanged += OnSessionsChanged;
        UpdateTimer();
        UpdateButtons();
    }

    /// <summary>Stops following the model and the timer.</summary>
    internal void Detach()
    {
        _timer.Stop();
        if (_model is { } model)
        {
            model.PropertyChanged -= OnModelPropertyChanged;
            model.Sessions.CollectionChanged -= OnSessionsChanged;
            _model = null;
        }
    }

    private SessionInfo? Selected => Grid.SelectedItem as SessionInfo;

    private void UpdateTimer()
    {
        if (_model is { AutoRefresh: true } && IsVisible)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_model is { IsLoading: false } model)
        {
            _ = model.RefreshAsync();
        }
    }

    private void OnModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActivityMonitorViewModel.AutoRefresh))
        {
            UpdateTimer();
        }
    }

    // A refresh replaces the rows; the session that was selected stays selected while it exists.
    private void OnSessionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_selectedId is not { } id || _model is not { } model || Grid.SelectedItem is SessionInfo { } current && current.Id == id)
        {
            return;
        }

        foreach (var session in model.Sessions)
        {
            if (session.Id == id)
            {
                Grid.SelectedItem = session;
                return;
            }
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Clearing the rows during a refresh also clears the selection; that is not a choice of the user.
        if (Selected is { } session)
        {
            _selectedId = session.Id;
        }
        else if (_model is not { IsLoading: true })
        {
            _selectedId = null;
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var session = Selected;
        KillQueryButton.IsEnabled = session is { IsSleeping: false };
        KillConnectionButton.IsEnabled = session is not null;
        KillQueryMenuItem.IsEnabled = KillQueryButton.IsEnabled;
        KillConnectionMenuItem.IsEnabled = KillConnectionButton.IsEnabled;
        CopyStatementMenuItem.IsEnabled = session?.Statement is not null;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = _model?.RefreshAsync();

    private void OnKillQuery(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Kill(KillKind.Query);
    }

    private void OnKillConnection(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Kill(KillKind.Connection);
    }

    // Every kill asks first, with the session and its statement in the question; No is the default button.
    private void Kill(KillKind kind)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_model is not { } model || Selected is not { } session)
        {
            return;
        }

        var statement = session.Statement is { } text ? Environment.NewLine + Environment.NewLine + Shorten(text) : string.Empty;
        var question = Strings.Format(
            kind == KillKind.Query ? Strings.ActivityKillQueryConfirm : Strings.ActivityKillConnectionConfirm,
            session.Id,
            session.User ?? string.Empty,
            model.ServerLabel) + statement;
        if (VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, question, Strings.ActivityTitle, OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) != (int)VSConstants.MessageBoxResult.IDYES)
        {
            return;
        }

        _ = model.KillAsync(session, kind);
    }

    private void OnCopyStatement(object sender, RoutedEventArgs e)
    {
        if (Selected?.Statement is { } statement)
        {
            try
            {
                Clipboard.SetText(statement);
            }
            catch (COMException)
            {
                // Another program holds the clipboard; the user can try again.
            }
        }
    }

    private static string Shorten(string text) => text.Length <= 500 ? text : text.Substring(0, 500) + "...";
}

/// <summary>Shows a statement on one line in the grid; the tooltip has the full text.</summary>
public sealed class SingleLineConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text ? string.Join(" ", text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)) : value;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Activity monitor window (US-1501): one per server, in the document well. Transient: Visual Studio does not reopen
/// it at startup, so it never asks for a password or queries a server by itself.
/// </summary>
[Guid(ToolWindowGuidString)]
public sealed class ActivityMonitorToolWindow : ToolWindowPane
{
    /// <summary>GUID of the tool window type.</summary>
    public const string ToolWindowGuidString = "b4e7c2a9-6f13-4d58-8e2b-0a9c5d7f3e61";

    private readonly ActivityMonitorControl _control = new();

    /// <summary>Initializes a new instance of the <see cref="ActivityMonitorToolWindow"/> class.</summary>
    public ActivityMonitorToolWindow()
        : base(null)
    {
        Content = _control;
    }

    /// <summary>Gets the model shown, or null before <see cref="Show"/>.</summary>
    internal ActivityMonitorViewModel? Model { get; private set; }

    /// <summary>Shows <paramref name="model"/> and reads the sessions.</summary>
    internal void Show(ActivityMonitorViewModel model)
    {
        Model = model;
        Caption = Strings.Format(Strings.ActivityCaption, model.ServerLabel);
        _control.Attach(model);
        _ = model.RefreshAsync();
    }

    /// <inheritdoc />
    protected override void OnClose()
    {
        _control.Detach();
        Model?.Dispose();
        Model = null;
        base.OnClose();
    }
}
