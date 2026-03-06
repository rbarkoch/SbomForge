using System.Text.RegularExpressions;
using CycloneDX.Models;

namespace SbomForge.Validation;

/// <summary>
/// Validates a CycloneDX BOM for structural correctness before serialization.
/// Returns a list of human-readable error descriptions.
/// </summary>
internal static class SbomValidator
{
    private static readonly Regex PurlPattern = new(
        @"^pkg:[a-z][a-z0-9.*+\-]+/.+@.+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Validates the given BOM and returns any structural errors found.
    /// An empty list indicates the BOM is structurally valid.
    /// </summary>
    public static List<string> Validate(Bom bom)
    {
        List<string> errors = [];

        ValidateMetadata(bom, errors);
        ValidateComponents(bom, errors);
        ValidateDependencies(bom, errors);

        return errors;
    }

    private static void ValidateMetadata(Bom bom, List<string> errors)
    {
        if (bom.Metadata is null)
        {
            errors.Add("BOM is missing required 'metadata' section.");
            return;
        }

        if (bom.Metadata.Component is null)
        {
            errors.Add("BOM metadata is missing required 'component' (the subject of this SBOM).");
            return;
        }

        if (string.IsNullOrEmpty(bom.Metadata.Component.Name))
            errors.Add("Metadata component is missing required 'name'.");

        if (string.IsNullOrEmpty(bom.Metadata.Component.Version))
            errors.Add("Metadata component is missing required 'version'.");

        if (string.IsNullOrEmpty(bom.Metadata.Component.BomRef))
            errors.Add("Metadata component is missing 'bom-ref' (required for dependency graph root).");
    }

    private static void ValidateComponents(Bom bom, List<string> errors)
    {
        if (bom.Components is null)
            return;

        HashSet<string> seenBomRefs = new(StringComparer.OrdinalIgnoreCase);

        // Include the metadata component's BomRef.
        if (!string.IsNullOrEmpty(bom.Metadata?.Component?.BomRef))
            seenBomRefs.Add(bom.Metadata!.Component!.BomRef);

        foreach (Component component in bom.Components)
        {
            string label = $"'{component.Name ?? "(unnamed)"}@{component.Version ?? "?"}'";

            if (string.IsNullOrEmpty(component.BomRef))
            {
                errors.Add($"Component {label} is missing 'bom-ref'.");
                continue;
            }

            if (!seenBomRefs.Add(component.BomRef))
            {
                errors.Add($"Duplicate 'bom-ref' detected: '{component.BomRef}'.");
            }

            if (string.IsNullOrEmpty(component.Name))
                errors.Add($"Component with bom-ref '{component.BomRef}' is missing 'name'.");

            if (string.IsNullOrEmpty(component.Version))
                errors.Add($"Component {label} is missing 'version'.");

            // Validate PURL format when present.
            if (!string.IsNullOrEmpty(component.Purl) && !PurlPattern.IsMatch(component.Purl))
                errors.Add($"Component {label} has malformed PURL: '{component.Purl}'.");

            // Validate hash content length when present.
            if (component.Hashes is not null)
            {
                foreach (Hash hash in component.Hashes)
                {
                    if (hash.Alg == Hash.HashAlgorithm.SHA_512 &&
                        !string.IsNullOrEmpty(hash.Content) &&
                        hash.Content.Length != 128)
                    {
                        errors.Add($"Component {label} SHA-512 hash has unexpected length ({hash.Content.Length} hex chars, expected 128).");
                    }
                }
            }
        }
    }

    private static void ValidateDependencies(Bom bom, List<string> errors)
    {
        if (bom.Dependencies is null)
            return;

        // Build the set of all known bom-refs (metadata component + components).
        HashSet<string> knownRefs = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(bom.Metadata?.Component?.BomRef))
            knownRefs.Add(bom.Metadata!.Component!.BomRef);

        if (bom.Components is not null)
        {
            foreach (Component component in bom.Components)
            {
                if (!string.IsNullOrEmpty(component.BomRef))
                    knownRefs.Add(component.BomRef);
            }
        }

        foreach (Dependency dep in bom.Dependencies)
        {
            if (string.IsNullOrEmpty(dep.Ref))
            {
                errors.Add("Dependency entry has a null or empty 'ref'.");
                continue;
            }

            if (!knownRefs.Contains(dep.Ref))
            {
                errors.Add($"Dependency 'ref' points to unknown component: '{dep.Ref}'.");
            }

            if (dep.Dependencies is not null)
            {
                foreach (Dependency child in dep.Dependencies)
                {
                    if (!string.IsNullOrEmpty(child.Ref) && !knownRefs.Contains(child.Ref))
                    {
                        errors.Add($"Dependency '{dep.Ref}' has 'dependsOn' reference to unknown component: '{child.Ref}'.");
                    }
                }
            }
        }
    }
}
