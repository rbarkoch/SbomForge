namespace SbomForge.Configuration;

/// <summary>
/// Configuration for a custom component SBOM (non-.NET project).
/// Allows creating SBOMs for Docker containers, npm packages, or other components
/// that can depend on .NET projects with cross-SBOM consistency.
/// </summary>
public class CustomComponentConfiguration
{
    /// <summary>
    /// Configuration for the custom component and its SBOM.
    /// </summary>
    public SbomConfiguration Sbom { get; set; } = new();

    /// <summary>
    /// List of explicit BomRefs this component depends on.
    /// These are resolved from the project registry during build.
    /// </summary>
    public List<string> DependsOnBomRefs { get; set; } = [];

    /// <summary>
    /// List of project paths this component depends on.
    /// These will be resolved to BomRefs during the build process.
    /// </summary>
    public List<string> DependsOnProjectPaths { get; set; } = [];

    /// <summary>
    /// List of external SBOM dependencies for this custom component.
    /// External SBOMs will be loaded and merged per ResolutionConfiguration.IncludeTransitive.
    /// </summary>
    public List<ExternalComponentConfiguration> ExternalDependencies { get; set; } = [];
}
