using CycloneDX.Models;
using Microsoft.Build.Construction;
using Microsoft.Build.Locator;

namespace SbomForge;

/// <summary>
/// CycloneDX <see cref="Component"/> clone and merge helpers.
/// </summary>
internal static class ComponentExtensions
{
    /// <summary>Creates a shallow clone of a CycloneDX component.</summary>
    public static Component Clone(this Component source)
    {
        return new Component
        {
            Type = source.Type,
            MimeType = source.MimeType,
            BomRef = source.BomRef,
            Supplier = source.Supplier,
            Manufacturer = source.Manufacturer,
            Publisher = source.Publisher,
            Group = source.Group,
            Name = source.Name,
            Version = source.Version,
            Description = source.Description,
            Scope = source.Scope,
            Copyright = source.Copyright,
            Cpe = source.Cpe,
            Purl = source.Purl,
            Hashes = source.Hashes is not null ? [.. source.Hashes] : null,
            Licenses = source.Licenses is not null ? [.. source.Licenses] : null,
            ExternalReferences = source.ExternalReferences is not null ? [.. source.ExternalReferences] : null,
            Properties = source.Properties is not null ? [.. source.Properties] : null,
        };
    }

    /// <summary>
    /// Merges two components — non-null values in <paramref name="overrideComponent"/>
    /// win over <paramref name="baseComponent"/>. Null or default values fall back to the base.
    /// </summary>
    public static Component MergeWith(
        this Component baseComponent,
        Component? overrideComponent)
    {
        if (overrideComponent is null)
            return baseComponent.Clone();

        var merged = baseComponent.Clone();

        if (overrideComponent.Type != default)
            merged.Type = overrideComponent.Type;

        if (!string.IsNullOrEmpty(overrideComponent.MimeType))
            merged.MimeType = overrideComponent.MimeType;

        if (!string.IsNullOrEmpty(overrideComponent.BomRef))
            merged.BomRef = overrideComponent.BomRef;

        if (overrideComponent.Supplier is not null)
            merged.Supplier = overrideComponent.Supplier;

        if (overrideComponent.Manufacturer is not null)
            merged.Manufacturer = overrideComponent.Manufacturer;

        if (!string.IsNullOrEmpty(overrideComponent.Publisher))
            merged.Publisher = overrideComponent.Publisher;

        if (!string.IsNullOrEmpty(overrideComponent.Group))
            merged.Group = overrideComponent.Group;

        if (!string.IsNullOrEmpty(overrideComponent.Name))
            merged.Name = overrideComponent.Name;

        if (!string.IsNullOrEmpty(overrideComponent.Version))
            merged.Version = overrideComponent.Version;

        if (!string.IsNullOrEmpty(overrideComponent.Description))
            merged.Description = overrideComponent.Description;

        if (overrideComponent.Scope is not null)
            merged.Scope = overrideComponent.Scope;

        if (!string.IsNullOrEmpty(overrideComponent.Copyright))
            merged.Copyright = overrideComponent.Copyright;

        if (!string.IsNullOrEmpty(overrideComponent.Cpe))
            merged.Cpe = overrideComponent.Cpe;

        if (!string.IsNullOrEmpty(overrideComponent.Purl))
            merged.Purl = overrideComponent.Purl;

        if (overrideComponent.Hashes is { Count: > 0 })
            merged.Hashes = [.. overrideComponent.Hashes];

        if (overrideComponent.Licenses is { Count: > 0 })
            merged.Licenses = [.. overrideComponent.Licenses];

        if (overrideComponent.ExternalReferences is { Count: > 0 })
            merged.ExternalReferences = [.. overrideComponent.ExternalReferences];

        // Properties: merge, with override winning on key conflicts
        if (overrideComponent.Properties is { Count: > 0 })
        {
            var mergedProps = new Dictionary<string, string>();

            if (merged.Properties is not null)
            {
                foreach (var p in merged.Properties)
                    mergedProps[p.Name] = p.Value;
            }

            foreach (var p in overrideComponent.Properties)
                mergedProps[p.Name] = p.Value;

            merged.Properties = [.. mergedProps.Select(kv => new Property { Name = kv.Key, Value = kv.Value })];
        }

        return merged;
    }
}

/// <summary>
/// Parses .sln files to discover executable projects using Microsoft.Build.
/// Used by <see cref="SbomBuilder.DiscoverExecutables"/>.
/// </summary>
internal sealed class SolutionResolver
{
    private readonly string _solutionPath;
    private static bool s_msBuildRegistered;

    public SolutionResolver(string solutionPath)
    {
        _solutionPath = solutionPath;
        EnsureMSBuildRegistered();
    }

    /// <summary>
    /// Finds all projects in the solution that produce an executable
    /// (OutputType = Exe or WinExe).
    /// </summary>
    public List<(string Name, string Path)> FindExecutableProjects()
    {
        var results = new List<(string Name, string Path)>();
        var solutionFile = SolutionFile.Parse(_solutionPath);
        var solutionDir = System.IO.Path.GetDirectoryName(_solutionPath) ?? ".";

        foreach (var project in solutionFile.ProjectsInOrder)
        {
            // Skip solution folders and non-MSBuild project types
            if (project.ProjectType == SolutionProjectType.SolutionFolder)
                continue;

            var projectPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(solutionDir, project.RelativePath));

            if (!File.Exists(projectPath))
                continue;

            // Check if the project produces an executable by inspecting the .csproj
            if (IsExecutableProject(projectPath))
            {
                results.Add((project.ProjectName, projectPath));
            }
        }

        return results;
    }

    /// <summary>
    /// Inspects a .csproj file for OutputType = Exe or WinExe without
    /// performing a full MSBuild evaluation (fast, no side effects).
    /// </summary>
    private static bool IsExecutableProject(string projectPath)
    {
        try
        {
            var root = ProjectRootElement.Open(projectPath);

            // Check for <OutputType>Exe</OutputType> or <OutputType>WinExe</OutputType>
            foreach (var propertyGroup in root.PropertyGroups)
            {
                foreach (var property in propertyGroup.Properties)
                {
                    if (string.Equals(property.Name, "OutputType", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = property.Value;
                        if (string.Equals(value, "Exe", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(value, "WinExe", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch
        {
            // If we can't parse the project, skip it
            return false;
        }
    }

    private static void EnsureMSBuildRegistered()
    {
        if (s_msBuildRegistered)
            return;

        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException)
        {
            // Already registered — ignore
        }

        s_msBuildRegistered = true;
    }
}
