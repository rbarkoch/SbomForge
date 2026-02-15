namespace SbomForge.Configuration;

/// <summary>
/// Configuration for including an external SBOM file in the generated output.
/// Allows loading and optionally overriding metadata from external SBOMs.
/// </summary>
public class ExternalComponentConfiguration
{
    /// <summary>
    /// The path to the external SBOM file.
    /// </summary>
    public string ExternalPath { get; set; }

    /// <summary>
    /// Override values for the primary loaded component from the external SBOM.
    /// </summary>
    public ComponentConfiguration Component { get; set; } = new();
}
