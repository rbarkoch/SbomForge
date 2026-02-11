namespace SbomForge.Resolver;

/// <summary>
/// The full resolved dependency graph for a project, including both
/// NuGet packages and project-to-project references.
/// </summary>
internal class DependencyGraph
{
    public string ProjectName { get; set; }
    public List<ResolvedPackage> Packages { get; set; } = [];
    public List<ResolvedProjectReference> ProjectReferences { get; set; } = [];
    public string SourceProjectPath { get; set; } = "";
}
