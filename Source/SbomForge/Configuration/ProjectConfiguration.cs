namespace SbomForge.Configuration;

public class ProjectConfiguration
{
    /// <summary>
    /// The path to the project. It is either an absolute path, or a relative path from the Base Path.
    /// </summary>
    public string ProjectPath { get; set; }

    /// <summary>
    /// Configuration for how to generate the resulting SBOM. These override the global sbom configuration.
    /// </summary>
    public SbomConfiguration Sbom { get; set; } = new();
}
