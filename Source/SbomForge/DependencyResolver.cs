using NuGet.ProjectModel;

namespace SbomForge;

/// <summary>
/// Represents a resolved package in the dependency graph.
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
/// The full resolved dependency graph for an executable.
/// </summary>
public class DependencyGraph
{
    public required string ExecutableName { get; init; }
    public List<ResolvedPackage> Packages { get; set; } = [];
    public List<string> SourceProjectPaths { get; init; } = [];
}

/// <summary>
/// Reads project.assets.json (the NuGet lock file written by <c>dotnet restore</c>)
/// to build the dependency graph using NuGet.ProjectModel — no MSBuild invocation required.
/// </summary>
internal sealed class DependencyResolver(DependencyResolutionOptions options)
{
    public Task<DependencyGraph> ResolveAsync(ExecutableDefinition executable)
    {
        var graph = new DependencyGraph
        {
            ExecutableName = executable.Name,
        };

        // Collect all project paths that feed into this executable
        var projectPaths = new List<string>();
        if (executable.ProjectPath is not null)
            projectPaths.Add(executable.ProjectPath);
        projectPaths.AddRange(executable.IncludedProjectPaths);

        var allPackages = new Dictionary<string, ResolvedPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in projectPaths)
        {
            graph.SourceProjectPaths.Add(projectPath);
            var packages = ReadAssetsFile(projectPath);

            // Merge packages — if same package appears across projects,
            // keep the higher version (simple conflict resolution strategy)
            foreach (var pkg in packages)
            {
                if (!allPackages.TryGetValue(pkg.Id, out var existing))
                {
                    allPackages[pkg.Id] = pkg;
                }
                else if (IsHigherVersion(pkg.Version, existing.Version))
                {
                    allPackages[pkg.Id] = pkg;
                }
            }
        }

        graph.Packages = [.. allPackages.Values];
        return Task.FromResult(graph);
    }

    private List<ResolvedPackage> ReadAssetsFile(string projectPath)
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

    private List<ResolvedPackage> ParseLockFile(LockFile lockFile, string projectPath)
    {
        var packages = new Dictionary<string, ResolvedPackage>(StringComparer.OrdinalIgnoreCase);

        // Select the correct target framework
        var target = ResolveTarget(lockFile);
        if (target is null)
            return [];

        // Precompute direct dependency names from the project spec
        var directDependencyNames = GetDirectDependencyNames(lockFile);

        foreach (var library in target.Libraries)
        {
            // Only include NuGet packages, not project references
            if (!string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase))
                continue;

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

        // If not including transitive, filter to only direct deps
        if (!options.IncludeTransitive)
            return [.. packages.Values.Where(p => p.IsDirect)];

        return [.. packages.Values];
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

    private static bool IsHigherVersion(string candidate, string current)
    {
        if (NuGet.Versioning.NuGetVersion.TryParse(candidate, out var v1) &&
            NuGet.Versioning.NuGetVersion.TryParse(current, out var v2))
            return v1 > v2;

        return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
