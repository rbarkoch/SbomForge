namespace SbomForge.Configuration;

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
