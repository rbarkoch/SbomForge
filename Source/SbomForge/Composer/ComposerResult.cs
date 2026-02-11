using CycloneDX.Models;

namespace SbomForge.Composer;

/// <summary>
/// The result of a single SBOM composition pass.
/// </summary>
internal class ComposerResult
{
    public Bom Bom { get; set; }
    public string OutputPath { get; set; }
}
