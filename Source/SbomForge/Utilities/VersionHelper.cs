using System.Reflection;

namespace SbomForge.Utilities;

/// <summary>
/// Provides version information for the SbomForge library.
/// </summary>
internal static class VersionHelper
{
    /// <summary>
    /// Gets the version of the SbomForge library from its assembly metadata.
    /// Returns the informational version (includes git commit hash when available),
    /// falling back to the assembly version, then "0.0.0" as a last resort.
    /// </summary>
    /// <returns>The SbomForge library version string.</returns>
    public static string GetSbomForgeVersion()
    {
        Assembly assembly = typeof(SbomBuilder).Assembly;

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrEmpty(informationalVersion))
        {
            // Strip commit metadata (e.g., "+abc1234") from the informational version.
            int plusIndex = informationalVersion!.IndexOf('+');
            return plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
