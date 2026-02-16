using SbomForge.Configuration;

namespace SbomForge;

/// <summary>
/// Builder for creating custom component SBOMs (non-.NET projects).
/// Allows creating SBOMs for Docker containers, npm packages, or other components
/// that can depend on .NET projects with cross-SBOM consistency.
/// </summary>
public class CustomComponentBuilder : BuilderBase<CustomComponentBuilder>
{
    private CustomComponentConfiguration _config = new();

    /// <summary>
    /// Exposes the built configuration so <see cref="SbomBuilder.ForComponent"/> can extract it.
    /// </summary>
    internal CustomComponentConfiguration Configuration => _config;

    /// <summary>
    /// Adds a dependency on another component by its BomRef.
    /// The BomRef must match a component registered via ForProject or ForComponent.
    /// </summary>
    /// <param name="bomRef">The BomRef of the component to depend on (e.g., "pkg:nuget/MyApp@1.0.0").</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CustomComponentBuilder DependsOn(string bomRef)
    {
        _config.DependsOnBomRefs.Add(bomRef);
        return this;
    }

    /// <summary>
    /// Adds a dependency on a .NET project by its path.
    /// The project path will be resolved to its BomRef during the build process.
    /// </summary>
    /// <param name="projectPath">Path to the .csproj file (absolute or relative to base path).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CustomComponentBuilder DependsOnProject(string projectPath)
    {
        _config.DependsOnProjectPaths.Add(projectPath);
        return this;
    }

    /// <inheritdoc />
    public override CustomComponentBuilder WithMetadata(Action<ComponentConfiguration>? component = null)
    {
        component?.Invoke(_config.Sbom.Component);
        return this;
    }

    /// <inheritdoc />
    public override CustomComponentBuilder WithFilters(Action<FiltersConfiguration>? filters = null)
    {
        filters?.Invoke(_config.Sbom.Filters);
        return this;
    }

    /// <inheritdoc />
    public override CustomComponentBuilder WithResolution(Action<ResolutionConfiguration>? resolution = null)
    {
        resolution?.Invoke(_config.Sbom.Resolution);
        return this;
    }

    /// <inheritdoc />
    public override CustomComponentBuilder WithOutput(Action<OutputConfiguration>? output = null)
    {
        output?.Invoke(_config.Sbom.Output);
        return this;
    }

    /// <inheritdoc />
    public override CustomComponentBuilder WithExternal(string path, Action<ComponentConfiguration>? component = null)
    {
        ExternalComponentConfiguration externalConfig = new()
        {
            ExternalPath = path
        };
        
        component?.Invoke(externalConfig.Component);

        _config.ExternalDependencies.Add(externalConfig);
        
        return this;
    }

    /// <inheritdoc />
    public override CustomComponentBuilder WithComponent(Action<ComponentConfiguration> component)
    {
        ComponentConfiguration config = new();
        component(config);

        _config.Sbom.CustomComponents.Add(config);

        return this;
    }
}
