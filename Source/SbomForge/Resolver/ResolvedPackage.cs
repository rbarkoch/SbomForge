namespace SbomForge.Resolver;

/// <summary>
/// Represents a resolved NuGet package in the dependency graph.
/// </summary>
internal class ResolvedPackage
{
    public string Id { get; set; }
    public string Version { get; set; }
    public string? PackageHash { get; set; }
    public string? LicenseExpression { get; set; }
    public string? ProjectUrl { get; set; }
    public string? Description { get; set; }
    public bool IsDirect { get; set; }
    public List<string> DependsOn { get; } = [];
}
