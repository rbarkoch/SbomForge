using CycloneDX.Models;

namespace SbomForge;

/// <summary>
/// Composes CycloneDX BOMs from resolved dependency graphs.
/// Uses the official CycloneDX.Models types and handles filtering,
/// purl construction, and the dependencies section.
/// </summary>
internal sealed class SbomComposer(ComponentFilter filter, Component globalMetadata)
{
    public Bom Compose(
        ExecutableDefinition executable,
        DependencyGraph graph,
        Component metadata)
    {
        // Start from the user-supplied component and apply executable defaults
        var rootComponent = metadata.Clone();
        rootComponent.Type = rootComponent.Type != default
            ? rootComponent.Type
            : Component.Classification.Application;
        rootComponent.Name ??= executable.Name;
        rootComponent.Version ??= executable.Version ?? "0.0.0";

        var bom = new Bom
        {
            SerialNumber = $"urn:uuid:{Guid.NewGuid()}",
            Version = 1,
            Metadata = new Metadata
            {
                Timestamp = DateTime.UtcNow,
                Component = rootComponent,
            },
            Components = [],
            Dependencies = [],
        };

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

        BuildDependencies(bom, graph);

        return bom;
    }

    /// <summary>
    /// Merges multiple per-executable BOMs into a single solution-level BOM.
    /// Deduplicates shared components and combines dependency edges.
    /// </summary>
    public Bom Merge(IEnumerable<Bom> boms)
    {
        var solutionComponent = globalMetadata.Clone();
        solutionComponent.Type = solutionComponent.Type != default
            ? solutionComponent.Type
            : Component.Classification.Application;
        solutionComponent.Name ??= "Solution";

        var merged = new Bom
        {
            SerialNumber = $"urn:uuid:{Guid.NewGuid()}",
            Version = 1,
            Metadata = new Metadata
            {
                Timestamp = DateTime.UtcNow,
                Component = solutionComponent,
            },
            Components = [],
            Dependencies = [],
        };

        // Deduplicate by PackageUrl (purl) — keep first occurrence
        var seen = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        var allDependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var bom in boms)
        {
            if (bom.Components is not null)
            {
                foreach (var component in bom.Components)
                {
                    var key = component.Purl ?? $"{component.Name}/{component.Version}";
                    seen.TryAdd(key, component);
                }
            }

            if (bom.Dependencies is not null)
            {
                foreach (var dep in bom.Dependencies)
                {
                    if (!allDependencies.TryGetValue(dep.Ref, out var depSet))
                    {
                        depSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        allDependencies[dep.Ref] = depSet;
                    }

                    if (dep.Dependencies is not null)
                    {
                        foreach (var d in dep.Dependencies)
                            depSet.Add(d.Ref);
                    }
                }
            }
        }

        merged.Components = [.. seen.Values];
        merged.Dependencies = [.. allDependencies.Select(kv => new Dependency
        {
            Ref = kv.Key,
            Dependencies = [.. kv.Value.Select(r => new Dependency { Ref = r })],
        })];

        return merged;
    }

    private bool ShouldExclude(ResolvedPackage pkg)
    {
        if (filter.ExcludePackageIds.Contains(pkg.Id, StringComparer.OrdinalIgnoreCase))
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

    private static void BuildDependencies(Bom bom, DependencyGraph graph)
    {
        // Add the root component's dependency on its direct packages
        var rootRef = bom.Metadata?.Component?.BomRef ?? bom.Metadata?.Component?.Purl;
        if (rootRef is not null)
        {
            var directDeps = graph.Packages
                .Where(p => p.IsDirect)
                .Select(p => new Dependency { Ref = BuildPurl(p) })
                .ToList();

            bom.Dependencies.Add(new Dependency
            {
                Ref = rootRef,
                Dependencies = directDeps,
            });
        }

        // Add each package's transitive dependencies
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
    }


}

/// <summary>
/// Result container for the SBOM generation pipeline.
/// </summary>
public class SbomResult
{
    public Dictionary<string, Bom> Boms { get; } = [];
    public Bom? SolutionBom { get; private set; }
    public List<string> WrittenFilePaths { get; } = [];

    internal void AddBom(string executableName, Bom bom)
        => Boms[executableName] = bom;

    internal void SetSolutionBom(Bom bom)
        => SolutionBom = bom;
}

/// <summary>
/// Serializes CycloneDX BOMs to disk using the official CycloneDX serializers.
/// Handles file naming from the template and creates the output directory if needed.
/// </summary>
internal sealed class SbomWriter(OutputOptions options)
{
    public async Task WriteAsync(SbomResult result)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        if (options.Scope != SbomScope.Solution)
        {
            foreach (var (name, bom) in result.Boms)
            {
                var fileName = options.FileNameTemplate
                    .Replace("{ExecutableName}", name)
                    .Replace("{Version}", bom.Metadata?.Component?.Version ?? "0.0.0");

                // Adjust extension based on format
                fileName = AdjustExtension(fileName);

                var path = Path.Combine(options.OutputDirectory, fileName);
                await SerializeAsync(bom, path);
                result.WrittenFilePaths.Add(path);
            }
        }

        if (result.SolutionBom is not null)
        {
            var solutionFileName = AdjustExtension("solution-sbom.json");
            var path = Path.Combine(options.OutputDirectory, solutionFileName);
            await SerializeAsync(result.SolutionBom, path);
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

        await File.WriteAllTextAsync(path, content);
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
