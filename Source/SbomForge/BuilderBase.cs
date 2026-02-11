using SbomForge.Configuration;

namespace SbomForge;

public abstract class BuilderBase<T>
{
    public abstract T WithMetadata(Action<ComponentConfiguration>? component = null);

    public abstract T WithFilters(Action<FiltersConfiguration>? filters = null);

    public abstract T WithResolution(Action<ResolutionConfiguration>? resolution = null);

    public abstract T WithOutput(Action<OutputConfiguration>? output = null);

    public abstract T WithExternal(string path, Action<ComponentConfiguration>? component = null);

    public abstract T WithComponent(Action<ComponentConfiguration> component);
}
