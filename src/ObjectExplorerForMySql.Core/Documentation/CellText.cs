using System;
using System.Collections.Generic;
using PdfSharp.Drawing;

namespace ObjectExplorerForMySql.Core.Documentation;

/// <summary>
/// Breaks words that are wider than a table column (US-1202). MigraDoc only wraps at spaces, so a long name such as
/// <c>CURRENT_TIMESTAMP</c> or <c>DEFAULT_GENERATED</c> would run into the next column. A word that does not fit
/// is split over lines, preferably after <c>_</c>, <c>,</c>, <c>.</c> or <c>(</c>.
/// </summary>
internal static class CellText
{
    private static readonly object MeasureLock = new();
    private static XGraphics? _measure;

    /// <summary>Returns <paramref name="text"/> with a line break (<c>\n</c>) inside every word wider than <paramref name="width"/> points.</summary>
    public static string Fit(string text, double width, double fontSize, bool bold)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            return text;
        }

        PdfDocumentWriter.EnsureFonts();
        var font = new XFont(PdfDocumentWriter.FontName, fontSize, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        lock (MeasureLock)
        {
            var measure = _measure ??= XGraphics.CreateMeasureContext(new XSize(4000, 4000), XGraphicsUnit.Point, XPageDirection.Downwards);
            double Width(string s) => measure.MeasureString(s, font).Width;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var l = 0; l < lines.Length; l++)
            {
                var words = lines[l].Split(' ');
                for (var w = 0; w < words.Length; w++)
                {
                    if (Width(words[w]) > width)
                    {
                        words[w] = string.Join("\n", Split(words[w], width, Width));
                    }
                }

                lines[l] = string.Join(" ", words);
            }

            return string.Join("\n", lines);
        }
    }

    // Takes as many characters as fit; when that piece holds a break character, the piece ends right after the last
    // one, so CURRENT_TIMESTAMP becomes CURRENT_ and TIMESTAMP. Every piece has at least one character.
    private static IEnumerable<string> Split(string word, double width, Func<string, double> measure)
    {
        var rest = word;
        while (rest.Length > 0)
        {
            if (measure(rest) <= width)
            {
                yield return rest;
                yield break;
            }

            var count = 1;
            while (count < rest.Length && measure(rest.Substring(0, count + 1)) <= width)
            {
                count++;
            }

            var cut = rest.LastIndexOfAny(['_', ',', '.', '(', '-'], count - 1);
            if (cut > 0 && cut < count - 1)
            {
                count = cut + 1;
            }

            yield return rest.Substring(0, count);
            rest = rest.Substring(count);
        }
    }
}
