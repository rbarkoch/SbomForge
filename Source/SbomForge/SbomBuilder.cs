using CycloneDX.Models;
using SbomForge.Composer;
using SbomForge.Configuration;
using SbomForge.Resolver;
using SbomForge.Utilities;

namespace SbomForge;

/// <summary>
/// Main entry point for generating CycloneDX 1.7 SBOMs from .NET projects.
/// Provides a fluent API for configuring and building Software Bill of Materials documents.
/// </summary>
public class SbomBuilder : BuilderBase<SbomBuilder>
{
    private string? _basePath;
    private SbomConfiguration _component = new();

    private List<ProjectConfiguration> _projects = [];
    private List<SbomConfiguration> _additionalComponents = [];
    private List<ExternalComponentConfiguration> _externalComponents = [];
    
    /// <summary>
    /// Sets the base path for resolving relative project paths.
    /// All relative project paths specified in <see cref="ForProject"/> will be resolved relative to this path.
    /// </summary>
    /// <param name="basePath">The base directory path.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SbomBuilder WithBasePath(string basePath)
    {
        _basePath = basePath;
        return this;
    }

    /// <summary>
    /// Automatically sets the base path by searching for a solution file in the current or parent directories.
    /// Supports both .sln and .slnx file extensions.
    /// </summary>
    /// <param name="solutionName">The solution file name (with or without extension).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the solution file cannot be found in any parent directory.</exception>
    public SbomBuilder WithBasePathFromSolution(string solutionName)
    {
        // Get the current working directory
        string? currentDirectory = Directory.GetCurrentDirectory();
        
        // Search up the directory tree
        while (currentDirectory is not null)
        {
            // Try the exact name
            string solutionPath = Path.Combine(currentDirectory, solutionName);
            if (File.Exists(solutionPath))
            {
                _basePath = currentDirectory;
                return this;
            }
            
            // If no extension, try .sln and .slnx
            if (!Path.HasExtension(solutionName))
            {
                string slnPath = Path.Combine(currentDirectory, solutionName + ".sln");
                if (File.Exists(slnPath))
                {
                    _basePath = currentDirectory;
                    return this;
                }
                
                string slnxPath = Path.Combine(currentDirectory, solutionName + ".slnx");
                if (File.Exists(slnxPath))
                {
                    _basePath = currentDirectory;
                    return this;
                }
            }
            
            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }
        
        throw new FileNotFoundException($"Solution file '{solutionName}' not found in any parent directory.");
    }

    /// <summary>
    /// Adds a project to the SBOM generation pipeline.
    /// Each project will generate its own SBOM file with its dependencies.
    /// </summary>
    /// <param name="projectPath">Path to the .csproj file (absolute or relative to the base path).</param>
    /// <param name="component">Optional configuration action for per-project settings using <see cref="ComponentBuilder"/>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SbomBuilder ForProject(string projectPath, Action<ComponentBuilder>? component = null)
    {
        ComponentBuilder builder = new();
        component?.Invoke(builder);

        ProjectConfiguration project = new()
        {
            ProjectPath = projectPath,
            Sbom = builder.Configuration
        };

        _projects.Add(project);
        return this;
    }

    /// <summary>
    /// Executes the SBOM generation process for all configured projects.
    /// Performs dependency resolution, applies filters, generates BOMs, and writes output files.
    /// </summary>
    /// <returns>A <see cref="SbomBuildResult"/> containing all generated BOMs and their file paths.</returns>
    public async Task<SbomBuildResult> BuildAsync()
    {
        string basePath = _basePath ?? Directory.GetCurrentDirectory();
        SbomBuildResult result = new();

        // ── Pass 1: Resolve all projects, read metadata, build registry ──

        // Project registry for cross-SBOM consistency.
        // Keyed by absolute project path (primary) and project name (secondary).
        Dictionary<string, ComponentConfiguration> projectRegistry = new(StringComparer.OrdinalIgnoreCase);

        List<(DependencyGraph Graph, SbomConfiguration Config)> resolved = [];

        foreach (ProjectConfiguration project in _projects)
        {
            // Merge global config with per-project overrides.
            SbomConfiguration effectiveConfig = _component.Merge(project.Sbom);

            // Resolve path.
            string projectPath = Path.IsPathRooted(project.ProjectPath)
                ? project.ProjectPath
                : Path.GetFullPath(Path.Combine(basePath, project.ProjectPath));

            // Auto-detect metadata from .csproj + Directory.Build.props.
            ProjectMetadata? metadata = ProjectMetadataReader.Read(projectPath);
            if (metadata is not null)
            {
                ApplyProjectDefaults(effectiveConfig.Component, metadata, projectPath);
            }

            // Resolve dependencies.
            DependencyResolver resolver = new(basePath, project, effectiveConfig.Resolution);
            DependencyGraph graph = await resolver.ResolveAsync();

            resolved.Add((graph, effectiveConfig));

            // Register in project registry for cross-SBOM consistency.
            string absolutePath = Path.GetFullPath(projectPath);
            if (!projectRegistry.ContainsKey(absolutePath))
                projectRegistry.Add(absolutePath, effectiveConfig.Component);
            if (!projectRegistry.ContainsKey(graph.ProjectName))
                projectRegistry.Add(graph.ProjectName, effectiveConfig.Component);
        }

        // ── Pass 2: Compose SBOMs ──

        foreach ((DependencyGraph graph, SbomConfiguration config) in resolved)
        {
            Composer.Composer composer = new(graph, config, basePath, projectRegistry);
            ComposerResult composerResult = await composer.ComposeAsync();

            result.WrittenFilePaths.Add(composerResult.OutputPath);
            result.Boms[graph.ProjectName] = composerResult.Bom;
        }

        return result;
    }

    /// <inheritdoc />
    public override SbomBuilder WithMetadata(Action<ComponentConfiguration>? component = null)
    {
        component?.Invoke(_component.Component);
        return this;
    }

    /// <inheritdoc />
    public override SbomBuilder WithFilters(Action<FiltersConfiguration>? filters = null)
    {
        filters?.Invoke(_component.Filters);
        return this;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override SbomBuilder WithResolution(Action<ResolutionConfiguration>? resolution = null)
    {
        resolution?.Invoke(_component.Resolution);
        return this;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override SbomBuilder WithOutput(Action<OutputConfiguration>? output = null)
    {
        output?.Invoke(_component.Output);
        return this;
    }

    /// <inheritdoc />
    public override SbomBuilder WithExternal(string path, Action<ComponentConfiguration>? component = null)
    {
        ComponentConfiguration? componentMetadata = new();
        component?.Invoke(componentMetadata);

        ExternalComponentConfiguration external = new()
        {
            ExternalPath = path,
            Component = componentMetadata
        };

        _externalComponents.Add(external);

        return this;
    }

    /// <inheritdoc />
    public override SbomBuilder WithComponent(Action<ComponentConfiguration> component)
    {
        throw new NotImplementedException();
    }

    // ──────────────────────── Auto-Detect Defaults ────────────────────────

    /// <summary>
    /// Fills any unset fields on <paramref name="component"/> from auto-detected
    /// <paramref name="metadata"/>. User-provided values always take precedence.
    /// </summary>
    private static void ApplyProjectDefaults(
        ComponentConfiguration component,
        ProjectMetadata metadata,
        string projectPath)
    {
        if (string.IsNullOrEmpty(component.Name))
        {
            component.Name = metadata.AssemblyName
                ?? Path.GetFileNameWithoutExtension(projectPath);
        }

        if (string.IsNullOrEmpty(component.Version))
        {
            component.Version = metadata.Version ?? "1.0.0";
        }

        if (string.IsNullOrEmpty(component.Description))
        {
            component.Description = metadata.Description;
        }

        if (component.Type == Component.Classification.Null)
        {
            component.Type = metadata.IsExecutable
                ? Component.Classification.Application
                : Component.Classification.Library;
        }

        if (string.IsNullOrEmpty(component.Purl))
        {
            string name = component.Name ?? Path.GetFileNameWithoutExtension(projectPath);
            string version = component.Version ?? "1.0.0";
            component.Purl = metadata.IsExecutable
                ? $"pkg:generic/{name}@{version}"
                : $"pkg:nuget/{name}@{version}";
        }

        if (component.Supplier is null && !string.IsNullOrEmpty(metadata.Company))
        {
            component.Supplier = new OrganizationalEntity { Name = metadata.Company };
        }

        if (string.IsNullOrEmpty(component.Publisher))
        {
            component.Publisher = metadata.Authors;
        }

        if (string.IsNullOrEmpty(component.Copyright))
        {
            component.Copyright = metadata.Copyright;
        }

        if ((component.Licenses is null || component.Licenses.Count == 0)
            && !string.IsNullOrEmpty(metadata.PackageLicenseExpression))
        {
            component.Licenses = [new LicenseChoice { Expression = metadata.PackageLicenseExpression }];
        }

        // Set BomRef to match Purl for determinism.
        if (string.IsNullOrEmpty(component.BomRef))
        {
            component.BomRef = component.Purl;
        }
    }
}
