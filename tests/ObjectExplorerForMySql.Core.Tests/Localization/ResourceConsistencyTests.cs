using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Localization;

/// <summary>
/// Checks the resource files of the VSIX project as files on disk (the net48 VSIX project cannot be
/// referenced from this net10.0 test project). Guards ADR-010: no hardcoded user-visible text, and a
/// new language only needs complete resource files.
/// </summary>
public partial class ResourceConsistencyTests
{
    private static readonly string _vsixDirectory = Path.Combine(FindRepositoryRoot(), "src", "ObjectExplorerForMySql.Vsix");
    private static readonly string _resourcesDirectory = Path.Combine(_vsixDirectory, "Resources");
    private static readonly string _vsextensionDirectory = Path.Combine(_vsixDirectory, ".vsextension");

    [Fact]
    public void NeutralResx_HasExactlyTheKeysExposedByStrings()
    {
        var stringsSource = File.ReadAllText(Path.Combine(_resourcesDirectory, "Strings.cs"));
        var exposedKeys = GetNameofKeyRegex().Matches(stringsSource).Select(m => m.Groups["key"].Value).ToHashSet();

        var resxKeys = ReadResx(Path.Combine(_resourcesDirectory, "Strings.resx")).Keys.ToHashSet();

        Assert.NotEmpty(exposedKeys);
        Assert.Empty(exposedKeys.Except(resxKeys));   // used in code but missing in Strings.resx
        Assert.Empty(resxKeys.Except(exposedKeys));   // in Strings.resx but never used
    }

    [Fact]
    public void NeutralResx_HasNoEmptyValues()
    {
        var empty = ReadResx(Path.Combine(_resourcesDirectory, "Strings.resx"))
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key);

        Assert.Empty(empty);
    }

    [Fact]
    public void CultureResx_HaveTheSameKeysAsNeutralResx()
    {
        var neutralKeys = ReadResx(Path.Combine(_resourcesDirectory, "Strings.resx")).Keys.ToHashSet();
        var problems = new List<string>();

        foreach (var file in Directory.GetFiles(_resourcesDirectory, "Strings.*.resx"))
        {
            var cultureEntries = ReadResx(file);
            var missing = neutralKeys.Except(cultureEntries.Keys);
            var extra = cultureEntries.Keys.Except(neutralKeys);
            var empty = cultureEntries.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key);

            problems.AddRange(missing.Select(key => $"{Path.GetFileName(file)}: missing '{key}'"));
            problems.AddRange(extra.Select(key => $"{Path.GetFileName(file)}: unknown '{key}'"));
            problems.AddRange(empty.Select(key => $"{Path.GetFileName(file)}: empty '{key}'"));
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void StringResourcesJson_ContainsEveryPlaceholderUsedInCode()
    {
        var defaultKeys = ReadStringResources(Path.Combine(_vsextensionDirectory, "string-resources.json")).Keys.ToHashSet();

        var usedKeys = Directory.GetFiles(_vsixDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => GetPlaceholderRegex().Matches(File.ReadAllText(path)).Select(m => m.Groups["key"].Value))
            .ToHashSet();

        Assert.NotEmpty(usedKeys);
        Assert.Empty(usedKeys.Except(defaultKeys));
    }

    [Fact]
    public void CultureStringResourcesJson_HaveTheSameKeysAsDefault()
    {
        var defaultKeys = ReadStringResources(Path.Combine(_vsextensionDirectory, "string-resources.json")).Keys.ToHashSet();
        var problems = new List<string>();

        foreach (var cultureDirectory in Directory.GetDirectories(_vsextensionDirectory))
        {
            var file = Path.Combine(cultureDirectory, "string-resources.json");
            if (!File.Exists(file))
            {
                continue;
            }

            var keys = ReadStringResources(file).Keys.ToHashSet();
            var culture = Path.GetFileName(cultureDirectory);
            problems.AddRange(defaultKeys.Except(keys).Select(key => $"{culture}: missing '{key}'"));
            problems.AddRange(keys.Except(defaultKeys).Select(key => $"{culture}: unknown '{key}'"));
        }

        Assert.Empty(problems);
    }

    private static Dictionary<string, string> ReadResx(string path) =>
        XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);

    private static Dictionary<string, string> ReadStringResources(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ObjectExplorerForMySql.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (ObjectExplorerForMySql.slnx) not found.");
    }

    [GeneratedRegex(@"Get\(nameof\((?<key>\w+)\)\)")]
    private static partial Regex GetNameofKeyRegex();

    [GeneratedRegex(@"""%(?<key>[\w.]+)%""")]
    private static partial Regex GetPlaceholderRegex();
}
