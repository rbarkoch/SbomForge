using SbomForge.Configuration;

namespace SbomForge;

public class ComponentBuilder : BuilderBase<ComponentBuilder>
{
    private SbomConfiguration _component = new ();

    /// <summary>
    /// Exposes the built configuration so <see cref="SbomBuilder.ForProject"/> can extract it.
    /// </summary>
    internal SbomConfiguration Configuration => _component;

    public override ComponentBuilder WithComponent(Action<ComponentConfiguration> component)
    {
        throw new NotImplementedException();
    }

    public override ComponentBuilder WithExternal(string path, Action<ComponentConfiguration>? component = null)
    {
        throw new NotImplementedException();
    }

    public override ComponentBuilder WithFilters(Action<FiltersConfiguration>? filters = null)
    {
        filters?.Invoke(_component.Filters);
        return this;
    }

    public override ComponentBuilder WithMetadata(Action<ComponentConfiguration>? component = null)
    {
        component?.Invoke(_component.Component);
        return this;
    }

    public override ComponentBuilder WithOutput(Action<OutputConfiguration>? output = null)
    {
        output?.Invoke(_component.Output);
        return this;
    }

    public override ComponentBuilder WithResolution(Action<ResolutionConfiguration>? resolution = null)
    {
        resolution?.Invoke(_component.Resolution);
        return this;
    }
}
