namespace SbomForge.Configuration;

/// <summary>
/// Configuration for dependency resolution behavior.
/// Controls whether to include transitive dependencies and which target framework to use.
/// </summary>
public class ResolutionConfiguration
{
    /// <summary>
    /// Include transitive (indirect) dependencies, not just direct refs.
    /// Null means inherit from parent configuration; defaults to true if not set anywhere.
    /// </summary>
    public bool? IncludeTransitive { get; set; }

    /// <summary>Target framework to resolve against when a project is multi-targeted.</summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// When true, components created from project references inherit global metadata
    /// (such as version, copyright, publisher, supplier, and licenses) from the parent configuration.
    /// Null means inherit from parent configuration; defaults to true if not set anywhere.
    /// </summary>
    public bool? UseGlobalMetadataForProjectReferences { get; set; }
}
