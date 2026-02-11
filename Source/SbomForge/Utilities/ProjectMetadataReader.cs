using System.Xml.Linq;

namespace SbomForge.Utilities;

/// <summary>
/// Reads MSBuild properties from a .csproj file and its <c>Directory.Build.props</c>
/// hierarchy to auto-detect component metadata for SBOM generation.
/// Properties from inner (closer) files override those from outer (parent) files.
/// </summary>
internal static class ProjectMetadataReader
{
    private static readonly string[] ExecutableOutputTypes = ["Exe", "WinExe"];

    private static readonly string[] PropertyNames =
    [
        "OutputType", "Version", "AssemblyName", "Description",
        "Company", "Authors", "Copyright",
        "PackageLicenseExpression", "PackageProjectUrl", "RepositoryUrl"
    ];

    /// <summary>
    /// Reads project metadata from the specified project file and its
    /// <c>Directory.Build.props</c> hierarchy (walking parent directories).
    /// Returns <c>null</c> if the project file cannot be read.
    /// </summary>
    public static ProjectMetadata? Read(string projectPath)
    {
        if (!File.Exists(projectPath))
            return null;

        try
        {
            // Read properties from the .csproj file itself (highest priority).
            Dictionary<string, string?> properties = ReadProperties(projectPath);

            // Walk parent directories for Directory.Build.props files.
            // Inner (closer) files have already been read, so we only fill
            // properties that haven't been set yet.
            string? projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            if (projectDir is not null)
            {
                MergeDirectoryBuildProps(projectDir, properties);
            }

            string? outputType = GetValue(properties, "OutputType");
            bool isExecutable = outputType is not null &&
                ExecutableOutputTypes.Contains(outputType, StringComparer.OrdinalIgnoreCase);

            return new ProjectMetadata
            {
                IsExecutable = isExecutable,
                Version = GetValue(properties, "Version"),
                AssemblyName = GetValue(properties, "AssemblyName"),
                Description = GetValue(properties, "Description"),
                Company = GetValue(properties, "Company"),
                Authors = GetValue(properties, "Authors"),
                Copyright = GetValue(properties, "Copyright"),
                PackageLicenseExpression = GetValue(properties, "PackageLicenseExpression"),
                PackageProjectUrl = GetValue(properties, "PackageProjectUrl"),
                RepositoryUrl = GetValue(properties, "RepositoryUrl")
            };
        }
        catch
        {
            return null;
        }
    }

    // ──────────────────────────── Property Reading ─────────────────────────────

    /// <summary>
    /// Reads MSBuild property values from an XML project/props file.
    /// Handles both namespaced and non-namespaced element lookups.
    /// </summary>
    private static Dictionary<string, string?> ReadProperties(string filePath)
    {
        Dictionary<string, string?> properties = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            XDocument doc = XDocument.Load(filePath);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            foreach (string name in PropertyNames)
            {
                // Try namespaced first, then non-namespaced.
                string? value = doc.Descendants(ns + name).FirstOrDefault()?.Value
                             ?? doc.Descendants(name).FirstOrDefault()?.Value;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    properties[name] = value.Trim();
                }
            }
        }
        catch
        {
            // Silently skip files that cannot be parsed.
        }

        return properties;
    }

    // ──────────────────────── Directory.Build.props Walk ───────────────────────

    /// <summary>
    /// Walks parent directories looking for <c>Directory.Build.props</c> files.
    /// Properties from closer files take precedence (already-set keys are not overwritten).
    /// Stops at the filesystem root.
    /// </summary>
    private static void MergeDirectoryBuildProps(string startDirectory, Dictionary<string, string?> properties)
    {
        DirectoryInfo? dir = new DirectoryInfo(startDirectory);

        while (dir is not null)
        {
            string propsPath = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(propsPath))
            {
                Dictionary<string, string?> propsFileProperties = ReadProperties(propsPath);

                // Only fill values that haven't been set by a closer file.
                foreach (var kvp in propsFileProperties)
                {
                    if (!properties.ContainsKey(kvp.Key))
                        properties.Add(kvp.Key, kvp.Value);
                }
            }

            dir = dir.Parent;
        }
    }

    // ──────────────────────────── Helpers ──────────────────────────────────────

    private static string? GetValue(Dictionary<string, string?> properties, string key)
    {
        return properties.TryGetValue(key, out string? value) ? value : null;
    }
}
