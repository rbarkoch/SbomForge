namespace SbomForge.Resolver;

/// <summary>
/// Represents a project-to-project reference discovered in the lock file.
/// </summary>
internal record ResolvedProjectReference
{
    public string Name { get; set; }
    public string? Version { get; set; }
    public string? ResolvedPath { get; set; }
    public List<string> DependsOn { get; set; } = [];
}
