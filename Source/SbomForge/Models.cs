using System.Collections.Generic;
using CycloneDX.Models;

namespace SbomForge;

/// <summary>
/// Represents a single project to generate an SBOM for.
/// Each project produces one SBOM file. When a project references another
/// configured project, the SBOM uses the referenced project's configured
/// BomRef, Purl, and version.
/// </summary>
public class ProjectDefinition
{
    public required string Name { get; set; }
    public required string ProjectPath { get; set; }
    public string? OutputType { get; set; }
    
    // Properties read from the project file
    public string? ProjectVersion { get; set; }
    public string? ProjectCopyright { get; set; }
    public string? ProjectCompany { get; set; }
    public string? ProjectAuthors { get; set; }
    public string? ProjectDescription { get; set; }

    /// <summary>
    /// CycloneDX component metadata for this project. Set Version, BomRef, Purl,
    /// Copyright, Type, and any other <see cref="Component"/> field directly.
    /// </summary>
    public Component Metadata { get; set; } = new();

    /// <summary>
    /// Paths to external CycloneDX BOM files whose components and dependencies
    /// will be merged into this project's SBOM.
    /// </summary>
    public List<string> ExternalBomPaths { get; set; } = [];

    /// <summary>
    /// Additional components to include in this project's SBOM.
    /// These are added alongside the auto-resolved NuGet and project reference components.
    /// </summary>
    public List<Component> AdditionalComponents { get; set; } = [];
}

/// <summary>
/// Controls how transitive dependencies are resolved and represented.
/// </summary>
public class DependencyResolutionOptions
{
    /// <summary>Include transitive (indirect) dependencies, not just direct refs.</summary>
    public bool IncludeTransitive { get; set; } = true;

    /// <summary>Target framework to resolve against when a project is multi-targeted.</summary>
    public string? TargetFramework { get; set; }
}

/// <summary>
/// Filter rules for including/excluding components from the SBOM.
/// </summary>
public class ComponentFilter
{
    /// <summary>Exact package IDs to exclude.</summary>
    public List<string> ExcludePackageIds { get; set; } = [];

    /// <summary>Exclude projects by name.</summary>
    public List<string> ExcludeProjectNames { get; set; } = [];

    /// <summary>Packages whose ID starts with any of these prefixes are excluded.</summary>
    public List<string> ExcludePackagePrefixes { get; set; } = [];

    /// <summary>When true, test projects are automatically excluded (default: true).</summary>
    public bool ExcludeTestProjects { get; set; } = true;
}

/// <summary>
/// Supported SBOM output formats.
/// </summary>
public enum SbomFormat
{
    CycloneDxJson,
    CycloneDxXml,
    SpdxJson,
}

/// <summary>
/// Output configuration for SBOM generation.
/// </summary>
public class OutputOptions
{
    /// <summary>Directory to write SBOM files (default: ./sbom-output).</summary>
    public string OutputDirectory { get; set; } = "./sbom-output";

    /// <summary>Output format (default: CycloneDX JSON).</summary>
    public SbomFormat Format { get; set; } = SbomFormat.CycloneDxJson;

    /// <summary>
    /// File name template — supports {ProjectName}, {ExecutableName}, and {Version} tokens.
    /// </summary>
    public string FileNameTemplate { get; set; } = "{ProjectName}-sbom.json";
}
