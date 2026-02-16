namespace SbomForge.Configuration;

/// <summary>
/// Root configuration container for SBOM generation.
/// Combines component metadata, dependency resolution, filtering, and output settings.
/// </summary>
public class SbomConfiguration
{
    /// <summary>
    /// Metadata for the resulting component. May be an override for global component properties.
    /// </summary>
    public ComponentConfiguration Component { get; } = new();

    /// <summary>
    /// Properties indicating how the SBOM should be resolved. May be an override for global resolution properties.
    /// </summary>
    public ResolutionConfiguration Resolution { get; } = new();

    /// <summary>
    /// Properties indicating how to filter some data from the SBOM. May be an override for global filter properties.
    /// </summary>
    public FiltersConfiguration Filters { get; } = new();

    /// <summary>
    /// Properties indicating how to output the SBOM. May be an override for global output properties.
    /// </summary>
    public OutputConfiguration Output { get; } = new();

    /// <summary>
    /// Custom components to include in the SBOM. These are manually-specified dependencies
    /// that cannot be auto-detected (e.g., Docker images, non-.NET dependencies).
    /// </summary>
    public List<ComponentConfiguration> CustomComponents { get; set; } = [];

    /// <summary>
    /// External SBOM dependencies to merge into the SBOM.
    /// External SBOMs will be loaded and merged per ResolutionConfiguration.IncludeTransitive.
    /// </summary>
    public List<ExternalComponentConfiguration> ExternalDependencies { get; set; } = [];
}
