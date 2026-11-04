using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// Writes a result set as CSV or JSON (US-0704). Values are written as the grid shows them, except binary data, which
/// is written in full as hex. A result set gives the rows in <see cref="QueryResultSet.Rows"/> (at most the row
/// limit); <see cref="FullResultExporter"/> passes all rows of a statement that runs again (US-0705).
/// </summary>
public static class QueryResultExporter
{
    /// <summary>
    /// Writes CSV as in RFC 4180: comma separated, a header line with the column names, fields with a comma, quote or
    /// line break in double quotes. NULL is an empty field.
    /// </summary>
    public static void WriteCsv(QueryResultSet set, TextWriter writer)
    {
        if (set is null)
        {
            throw new ArgumentNullException(nameof(set));
        }

        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        WriteCsv(set.Columns, set.Rows, writer);
    }

    /// <summary>
    /// Writes CSV with <paramref name="columns"/> and the rows as they come, without keeping them (US-0705). Returns the
    /// number of rows written.
    /// </summary>
    public static long WriteCsv(IReadOnlyList<string> columns, IEnumerable<object?[]> rows, TextWriter writer)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        WriteCsvLine(writer, columns);
        var fields = new string[columns.Count];
        long count = 0;
        foreach (var row in rows)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                var value = i < row.Length ? row[i] : null;
                fields[i] = value is null or DBNull ? string.Empty : Text(value);
            }

            WriteCsvLine(writer, fields);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Writes JSON: an array with one object per row. Numbers stay numbers, NULL is <c>null</c>, everything else is a
    /// string. A column name that occurs twice gets a suffix (<c>id_2</c>), so every property is unique.
    /// </summary>
    public static void WriteJson(QueryResultSet set, TextWriter writer)
    {
        if (set is null)
        {
            throw new ArgumentNullException(nameof(set));
        }

        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        WriteJson(set.Columns, set.Rows, writer);
    }

    /// <summary>
    /// Writes JSON with <paramref name="columns"/> and the rows as they come, without keeping them (US-0705). Returns
    /// the number of rows written.
    /// </summary>
    public static long WriteJson(IReadOnlyList<string> columns, IEnumerable<object?[]> rows, TextWriter writer)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        var names = UniqueNames(columns);
        writer.Write('[');
        long count = 0;
        foreach (var row in rows)
        {
            writer.Write(count == 0 ? "\n  {" : ",\n  {");
            for (var i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    writer.Write(", ");
                }

                WriteJsonString(writer, names[i]);
                writer.Write(": ");
                var value = i < row.Length ? row[i] : null;
                if (value is null or DBNull)
                {
                    writer.Write("null");
                }
                else if (IsNumber(value))
                {
                    writer.Write(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                }
                else
                {
                    WriteJsonString(writer, Text(value));
                }
            }

            writer.Write('}');
            count++;
        }

        writer.Write(count == 0 ? "]\n" : "\n]\n");
        return count;
    }

    private static string Text(object value) => value is byte[] bytes ? FullHex(bytes) : QueryValueFormatter.Format(value);

    private static bool IsNumber(object value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or decimal
        || (value is double d && !double.IsNaN(d) && !double.IsInfinity(d))
        || (value is float f && !float.IsNaN(f) && !float.IsInfinity(f));

    private static string FullHex(byte[] bytes)
    {
        var builder = new StringBuilder(2 + (bytes.Length * 2));
        builder.Append("0x");
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static List<string> UniqueNames(IReadOnlyList<string> columns)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>(columns.Count);
        foreach (var column in columns)
        {
            var name = column;
            for (var n = 2; !used.Add(name); n++)
            {
                name = column + "_" + n.ToString(CultureInfo.InvariantCulture);
            }

            names.Add(name);
        }

        return names;
    }

    private static void WriteCsvLine(TextWriter writer, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }

            var field = fields[i] ?? string.Empty;
            if (field.IndexOfAny([',', '"', '\r', '\n']) >= 0)
            {
                writer.Write('"');
                writer.Write(field.Replace("\"", "\"\""));
                writer.Write('"');
            }
            else
            {
                writer.Write(field);
            }
        }

        writer.Write("\r\n");
    }

    private static void WriteJsonString(TextWriter writer, string text)
    {
        writer.Write('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    writer.Write("\\\"");
                    break;
                case '\\':
                    writer.Write("\\\\");
                    break;
                case '\n':
                    writer.Write("\\n");
                    break;
                case '\r':
                    writer.Write("\\r");
                    break;
                case '\t':
                    writer.Write("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        writer.Write("\\u");
                        writer.Write(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.Write(c);
                    }

                    break;
            }
        }

        writer.Write('"');
    }
}
