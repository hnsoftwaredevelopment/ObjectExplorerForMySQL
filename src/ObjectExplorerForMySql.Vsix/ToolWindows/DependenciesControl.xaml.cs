using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using ObjectExplorerForMySql.Core.Dependencies;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.ToolWindows;

/// <summary>Content of the dependency window (US-1303).</summary>
public partial class DependenciesControl : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="DependenciesControl"/> class.</summary>
    public DependenciesControl()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user opens an object (double-click or Enter), so the explorer can select it.</summary>
    public event EventHandler<SchemaObject>? ObjectActivated;

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: DependencyItemViewModel item })
        {
            ObjectActivated?.Invoke(this, item.Dependency.Object);
            e.Handled = true;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Tree.SelectedItem is DependencyItemViewModel item)
        {
            ObjectActivated?.Invoke(this, item.Dependency.Object);
            e.Handled = true;
        }
    }
}

/// <summary>Collapses an element when the bound value is null or empty.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string text && text.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The "MySQL Dependencies" tool window (US-1303): one window, reused for every View Dependencies.</summary>
[Guid(ToolWindowGuidString)]
public sealed class DependenciesToolWindow : ToolWindowPane
{
    /// <summary>GUID of the tool window.</summary>
    public const string ToolWindowGuidString = "5e2b7c41-9d38-4f0a-b6e3-71c2a8d94f15";

    private readonly DependenciesControl _control = new();

    /// <summary>Initializes a new instance of the <see cref="DependenciesToolWindow"/> class.</summary>
    public DependenciesToolWindow()
        : base(null)
    {
        Caption = Strings.DependenciesCaption;
        BitmapImageMoniker = KnownMonikers.Relationship;
        Content = _control;
        _control.ObjectActivated += (_, target) => ObjectActivated?.Invoke(this, target);
    }

    /// <summary>Raised when the user opens an object in the window.</summary>
    internal event EventHandler<SchemaObject>? ObjectActivated;

    /// <summary>Shows <paramref name="model"/>.</summary>
    internal void Show(DependencyViewModel model) => _control.DataContext = model;
}
