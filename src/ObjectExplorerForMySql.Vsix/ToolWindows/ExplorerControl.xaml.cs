using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.DragDrop;
using ObjectExplorerForMySql.Logging;

namespace ObjectExplorerForMySql.ToolWindows;

/// <summary>
/// Content of the explorer tool window. Keeps the selection in <see cref="ExplorerViewModel"/> and
/// asks the tool window pane to show the Visual Studio context menu or run a command (ADR-011).
/// </summary>
public partial class ExplorerControl : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ExplorerControl"/> class.</summary>
    public ExplorerControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised when the context menu for the selected node must be shown at a screen position (device pixels).
    /// The pane picks the server menu or the node menu from the selection.
    /// </summary>
    public event EventHandler<Point>? ContextMenuRequested;

    /// <summary>Raised when the Delete key is pressed on a selected server.</summary>
    public event EventHandler? RemoveServerRequested;

    /// <summary>Raised when an inline rename ends with Enter or by leaving the box (US-0902).</summary>
    public event EventHandler<RenameCommittedEventArgs>? RenameCommitted;

    private Point? _dragStart;
    private TreeNodeViewModel? _dragNode;
    private TreeNodeViewModel? _deferredSelect;

    private ExplorerViewModel? Explorer => DataContext as ExplorerViewModel;

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Explorer is { } explorer)
        {
            explorer.SelectedNode = e.NewValue as TreeNodeViewModel;
        }
    }

    // Selected bubbles up through the parent items; only the item itself scrolls into view.
    private void OnItemSelected(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem item && ReferenceEquals(e.OriginalSource, item))
        {
            item.BringIntoView();
        }
    }

    // Right-click selects the item under the mouse first, like Solution Explorer.
    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindItem(e.OriginalSource as DependencyObject) is { } item)
        {
            item.Focus();
            item.IsSelected = true;
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindItem(e.OriginalSource as DependencyObject) is { DataContext: TreeNodeViewModel })
        {
            ContextMenuRequested?.Invoke(this, Tree.PointToScreen(e.GetPosition(Tree)));
            e.Handled = true;
        }
    }

    // Keyboard: Shift+F10 or the context menu key. Mouse clicks are handled in OnMouseRightButtonUp.
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (Mouse.RightButton == MouseButtonState.Pressed || e.CursorLeft >= 0)
        {
            return;
        }

        // The focused item is the selected one at any depth; ContainerFromItem on the tree only finds servers.
        if (Keyboard.FocusedElement is TreeViewItem { DataContext: TreeNodeViewModel, IsSelected: true } item)
        {
            // Below the item's own row; ActualHeight of the item includes its expanded children.
            var header = item.Template.FindName("PART_Header", item) as FrameworkElement ?? item;
            ContextMenuRequested?.Invoke(this, header.PointToScreen(new Point(16, Math.Min(header.ActualHeight, 20))));
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None && Tree.SelectedItem is ServerNodeViewModel)
        {
            RemoveServerRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    // Drag & drop (US-0501): remember where the left button went down on an item; start the drag in OnPreviewMouseMove
    // once the mouse moved the system drag distance, so a simple click still selects and expands.
    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragNode = null;
        _deferredSelect = null;
        var source = e.OriginalSource as DependencyObject;
        if (IsInsideExpander(source) || IsInsideTextBox(source) || FindItem(source) is not { DataContext: TreeNodeViewModel node } || Explorer is not { } explorer)
        {
            return;
        }

        // Multi-select of columns (US-0502): Ctrl toggles, Shift selects a range. Other nodes select as usual.
        var handled = Keyboard.Modifiers switch
        {
            ModifierKeys.Control => explorer.ToggleSelection(node),
            ModifierKeys.Shift => explorer.ExtendSelection(node),
            _ => false,
        };
        if (handled)
        {
            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(Tree);
        _dragNode = node;

        // A press on a node of a multi-selection must not reset the selection yet: the user may start dragging all
        // selected columns. Without a drag, the mouse-up selects only this node, like Windows Explorer.
        if (Keyboard.Modifiers == ModifierKeys.None && explorer.SelectedNodes.Count > 1 && explorer.SelectedNodes.Contains(node))
        {
            _deferredSelect = node;
            e.Handled = true;
        }
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferredSelect is { } node && Explorer is { } explorer)
        {
            explorer.ClearMultiSelection();
            node.IsSelected = true;
        }

        _deferredSelect = null;
        _dragStart = null;
        _dragNode = null;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start || _dragNode is not { } node)
        {
            return;
        }

        var position = e.GetPosition(Tree);
        if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        _dragNode = null;
        _deferredSelect = null;
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        if (Explorer is not { } explorer)
        {
            return;
        }

        try
        {
            if (DragData.CreateFor(explorer, node) is { } data)
            {
                System.Windows.DragDrop.DoDragDrop(Tree, data, DragDropEffects.Copy);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            ExtensionLog.Logger.Error($"Dragging '{node.Label}' failed.", ex);
        }
    }

    // Inline rename (US-0902): the box shows the name (for a column without type), focused and selected.
    private void OnEditBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true, DataContext: TreeNodeViewModel node } box)
        {
            return;
        }

        box.Text = node.Key;

        // Focus after the layout pass that made the box visible.
#pragma warning disable VSSDK007 // Fire and forget: only moves the focus.
        _ = Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
            if (node.IsEditing)
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            }
        });
#pragma warning restore VSSDK007
    }

    private void OnEditBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TreeNodeViewModel node } box)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                EndEdit(box, node, commit: true);
                e.Handled = true;
                break;
            case Key.Escape:
                EndEdit(box, node, commit: false);
                e.Handled = true;
                break;
            case Key.Up or Key.Down or Key.PageUp or Key.PageDown:
                // Keep the tree from moving the selection while the name is edited.
                e.Handled = true;
                break;
        }
    }

    // Clicking elsewhere renames, like Solution Explorer.
    private void OnEditBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: TreeNodeViewModel { IsEditing: true } node } box)
        {
            EndEdit(box, node, commit: true);
        }
    }

    private void EndEdit(TextBox box, TreeNodeViewModel node, bool commit)
    {
        if (!node.IsEditing)
        {
            return;
        }

        var name = box.Text;
        var hadFocus = box.IsKeyboardFocusWithin;
        node.IsEditing = false;
        if (hadFocus && FindItem(box) is { } item)
        {
            item.Focus();
        }

        if (commit)
        {
            RenameCommitted?.Invoke(this, new RenameCommittedEventArgs(node, name));
        }
    }

    // A press inside the rename box places the caret; it must not start a drag or change the selection.
    private static bool IsInsideTextBox(DependencyObject? source)
    {
        while (source is not null and not TreeViewItem)
        {
            if (source is TextBox)
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    // The expand arrow is a ToggleButton inside the TreeViewItem; a press on it must never start a drag.
    private static bool IsInsideExpander(DependencyObject? source)
    {
        while (source is not null and not TreeViewItem)
        {
            if (source is System.Windows.Controls.Primitives.ToggleButton)
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private static TreeViewItem? FindItem(DependencyObject? source)
    {
        while (source is not null and not TreeViewItem)
        {
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return source as TreeViewItem;
    }
}

/// <summary>The node and the typed name of an inline rename (US-0902).</summary>
public sealed class RenameCommittedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="RenameCommittedEventArgs"/> class.</summary>
    public RenameCommittedEventArgs(TreeNodeViewModel node, string newName)
    {
        Node = node;
        NewName = newName;
    }

    /// <summary>Gets the renamed node.</summary>
    public TreeNodeViewModel Node { get; }

    /// <summary>Gets the typed name, unchanged (not trimmed).</summary>
    public string NewName { get; }
}
