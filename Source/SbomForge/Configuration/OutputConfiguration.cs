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
}
