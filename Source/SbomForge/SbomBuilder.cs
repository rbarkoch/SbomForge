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
    private ComponentConfiguration _tool = new();

    private List<ProjectConfiguration> _projects = [];
    private List<CustomComponentConfiguration> _customComponents = [];
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
    /// Adds a custom component to the SBOM generation pipeline.
    /// This allows creating SBOMs for non-.NET components (Docker containers, npm packages, etc.)
    /// that can depend on .NET projects with cross-SBOM consistency.
    /// </summary>
    /// <param name="component">Configuration action for the custom component using <see cref="CustomComponentBuilder"/>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SbomBuilder ForComponent(Action<CustomComponentBuilder> component)
    {
        CustomComponentBuilder builder = new();
        component(builder);

        _customComponents.Add(builder.Configuration);
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
            
            // Also register by BomRef for cross-component dependencies
            if (!string.IsNullOrEmpty(effectiveConfig.Component.BomRef))
            {
                if (!projectRegistry.ContainsKey(effectiveConfig.Component.BomRef))
                    projectRegistry.Add(effectiveConfig.Component.BomRef, effectiveConfig.Component);
            }
        }

        // ── Pass 1b: Register custom components in registry ──

        foreach (CustomComponentConfiguration customComp in _customComponents)
        {
            // Merge global config with per-component overrides
            SbomConfiguration effectiveConfig = _component.Merge(customComp.Sbom);

            // Ensure BomRef is set
            if (string.IsNullOrEmpty(effectiveConfig.Component.BomRef))
            {
                string name = effectiveConfig.Component.Name ?? "custom-component";
                string version = effectiveConfig.Component.Version ?? "0.0.0";
                effectiveConfig.Component.BomRef = effectiveConfig.Component.Purl
                    ?? $"pkg:generic/{name}@{version}";
            }

            // Register by BomRef (primary key)
            if (!projectRegistry.ContainsKey(effectiveConfig.Component.BomRef))
                projectRegistry.Add(effectiveConfig.Component.BomRef, effectiveConfig.Component);

            // Register by Name (secondary key for convenience)
            if (!string.IsNullOrEmpty(effectiveConfig.Component.Name))
            {
                if (!projectRegistry.ContainsKey(effectiveConfig.Component.Name))
                    projectRegistry.Add(effectiveConfig.Component.Name, effectiveConfig.Component);
            }
        }

        // ── Pass 1c: Resolve project paths to BomRefs for custom components ──

        foreach (CustomComponentConfiguration customComp in _customComponents)
        {
            // Resolve project paths to BomRefs
            foreach (string projectPath in customComp.DependsOnProjectPaths)
            {
                string resolvedPath = Path.IsPathRooted(projectPath)
                    ? projectPath
                    : Path.GetFullPath(Path.Combine(basePath, projectPath));

                if (projectRegistry.TryGetValue(resolvedPath, out ComponentConfiguration? projConfig))
                {
                    // Add the project's BomRef to the explicit BomRef dependencies
                    string bomRef = projConfig.BomRef ?? throw new InvalidOperationException(
                        $"Project '{projectPath}' does not have a BomRef set.");
                    customComp.DependsOnBomRefs.Add(bomRef);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Custom component depends on project '{projectPath}', but it was not found in the registry. " +
                        $"Ensure the project is registered via ForProject() before this component.");
                }
            }
        }

        // ── Pass 1d: Load and register external SBOMs ──

        // Dictionary to store loaded external BOMs for later composition
        // Key: (source identifier, ExternalComponentConfiguration), Value: loaded Bom
        Dictionary<(string Source, ExternalComponentConfiguration Config), Bom> loadedExternalBoms = [];

        // Helper to load and register an external SBOM
        void LoadAndRegisterExternalBom(ExternalComponentConfiguration externalConfig, string source)
        {
            // Resolve path (absolute or relative to basePath)
            string externalPath = Path.IsPathRooted(externalConfig.ExternalPath)
                ? externalConfig.ExternalPath
                : Path.GetFullPath(Path.Combine(basePath, externalConfig.ExternalPath));

            // Load the external SBOM
            Bom externalBom = Composer.Composer.DeserializeBom(externalPath);

            // Get the main component from the external SBOM's metadata
            Component? externalMainComponent = externalBom.Metadata?.Component;
            if (externalMainComponent == null)
            {
                throw new InvalidOperationException(
                    $"External SBOM at '{externalPath}' does not have a metadata.component defined.");
            }

            // Create ComponentConfiguration from external component
            ComponentConfiguration mainComponent = new()
            {
                Name = externalMainComponent.Name,
                Version = externalMainComponent.Version,
                Description = externalMainComponent.Description,
                Type = externalMainComponent.Type,
                BomRef = externalMainComponent.BomRef,
                Purl = externalMainComponent.Purl,
                Group = externalMainComponent.Group,
                Publisher = externalMainComponent.Publisher,
                Copyright = externalMainComponent.Copyright,
                Supplier = externalMainComponent.Supplier,
                Licenses = externalMainComponent.Licenses
            };

            // Apply metadata overrides from configuration
            if (!string.IsNullOrEmpty(externalConfig.Component.Name))
                mainComponent.Name = externalConfig.Component.Name;
            if (!string.IsNullOrEmpty(externalConfig.Component.Version))
                mainComponent.Version = externalConfig.Component.Version;
            if (!string.IsNullOrEmpty(externalConfig.Component.Description))
                mainComponent.Description = externalConfig.Component.Description;
            if (externalConfig.Component.Type != Component.Classification.Null)
                mainComponent.Type = externalConfig.Component.Type;
            if (!string.IsNullOrEmpty(externalConfig.Component.BomRef))
                mainComponent.BomRef = externalConfig.Component.BomRef;
            if (!string.IsNullOrEmpty(externalConfig.Component.Purl))
                mainComponent.Purl = externalConfig.Component.Purl;
            if (!string.IsNullOrEmpty(externalConfig.Component.Group))
                mainComponent.Group = externalConfig.Component.Group;
            if (!string.IsNullOrEmpty(externalConfig.Component.Publisher))
                mainComponent.Publisher = externalConfig.Component.Publisher;
            if (!string.IsNullOrEmpty(externalConfig.Component.Copyright))
                mainComponent.Copyright = externalConfig.Component.Copyright;
            if (externalConfig.Component.Supplier != null)
                mainComponent.Supplier = externalConfig.Component.Supplier;
            if (externalConfig.Component.Licenses != null && externalConfig.Component.Licenses.Count > 0)
                mainComponent.Licenses = externalConfig.Component.Licenses;

            // Handle BomRef collision by prefixing if necessary
            string originalBomRef = mainComponent.BomRef ?? $"pkg:generic/{mainComponent.Name}@{mainComponent.Version}";
            string bomRef = originalBomRef;
            
            if (projectRegistry.ContainsKey(bomRef))
            {
                // Collision detected - prefix with "external:"
                bomRef = $"external:{bomRef}";
                mainComponent.BomRef = bomRef;
                
                // Also update all references in the external BOM's dependency tree
                if (externalBom.Dependencies != null)
                {
                    foreach (var dep in externalBom.Dependencies)
                    {
                        if (dep.Ref == originalBomRef)
                            dep.Ref = bomRef;
                        
                        if (dep.Dependencies != null)
                        {
                            for (int i = 0; i < dep.Dependencies.Count; i++)
                            {
                                if (dep.Dependencies[i].Ref == originalBomRef)
                                    dep.Dependencies[i].Ref = bomRef;
                            }
                        }
                    }
                }
                
                // Update component references in external BOM
                if (externalBom.Components != null)
                {
                    foreach (var comp in externalBom.Components)
                    {
                        if (comp.BomRef == originalBomRef)
                            comp.BomRef = bomRef;
                    }
                }
            }

            // Register the main component in the project registry
            projectRegistry[bomRef] = mainComponent;
            
            if (!string.IsNullOrEmpty(mainComponent.Name))
            {
                if (!projectRegistry.ContainsKey(mainComponent.Name))
                    projectRegistry[mainComponent.Name] = mainComponent;
            }

            // Store the loaded BOM for Pass 2
            loadedExternalBoms[(source, externalConfig)] = externalBom;
        }

        // Process global external components
        foreach (ExternalComponentConfiguration externalConfig in _externalComponents)
        {
            LoadAndRegisterExternalBom(externalConfig, "global");
        }

        // Process per-project external components
        for (int i = 0; i < _projects.Count; i++)
        {
            ProjectConfiguration project = _projects[i];
            SbomConfiguration effectiveConfig = _component.Merge(project.Sbom);
            
            // Resolve project path to absolute to match graph.SourceProjectPath in Pass 2
            string projectPath = Path.IsPathRooted(project.ProjectPath)
                ? project.ProjectPath
                : Path.GetFullPath(Path.Combine(basePath, project.ProjectPath));
            
            foreach (ExternalComponentConfiguration externalConfig in effectiveConfig.ExternalDependencies)
            {
                LoadAndRegisterExternalBom(externalConfig, $"project:{projectPath}");
            }
        }

        // Process custom component external components
        for (int i = 0; i < _customComponents.Count; i++)
        {
            CustomComponentConfiguration customComp = _customComponents[i];
            SbomConfiguration effectiveConfig = _component.Merge(customComp.Sbom);
            
            foreach (ExternalComponentConfiguration externalConfig in customComp.ExternalDependencies)
            {
                LoadAndRegisterExternalBom(externalConfig, $"custom:{effectiveConfig.Component.Name ?? i.ToString()}");
            }
        }

        // ── Pass 2: Compose SBOMs ──

        // Build a lookup from BomRef to DependencyGraph for transitive dependencies
        Dictionary<string, DependencyGraph> dependencyGraphLookup = new(StringComparer.OrdinalIgnoreCase);
        foreach ((DependencyGraph graph, SbomConfiguration config) in resolved)
        {
            if (!string.IsNullOrEmpty(config.Component.BomRef))
            {
                dependencyGraphLookup[config.Component.BomRef] = graph;
            }
            dependencyGraphLookup[graph.ProjectName] = graph;
            if (!string.IsNullOrEmpty(graph.SourceProjectPath))
            {
                dependencyGraphLookup[graph.SourceProjectPath] = graph;
            }
        }

        // Helper to add external SBOM components to a DependencyGraph
        void AddExternalDependenciesToGraph(
            DependencyGraph graph,
            List<ExternalComponentConfiguration> externalConfigs,
            string source,
            bool includeTransitive)
        {
            HashSet<string> addedComponents = new(StringComparer.OrdinalIgnoreCase);
            
            // Track already added packages/projects to avoid duplicates
            foreach (var pkg in graph.Packages)
                addedComponents.Add($"{pkg.Id}/{pkg.Version}");
            foreach (var proj in graph.ProjectReferences)
                addedComponents.Add($"{proj.Name}/{proj.Version}");

            foreach (ExternalComponentConfiguration externalConfig in externalConfigs)
            {
                // Find the loaded BOM
                if (!loadedExternalBoms.TryGetValue((source, externalConfig), out Bom? externalBom))
                    continue;

                Component? mainComponent = externalBom.Metadata?.Component;
                if (mainComponent == null)
                    continue;

                // Add main component as a project reference
                string mainKey = $"{mainComponent.Name}/{mainComponent.Version}";
                if (!addedComponents.Contains(mainKey))
                {
                    graph.ProjectReferences.Add(new ResolvedProjectReference
                    {
                        Name = mainComponent.Name ?? "external-component",
                        Version = mainComponent.Version,
                        ResolvedPath = null,
                        DependsOn = []
                    });
                    addedComponents.Add(mainKey);
                }

                // If IncludeTransitive is true, add all components from the external SBOM
                if (includeTransitive && externalBom.Components != null)
                {
                    foreach (Component comp in externalBom.Components)
                    {
                        string componentKey = $"{comp.Name}/{comp.Version}";
                        if (addedComponents.Contains(componentKey))
                            continue;

                        // Determine if this is a NuGet package or another type of component
                        bool isNuGetPackage = comp.Purl?.StartsWith("pkg:nuget/", StringComparison.OrdinalIgnoreCase) ?? false;

                        if (isNuGetPackage)
                        {
                            // Add as a package
                            graph.Packages.Add(new ResolvedPackage
                            {
                                Id = comp.Name ?? "unknown",
                                Version = comp.Version ?? "0.0.0",
                                IsDirect = false, // External dependencies are transitive
                                LicenseExpression = comp.Licenses?.FirstOrDefault()?.License?.Id,
                                Description = comp.Description,
                                ProjectUrl = comp.ExternalReferences?.FirstOrDefault(r => r.Type == CycloneDX.Models.ExternalReference.ExternalReferenceType.Website)?.Url?.ToString(),
                                PackageHash = null
                            });
                        }
                        else
                        {
                            // Add as a project reference
                            graph.ProjectReferences.Add(new ResolvedProjectReference
                            {
                                Name = comp.Name ?? "unknown",
                                Version = comp.Version,
                                ResolvedPath = null,
                                DependsOn = []
                            });
                        }

                        addedComponents.Add(componentKey);
                    }
                }
            }
        }

        foreach ((DependencyGraph graph, SbomConfiguration config) in resolved)
        {
            // Add global external dependencies to this project's graph
            // Default to true if IncludeTransitive is not explicitly set
            bool includeTransitive = config.Resolution.IncludeTransitive ?? true;
            AddExternalDependenciesToGraph(graph, _externalComponents, "global", includeTransitive);
            
            // Add project-specific external dependencies
            AddExternalDependenciesToGraph(graph, config.ExternalDependencies, $"project:{graph.SourceProjectPath}", includeTransitive);

            Composer.Composer composer = new(graph, config, basePath, projectRegistry, _tool);
            ComposerResult composerResult = await composer.ComposeAsync();

            result.WrittenFilePaths.Add(composerResult.OutputPath);
            result.Boms[graph.ProjectName] = composerResult.Bom;
        }

        // ── Pass 2b: Compose custom component SBOMs ──

        foreach (CustomComponentConfiguration customComp in _customComponents)
        {
            // Merge global config with per-component overrides
            SbomConfiguration effectiveConfig = _component.Merge(customComp.Sbom);

            // Build dependency graph manually from references
            DependencyGraph graph = new()
            {
                ProjectName = effectiveConfig.Component.Name ?? "custom-component",
                SourceProjectPath = "",
                Packages = [],
                ProjectReferences = []
            };

            // Track which packages and projects we've already added to avoid duplicates
            HashSet<string> addedPackages = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> addedProjects = new(StringComparer.OrdinalIgnoreCase);

            // Resolve each BomRef dependency
            foreach (string bomRef in customComp.DependsOnBomRefs)
            {
                if (projectRegistry.TryGetValue(bomRef, out ComponentConfiguration? depConfig))
                {
                    // Add as project reference for dependency tracking (check for duplicates first)
                    string projectKey = $"{depConfig.Name ?? bomRef}/{depConfig.Version ?? "0.0.0"}";
                    if (!addedProjects.Contains(projectKey))
                    {
                        graph.ProjectReferences.Add(new ResolvedProjectReference
                        {
                            Name = depConfig.Name ?? bomRef,
                            Version = depConfig.Version,
                            ResolvedPath = null,
                            DependsOn = []
                        });
                        addedProjects.Add(projectKey);
                    }

                    // Include transitive dependencies if this is a .NET project
                    if (dependencyGraphLookup.TryGetValue(bomRef, out DependencyGraph? projectGraph))
                    {
                        // Add all packages from the referenced project
                        foreach (ResolvedPackage pkg in projectGraph.Packages)
                        {
                            string packageKey = $"{pkg.Id}/{pkg.Version}";
                            if (!addedPackages.Contains(packageKey))
                            {
                                // Add as transitive (not direct) since it's a dependency of our dependency
                                var transitivePackage = new ResolvedPackage
                                {
                                    Id = pkg.Id,
                                    Version = pkg.Version,
                                    IsDirect = false,  // Transitive dependency
                                    LicenseExpression = pkg.LicenseExpression,
                                    Description = pkg.Description,
                                    ProjectUrl = pkg.ProjectUrl,
                                    PackageHash = pkg.PackageHash
                                };
                                
                                // Copy dependencies
                                foreach (var dep in pkg.DependsOn)
                                {
                                    transitivePackage.DependsOn.Add(dep);
                                }
                                
                                graph.Packages.Add(transitivePackage);
                                addedPackages.Add(packageKey);
                            }
                        }

                        // Add nested project references too
                        foreach (ResolvedProjectReference projRef in projectGraph.ProjectReferences)
                        {
                            string nestedProjectKey = $"{projRef.Name}/{projRef.Version ?? "0.0.0"}";
                            if (!addedProjects.Contains(nestedProjectKey))
                            {
                                graph.ProjectReferences.Add(new ResolvedProjectReference
                                {
                                    Name = projRef.Name,
                                    Version = projRef.Version,
                                    ResolvedPath = projRef.ResolvedPath,
                                    DependsOn = projRef.DependsOn
                                });
                                addedProjects.Add(nestedProjectKey);
                            }
                        }
                    }
                }
                else
                {
                    // Provide helpful error message with available options
                    string available = string.Join(", ", projectRegistry.Keys.Take(10));
                    throw new InvalidOperationException(
                        $"Custom component '{effectiveConfig.Component.Name}' depends on BomRef '{bomRef}', " +
                        $"but it was not found in the registry. " +
                        $"Available components: {available}");
                }
            }

            // Add global external dependencies to this custom component's graph
            // Default to true if IncludeTransitive is not explicitly set
            bool includeTransitive = effectiveConfig.Resolution.IncludeTransitive ?? true;
            AddExternalDependenciesToGraph(graph, _externalComponents, "global", includeTransitive);
            
            // Add custom component-specific external dependencies
            AddExternalDependenciesToGraph(graph, customComp.ExternalDependencies, $"custom:{effectiveConfig.Component.Name ?? customComp.GetHashCode().ToString()}", includeTransitive);

            // Compose SBOM
            Composer.Composer composer = new(graph, effectiveConfig, basePath, projectRegistry, _tool);
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

    /// <summary>
    /// Configures the tool metadata included in every generated SBOM.
    /// By default, the tool is identified as SbomForge with its assembly version.
    /// Use this to override the version or other metadata when the assembly version
    /// does not reflect the actual version (e.g., when SbomForge is referenced as a project reference).
    /// </summary>
    /// <param name="tool">Configuration action for tool component metadata.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public SbomBuilder WithTool(Action<ComponentConfiguration>? tool = null)
    {
        tool?.Invoke(_tool);
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
        ComponentConfiguration config = new();
        component(config);
        
        _component.CustomComponents.Add(config);
        
        return this;
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
