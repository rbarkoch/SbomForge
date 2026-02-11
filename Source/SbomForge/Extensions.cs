using CycloneDX.Models;

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
}
