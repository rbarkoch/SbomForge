using System.Collections;
using System.Reflection;
using CycloneDX.Models;

namespace SbomForge.Configuration;

/// <summary>
/// Component metadata configuration that extends the CycloneDX <see cref="Component"/> class.
/// Provides properties for component name, version, description, author, license, and other metadata.
/// </summary>
public class ComponentConfiguration : Component
{
    private static readonly PropertyInfo[] _componentProperties =
        typeof(Component).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();

    /// <summary>
    /// Returns a new plain <see cref="Component"/> instance with all property values copied
    /// from this <see cref="ComponentConfiguration"/>.
    /// This is required when passing the component to CycloneDX APIs that perform protobuf
    /// serialization (e.g. for deep-copy or spec-version downgrade), because protobuf does not
    /// know about this subtype and would throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public Component ToComponent()
    {
        Component target = new();
        foreach (PropertyInfo prop in _componentProperties)
        {
            object? value;
            try { value = prop.GetValue(this); }
            catch { continue; }

            try { prop.SetValue(target, value); }
            catch { continue; }
        }
        return target;
    }

    /// <summary>
    /// Copies all non-default property values from <paramref name="source"/> onto this instance.
    /// String properties are skipped when null or empty; enum properties are skipped when zero
    /// (the conventional "not set" sentinel used by CycloneDX); collections are skipped when
    /// null or empty; all other reference types are skipped when null.
    /// New properties added to <see cref="Component"/> are handled automatically.
    /// </summary>
    public void MergeFrom(ComponentConfiguration source)
    {
        foreach (PropertyInfo prop in _componentProperties)
        {
            object? value;
            try
            {
                value = prop.GetValue(source);
            }
            catch
            {
                // Skip computed/wrapper properties that throw when their
                // underlying nullable backing field has no value (e.g. NonNullableScope).
                continue;
            }

            if (value is null)
                continue;

            if (value is string s && string.IsNullOrEmpty(s))
                continue;

            if (prop.PropertyType.IsEnum && Convert.ToInt32(value) == 0)
                continue;

            if (value is IList list && list.Count == 0)
                continue;

            prop.SetValue(this, value);
        }
    }
}
