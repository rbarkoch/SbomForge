using CycloneDX.Models;

namespace SbomForge;

/// <summary>
/// The result returned by <see cref="SbomBuilder.BuildAsync"/> containing
/// all generated SBOMs and their file paths.
/// </summary>
public class SbomBuildResult
{
    /// <summary>Written SBOM file paths.</summary>
    public List<string> WrittenFilePaths { get; } = [];

    /// <summary>Generated BOMs keyed by project name.</summary>
    public Dictionary<string, Bom> Boms { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Non-fatal issues detected during SBOM generation.
    /// These indicate potentially incomplete or degraded SBOM output
    /// (e.g. missing nuspec metadata, unresolvable project references,
    /// or structural validation issues in the generated SBOMs).
    /// </summary>
    public List<string> Warnings { get; } = [];
}
