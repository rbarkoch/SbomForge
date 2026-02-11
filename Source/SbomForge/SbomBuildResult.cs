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
}
