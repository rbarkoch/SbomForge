namespace SbomForge.Utilities;

/// <summary>
/// Metadata auto-detected from a .csproj file and its Directory.Build.props hierarchy.
/// These serve as defaults that can be overridden by user-provided <see cref="Configuration.ComponentConfiguration"/>.
/// </summary>
internal record ProjectMetadata
{
    /// <summary>True when <c>OutputType</c> is <c>Exe</c> or <c>WinExe</c>.</summary>
    public bool IsExecutable { get; set; }

    public string? Version { get; set; }
    public string? AssemblyName { get; set; }
    public string? Description { get; set; }
    public string? Company { get; set; }
    public string? Authors { get; set; }
    public string? Copyright { get; set; }
    public string? PackageLicenseExpression { get; set; }
    public string? PackageProjectUrl { get; set; }
    public string? RepositoryUrl { get; set; }
}
