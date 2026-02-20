using CycloneDX;
using CycloneDX.Json;
using CycloneDX.Models;
using SbomForge.Configuration;
using SbomForge.Resolver;
using SbomForge.Utilities;

namespace SbomForge.Composer;

/// <summary>
/// Composes a CycloneDX 1.7 SBOM from a resolved <see cref="DependencyGraph"/>
/// and an effective <see cref="SbomConfiguration"/>.
/// Uses a project registry for cross-SBOM consistency of project references.
/// </summary>
internal class Composer
{
    private readonly DependencyGraph _graph;
    private readonly SbomConfiguration _config;
    private readonly string _basePath;
    private readonly IReadOnlyDictionary<string, ComponentConfiguration> _projectRegistry;
    private readonly ComponentConfiguration _tool;

    public Composer(
        DependencyGraph graph,
        SbomConfiguration config,
        string basePath,
        IReadOnlyDictionary<string, ComponentConfiguration> projectRegistry,
        ComponentConfiguration tool)
    {
        _graph = graph;
        _config = config;
        _basePath = basePath;
        _projectRegistry = projectRegistry;
        _tool = tool;
    }

    /// <summary>
    /// Orchestrates filter → build → serialize → write.
    /// </summary>
    public async Task<ComposerResult> ComposeAsync()
    {
        DependencyGraph filtered = ApplyFilters(_graph, _config.Filters);
        Bom bom = BuildBom(filtered, _config);
        string outputPath = await SerializeAndWriteAsync(bom, _config.Output, filtered.ProjectName, _basePath);

        return new ComposerResult { Bom = bom, OutputPath = outputPath };
    }

    // ──────────────────────────────── Filtering ──────────────────────────────────

    /// <summary>
    /// Returns a new <see cref="DependencyGraph"/> with excluded packages and
    /// project references removed according to <paramref name="filters"/>.
    /// </summary>
    private static DependencyGraph ApplyFilters(DependencyGraph graph, FiltersConfiguration filters)
    {
        HashSet<string> excludeIds = new(
            filters.ExcludePackageIds ?? [], StringComparer.OrdinalIgnoreCase);

        List<string> excludePrefixes = filters.ExcludePackagePrefixes ?? [];

        HashSet<string> excludeProjectNames = new(
            filters.ExcludeProjectNames ?? [], StringComparer.OrdinalIgnoreCase);

        bool ShouldExcludePackage(ResolvedPackage p) =>
            excludeIds.Contains(p.Id)
            || excludePrefixes.Exists(prefix => p.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        bool ShouldExcludeProject(ResolvedProjectReference p) =>
            excludeProjectNames.Contains(p.Name);

        // Collect excluded package keys so we can strip them from DependsOn lists.
        HashSet<string> excludedKeys = new(StringComparer.OrdinalIgnoreCase);

        List<ResolvedPackage> packages = [];
        foreach (ResolvedPackage pkg in graph.Packages)
        {
            if (ShouldExcludePackage(pkg))
            {
                excludedKeys.Add($"{pkg.Id}/{pkg.Version}");
            }
            else
            {
                packages.Add(pkg);
            }
        }

        List<ResolvedProjectReference> projects = [];
        foreach (ResolvedProjectReference proj in graph.ProjectReferences)
        {
            if (!ShouldExcludeProject(proj))
            {
                projects.Add(proj);
            }
        }

        // Strip excluded keys from DependsOn lists.
        if (excludedKeys.Count > 0)
        {
            foreach (ResolvedPackage pkg in packages)
                pkg.DependsOn.RemoveAll(d => excludedKeys.Contains(d));

            foreach (ResolvedProjectReference proj in projects)
                proj.DependsOn.RemoveAll(d => excludedKeys.Contains(d));
        }

        return new DependencyGraph
        {
            ProjectName = graph.ProjectName,
            SourceProjectPath = graph.SourceProjectPath,
            Packages = packages,
            ProjectReferences = projects
        };
    }

    // ───────────────────────────────── BOM Building ─────────────────────────────────

    /// <summary>
    /// Builds the complete CycloneDX BOM from the filtered dependency graph.
    /// </summary>
    private Bom BuildBom(DependencyGraph graph, SbomConfiguration config)
    {
        ComponentConfiguration subject = config.Component;

        // Ensure BomRef is set on the subject for the dependency tree root.
        if (string.IsNullOrEmpty(subject.BomRef))
        {
            subject.BomRef = subject.Purl
                ?? $"pkg:generic/{subject.Name ?? graph.ProjectName}@{subject.Version ?? "0.0.0"}";
        }

        Bom bom = new()
        {
            Version = 1,
            SerialNumber = $"urn:uuid:{Guid.NewGuid()}",
            Metadata = BuildMetadata(subject),
            Components = [],
            Dependencies = [],
            SpecVersion = SpecificationVersion.v1_7
        };

        // Root dependency node.
        Dependency rootDep = new()
        {
            Ref = subject.BomRef,
            Dependencies = []
        };

        // ── Package components ──

        Dictionary<string, string> bomRefLookup = new(StringComparer.OrdinalIgnoreCase);

        foreach (ResolvedPackage pkg in graph.Packages)
        {
            Component component = BuildPackageComponent(pkg);
            bom.Components.Add(component);

            string key = $"{pkg.Id}/{pkg.Version}";
            bomRefLookup[key] = component.BomRef;

            if (pkg.IsDirect)
            {
                rootDep.Dependencies.Add(new Dependency { Ref = component.BomRef });
            }
        }

        // ── Project reference components ──

        foreach (ResolvedProjectReference projRef in graph.ProjectReferences)
        {
            Component component = BuildProjectReferenceComponent(projRef);
            bom.Components.Add(component);

            string key = $"{projRef.Name}/{projRef.Version ?? "0.0.0"}";
            bomRefLookup[key] = component.BomRef;

            // Project references are always direct dependencies.
            rootDep.Dependencies.Add(new Dependency { Ref = component.BomRef });
        }

        // ── Custom components ──

        foreach (ComponentConfiguration customComp in config.CustomComponents)
        {
            Component component = BuildCustomComponent(customComp);
            bom.Components.Add(component);

            // Add as direct dependency unless scope is Excluded
            if (customComp.Scope != Component.ComponentScope.Excluded)
            {
                rootDep.Dependencies.Add(new Dependency { Ref = component.BomRef });
            }
        }

        bom.Dependencies.Add(rootDep);

        // ── Build nested dependency nodes ──

        foreach (ResolvedPackage pkg in graph.Packages)
        {
            string key = $"{pkg.Id}/{pkg.Version}";
            if (!bomRefLookup.TryGetValue(key, out string? pkgRef))
                continue;

            Dependency dep = new()
            {
                Ref = pkgRef,
                Dependencies = []
            };

            foreach (string depKey in pkg.DependsOn)
            {
                if (bomRefLookup.TryGetValue(depKey, out string? depRef))
                {
                    dep.Dependencies.Add(new Dependency { Ref = depRef });
                }
            }

            bom.Dependencies.Add(dep);
        }

        foreach (ResolvedProjectReference projRef in graph.ProjectReferences)
        {
            string key = $"{projRef.Name}/{projRef.Version ?? "0.0.0"}";
            if (!bomRefLookup.TryGetValue(key, out string? projRefBomRef))
                continue;

            Dependency dep = new()
            {
                Ref = projRefBomRef,
                Dependencies = []
            };

            foreach (string depKey in projRef.DependsOn)
            {
                if (bomRefLookup.TryGetValue(depKey, out string? depRef))
                {
                    dep.Dependencies.Add(new Dependency { Ref = depRef });
                }
            }

            bom.Dependencies.Add(dep);
        }

        return bom;
    }

    // ───────────────────────────────── Metadata ─────────────────────────────────

    private CycloneDX.Models.Metadata BuildMetadata(ComponentConfiguration subject)
    {
        if (subject.Type == Component.Classification.Null)
            subject.Type = Component.Classification.Application;

        return new CycloneDX.Models.Metadata
        {
            Timestamp = DateTime.UtcNow,
            Tools = BuildToolChoices(),
            Component = subject
        };
    }

    /// <summary>
    /// Builds the tool choices for SBOM metadata, applying defaults for any unset fields.
    /// </summary>
    private ToolChoices BuildToolChoices()
    {
        string name = !string.IsNullOrEmpty(_tool.Name) ? _tool.Name : "SbomForge";
        string group = !string.IsNullOrEmpty(_tool.Group) ? _tool.Group : "SbomForge";
        string version = !string.IsNullOrEmpty(_tool.Version) ? _tool.Version : VersionHelper.GetSbomForgeVersion();

        Component toolComponent = new()
        {
            Type = _tool.Type != Component.Classification.Null
                ? _tool.Type
                : Component.Classification.Application,
            Group = group,
            Name = name,
            Version = version,
            Publisher = _tool.Publisher,
            Description = _tool.Description,
            ExternalReferences = _tool.ExternalReferences?.Count > 0
                ? _tool.ExternalReferences
                :
                [
                    new ExternalReference
                    {
                        Type = ExternalReference.ExternalReferenceType.Website,
                        Url = "https://github.com/rbarkoch/SbomForge"
                    }
                ]
        };

        return new ToolChoices
        {
            Components = [toolComponent]
        };
    }

    // ───────────────────────────── Package Components ────────────────────────────

    private static Component BuildPackageComponent(ResolvedPackage pkg)
    {
        string purl = $"pkg:nuget/{pkg.Id}@{pkg.Version}";

        Component component = new()
        {
            Type = Component.Classification.Library,
            BomRef = purl,
            Name = pkg.Id,
            Version = pkg.Version,
            Purl = purl,
            Scope = pkg.IsDirect
                ? Component.ComponentScope.Required
                : Component.ComponentScope.Optional
        };

        // Hashes.
        if (!string.IsNullOrEmpty(pkg.PackageHash))
        {
            component.Hashes =
            [
                new Hash
                {
                    Alg = Hash.HashAlgorithm.SHA_512,
                    Content = pkg.PackageHash
                }
            ];
        }

        // Nuspec.
        if(pkg.Nuspec is not null)
        {
            if(!string.IsNullOrWhiteSpace(pkg.Nuspec.Metadata.Description))
            {
                component.Description = pkg.Nuspec.Metadata.Description;
            }

            if(!string.IsNullOrWhiteSpace(pkg.Nuspec.Metadata.Copyright))
            {
                component.Copyright = pkg.Nuspec.Metadata.Copyright;
            }

            if(!string.IsNullOrWhiteSpace(pkg.Nuspec.Metadata.Authors))
            {
                component.Authors = [.. pkg.Nuspec.Metadata.Authors.Split(',').Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => new OrganizationalContact(){ Name = a.Trim()})];
            }

            if(!string.IsNullOrWhiteSpace(pkg.Nuspec.Metadata.License.Type))
            {
                component.Licenses = [new LicenseChoice() {
                    Expression = pkg.Nuspec.Metadata.License.Text
                }];
            }

            if (!string.IsNullOrWhiteSpace(pkg.Nuspec.Metadata.ProjectUrl))
            {
                component.ExternalReferences =
                [
                    new ExternalReference
                    {
                        Type = ExternalReference.ExternalReferenceType.Website,
                        Url = pkg.Nuspec.Metadata.ProjectUrl
                    }
                ];
            }
        }

        return component;
    }

    // ──────────────────────── Project Reference Components ───────────────────────

    /// <summary>
    /// Builds a <see cref="Component"/> for a project-to-project reference.
    /// Uses the project registry for cross-SBOM consistency when available,
    /// otherwise falls back to auto-detected metadata.
    /// </summary>
    private Component BuildProjectReferenceComponent(ResolvedProjectReference projRef)
    {
        // Try registry lookup by absolute path first, then by name.
        ComponentConfiguration? registryEntry = null;
        if (projRef.ResolvedPath is not null)
        {
            string absolutePath = Path.IsPathRooted(projRef.ResolvedPath)
                ? projRef.ResolvedPath
                : Path.GetFullPath(Path.Combine(_basePath, projRef.ResolvedPath));

            _projectRegistry.TryGetValue(absolutePath, out registryEntry);
        }

        if (registryEntry is null)
            _projectRegistry.TryGetValue(projRef.Name, out registryEntry);

        if (registryEntry is not null)
        {
            if (registryEntry.Type == Component.Classification.Null)
                registryEntry.Type = Component.Classification.Library;

            string purl = registryEntry.Purl
                ?? $"pkg:generic/{registryEntry.Name ?? projRef.Name}@{registryEntry.Version ?? projRef.Version ?? "0.0.0"}";

            registryEntry.Purl = purl;
            registryEntry.BomRef ??= purl;
            registryEntry.Name ??= projRef.Name;
            registryEntry.Version ??= projRef.Version;

            return registryEntry;
        }

        // Fallback: auto-detect from resolved path if available.
        string fallbackVersion = projRef.Version ?? "0.0.0";
        string fallbackName = projRef.Name;
        Component.Classification fallbackType = Component.Classification.Library;
        string? fallbackPurl = null;

        if (projRef.ResolvedPath is not null && File.Exists(projRef.ResolvedPath))
        {
            ProjectMetadata? meta = ProjectMetadataReader.Read(projRef.ResolvedPath);
            if (meta is not null)
            {
                fallbackName = meta.AssemblyName ?? projRef.Name;
                fallbackVersion = meta.Version ?? fallbackVersion;
                fallbackType = meta.IsExecutable
                    ? Component.Classification.Application
                    : Component.Classification.Library;
            }
        }

        fallbackPurl ??= fallbackType == Component.Classification.Application
            ? $"pkg:generic/{fallbackName}@{fallbackVersion}"
            : $"pkg:nuget/{fallbackName}@{fallbackVersion}";

        return new Component
        {
            Type = fallbackType,
            BomRef = fallbackPurl,
            Name = fallbackName,
            Version = fallbackVersion,
            Purl = fallbackPurl,
            Scope = Component.ComponentScope.Required
        };
    }

    // ──────────────────────── Custom Components ──────────────────────────

    /// <summary>
    /// Builds a <see cref="Component"/> from a manually-specified <see cref="ComponentConfiguration"/>.
    /// Used for custom dependencies that cannot be auto-detected (e.g., Docker images, non-.NET packages).
    /// </summary>
    private static Component BuildCustomComponent(ComponentConfiguration config)
    {
        if (config.Type == Component.Classification.Null)
            config.Type = Component.Classification.Library;

        string bomRef = config.BomRef
            ?? config.Purl
            ?? $"pkg:generic/{config.Name ?? "unknown"}@{config.Version ?? "0.0.0"}";

        config.BomRef = bomRef;
        config.Purl ??= bomRef;

        return config;
    }

    // ──────────────────────── Serialize & Write ──────────────────────────

    /// <summary>
    /// Serializes the BOM to CycloneDX JSON and writes it to disk.
    /// </summary>
    private static Task<string> SerializeAndWriteAsync(
        Bom bom,
        OutputConfiguration output,
        string projectName,
        string basePath)
    {
        string outputDir = output.OutputDirectory ?? "./sbom-output";
        string template = output.FileNameTemplate ?? "{ProjectName}-{Version}.sbom.json";

        string fileName = template
            .Replace("{ProjectName}", projectName)
            .Replace("{Version}", bom.Metadata?.Component?.Version ?? "0.0.0");

        // Resolve output directory relative to basePath if not rooted
        string fullDir = Path.IsPathRooted(outputDir)
            ? outputDir
            : Path.GetFullPath(Path.Combine(basePath, outputDir));
        Directory.CreateDirectory(fullDir);

        string fullPath = Path.Combine(fullDir, fileName);

        string json = Serializer.Serialize(bom);

        File.WriteAllText(fullPath, json);

        return Task.FromResult(fullPath);
    }

    /// <summary>
    /// Deserializes a CycloneDX SBOM from a JSON file.
    /// </summary>
    /// <param name="filePath">Absolute path to the SBOM file.</param>
    /// <returns>Deserialized BOM object.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the file is not valid CycloneDX JSON.</exception>
    internal static Bom DeserializeBom(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"External SBOM file not found: {filePath}");
        }

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read external SBOM file: {filePath}", ex);
        }

        Bom? bom;
        try
        {
            bom = Serializer.Deserialize(json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize external SBOM file as CycloneDX JSON: {filePath}", ex);
        }

        if (bom == null)
        {
            throw new InvalidOperationException($"External SBOM file deserialized to null: {filePath}");
        }

        return bom;
    }
}
