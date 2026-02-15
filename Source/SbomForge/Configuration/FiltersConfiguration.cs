namespace SbomForge.Configuration;

/// <summary>
/// Configuration for filtering packages and projects from the generated SBOM.
/// Supports exclusion by package ID, project name, package prefix, and test projects.
/// </summary>
public class FiltersConfiguration
{
    /// <summary>Exact package IDs to exclude.</summary>
    public List<string> ExcludePackageIds { get; set; } = [];

    /// <summary>Exclude projects by name.</summary>
    public List<string> ExcludeProjectNames { get; set; } = [];

    /// <summary>Packages whose ID starts with any of these prefixes are excluded.</summary>
    public List<string> ExcludePackagePrefixes { get; set; } = [];

    /// <summary>When true, test projects are automatically excluded (default: true).</summary>
    public bool ExcludeTestProjects { get; set; } = true;
}
