using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CycloneDX.Models;

namespace SbomForge;

/// <summary>
/// Entry point for configuring and generating SBOMs.
/// Use <c>SbomBuilder.AddBasePath(...)</c> to start, then chain
/// <c>.AddProject(...)</c> calls to declare each project.
/// </summary>
public class SbomBuilder
{
    private string? _basePath;
    private readonly List<ProjectDefinition> _projects = [];
    private readonly ComponentFilter _filter = new();
    private readonly DependencyResolutionOptions _resolution = new();
    private readonly OutputOptions _output = new();

    private SbomBuilder() { }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Set the base directory against which project paths are resolved.
    /// </summary>
    public static SbomBuilder AddBasePath(string basePath)
    {
        var fullPath = Path.GetFullPath(basePath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Base path not found: {fullPath}");

        return new SbomBuilder { _basePath = fullPath };
    }

    // -------------------------------------------------------------------------
    // Project registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Add a project to the SBOM generation pipeline. The returned
    /// <see cref="ProjectBuilder"/> lets you configure version and metadata
    /// for this project before adding more projects or finishing configuration.
    /// </summary>
    public ProjectBuilder AddProject(string projectPath)
    {
        var fullPath = _basePath is not null
            ? Path.GetFullPath(Path.Combine(_basePath, projectPath))
            : Path.GetFullPath(projectPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Project not found: {fullPath}");

        var projectMetadata = ReadProjectMetadata(fullPath);
        
        var definition = new ProjectDefinition
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            ProjectPath = fullPath,
            OutputType = projectMetadata.OutputType,
            ProjectVersion = projectMetadata.Version,
            ProjectCopyright = projectMetadata.Copyright,
            ProjectCompany = projectMetadata.Company,
            ProjectAuthors = projectMetadata.Authors,
            ProjectDescription = projectMetadata.Description,
        };

        _projects.Add(definition);
        return new ProjectBuilder(this, definition);
    }

    private static ProjectMetadata ReadProjectMetadata(string projectPath)
    {
        var metadata = new ProjectMetadata();
        
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath);
            if (projectDir == null)
                return metadata;
            
            // First, read Directory.Build.props files from the directory tree (parent to child)
            // This establishes the base values that can be overridden
            var buildPropsFiles = new List<string>();
            var currentDir = projectDir;
            
            // Walk up the directory tree to find all Directory.Build.props files
            while (currentDir != null)
            {
                var buildPropsPath = Path.Combine(currentDir, "Directory.Build.props");
                if (File.Exists(buildPropsPath))
                {
                    buildPropsFiles.Insert(0, buildPropsPath); // Insert at beginning for bottom-up processing
                }
                
                var parent = Directory.GetParent(currentDir);
                currentDir = parent?.FullName;
            }
            
            // Apply Directory.Build.props files in order (higher in tree first, so lower ones override)
            foreach (var buildPropsFile in buildPropsFiles)
            {
                var content = File.ReadAllText(buildPropsFile);
                ApplyMetadataFromContent(metadata, content);
            }
            
            // Finally, apply the project file itself (highest precedence)
            var projectContent = File.ReadAllText(projectPath);
            ApplyMetadataFromContent(metadata, projectContent);
        }
        catch
        {
            // If we can't read the file, just return empty metadata
        }
        
        return metadata;
    }
    
    private static void ApplyMetadataFromContent(ProjectMetadata metadata, string content)
    {
        // Later values override earlier ones (project file overrides Directory.Build.props)
        var outputType = ReadXmlElement(content, "OutputType");
        if (outputType != null) metadata.OutputType = outputType;
        
        var version = ReadXmlElement(content, "Version");
        if (version != null) metadata.Version = version;
        
        var copyright = ReadXmlElement(content, "Copyright");
        if (copyright != null) metadata.Copyright = copyright;
        
        var company = ReadXmlElement(content, "Company");
        if (company != null) metadata.Company = company;
        
        var authors = ReadXmlElement(content, "Authors");
        if (authors != null) metadata.Authors = authors;
        
        var description = ReadXmlElement(content, "Description");
        if (description != null) metadata.Description = description;
    }
    
    private static string? ReadXmlElement(string content, string elementName)
    {
        var startTag = $"<{elementName}>";
        var endTag = $"</{elementName}>";
        var startIndex = content.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        
        if (startIndex == -1)
            return null;
        
        startIndex += startTag.Length;
        var endIndex = content.IndexOf(endTag, startIndex, StringComparison.OrdinalIgnoreCase);
        
        if (endIndex == -1)
            return null;
        
        return content[startIndex..endIndex].Trim();
    }
    
    private class ProjectMetadata
    {
        public string? OutputType { get; set; }
        public string? Version { get; set; }
        public string? Copyright { get; set; }
        public string? Company { get; set; }
        public string? Authors { get; set; }
        public string? Description { get; set; }
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
    /// Configure how dependencies are resolved (transitive inclusion, TFM).
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
    /// Configure output format, directory, and file naming.
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
    /// compose BOMs (one per project), and write to disk.
    /// </summary>
    public async Task<SbomResult> BuildAsync()
    {
        if (_projects.Count == 0)
            throw new InvalidOperationException(
                "No projects defined. Call AddProject() first.");

        var resolver = new DependencyResolver(_resolution);
        var composer = new SbomComposer(_filter, _projects);
        var writer = new SbomWriter(_output);

        var result = new SbomResult();

        foreach (var project in _projects)
        {
            var graph = await resolver.ResolveAsync(project);
            var bom = composer.Compose(project, graph);
            result.AddBom(project.Name, bom);
        }

        await writer.WriteAsync(result);

        return result;
    }
}

/// <summary>
/// Fluent builder for configuring a single project within the SBOM pipeline.
/// Returned by <see cref="SbomBuilder.AddProject"/>. Chain
/// <c>.WithMetadata()</c> to configure the project,
/// then continue with <c>.AddProject()</c>, <c>.WithResolution()</c>,
/// <c>.WithOutput()</c>, or <c>.BuildAsync()</c>.
/// </summary>
public class ProjectBuilder
{
    private readonly SbomBuilder _builder;
    private readonly ProjectDefinition _project;

    internal ProjectBuilder(SbomBuilder builder, ProjectDefinition project)
    {
        _builder = builder;
        _project = project;
    }

    /// <summary>
    /// Configure CycloneDX component metadata for this project.
    /// Set Version, BomRef, Purl, Copyright, Type, and any other <see cref="Component"/> fields.
    /// </summary>
    public ProjectBuilder WithMetadata(Action<Component> configure)
    {
        configure(_project.Metadata);
        return this;
    }

    // ----- Delegate back to the parent SbomBuilder -----

    /// <inheritdoc cref="SbomBuilder.AddProject"/>
    public ProjectBuilder AddProject(string projectPath) => _builder.AddProject(projectPath);

    /// <inheritdoc cref="SbomBuilder.WithFilters"/>
    public SbomBuilder WithFilters(Action<ComponentFilter> configure) => _builder.WithFilters(configure);

    /// <inheritdoc cref="SbomBuilder.WithResolution"/>
    public SbomBuilder WithResolution(Action<DependencyResolutionOptions> configure) => _builder.WithResolution(configure);

    /// <inheritdoc cref="SbomBuilder.WithOutput"/>
    public SbomBuilder WithOutput(Action<OutputOptions> configure) => _builder.WithOutput(configure);

    /// <inheritdoc cref="SbomBuilder.BuildAsync"/>
    public Task<SbomResult> BuildAsync() => _builder.BuildAsync();
}
