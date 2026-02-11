using NuGet.ProjectModel;

namespace SbomForge;

/// <summary>
/// Represents a resolved NuGet package in the dependency graph.
/// </summary>
public record ResolvedPackage
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public string? PackageHash { get; init; }
    public string? LicenseExpression { get; init; }
    public string? ProjectUrl { get; init; }
    public string? Description { get; init; }
    public bool IsDirect { get; init; }
    public List<string> DependsOn { get; init; } = [];
}

/// <summary>
/// Represents a project-to-project reference discovered in the lock file.
/// </summary>
public record ResolvedProjectReference
{
    public required string Name { get; init; }
    public string? ResolvedPath { get; init; }
    public List<string> DependsOn { get; init; } = [];
}

/// <summary>
/// The full resolved dependency graph for a project, including both
/// NuGet packages and project-to-project references.
/// </summary>
public class DependencyGraph
{
    public required string ProjectName { get; init; }
    public List<ResolvedPackage> Packages { get; set; } = [];
    public List<ResolvedProjectReference> ProjectReferences { get; set; } = [];
    public string SourceProjectPath { get; init; } = "";
}

/// <summary>
/// Reads project.assets.json (the NuGet lock file written by <c>dotnet restore</c>)
/// to build the dependency graph using NuGet.ProjectModel — no MSBuild invocation required.
/// Also extracts project-to-project references so they can be cross-linked with
/// configured project metadata.
/// </summary>
internal sealed class DependencyResolver(DependencyResolutionOptions options)
{
    public Task<DependencyGraph> ResolveAsync(ProjectDefinition project)
    {
        var graph = new DependencyGraph
        {
            ProjectName = project.Name,
            SourceProjectPath = project.ProjectPath,
        };

        var (packages, projectRefs) = ReadAssetsFile(project.ProjectPath);
        graph.Packages = packages;
        graph.ProjectReferences = projectRefs;

        return Task.FromResult(graph);
    }

    private (List<ResolvedPackage>, List<ResolvedProjectReference>) ReadAssetsFile(string projectPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for: {projectPath}");
        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");

        if (!File.Exists(assetsPath))
            throw new FileNotFoundException(
                $"project.assets.json not found for {projectPath}. " +
                $"Run 'dotnet restore' first.\n  Expected: {assetsPath}");

        // Use NuGet.ProjectModel for proper parsing instead of raw JSON
        var lockFile = LockFileUtilities.GetLockFile(assetsPath, NuGet.Common.NullLogger.Instance);
        if (lockFile is null)
            throw new InvalidOperationException($"Failed to parse lock file: {assetsPath}");

        return ParseLockFile(lockFile, projectPath);
    }

    private (List<ResolvedPackage>, List<ResolvedProjectReference>) ParseLockFile(
        LockFile lockFile, string projectPath)
    {
        var packages = new Dictionary<string, ResolvedPackage>(StringComparer.OrdinalIgnoreCase);
        var projectRefs = new List<ResolvedProjectReference>();
        var projectDir = Path.GetDirectoryName(projectPath) ?? ".";

        // Select the correct target framework
        var target = ResolveTarget(lockFile);
        if (target is null)
            return ([], []);

        // Precompute direct dependency names from the project spec
        var directDependencyNames = GetDirectDependencyNames(lockFile);

        foreach (var library in target.Libraries)
        {
            if (string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase))
            {
                var pkg = new ResolvedPackage
                {
                    Id = library.Name!,
                    Version = library.Version?.ToNormalizedString() ?? "0.0.0",
                    IsDirect = directDependencyNames.Contains(library.Name!),
                    DependsOn = options.IncludeTransitive
                        ? [.. library.Dependencies.Select(d => d.Id)]
                        : [],
                };

                // Enrich from the libraries section (contains content hash, path info)
                if (lockFile.Libraries
                    .FirstOrDefault(l => string.Equals(l.Name, library.Name, StringComparison.OrdinalIgnoreCase)
                                         && l.Version == library.Version) is { } libraryInfo)
                {
                    pkg = pkg with { PackageHash = libraryInfo.Sha512 };
                }

                packages[pkg.Id!] = pkg;
            }
            else if (string.Equals(library.Type, "project", StringComparison.OrdinalIgnoreCase))
            {
                // Find the library entry to resolve the project path
                var libInfo = lockFile.Libraries
                    .FirstOrDefault(l => string.Equals(l.Name, library.Name, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(l.Type, "project", StringComparison.OrdinalIgnoreCase));

                string? resolvedPath = null;
                if (libInfo?.MSBuildProject is not null)
                {
                    resolvedPath = Path.GetFullPath(Path.Combine(projectDir, libInfo.MSBuildProject));
                }

                projectRefs.Add(new ResolvedProjectReference
                {
                    Name = library.Name!,
                    ResolvedPath = resolvedPath,
                    DependsOn = [.. library.Dependencies.Select(d => d.Id)],
                });
            }
        }

        // If not including transitive, filter to only direct deps
        if (!options.IncludeTransitive)
            return ([.. packages.Values.Where(p => p.IsDirect)], projectRefs);

        return ([.. packages.Values], projectRefs);
    }

    private LockFileTarget? ResolveTarget(LockFile lockFile)
    {
        // If user specified a TFM, find the matching target
        if (!string.IsNullOrEmpty(options.TargetFramework))
        {
            var match = lockFile.Targets
                .FirstOrDefault(t => t.TargetFramework.GetShortFolderName()
                    .Contains(options.TargetFramework, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(t.RuntimeIdentifier));

            if (match is not null)
                return match;
        }

        // Prefer targets without a runtime identifier (pure TFM targets)
        return lockFile.Targets
            .FirstOrDefault(t => string.IsNullOrEmpty(t.RuntimeIdentifier))
            ?? lockFile.Targets.FirstOrDefault();
    }

    private static HashSet<string> GetDirectDependencyNames(LockFile lockFile)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in lockFile.ProjectFileDependencyGroups)
        {
            foreach (var dep in group.Dependencies)
            {
                // Dependency strings look like "PackageName >= 1.0.0"
                var spaceIndex = dep.IndexOf(' ');
                var name = spaceIndex > 0 ? dep[..spaceIndex] : dep;
                names.Add(name);
            }
        }

        return names;
    }
}
