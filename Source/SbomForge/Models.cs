using CycloneDX.Models;

namespace SbomForge;

/// <summary>
/// Represents a single executable/deployable artifact within the solution.
/// This is the central abstraction — it declares which projects compose a
/// deployable unit so that its SBOM accurately reflects everything it ships with.
/// </summary>
public class ExecutableDefinition
{
    public required string Name { get; set; }
    public string? Version { get; set; }
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Additional projects whose dependencies should be merged into this
    /// executable's SBOM (e.g. shared libraries, plugins, runtime-loaded modules).
    /// </summary>
    public List<string> IncludedProjectPaths { get; set; } = [];

    /// <summary>
    /// Per-executable CycloneDX component metadata overrides. Values set here win
    /// over global defaults; null/empty values fall back to the global component.
    /// This is the raw CycloneDX <see cref="Component"/> — set any field directly.
    /// </summary>
    public Component Metadata { get; set; } = new();
}

/// <summary>
/// Controls how transitive dependencies are resolved and represented.
/// </summary>
public class DependencyResolutionOptions
{
    /// <summary>Include transitive (indirect) dependencies, not just direct refs.</summary>
    public bool IncludeTransitive { get; set; } = true;

    /// <summary>
    /// When true, deduplicates shared packages across executables in a
    /// merged SBOM — one component entry, multiple dependsOn references.
    /// </summary>
    public bool DeduplicateSharedPackages { get; set; } = true;

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
/// Controls how many SBOM files are produced and at what level.
/// </summary>
public enum SbomScope
{
    /// <summary>One SBOM per declared executable definition.</summary>
    PerExecutable,

    /// <summary>A single merged SBOM covering all executables, with shared packages deduplicated.</summary>
    Solution,

    /// <summary>Both — individual SBOMs plus a rolled-up solution SBOM.</summary>
    Both,
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

    /// <summary>Output scope — per-executable, solution, or both.</summary>
    public SbomScope Scope { get; set; } = SbomScope.PerExecutable;

    /// <summary>
    /// File name template — supports {ExecutableName} and {Version} tokens.
    /// </summary>
    public string FileNameTemplate { get; set; } = "{ExecutableName}-sbom.json";
}
