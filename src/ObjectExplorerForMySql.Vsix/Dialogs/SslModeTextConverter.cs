using System;
using System.Globalization;
using System.Windows.Data;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Resources;

namespace ObjectExplorerForMySql.Dialogs;

/// <summary>Shows an <see cref="SslMode"/> as localized text.</summary>
public sealed class SslModeTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        SslMode.None => Strings.SslModeNone,
        SslMode.Preferred => Strings.SslModePreferred,
        SslMode.Required => Strings.SslModeRequired,
        SslMode.VerifyCA => Strings.SslModeVerifyCA,
        SslMode.VerifyFull => Strings.SslModeVerifyFull,
        _ => value?.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
