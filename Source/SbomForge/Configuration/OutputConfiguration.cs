using CycloneDX;

namespace SbomForge.Configuration;

/// <summary>
/// Configuration for SBOM output settings.
/// Controls where SBOM files are written and how they are named.
/// </summary>
public class OutputConfiguration
{
    /// <summary>Directory to write SBOM files (default: ./sbom-output).</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// File name template — supports {ProjectName}, {ExecutableName}, and {Version} tokens.
    /// </summary>
    public string? FileNameTemplate { get; set; }

    /// <summary>
    /// The CycloneDX specification version to use when generating the SBOM.
    /// Supported values: <see cref="SpecificationVersion.v1_4"/> through <see cref="SpecificationVersion.v1_7"/>.
    /// Defaults to <see cref="SpecificationVersion.v1_7"/> when not set.
    /// </summary>
    public SpecificationVersion? SpecVersion { get; set; }
}
