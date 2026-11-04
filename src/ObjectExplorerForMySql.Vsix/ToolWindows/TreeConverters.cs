using System;
using System.Globalization;
using System.Windows.Data;
using Microsoft.VisualStudio.Imaging;
using ObjectExplorerForMySql.Core.Explorer;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.ToolWindows;

/// <summary>Maps a <see cref="NodeIcon"/> to a Visual Studio image moniker, so icons follow the theme.</summary>
public sealed class NodeIconConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        NodeIcon.ServerDisconnected => KnownMonikers.DataServer,
        NodeIcon.ServerConnecting => KnownMonikers.Sync,
        NodeIcon.ServerConnected => KnownMonikers.DataServer,
        NodeIcon.ServerError => KnownMonikers.StatusError,
        NodeIcon.FolderClosed => KnownMonikers.FolderClosed,
        NodeIcon.FolderOpened => KnownMonikers.FolderOpened,
        NodeIcon.Database => KnownMonikers.Database,
        NodeIcon.Table => KnownMonikers.Table,
        NodeIcon.Column => KnownMonikers.Column,
        NodeIcon.PrimaryKeyColumn => KnownMonikers.KeyColumn,
        NodeIcon.ForeignKeyColumn => KnownMonikers.ForeignKey,
        NodeIcon.GeneratedColumn => KnownMonikers.ComputedColumn,
        NodeIcon.PrimaryKey => KnownMonikers.Key,
        NodeIcon.UniqueKey => KnownMonikers.UniqueKey,
        NodeIcon.ForeignKey => KnownMonikers.ForeignKey,
        NodeIcon.CheckConstraint => KnownMonikers.CheckConstraint,
        NodeIcon.PrimaryIndex => KnownMonikers.ClusteredIndex,
        NodeIcon.Index => KnownMonikers.SortAscending,
        NodeIcon.FullTextIndex => KnownMonikers.Search,
        NodeIcon.Trigger => KnownMonikers.Trigger,
        NodeIcon.View => KnownMonikers.View,
        NodeIcon.StoredProcedure => KnownMonikers.StoredProcedure,
        NodeIcon.Function => KnownMonikers.ScalarFunction,
        NodeIcon.InputParameter => KnownMonikers.InputParameter,
        NodeIcon.OutputParameter => KnownMonikers.OutputParameter,
        NodeIcon.InputOutputParameter => KnownMonikers.Parameter,
        NodeIcon.Event => KnownMonikers.Timer,
        NodeIcon.User => KnownMonikers.User,
        NodeIcon.LockedUser => KnownMonikers.Lock,
        NodeIcon.Role => KnownMonikers.AccountGroup,
        NodeIcon.Grant => KnownMonikers.Permission,
        NodeIcon.Loading => KnownMonikers.Loading,
        NodeIcon.Error => KnownMonikers.StatusError,
        _ => KnownMonikers.QuestionMark,
    };

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows a <see cref="ConnectionState"/> as localized text.</summary>
public sealed class ConnectionStateTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ConnectionState.NotConnected => Strings.StateNotConnected,
        ConnectionState.Connecting => Strings.StateConnecting,
        ConnectionState.Connected => Strings.StateConnected,
        ConnectionState.Error => Strings.StateError,
        _ => value?.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
