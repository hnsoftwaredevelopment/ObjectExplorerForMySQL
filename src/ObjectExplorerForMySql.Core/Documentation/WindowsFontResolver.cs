using System;
using System.Collections.Concurrent;
using System.IO;
using PdfSharp.Fonts;

namespace ObjectExplorerForMySql.Core.Documentation;

/// <summary>
/// Gives PDFsharp the Segoe UI files from the Windows font folder (US-1202), with Arial when Segoe UI is missing.
/// Every family resolves to these files, so a font that MigraDoc asks for on its own never fails the document.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    // Regular, bold, italic, bold italic.
    private static readonly string[][] Families =
    [
        ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"],
        ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"],
    ];

    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the Windows font folder.</summary>
    internal static string FontFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    /// <inheritdoc/>
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var style = (bold ? 1 : 0) + (italic ? 2 : 0);
        foreach (var family in Families)
        {
            // A style without its own file is simulated from the regular file.
            if (File.Exists(Path.Combine(FontFolder, family[style])))
            {
                return new FontResolverInfo(family[style]);
            }

            if (File.Exists(Path.Combine(FontFolder, family[0])))
            {
                return new FontResolverInfo(family[0], bold, italic);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public byte[]? GetFont(string faceName) =>
        _files.GetOrAdd(faceName, name => File.ReadAllBytes(Path.Combine(FontFolder, name)));
}
