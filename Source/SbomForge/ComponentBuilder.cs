using SbomForge.Configuration;

namespace SbomForge;

/// <summary>
/// Provides per-project configuration overrides when building SBOMs.
/// Used within <see cref="SbomBuilder.ForProject"/> to customize settings for individual projects.
/// </summary>
public class ComponentBuilder : BuilderBase<ComponentBuilder>
{
    private SbomConfiguration _component = new ();

    /// <summary>
    /// Exposes the built configuration so <see cref="SbomBuilder.ForProject"/> can extract it.
    /// </summary>
    internal SbomConfiguration Configuration => _component;

    /// <inheritdoc />
    /// <inheritdoc />
    public override ComponentBuilder WithComponent(Action<ComponentConfiguration> component)
    {
        ComponentConfiguration config = new();
        component(config);
        
        _component.CustomComponents.Add(config);
        
        return this;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override ComponentBuilder WithExternal(string path, Action<ComponentConfiguration>? component = null)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override ComponentBuilder WithFilters(Action<FiltersConfiguration>? filters = null)
    {
        filters?.Invoke(_component.Filters);
        return this;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override ComponentBuilder WithMetadata(Action<ComponentConfiguration>? component = null)
    {
        component?.Invoke(_component.Component);
        return this;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override ComponentBuilder WithOutput(Action<OutputConfiguration>? output = null)
    {
        output?.Invoke(_component.Output);
        return this;
    }

    /// <inheritdoc />
    public override ComponentBuilder WithResolution(Action<ResolutionConfiguration>? resolution = null)
    {
        resolution?.Invoke(_component.Resolution);
        return this;
    }
}
