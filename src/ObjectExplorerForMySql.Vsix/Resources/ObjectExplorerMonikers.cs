using System;
using Microsoft.VisualStudio.Imaging.Interop;

namespace ObjectExplorerForMySql.Resources;

/// <summary>Own images from ObjectExplorerImages.imagemanifest; the guid and ids match the manifest and the VSCT file.</summary>
public static class ObjectExplorerMonikers
{
    /// <summary>Guid of the image catalog of the extension.</summary>
    public static readonly Guid ImageCatalogGuid = new("8d3c6f5e-2a41-4b7e-9c15-6e0f4a7b3d92");

    /// <summary>Gets the Explain image: a small plan tree with the costly step in orange (US-1301).</summary>
    public static ImageMoniker Explain => new() { Guid = ImageCatalogGuid, Id = 1 };

    /// <summary>Gets the Explain Analyze image: the plan tree with a clock (US-1302).</summary>
    public static ImageMoniker ExplainAnalyze => new() { Guid = ImageCatalogGuid, Id = 2 };
}
