using SbomForge.Configuration;

namespace SbomForge;

/// <summary>
/// Base class for fluent configuration builders in SbomForge.
/// Provides the core configuration API contract for <see cref="SbomBuilder"/> and <see cref="ComponentBuilder"/>.
/// </summary>
/// <typeparam name="T">The concrete builder type for fluent method chaining.</typeparam>
public abstract class BuilderBase<T>
{
    /// <summary>
    /// Configures component metadata (name, version, description, author, etc.).
    /// </summary>
    /// <param name="component">Configuration action for component metadata.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public abstract T WithMetadata(Action<ComponentConfiguration>? component = null);

    /// <summary>
    /// Configures filtering rules to exclude specific packages or projects from the SBOM.
    /// </summary>
    /// <param name="filters">Configuration action for filtering rules.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public abstract T WithFilters(Action<FiltersConfiguration>? filters = null);

    /// <summary>
    /// Configures how dependencies are resolved (transitive dependencies, target framework, etc.).
    /// </summary>
    /// <param name="resolution">Configuration action for dependency resolution settings.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public abstract T WithResolution(Action<ResolutionConfiguration>? resolution = null);

    /// <summary>
    /// Configures SBOM output settings (directory, file naming, etc.).
    /// </summary>
    /// <param name="output">Configuration action for output settings.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public abstract T WithOutput(Action<OutputConfiguration>? output = null);

    /// <summary>
    /// Includes an external SBOM file in the generated output.
    /// </summary>
    /// <param name="path">Path to the external SBOM file.</param>
    /// <param name="component">Optional configuration to override component metadata from the external SBOM.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public abstract T WithExternal(string path, Action<ComponentConfiguration>? component = null);

    /// <summary>
    /// Configures additional component settings.
    /// </summary>
    /// <param name="component">Configuration action for component settings.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public abstract T WithComponent(Action<ComponentConfiguration> component);
}
