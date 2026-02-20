namespace SbomForge.Resolver;

/// <summary>
/// Represents a resolved NuGet package in the dependency graph.
/// </summary>
internal class ResolvedPackage
{
    public string Id { get; set; } = null!;
    public string Version { get; set; } = null!;
    public string? PackageHash { get; set; }
    public Nuspec? Nuspec { get; set; } = null!;
    public bool IsDirect { get; set; }
    
    public List<string> DependsOn { get; } = [];
}
