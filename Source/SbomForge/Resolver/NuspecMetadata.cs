namespace SbomForge.Resolver;

/// <summary>
/// Metadata extracted from a NuGet package's .nuspec file.
/// </summary>
internal record NuspecMetadata
{
    public string? LicenseExpression { get; set; }
    public string? ProjectUrl { get; set; }
    public string? Description { get; set; }
}
