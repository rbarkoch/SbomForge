using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CycloneDX.Models;

namespace SbomForge;

/// <summary>
/// Composes CycloneDX BOMs from resolved dependency graphs.
/// Uses the official CycloneDX.Models types and handles filtering,
/// purl construction, project reference cross-linking, and the dependencies section.
/// </summary>
internal sealed class SbomComposer(ComponentFilter filter, List<ProjectDefinition> allProjects)
{
    public Bom Compose(ProjectDefinition project, DependencyGraph graph)
    {
        // Start from the user-supplied component and apply project defaults
        var rootComponent = project.Metadata.Clone();
        
        // Determine if this is an executable
        var isExecutable = IsExecutable(project.OutputType);
        
        // Set default Type based on OutputType
        rootComponent.Type = rootComponent.Type != default
            ? rootComponent.Type
            : (isExecutable ? Component.Classification.Application : Component.Classification.Library);
        
        rootComponent.Name ??= project.Name;
        rootComponent.Version ??= project.ProjectVersion ?? "0.0.0";
        rootComponent.Copyright ??= project.ProjectCopyright;
        rootComponent.Description ??= project.ProjectDescription;
        
        // Set supplier from Company or Authors if not already set
        if (rootComponent.Supplier == null && !string.IsNullOrEmpty(project.ProjectCompany))
        {
            rootComponent.Supplier = new OrganizationalEntity { Name = project.ProjectCompany };
        }
        else if (rootComponent.Supplier == null && !string.IsNullOrEmpty(project.ProjectAuthors))
        {
            rootComponent.Supplier = new OrganizationalEntity { Name = project.ProjectAuthors };
        }
        
        // Set default Purl if not provided
        if (string.IsNullOrEmpty(rootComponent.Purl))
        {
            var purlType = isExecutable ? "generic" : "nuget";
            rootComponent.Purl = $"pkg:{purlType}/{rootComponent.Name}@{rootComponent.Version}";
        }
        
        // Set default BomRef if not provided (use Purl as default)
        rootComponent.BomRef ??= rootComponent.Purl;

        var bom = new Bom
        {
            SerialNumber = $"urn:uuid:{Guid.NewGuid()}",
            Version = 1,
            Metadata = new Metadata
            {
                Timestamp = DateTime.UtcNow,
                Component = rootComponent,
                Tools = new ToolChoices
                {
                    Components = 
                    [
                        new Component
                        {
                            Type = Component.Classification.Application,
                            Name = "SbomForge",
                            Version = GetSbomForgeVersion(),
                        },
                    ],
                },
            },
            Components = [],
            Dependencies = [],
        };

        // Add NuGet package components
        foreach (var pkg in graph.Packages)
        {
            if (ShouldExclude(pkg))
                continue;

            var component = new Component
            {
                Type = Component.Classification.Library,
                Name = pkg.Id,
                Version = pkg.Version,
                Purl = BuildPurl(pkg),
                BomRef = BuildPurl(pkg),
            };

            if (pkg.PackageHash is not null)
            {
                component.Hashes =
                [
                    new Hash
                    {
                        Alg = Hash.HashAlgorithm.SHA_512,
                        Content = pkg.PackageHash,
                    },
                ];
            }

            if (pkg.LicenseExpression is not null)
            {
                component.Licenses =
                [
                    new LicenseChoice { Expression = pkg.LicenseExpression },
                ];
            }

            if (pkg.ProjectUrl is not null)
            {
                component.ExternalReferences =
                [
                    new ExternalReference
                    {
                        Type = ExternalReference.ExternalReferenceType.Website,
                        Url = pkg.ProjectUrl,
                    },
                ];
            }

            var properties = BuildProperties(pkg);
            if (properties.Count > 0)
                component.Properties = properties;

            bom.Components.Add(component);
        }

        // Add project reference components — use the configured metadata
        // (BomRef, Purl, Version, etc.) from the matching ProjectDefinition
        foreach (var projRef in graph.ProjectReferences)
        {
            var configured = FindConfiguredProject(projRef);
            if (configured is null)
                continue;

            var component = configured.Metadata.Clone();
            
            // Determine if this is an executable
            var refIsExecutable = IsExecutable(configured.OutputType);
            
            // Set default Type based on OutputType
            component.Type = component.Type != default
                ? component.Type
                : (refIsExecutable ? Component.Classification.Application : Component.Classification.Library);
            
            component.Name ??= configured.Name;
            component.Version ??= configured.ProjectVersion ?? "0.0.0";
            component.Copyright ??= configured.ProjectCopyright;
            component.Description ??= configured.ProjectDescription;
            
            // Set supplier from Company or Authors if not already set
            if (component.Supplier == null && !string.IsNullOrEmpty(configured.ProjectCompany))
            {
                component.Supplier = new OrganizationalEntity { Name = configured.ProjectCompany };
            }
            else if (component.Supplier == null && !string.IsNullOrEmpty(configured.ProjectAuthors))
            {
                component.Supplier = new OrganizationalEntity { Name = configured.ProjectAuthors };
            }
            
            // Set default Purl if not provided
            if (string.IsNullOrEmpty(component.Purl))
            {
                var purlType = refIsExecutable ? "generic" : "nuget";
                component.Purl = $"pkg:{purlType}/{component.Name}@{component.Version}";
            }
            
            // Set default BomRef if not provided (use Purl as default)
            component.BomRef ??= component.Purl;

            bom.Components.Add(component);
        }

        BuildDependencies(bom, graph);

        return bom;
    }

    private static bool IsExecutable(string? outputType)
    {
        return !string.IsNullOrEmpty(outputType) &&
               (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetSbomForgeVersion()
    {
        try
        {
            var assembly = typeof(SbomComposer).Assembly;
            var version = assembly.GetName().Version;
            return version?.ToString(3) ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private ProjectDefinition? FindConfiguredProject(ResolvedProjectReference projRef)
    {
        // Try to match by resolved path first (most reliable)
        if (projRef.ResolvedPath is not null)
        {
            var match = allProjects.FirstOrDefault(p =>
                string.Equals(
                    Path.GetFullPath(p.ProjectPath),
                    projRef.ResolvedPath,
                    StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match;
        }

        // Fall back to name matching
        return allProjects.FirstOrDefault(p =>
            string.Equals(p.Name, projRef.Name, StringComparison.OrdinalIgnoreCase));
    }

    private string? GetProjectRefBomRef(ResolvedProjectReference projRef)
    {
        var configured = FindConfiguredProject(projRef);
        if (configured is null)
            return null;

        return configured.Metadata.BomRef ?? configured.Metadata.Purl;
    }

    private bool ShouldExclude(ResolvedPackage pkg)
    {
        if (filter.ExcludePackageIds.Any(id => 
            string.Equals(id, pkg.Id, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (filter.ExcludePackagePrefixes.Any(prefix =>
            pkg.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    internal static string BuildPurl(ResolvedPackage pkg)
        => $"pkg:nuget/{pkg.Id}@{pkg.Version}";

    private static List<Property> BuildProperties(ResolvedPackage pkg)
    {
        var props = new List<Property>();

        if (!pkg.IsDirect)
            props.Add(new Property { Name = "cdx:nuget:isDirect", Value = "false" });

        return props;
    }

    private void BuildDependencies(Bom bom, DependencyGraph graph)
    {
        // Add the root component's dependency on its direct packages and project references
        var rootRef = bom.Metadata?.Component?.BomRef ?? bom.Metadata?.Component?.Purl;
        if (rootRef is not null)
        {
            var directDeps = new List<Dependency>();

            // Direct NuGet packages
            directDeps.AddRange(graph.Packages
                .Where(p => p.IsDirect)
                .Select(p => new Dependency { Ref = BuildPurl(p) }));

            // Project references (using their configured BomRef/Purl)
            foreach (var projRef in graph.ProjectReferences)
            {
                var refStr = GetProjectRefBomRef(projRef);
                if (refStr is not null)
                    directDeps.Add(new Dependency { Ref = refStr });
            }

            bom.Dependencies.Add(new Dependency
            {
                Ref = rootRef,
                Dependencies = directDeps,
            });
        }

        // Add each NuGet package's transitive dependencies
        foreach (var pkg in graph.Packages)
        {
            var childDeps = pkg.DependsOn
                .Select(depName =>
                {
                    var resolved = graph.Packages
                        .FirstOrDefault(p => string.Equals(p.Id, depName, StringComparison.OrdinalIgnoreCase));
                    var purl = resolved is not null
                        ? BuildPurl(resolved)
                        : $"pkg:nuget/{depName}";
                    return new Dependency { Ref = purl };
                })
                .ToList();

            bom.Dependencies.Add(new Dependency
            {
                Ref = BuildPurl(pkg),
                Dependencies = childDeps,
            });
        }

        // Add each project reference's dependencies
        foreach (var projRef in graph.ProjectReferences)
        {
            var refStr = GetProjectRefBomRef(projRef);
            if (refStr is null)
                continue;

            var childDeps = projRef.DependsOn
                .Select(depName =>
                {
                    // Check if it's a NuGet package
                    var resolved = graph.Packages
                        .FirstOrDefault(p => string.Equals(p.Id, depName, StringComparison.OrdinalIgnoreCase));
                    if (resolved is not null)
                        return new Dependency { Ref = BuildPurl(resolved) };

                    // Check if it's another project reference
                    var projDep = graph.ProjectReferences
                        .FirstOrDefault(pr => string.Equals(pr.Name, depName, StringComparison.OrdinalIgnoreCase));
                    if (projDep is not null)
                    {
                        var depRef = GetProjectRefBomRef(projDep);
                        if (depRef is not null)
                            return new Dependency { Ref = depRef };
                    }

                    return new Dependency { Ref = $"pkg:nuget/{depName}" };
                })
                .ToList();

            bom.Dependencies.Add(new Dependency
            {
                Ref = refStr,
                Dependencies = childDeps,
            });
        }
    }
}

/// <summary>
/// Result container for the SBOM generation pipeline.
/// Contains one BOM per configured project.
/// </summary>
public class SbomResult
{
    public Dictionary<string, Bom> Boms { get; } = [];
    public List<string> WrittenFilePaths { get; } = [];

    internal void AddBom(string projectName, Bom bom)
        => Boms[projectName] = bom;
}

/// <summary>
/// Serializes CycloneDX BOMs to disk using the official CycloneDX serializers.
/// Writes one file per project using the configured file name template.
/// </summary>
internal sealed class SbomWriter(OutputOptions options)
{
    public async Task WriteAsync(SbomResult result)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        foreach (var (name, bom) in result.Boms)
        {
            var fileName = options.FileNameTemplate
                .Replace("{ProjectName}", name)
                .Replace("{ExecutableName}", name)
                .Replace("{Version}", bom.Metadata?.Component?.Version ?? "0.0.0");

            // Adjust extension based on format
            fileName = AdjustExtension(fileName);

            var path = Path.Combine(options.OutputDirectory, fileName);
            await SerializeAsync(bom, path);
            result.WrittenFilePaths.Add(path);
        }
    }

    private async Task SerializeAsync(Bom bom, string path)
    {
        string content;

        switch (options.Format)
        {
            case SbomFormat.CycloneDxJson:
                content = CycloneDX.Json.Serializer.Serialize(bom);
                break;

            case SbomFormat.CycloneDxXml:
                content = CycloneDX.Xml.Serializer.Serialize(bom);
                break;

            case SbomFormat.SpdxJson:
                throw new NotSupportedException(
                    "SPDX JSON output is not yet supported. Use CycloneDxJson or CycloneDxXml.");

            default:
                throw new ArgumentOutOfRangeException(nameof(options.Format));
        }

#if NET48
        File.WriteAllText(path, content);
        await Task.CompletedTask;
#else
        await File.WriteAllTextAsync(path, content);
#endif
    }

    private string AdjustExtension(string fileName)
    {
        return options.Format switch
        {
            SbomFormat.CycloneDxXml => Path.ChangeExtension(fileName, ".xml"),
            _ => fileName,
        };
    }
}
