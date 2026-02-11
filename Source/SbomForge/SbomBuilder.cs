using CycloneDX.Models;

namespace SbomForge;

/// <summary>
/// Entry point — users call <c>SbomBuilder.ForSolution(...)</c> or
/// <c>SbomBuilder.ForProject(...)</c> to start configuring SBOM generation.
/// </summary>
public class SbomBuilder
{
    private readonly string? _solutionPath;
    private readonly List<ExecutableDefinition> _executables = [];
    private readonly ComponentFilter _filter = new();
    private readonly DependencyResolutionOptions _resolution = new();
    private readonly OutputOptions _output = new();
    private readonly Component _globalMetadata = new();

    private SbomBuilder(string? solutionPath)
    {
        _solutionPath = solutionPath;
    }

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Start building SBOMs for a .sln solution file. All project paths
    /// used later in <see cref="AddExecutable"/> are relative to the solution directory.
    /// </summary>
    public static SbomBuilder ForSolution(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution not found: {solutionPath}");

        return new SbomBuilder(Path.GetFullPath(solutionPath));
    }

    /// <summary>
    /// Convenience entry point for single-project use. The project is
    /// automatically treated as a single executable definition.
    /// </summary>
    public static SbomBuilder ForProject(string projectPath)
    {
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project not found: {projectPath}");

        var fullPath = Path.GetFullPath(projectPath);
        var builder = new SbomBuilder(null);

        builder._executables.Add(new ExecutableDefinition
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            ProjectPath = fullPath,
        });

        return builder;
    }

    // -------------------------------------------------------------------------
    // Global metadata — applies to all executables unless overridden
    // -------------------------------------------------------------------------

    /// <summary>
    /// Set global CycloneDX component metadata applied to all executables.
    /// Individual executables can override any field via their own <c>WithMetadata</c>.
    /// </summary>
    public SbomBuilder WithMetadata(Action<Component> configure)
    {
        configure(_globalMetadata);
        return this;
    }

    // -------------------------------------------------------------------------
    // Executable registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Declare an executable and its constituent projects. This is the key
    /// feature: explicitly define what constitutes each deployable unit and
    /// which projects feed into it.
    /// </summary>
    public SbomBuilder AddExecutable(string name, Action<ExecutableBuilder> configure)
    {
        var execBuilder = new ExecutableBuilder(name, _solutionPath);
        configure(execBuilder);
        _executables.Add(execBuilder.Build());
        return this;
    }

    /// <summary>
    /// Auto-discover executable projects from the solution file (projects with
    /// OutputType = Exe or WinExe). Requires Microsoft.Build packages.
    /// Less control than explicit <see cref="AddExecutable"/> declarations.
    /// </summary>
    public SbomBuilder DiscoverExecutables()
    {
        if (_solutionPath is null)
            throw new InvalidOperationException(
                "DiscoverExecutables() requires a solution path. Use ForSolution() instead of ForProject().");

        var resolver = new SolutionResolver(_solutionPath);
        var discovered = resolver.FindExecutableProjects();

        foreach (var (name, path) in discovered)
        {
            _executables.Add(new ExecutableDefinition
            {
                Name = name,
                ProjectPath = path,
                Metadata = _globalMetadata.Clone(),
            });
        }

        return this;
    }

    // -------------------------------------------------------------------------
    // Filtering
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configure package and project exclusion rules.
    /// </summary>
    public SbomBuilder WithFilters(Action<ComponentFilter> configure)
    {
        configure(_filter);
        return this;
    }

    // -------------------------------------------------------------------------
    // Dependency resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configure how dependencies are resolved (transitive inclusion, TFM, dedup).
    /// </summary>
    public SbomBuilder WithResolution(Action<DependencyResolutionOptions> configure)
    {
        configure(_resolution);
        return this;
    }

    // -------------------------------------------------------------------------
    // Output
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configure output format, directory, scope, and file naming.
    /// </summary>
    public SbomBuilder WithOutput(Action<OutputOptions> configure)
    {
        configure(_output);
        return this;
    }

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Execute the full SBOM generation pipeline: resolve dependencies,
    /// compose BOMs, optionally merge, and write to disk.
    /// </summary>
    public async Task<SbomResult> BuildAsync()
    {
        if (_executables.Count == 0)
            throw new InvalidOperationException(
                "No executables defined. Call AddExecutable() or DiscoverExecutables() first.");

        var resolver = new DependencyResolver(_resolution);
        var composer = new SbomComposer(_filter, _globalMetadata);
        var writer = new SbomWriter(_output);

        var result = new SbomResult();

        foreach (var executable in _executables)
        {
            // Merge component: global defaults, then per-executable overrides
            var effectiveMetadata = _globalMetadata.MergeWith(executable.Metadata);

            // Resolve the full dependency graph for this executable
            var graph = await resolver.ResolveAsync(executable);

            // Compose into a CycloneDX BOM
            var bom = composer.Compose(executable, graph, effectiveMetadata);

            result.AddBom(executable.Name, bom);
        }

        // If solution-scoped output is requested, merge all BOMs
        if (_output.Scope is SbomScope.Solution or SbomScope.Both)
        {
            var merged = composer.Merge(result.Boms.Values);
            result.SetSolutionBom(merged);
        }

        // Write to disk
        await writer.WriteAsync(result);

        return result;
    }
}

/// <summary>
/// Fluent builder for configuring a single executable definition.
/// </summary>
public class ExecutableBuilder
{
    private readonly ExecutableDefinition _definition;
    private readonly string? _solutionDirectory;

    internal ExecutableBuilder(string name, string? solutionPath)
    {
        _definition = new ExecutableDefinition { Name = name };
        _solutionDirectory = solutionPath is not null ? Path.GetDirectoryName(solutionPath) : null;
    }

    /// <summary>
    /// The primary .csproj for this executable. Path can be relative to
    /// the solution directory (when using ForSolution) or absolute.
    /// </summary>
    public ExecutableBuilder FromProject(string projectPath)
    {
        _definition.ProjectPath = ResolvePath(projectPath);
        return this;
    }

    /// <summary>Version string written to the SBOM metadata.</summary>
    public ExecutableBuilder WithVersion(string version)
    {
        _definition.Version = version;
        return this;
    }

    /// <summary>
    /// Add an additional project whose dependencies are merged into this
    /// executable's SBOM — useful for plugins, shared libraries loaded
    /// at runtime, etc.
    /// </summary>
    public ExecutableBuilder IncludesProject(string projectPath)
    {
        _definition.IncludedProjectPaths.Add(ResolvePath(projectPath));
        return this;
    }

    /// <summary>
    /// Override global CycloneDX component metadata for this executable only.
    /// </summary>
    public ExecutableBuilder WithMetadata(Action<Component> configure)
    {
        configure(_definition.Metadata);
        return this;
    }

    internal ExecutableDefinition Build() => _definition;

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        if (_solutionDirectory is not null)
            return Path.GetFullPath(Path.Combine(_solutionDirectory, path));

        return Path.GetFullPath(path);
    }
}
