using System.Globalization;
using System.Resources;

namespace ObjectExplorerForMySql.Core.ObjectProperties;

/// <summary>
/// Category, property and object type names shown in the Visual Studio Properties window (E04).
/// The texts live in <c>Resources/PropertyTexts.resx</c> of this library, so the mapping can be unit tested
/// without Visual Studio; a language is added with a culture-specific copy of that file (ADR-005).
/// </summary>
public static class PropertyTexts
{
    /// <summary>Manifest name of the embedded resources.</summary>
    internal const string ResourceName = "ObjectExplorerForMySql.Core.Resources.PropertyTexts";

    private static readonly ResourceManager _resourceManager = new(ResourceName, typeof(PropertyTexts).Assembly);

    /// <summary>Returns the text for <paramref name="key"/> in the current UI culture, or the key itself when it is missing.</summary>
    public static string Get(string key) => _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>Returns true when <paramref name="key"/> exists in the neutral resources.</summary>
    internal static bool Exists(string key) => _resourceManager.GetString(key, CultureInfo.InvariantCulture) is not null;
}
