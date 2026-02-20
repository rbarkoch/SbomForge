using CycloneDX.Models;
using SbomForge.Configuration;

namespace SbomForge.Utilities;

internal static class Extensions
{
    public static FiltersConfiguration Merge(this FiltersConfiguration filters, params FiltersConfiguration[] other)
    {
        FiltersConfiguration merged = new();

        IEnumerable<FiltersConfiguration> filtersConfigurations = [filters, .. other];
        foreach(FiltersConfiguration filtersConfiguration in filtersConfigurations)
        {
            merged.ExcludePackageIds = [.. merged.ExcludePackageIds, .. filtersConfiguration.ExcludePackageIds];
            merged.ExcludePackagePrefixes = [.. merged.ExcludePackagePrefixes, .. filtersConfiguration.ExcludePackagePrefixes];
            merged.ExcludeProjectNames = [.. merged.ExcludeProjectNames, .. filtersConfiguration.ExcludeProjectNames];
            merged.ExcludeTestProjects = filtersConfiguration.ExcludeTestProjects;
        }

        merged.ExcludePackageIds = [.. merged.ExcludePackageIds.Distinct()];
        merged.ExcludePackagePrefixes = [.. merged.ExcludePackagePrefixes.Distinct()];
        merged.ExcludeProjectNames = [.. merged.ExcludeProjectNames.Distinct()];

        return merged;
    }

    public static ResolutionConfiguration Merge(this ResolutionConfiguration resolution, params ResolutionConfiguration[] other)
    {
        ResolutionConfiguration merged = new()
        {
            IncludeTransitive = resolution.IncludeTransitive,
            TargetFramework = resolution.TargetFramework
        };

        foreach(ResolutionConfiguration config in other)
        {
            // Only override if explicitly set (not null)
            if (config.IncludeTransitive.HasValue)
            {
                merged.IncludeTransitive = config.IncludeTransitive;
            }
            if (!string.IsNullOrEmpty(config.TargetFramework))
            {
                merged.TargetFramework = config.TargetFramework;
            }
        }

        return merged;
    }

    public static OutputConfiguration Merge(this OutputConfiguration output, params OutputConfiguration[] other)
    {
        OutputConfiguration merged = new()
        {
            OutputDirectory = output.OutputDirectory,
            FileNameTemplate = output.FileNameTemplate
        };

        foreach(OutputConfiguration config in other)
        {
            if (config.OutputDirectory is not null)
            {
                merged.OutputDirectory = config.OutputDirectory;
            }
            if (config.FileNameTemplate is not null)
            {
                merged.FileNameTemplate = config.FileNameTemplate;
            }
        }

        return merged;
    }

    public static SbomConfiguration Merge(this SbomConfiguration component, params SbomConfiguration[] other)
    {
        SbomConfiguration merged = new();

        // Start with the base component
        List<SbomConfiguration> allConfigs = [component, .. other];
        
        // Collect all filters, resolutions, and outputs to merge
        List<FiltersConfiguration> allFilters = allConfigs.Select(c => c.Filters).ToList();
        List<ResolutionConfiguration> allResolutions = allConfigs.Select(c => c.Resolution).ToList();
        List<OutputConfiguration> allOutputs = allConfigs.Select(c => c.Output).ToList();

        // Merge each sub-configuration
        if (allFilters.Count > 0)
        {
            merged.Filters.ExcludePackageIds = allFilters.First().Merge(allFilters.Skip(1).ToArray()).ExcludePackageIds;
            merged.Filters.ExcludePackagePrefixes = allFilters.First().Merge(allFilters.Skip(1).ToArray()).ExcludePackagePrefixes;
            merged.Filters.ExcludeProjectNames = allFilters.First().Merge(allFilters.Skip(1).ToArray()).ExcludeProjectNames;
            merged.Filters.ExcludeTestProjects = allFilters.First().Merge(allFilters.Skip(1).ToArray()).ExcludeTestProjects;
        }

        if (allResolutions.Count > 0)
        {
            var mergedResolution = allResolutions.First().Merge(allResolutions.Skip(1).ToArray());
            merged.Resolution.IncludeTransitive = mergedResolution.IncludeTransitive;
            merged.Resolution.TargetFramework = mergedResolution.TargetFramework;
        }

        if (allOutputs.Count > 0)
        {
            var mergedOutput = allOutputs.First().Merge(allOutputs.Skip(1).ToArray());
            merged.Output.OutputDirectory = mergedOutput.OutputDirectory;
            merged.Output.FileNameTemplate = mergedOutput.FileNameTemplate;
        }

        // Merge component metadata from all configurations
        foreach (SbomConfiguration config in allConfigs)
        {
            merged.Component.MergeFrom(config.Component);
        }

        // Merge custom components from all configurations (combine all lists)
        foreach (SbomConfiguration config in allConfigs)
        {
            merged.CustomComponents.AddRange(config.CustomComponents);
        }

        // Merge external dependencies from all configurations (combine all lists)
        foreach (SbomConfiguration config in allConfigs)
        {
            merged.ExternalDependencies.AddRange(config.ExternalDependencies);
        }

        return merged;
    }

    public static ProjectConfiguration Merge(this ProjectConfiguration project, params ProjectConfiguration[] other)
    {
        ProjectConfiguration merged = new()
        {
            ProjectPath = project.ProjectPath
        };

        // Collect all component configurations
        List<SbomConfiguration> allComponents = [project.Sbom, .. other.Select(p => p.Sbom)];
        merged.Sbom = allComponents.First().Merge(allComponents.Skip(1).ToArray());

        // Use the last non-empty project path
        foreach(ProjectConfiguration config in other)
        {
            if (!string.IsNullOrEmpty(config.ProjectPath))
            {
                merged.ProjectPath = config.ProjectPath;
            }
        }

        return merged;
    }

    public static ExternalComponentConfiguration Merge(this ExternalComponentConfiguration external, params ExternalComponentConfiguration[] other)
    {
        ExternalComponentConfiguration merged = new()
        {
            ExternalPath = external.ExternalPath
        };

        // Use the last non-empty external path
        foreach(ExternalComponentConfiguration config in other)
        {
            if (!string.IsNullOrEmpty(config.ExternalPath))
            {
                merged.ExternalPath = config.ExternalPath;
            }
        }

        // Merge component metadata
        List<ExternalComponentConfiguration> allConfigs = [external, .. other];
        foreach (ExternalComponentConfiguration config in allConfigs)
        {
            merged.Component.MergeFrom(config.Component);
        }

        return merged;
    }
}
