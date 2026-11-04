using Microsoft.VisualStudio.Extensibility;

namespace ObjectExplorerForMySql;

/// <summary>
/// VisualStudio.Extensibility entry point. Runs in-process so it can share the VSSDK package (ADR-001).
/// </summary>
[VisualStudioContribution]
internal sealed class ExtensionEntrypoint : Extension
{
    /// <inheritdoc />
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        RequiresInProcessHosting = true,
    };
}
