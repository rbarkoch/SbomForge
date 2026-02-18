using System.Diagnostics;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.ProjectModel;
using SbomForge.Configuration;

namespace SbomForge.Resolver;

/// <summary>
/// Resolves NuGet and project-to-project dependencies for a .NET project
/// by reading the NuGet lock file (project.assets.json). Supports SDK-style
/// projects using PackageReference across all .NET project types
/// (.csproj, .fsproj, .vbproj), single and multi-targeted frameworks,
/// and transitive dependency resolution.
/// </summary>
internal class DependencyResolver
{
    private static readonly string[] ProjectFileExtensions = [".csproj", ".fsproj", ".vbproj"];

    private readonly string _basePath;
    private readonly ResolutionConfiguration _resolution;
    private readonly ProjectConfiguration _project;

    public DependencyResolver(string basePath, ProjectConfiguration project, ResolutionConfiguration resolution)
    {
        _basePath = basePath;
        _project = project;
        _resolution = resolution;
    }

    /// <summary>
    /// Resolves all dependencies for the configured project and builds a
    /// <see cref="DependencyGraph"/> containing NuGet packages and project references.
    /// Runs <c>dotnet restore</c> if the assets file is missing.
    /// </summary>
    public async Task<DependencyGraph> ResolveAsync()
    {
        string projectPath = ResolveProjectPath();
        await EnsureRestoredAsync(projectPath);

        string assetsFilePath = FindAssetsFile(projectPath);
        LockFile lockFile = LoadLockFile(assetsFilePath);

        LockFileTarget target = SelectTarget(lockFile);
        HashSet<string> directDependencyIds = GetDirectDependencyIds(lockFile, target);
        string? packagesPath = GetPackagesPath(lockFile);

        // Build a lookup of library-level metadata (sha512, path, msbuildProject) keyed by "Name/Version".
        Dictionary<string, LockFileLibrary> libraryLookup = BuildLibraryLookup(lockFile);

        // Identify which target libraries are project references so we can tag
        // DependsOn entries with the correct reference type.
        HashSet<string> projectReferenceNames = GetProjectReferenceNames(target);

        DependencyGraph graph = new()
        {
            ProjectName = lockFile.PackageSpec?.Name
                          ?? Path.GetFileNameWithoutExtension(projectPath),
            SourceProjectPath = projectPath
        };

        foreach (LockFileTargetLibrary targetLib in target.Libraries)
        {
            string key = $"{targetLib.Name}/{targetLib.Version}";
            libraryLookup.TryGetValue(key, out LockFileLibrary? library);

            if (IsProjectReference(targetLib))
            {
                graph.ProjectReferences.Add(
                    BuildProjectReference(targetLib, library, projectPath, projectReferenceNames));
            }
            else
            {
                graph.Packages.Add(
                    BuildResolvedPackage(targetLib, library, packagesPath, directDependencyIds, projectReferenceNames));
            }
        }

        return graph;
    }

    // ───────────────────────────── Project Path Resolution ─────────────────────────────

    /// <summary>
    /// Resolves the configured project path to an absolute path. If the path
    /// points to a directory, the first project file found inside it is used.
    /// </summary>
    private string ResolveProjectPath()
    {
        string path = Path.IsPathRooted(_project.ProjectPath)
            ? _project.ProjectPath
            : Path.GetFullPath(Path.Combine(_basePath, _project.ProjectPath));

        // If the path points to a directory, search for a project file inside it.
        if (Directory.Exists(path))
        {
            string? found = FindProjectFileInDirectory(path);
            if (found is null)
                throw new FileNotFoundException(
                    $"No project file (.csproj, .fsproj, .vbproj) found in directory: {path}");
            path = found;
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"Project file not found: {path}");

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Searches a directory for the first project file matching known extensions.
    /// </summary>
    private static string? FindProjectFileInDirectory(string directory)
    {
        foreach (string ext in ProjectFileExtensions)
        {
            string[] matches = Directory.GetFiles(directory, $"*{ext}", SearchOption.TopDirectoryOnly);
            if (matches.Length > 0)
                return matches[0];
        }
        return null;
    }

    // ──────────────────────────────── Restore / Assets ─────────────────────────────────

    /// <summary>
    /// Runs <c>dotnet restore</c> when the project.assets.json file is missing.
    /// </summary>
    private static async Task EnsureRestoredAsync(string projectPath)
    {
        string projectDir = Path.GetDirectoryName(projectPath)!;
        string assetsFile = Path.Combine(projectDir, "obj", "project.assets.json");

        if (File.Exists(assetsFile))
            return;

        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            Arguments = $"restore \"{projectPath}\"",
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start 'dotnet restore'.");

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"'dotnet restore' failed for {projectPath} (exit code {process.ExitCode}):\n{stderr}");
        }

        if (!File.Exists(assetsFile))
            throw new FileNotFoundException(
                $"project.assets.json was not generated after restore: {assetsFile}");
    }

    /// <summary>
    /// Locates the project.assets.json file for the given project path.
    /// Supports both standard (<c>obj/project.assets.json</c>) and custom intermediate
    /// output paths declared via <c>BaseIntermediateOutputPath</c>.
    /// </summary>
    private static string FindAssetsFile(string projectPath)
    {
        string projectDir = Path.GetDirectoryName(projectPath)!;

        // Standard location.
        string standard = Path.Combine(projectDir, "obj", "project.assets.json");
        if (File.Exists(standard))
            return standard;

        // Check for a custom intermediate output path declared in the project file.
        string? customIntermediateOutputPath = TryReadIntermediateOutputPath(projectPath);
        if (customIntermediateOutputPath is not null)
        {
            string customPath = Path.IsPathRooted(customIntermediateOutputPath)
                ? Path.Combine(customIntermediateOutputPath, "project.assets.json")
                : Path.Combine(projectDir, customIntermediateOutputPath, "project.assets.json");
            if (File.Exists(customPath))
                return customPath;
        }

        throw new FileNotFoundException(
            $"project.assets.json not found for project: {projectPath}. " +
            "Ensure the project has been restored (dotnet restore).");
    }

    /// <summary>
    /// Attempts to read <c>BaseIntermediateOutputPath</c> or <c>IntermediateOutputPath</c>
    /// from the MSBuild project file for non-standard obj locations.
    /// </summary>
    private static string? TryReadIntermediateOutputPath(string projectPath)
    {
        try
        {
            XDocument doc = XDocument.Load(projectPath);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            string? value = doc.Descendants(ns + "BaseIntermediateOutputPath").FirstOrDefault()?.Value
                         ?? doc.Descendants(ns + "IntermediateOutputPath").FirstOrDefault()?.Value
                         // Handle non-namespaced elements (common in SDK-style projects).
                         ?? doc.Descendants("BaseIntermediateOutputPath").FirstOrDefault()?.Value
                         ?? doc.Descendants("IntermediateOutputPath").FirstOrDefault()?.Value;

            return string.IsNullOrWhiteSpace(value) ? null : value!.TrimEnd('/', '\\');
        }
        catch
        {
            return null;
        }
    }

    // ────────────────────────────── Lock File Parsing ──────────────────────────────────

    /// <summary>
    /// Loads and validates the NuGet lock file.
    /// </summary>
    private static LockFile LoadLockFile(string assetsFilePath)
    {
        LockFile? lockFile = LockFileUtilities.GetLockFile(assetsFilePath, NullLogger.Instance);
        if (lockFile is null)
            throw new InvalidOperationException(
                $"Failed to parse lock file: {assetsFilePath}");
        return lockFile;
    }

    /// <summary>
    /// Selects the <see cref="LockFileTarget"/> matching the configured target framework.
    /// When no framework is specified, the first (or only) target is used.
    /// Falls back to a case-insensitive short-name match (e.g. <c>net8.0</c>).
    /// </summary>
    private LockFileTarget SelectTarget(LockFile lockFile)
    {
        IList<LockFileTarget> targets = lockFile.Targets;
        if (targets.Count == 0)
            throw new InvalidOperationException("Lock file contains no resolved targets.");

        // No explicit framework requested — use the first non-RID-specific target
        // (RID-specific targets duplicate the base target with runtime assets).
        if (string.IsNullOrEmpty(_resolution.TargetFramework))
            return targets.FirstOrDefault(t => string.IsNullOrEmpty(t.RuntimeIdentifier))
                   ?? targets[0];

        string requested = _resolution.TargetFramework!;

        // Try an exact match on the full framework name first, then fall back to
        // the short folder name (e.g. "net8.0", "net462", "netstandard2.0").
        LockFileTarget? match = targets.FirstOrDefault(t =>
                string.IsNullOrEmpty(t.RuntimeIdentifier) &&
                t.TargetFramework.ToString().Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? targets.FirstOrDefault(t =>
                string.IsNullOrEmpty(t.RuntimeIdentifier) &&
                t.TargetFramework.GetShortFolderName().Equals(requested, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            string available = string.Join(", ",
                targets
                    .Where(t => string.IsNullOrEmpty(t.RuntimeIdentifier))
                    .Select(t => t.TargetFramework.GetShortFolderName()));
            throw new InvalidOperationException(
                $"Target framework '{requested}' not found in lock file. Available: {available}");
        }

        return match!;
    }

    // ─────────────────────────── Direct Dependency Detection ───────────────────────────

    /// <summary>
    /// Determines the set of package/project IDs that are direct dependencies of the project
    /// (as opposed to transitive). Uses <c>projectFileDependencyGroups</c> from the lock file,
    /// which lists dependencies declared directly in the project file.
    /// </summary>
    private static HashSet<string> GetDirectDependencyIds(LockFile lockFile, LockFileTarget target)
    {
        HashSet<string> directIds = new(StringComparer.OrdinalIgnoreCase);

        // projectFileDependencyGroups is keyed by framework short name (e.g. "net8.0")
        // or empty string for framework-agnostic entries.
        string tfmShortName = target.TargetFramework.GetShortFolderName();

        foreach (ProjectFileDependencyGroup group in lockFile.ProjectFileDependencyGroups)
        {
            // Include the framework-specific group and the empty (agnostic) group.
            if (!string.IsNullOrEmpty(group.FrameworkName) &&
                !group.FrameworkName.Equals(tfmShortName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (string entry in group.Dependencies)
            {
                // Entries are formatted as "PackageId >= Version" or just "PackageId".
                string id = entry.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
                directIds.Add(id);
            }
        }

        // Also include project references declared in the lock file's PackageSpec.
        PackageSpec? packageSpec = lockFile.PackageSpec;
        if (packageSpec is not null)
        {
            foreach (var frameworkInfo in packageSpec.RestoreMetadata.TargetFrameworks)
            {
                foreach (var projRef in frameworkInfo.ProjectReferences)
                {
                    string? projName = Path.GetFileNameWithoutExtension(projRef.ProjectPath);
                    if (projName is not null)
                        directIds.Add(projName);
                }
            }
        }

        return directIds;
    }

    // ────────────────────────────── Library Lookups ────────────────────────────────────

    /// <summary>
    /// Builds a dictionary of <see cref="LockFileLibrary"/> entries keyed by "Name/Version".
    /// This provides access to library-level metadata (sha512, package path, nuspec path).
    /// </summary>
    private static Dictionary<string, LockFileLibrary> BuildLibraryLookup(LockFile lockFile)
    {
        Dictionary<string, LockFileLibrary> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (LockFileLibrary lib in lockFile.Libraries)
        {
            string key = $"{lib.Name}/{lib.Version?.ToNormalizedString()}";
            if (!lookup.ContainsKey(key))
                lookup.Add(key, lib);
        }
        return lookup;
    }

    /// <summary>
    /// Collects the names of all target libraries whose type is "project".
    /// Used to classify DependsOn entries as project references vs NuGet packages.
    /// </summary>
    private static HashSet<string> GetProjectReferenceNames(LockFileTarget target)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (LockFileTargetLibrary lib in target.Libraries)
        {
            if (IsProjectReference(lib) && lib.Name is not null)
                names.Add(lib.Name);
        }
        return names;
    }

    /// <summary>
    /// Returns true when the target library represents a project-to-project reference.
    /// </summary>
    private static bool IsProjectReference(LockFileTargetLibrary lib)
    {
        return string.Equals(lib.Type, "project", StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────── Packages Path Resolution ─────────────────────────────

    /// <summary>
    /// Extracts the global NuGet packages directory from the lock file's <c>PackageSpec</c>.
    /// Falls back to the default <c>~/.nuget/packages</c> location.
    /// </summary>
    private static string? GetPackagesPath(LockFile lockFile)
    {
        string? path = lockFile.PackageSpec?.RestoreMetadata?.PackagesPath;
        if (!string.IsNullOrEmpty(path))
            return path;

        // Default NuGet packages path.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultPath = Path.Combine(home, ".nuget", "packages");
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }

    // ────────────────────────────── Graph Node Builders ────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ResolvedProjectReference"/> from a target library entry.
    /// </summary>
    private static ResolvedProjectReference BuildProjectReference(
        LockFileTargetLibrary targetLib,
        LockFileLibrary? library,
        string projectPath,
        HashSet<string> projectReferenceNames)
    {
        string? resolvedPath = ResolveProjectReferencePath(library, projectPath);
        List<string> dependsOn = BuildDependsOnList(targetLib, projectReferenceNames);

        return new ResolvedProjectReference
        {
            Name = targetLib.Name ?? "",
            Version = targetLib.Version?.ToNormalizedString(),
            ResolvedPath = resolvedPath,
            DependsOn = dependsOn
        };
    }

    /// <summary>
    /// Builds a <see cref="ResolvedPackage"/> from a target library entry,
    /// enriched with metadata from the .nuspec file when available.
    /// </summary>
    private static ResolvedPackage BuildResolvedPackage(
        LockFileTargetLibrary targetLib,
        LockFileLibrary? library,
        string? packagesPath,
        HashSet<string> directDependencyIds,
        HashSet<string> projectReferenceNames)
    {
        NuspecMetadata metadata = TryReadNuspecMetadata(library, packagesPath);
        List<string> dependsOn = BuildDependsOnList(targetLib, projectReferenceNames);
        string name = targetLib.Name ?? "";

        ResolvedPackage package = new()
        {
            Id = name,
            Version = targetLib.Version?.ToNormalizedString() ?? "0.0.0",
            PackageHash = library?.Sha512,
            LicenseExpression = metadata.LicenseExpression,
            ProjectUrl = metadata.ProjectUrl,
            Description = metadata.Description,
            IsDirect = directDependencyIds.Contains(name)
        };

        package.DependsOn.AddRange(dependsOn);
        return package;
    }

    /// <summary>
    /// Generates the DependsOn list for a library, using the format
    /// <c>DependencyId/ResolvedVersion</c> for each transitive dependency.
    /// </summary>
    private static List<string> BuildDependsOnList(
        LockFileTargetLibrary targetLib,
        HashSet<string> projectReferenceNames)
    {
        List<string> dependsOn = [];
        foreach (var dep in targetLib.Dependencies)
        {
            // Use MinVersion as the resolved version — the lock file records the
            // exact resolved version as the lower bound of the range.
            string version = dep.VersionRange?.MinVersion?.ToNormalizedString() ?? "0.0.0";
            dependsOn.Add($"{dep.Id}/{version}");
        }
        return dependsOn;
    }

    // ───────────────────────── Project Reference Path Resolution ───────────────────────

    /// <summary>
    /// Resolves the absolute file path to a referenced project's .csproj / .fsproj / .vbproj file.
    /// The lock file's <c>LockFileLibrary.Path</c> and <c>MSBuildProject</c> properties hold
    /// relative paths from the owning project's directory.
    /// </summary>
    private static string? ResolveProjectReferencePath(
        LockFileLibrary? library,
        string projectPath)
    {
        if (library is null)
            return null;

        // MSBuildProject is typically the relative path to the .csproj.
        string? relativePath = library.MSBuildProject ?? library.Path;
        if (string.IsNullOrEmpty(relativePath))
            return null;

        string projectDir = Path.GetDirectoryName(projectPath)!;
        string absolutePath = Path.GetFullPath(Path.Combine(projectDir, relativePath));
        return File.Exists(absolutePath) ? absolutePath : null;
    }

    // ──────────────────────────── NuSpec Metadata Reading ──────────────────────────────

    /// <summary>
    /// Attempts to read metadata from the package's .nuspec file in the global
    /// NuGet packages directory. Returns an empty <see cref="NuspecMetadata"/>
    /// if the file cannot be found or parsed.
    /// </summary>
    private static NuspecMetadata TryReadNuspecMetadata(
        LockFileLibrary? library,
        string? packagesPath)
    {
        if (library is null || string.IsNullOrEmpty(packagesPath) || string.IsNullOrEmpty(library.Path))
            return new NuspecMetadata();

        // The .nuspec file lives at:
        // {packagesPath}/{library.Path}/{packageId.ToLowerInvariant()}.nuspec
        string nuspecPath = Path.Combine(
            packagesPath,
            library.Path,
            $"{library.Name.ToLowerInvariant()}.nuspec");

        if (!File.Exists(nuspecPath))
            return new NuspecMetadata();

        return ParseNuspec(nuspecPath);
    }

    /// <summary>
    /// Parses a .nuspec XML file and extracts license, project URL, and description metadata.
    /// Handles both namespaced and non-namespaced nuspec formats.
    /// </summary>
    private static NuspecMetadata ParseNuspec(string nuspecPath)
    {
        try
        {
            XDocument doc = XDocument.Load(nuspecPath);
            XElement? root = doc.Root;
            if (root is null)
                return new NuspecMetadata();

            XNamespace ns = root.GetDefaultNamespace();
            XElement? metadata = root.Element(ns + "metadata") ?? root.Element("metadata");
            if (metadata is null)
                return new NuspecMetadata();

            // License can be in <license type="expression"> or <licenseUrl>.
            string? license = GetElementValue(metadata, ns, "license")
                           ?? GetElementValue(metadata, ns, "licenseUrl");

            string? projectUrl = GetElementValue(metadata, ns, "projectUrl");
            string? description = GetElementValue(metadata, ns, "description");

            return new NuspecMetadata
            {
                LicenseExpression = license,
                ProjectUrl = projectUrl,
                Description = description
            };
        }
        catch
        {
            return new NuspecMetadata();
        }
    }

    /// <summary>
    /// Reads an element's text value, trying the namespaced name first,
    /// then falling back to the local name for compatibility with older nuspec formats.
    /// </summary>
    private static string? GetElementValue(XElement parent, XNamespace ns, string localName)
    {
        string? value = parent.Element(ns + localName)?.Value
                     ?? parent.Element(localName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}
