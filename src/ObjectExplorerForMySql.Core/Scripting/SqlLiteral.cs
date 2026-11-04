using System;
using System.Globalization;
using System.Text;

namespace ObjectExplorerForMySql.Core.Scripting;

/// <summary>
/// Writes column values as MySQL literals for the INSERT statements of Script Database (US-0603). The script sets
/// SQL_MODE without NO_BACKSLASH_ESCAPES, so strings use backslash escapes, as mysqldump does.
/// </summary>
public static class SqlLiteral
{
    /// <summary>Returns the literal for <paramref name="value"/>; <paramref name="dataType"/> (DATA_TYPE, for example "date") refines dates and times.</summary>
    public static string Format(object? value, string? dataType = null)
    {
        var type = (dataType ?? string.Empty).ToLowerInvariant();
        switch (value)
        {
            case null:
            case DBNull:
                return "NULL";
            case string s:
                return Quote(s);
            case bool b:
                return b ? "1" : "0";
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case decimal m:
                return m.ToString(CultureInfo.InvariantCulture);
            case DateTime dt:
                return Quote(FormatDateTime(dt, type));
            case TimeSpan ts:
                return Quote(FormatTime(ts));
            case byte[] bytes:
                return Hex(bytes);
            case Guid g:
                return Quote(g.ToString());
            case MySqlConnector.MySqlDateTime mdt:
                return mdt.IsValidDateTime
                    ? Quote(FormatDateTime(mdt.GetDateTime(), type))
                    : Quote(type == "date" ? "0000-00-00" : "0000-00-00 00:00:00");
            case MySqlConnector.MySqlGeometry geometry:
                return Hex(geometry.Value);
            default:
                return Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    /// <summary>Quotes a string with backslash escapes: <c>'it\'s'</c>.</summary>
    public static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('\'');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '\'': builder.Append("\\'"); break;
                case '\0': builder.Append("\\0"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\u001a': builder.Append("\\Z"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.Append('\'').ToString();
    }

    /// <summary>Returns <c>0x0A1B...</c>, or <c>''</c> for an empty value (<c>0x</c> alone is not valid).</summary>
    public static string Hex(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return "''";
        }

        var builder = new StringBuilder(2 + (bytes.Length * 2));
        builder.Append("0x");
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string FormatDateTime(DateTime value, string dataType)
    {
        if (dataType == "date")
        {
            return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var text = value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var fraction = value.Ticks % TimeSpan.TicksPerSecond;
        return fraction == 0 ? text : text + "." + (fraction / 10).ToString("000000", CultureInfo.InvariantCulture).TrimEnd('0');
    }

    // TIME can be negative and above 24 hours: -838:59:59 .. 838:59:59.
    private static string FormatTime(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var abs = value.Duration();
        var text = string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}:{3:00}", sign, (long)abs.TotalHours, abs.Minutes, abs.Seconds);
        var fraction = abs.Ticks % TimeSpan.TicksPerSecond;
        return fraction == 0 ? text : text + "." + (fraction / 10).ToString("000000", CultureInfo.InvariantCulture).TrimEnd('0');
    }
}
