namespace SbomForge.Configuration;

public class ResolutionConfiguration
{
    /// <summary>Include transitive (indirect) dependencies, not just direct refs.</summary>
    public bool IncludeTransitive { get; set; } = true;

    /// <summary>Target framework to resolve against when a project is multi-targeted.</summary>
    public string? TargetFramework { get; set; }
}
