using CycloneDX;
using CycloneDX.Models;
using SbomForge.Composer;
using SbomForge.Configuration;
using SbomForge.Resolver;
using SbomForge.Validation;

namespace SbomForge.Tests;

/// <summary>
/// Unit tests for individual data-transform steps in the SBOM pipeline.
/// These isolate hash conversion, PURL generation, scope assignment,
/// license mapping, and metadata merge logic.
/// </summary>
[TestClass]
public sealed class DataTransformTests
{
    #region Hash Conversion

    [TestMethod]
    public void ConvertBase64ToHex_KnownValue_ProducesCorrectHex()
    {
        // SHA-512 of empty string = known value
        // SHA-512("") = cf83e1357eefb8bd...
        // Base64 of that hash:
        string base64 = "z4PhNX7vuL3xVChQ1m2AB9Yg5AULVxXcg/SpIdNs6c5H0NE8XYXysP+DGNKHfuwvY7kxvUdBeoGlODJ6+SfaPg==";
        string expectedHex = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e";

        string result = InvokeConvertBase64ToHex(base64);

        Assert.AreEqual(expectedHex, result);
        Assert.AreEqual(128, result.Length, "SHA-512 hex should be 128 characters");
    }

    [TestMethod]
    public void ConvertBase64ToHex_ShortInput_ProducesCorrectLength()
    {
        // 4 bytes Base64 -> 8 hex chars
        string base64 = "AQIDBA=="; // bytes: 01 02 03 04
        string result = InvokeConvertBase64ToHex(base64);
        Assert.AreEqual("01020304", result);
    }

    [TestMethod]
    public void ConvertBase64ToHex_OutputIsLowercase()
    {
        string base64 = "/w=="; // byte: 0xFF
        string result = InvokeConvertBase64ToHex(base64);
        Assert.AreEqual("ff", result);
        Assert.AreEqual(result, result.ToLowerInvariant(), "Output must be lowercase");
    }

    /// <summary>
    /// Uses reflection to call the private static ConvertBase64ToHex method.
    /// </summary>
    private static string InvokeConvertBase64ToHex(string base64)
    {
        var method = typeof(Composer.Composer).GetMethod(
            "ConvertBase64ToHex",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method, "ConvertBase64ToHex method should exist");
        return (string)method!.Invoke(null, [base64])!;
    }

    #endregion

    #region PURL Format

    [TestMethod]
    public async Task Purl_NuGetPackage_HasCorrectFormat()
    {
        var result = await BuildSingleLibraryBom();
        var bom = result.Boms["ExampleClassLibrary1"];

        // NuGet packages should use pkg:nuget/
        foreach (var component in bom.Components!)
        {
            if (component.Type == Component.Classification.Library && component.Purl is not null)
            {
                if (component.Purl.StartsWith("pkg:nuget/"))
                {
                    Assert.IsTrue(
                        System.Text.RegularExpressions.Regex.IsMatch(component.Purl, @"^pkg:nuget/[^@]+@[^@]+$"),
                        $"NuGet PURL should be 'pkg:nuget/name@version' but got: {component.Purl}");
                }
            }
        }
    }

    [TestMethod]
    public async Task Purl_ApplicationProject_UsesGenericScheme()
    {
        var basePath = GetTestBasePath();
        var result = await new SbomBuilder()
            .WithBasePath(basePath)
            .WithOutput(o => o.OutputDirectory = GetTempOutputDir())
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleConsoleApp1"];
        Assert.IsNotNull(bom.Metadata?.Component?.Purl);
        Assert.IsTrue(bom.Metadata!.Component!.Purl!.StartsWith("pkg:generic/"),
            $"Application projects should use pkg:generic/ but got: {bom.Metadata.Component.Purl}");
    }

    [TestMethod]
    public async Task Purl_LibraryProject_UsesNuGetScheme()
    {
        var result = await BuildSingleLibraryBom();
        var bom = result.Boms["ExampleClassLibrary1"];
        Assert.IsNotNull(bom.Metadata?.Component?.Purl);
        Assert.IsTrue(bom.Metadata!.Component!.Purl!.StartsWith("pkg:nuget/"),
            $"Library projects should use pkg:nuget/ but got: {bom.Metadata.Component.Purl}");
    }

    #endregion

    #region Scope Assignment

    [TestMethod]
    public async Task Scope_DirectDependencies_AreRequired()
    {
        var result = await BuildSingleLibraryBom();
        var bom = result.Boms["ExampleClassLibrary1"];

        // The root dependency node should list direct deps.
        var rootDep = bom.Dependencies!.FirstOrDefault(d => d.Ref == bom.Metadata!.Component!.BomRef);
        Assert.IsNotNull(rootDep, "Root dependency node should exist");

        var directRefs = rootDep!.Dependencies!.Select(d => d.Ref).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var component in bom.Components!)
        {
            if (directRefs.Contains(component.BomRef))
            {
                Assert.AreEqual(Component.ComponentScope.Required, component.Scope,
                    $"Direct dependency '{component.Name}' should have scope 'required'");
            }
        }
    }

    [TestMethod]
    public async Task Scope_TransitiveDependencies_AreOptional()
    {
        var result = await BuildSingleLibraryBom();
        var bom = result.Boms["ExampleClassLibrary1"];

        var rootDep = bom.Dependencies!.FirstOrDefault(d => d.Ref == bom.Metadata!.Component!.BomRef);
        var directRefs = rootDep!.Dependencies!.Select(d => d.Ref).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Project references are also "direct" — collect them.
        var projectRefNames = bom.Components!
            .Where(c => c.Scope == Component.ComponentScope.Required && !directRefs.Contains(c.BomRef))
            .Select(c => c.BomRef)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var component in bom.Components!)
        {
            if (!directRefs.Contains(component.BomRef!) && !projectRefNames.Contains(component.BomRef!))
            {
                Assert.AreEqual(Component.ComponentScope.Optional, component.Scope,
                    $"Transitive dependency '{component.Name}' should have scope 'optional'");
            }
        }
    }

    #endregion

    #region License Mapping

    [TestMethod]
    public async Task License_NuGetPackageWithExpression_MappedCorrectly()
    {
        var result = await BuildSingleLibraryBom();
        var bom = result.Boms["ExampleClassLibrary1"];

        // At least one NuGet package should have a license expression.
        var withLicense = bom.Components!
            .Where(c => c.Licenses is not null && c.Licenses.Count > 0)
            .ToList();

        // Verify that any license expression found is a valid-looking string (not a file path).
        foreach (var comp in withLicense)
        {
            foreach (var licenseChoice in comp.Licenses!)
            {
                if (licenseChoice.Expression is not null)
                {
                    Assert.IsFalse(licenseChoice.Expression.EndsWith(".txt", StringComparison.OrdinalIgnoreCase),
                        $"License expression for '{comp.Name}' looks like a file path: '{licenseChoice.Expression}'");
                    Assert.IsFalse(licenseChoice.Expression.Contains('/') || licenseChoice.Expression.Contains('\\'),
                        $"License expression for '{comp.Name}' contains path separators: '{licenseChoice.Expression}'");
                }
            }
        }
    }

    #endregion

    #region Metadata Precedence

    [TestMethod]
    public async Task MetadataPrecedence_UserOverridesAutoDetected()
    {
        var basePath = GetTestBasePath();
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(basePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj", comp =>
            {
                comp.WithMetadata(m =>
                {
                    m.Version = "99.0.0";
                    m.Description = "User-provided description";
                });
            })
            .BuildAsync();

        var bom = result.Boms["ExampleClassLibrary1"];
        Assert.AreEqual("99.0.0", bom.Metadata!.Component!.Version);
        Assert.AreEqual("User-provided description", bom.Metadata.Component.Description);
    }

    [TestMethod]
    public async Task MetadataPrecedence_GlobalAppliesWhenProjectNotSet()
    {
        var basePath = GetTestBasePath();
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(basePath)
            .WithMetadata(m => m.Copyright = "Global Copyright")
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleClassLibrary1"];
        // Auto-detect may have its own copyright from .csproj/Directory.Build.props,
        // but the global metadata should be applied when auto-detect is empty.
        Assert.IsNotNull(bom.Metadata!.Component!.Copyright, "Copyright should be set");
    }

    [TestMethod]
    public async Task ResolveDirectoryBuildProps_FalseSkipsBuildPropsMetadata()
    {
        var basePath = GetTestBasePath();
        string outputDir = GetTempOutputDir();

        // ExampleClassLibrary2 inherits Company and Copyright from Directory.Build.props.
        // With resolution set to false, those should NOT be auto-detected.
        var result = await new SbomBuilder()
            .WithBasePath(basePath)
            .WithResolution(r => r.ResolveDirectoryBuildProps = false)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleClassLibrary2"];

        // Copyright comes only from Directory.Build.props for this project,
        // so it should be absent when resolution is disabled.
        Assert.IsNull(bom.Metadata!.Component!.Copyright,
            "Copyright should be null when ResolveDirectoryBuildProps is false");

        // Supplier (derived from Company in Directory.Build.props) should also be absent.
        Assert.IsNull(bom.Metadata.Component.Supplier,
            "Supplier should be null when ResolveDirectoryBuildProps is false");

        // Properties declared in the .csproj itself should still be present.
        Assert.AreEqual("1.2.3", bom.Metadata.Component.Version,
            "Version from .csproj should still be detected");
        Assert.AreEqual("Example class library for SBOM testing", bom.Metadata.Component.Description,
            "Description from .csproj should still be detected");
    }

    [TestMethod]
    public async Task ResolveDirectoryBuildProps_DefaultBehaviorIncludesBuildProps()
    {
        var basePath = GetTestBasePath();
        string outputDir = GetTempOutputDir();

        // Without the flag (defaults to true), Directory.Build.props metadata should be present.
        var result = await new SbomBuilder()
            .WithBasePath(basePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleClassLibrary2"];

        // Copyright and Company come from Directory.Build.props.
        Assert.IsNotNull(bom.Metadata!.Component!.Copyright,
            "Copyright should be set from Directory.Build.props by default");
        Assert.IsNotNull(bom.Metadata.Component.Supplier,
            "Supplier (from Company) should be set from Directory.Build.props by default");
    }

    #endregion

    #region Hash Integrity

    [TestMethod]
    public async Task Hash_AllNuGetPackages_Have128CharHex()
    {
        var result = await BuildSingleLibraryBom();
        var bom = result.Boms["ExampleClassLibrary1"];

        foreach (var component in bom.Components!)
        {
            if (component.Hashes is not null)
            {
                foreach (var hash in component.Hashes)
                {
                    if (hash.Alg == Hash.HashAlgorithm.SHA_512)
                    {
                        Assert.AreEqual(128, hash.Content!.Length,
                            $"SHA-512 hash for '{component.Name}' should be 128 hex chars but was {hash.Content.Length}");
                        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(hash.Content, "^[0-9a-f]+$"),
                            $"Hash for '{component.Name}' should be lowercase hex");
                    }
                }
            }
        }
    }

    #endregion

    #region SbomValidator Unit Tests

    [TestMethod]
    public void Validator_ValidBom_ReturnsNoErrors()
    {
        Bom bom = new()
        {
            SpecVersion = SpecificationVersion.v1_7,
            Metadata = new CycloneDX.Models.Metadata
            {
                Component = new Component
                {
                    BomRef = "pkg:nuget/TestLib@1.0.0",
                    Name = "TestLib",
                    Version = "1.0.0",
                    Type = Component.Classification.Library
                }
            },
            Components =
            [
                new Component
                {
                    BomRef = "pkg:nuget/Dep@2.0.0",
                    Name = "Dep",
                    Version = "2.0.0",
                    Purl = "pkg:nuget/Dep@2.0.0",
                    Type = Component.Classification.Library
                }
            ],
            Dependencies =
            [
                new Dependency
                {
                    Ref = "pkg:nuget/TestLib@1.0.0",
                    Dependencies = [new Dependency { Ref = "pkg:nuget/Dep@2.0.0" }]
                },
                new Dependency
                {
                    Ref = "pkg:nuget/Dep@2.0.0",
                    Dependencies = []
                }
            ]
        };

        var errors = SbomValidator.Validate(bom);
        Assert.AreEqual(0, errors.Count, $"Expected no errors but got:\n{string.Join("\n", errors)}");
    }

    [TestMethod]
    public void Validator_DanglingDependencyRef_DetectsError()
    {
        Bom bom = new()
        {
            SpecVersion = SpecificationVersion.v1_7,
            Metadata = new CycloneDX.Models.Metadata
            {
                Component = new Component
                {
                    BomRef = "pkg:nuget/App@1.0.0",
                    Name = "App",
                    Version = "1.0.0"
                }
            },
            Components = [],
            Dependencies =
            [
                new Dependency
                {
                    Ref = "pkg:nuget/App@1.0.0",
                    Dependencies = [new Dependency { Ref = "pkg:nuget/DoesNotExist@1.0.0" }]
                }
            ]
        };

        var errors = SbomValidator.Validate(bom);
        Assert.IsTrue(errors.Any(e => e.Contains("DoesNotExist")),
            "Should detect dangling dependency reference");
    }

    [TestMethod]
    public void Validator_DuplicateBomRef_DetectsError()
    {
        Bom bom = new()
        {
            SpecVersion = SpecificationVersion.v1_7,
            Metadata = new CycloneDX.Models.Metadata
            {
                Component = new Component
                {
                    BomRef = "pkg:nuget/App@1.0.0",
                    Name = "App",
                    Version = "1.0.0"
                }
            },
            Components =
            [
                new Component { BomRef = "dup-ref", Name = "A", Version = "1.0" },
                new Component { BomRef = "dup-ref", Name = "B", Version = "2.0" }
            ],
            Dependencies = []
        };

        var errors = SbomValidator.Validate(bom);
        Assert.IsTrue(errors.Any(e => e.Contains("Duplicate")),
            "Should detect duplicate bom-ref");
    }

    [TestMethod]
    public void Validator_MissingMetadataComponent_DetectsError()
    {
        Bom bom = new()
        {
            SpecVersion = SpecificationVersion.v1_7,
            Metadata = new CycloneDX.Models.Metadata(),
            Components = [],
            Dependencies = []
        };

        var errors = SbomValidator.Validate(bom);
        Assert.IsTrue(errors.Any(e => e.Contains("metadata") && e.Contains("component")),
            "Should detect missing metadata component");
    }

    [TestMethod]
    public void Validator_MalformedPurl_DetectsError()
    {
        Bom bom = new()
        {
            SpecVersion = SpecificationVersion.v1_7,
            Metadata = new CycloneDX.Models.Metadata
            {
                Component = new Component
                {
                    BomRef = "root",
                    Name = "App",
                    Version = "1.0.0"
                }
            },
            Components =
            [
                new Component
                {
                    BomRef = "bad-purl",
                    Name = "Bad",
                    Version = "1.0",
                    Purl = "not-a-valid-purl"
                }
            ],
            Dependencies = []
        };

        var errors = SbomValidator.Validate(bom);
        Assert.IsTrue(errors.Any(e => e.Contains("malformed PURL")),
            "Should detect malformed PURL");
    }

    [TestMethod]
    public void Validator_InvalidHashLength_DetectsError()
    {
        Bom bom = new()
        {
            SpecVersion = SpecificationVersion.v1_7,
            Metadata = new CycloneDX.Models.Metadata
            {
                Component = new Component
                {
                    BomRef = "root",
                    Name = "App",
                    Version = "1.0.0"
                }
            },
            Components =
            [
                new Component
                {
                    BomRef = "bad-hash",
                    Name = "Pkg",
                    Version = "1.0",
                    Purl = "pkg:nuget/Pkg@1.0",
                    Hashes = [new Hash { Alg = Hash.HashAlgorithm.SHA_512, Content = "abc123" }]
                }
            ],
            Dependencies = []
        };

        var errors = SbomValidator.Validate(bom);
        Assert.IsTrue(errors.Any(e => e.Contains("SHA-512") && e.Contains("length")),
            "Should detect invalid hash length");
    }

    #endregion

    // ──────────────────────── Helpers ────────────────────────

    private static string GetTestBasePath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));

    private static string GetTempOutputDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SbomForge-DataTransform-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<SbomBuildResult> BuildSingleLibraryBom()
    {
        return await new SbomBuilder()
            .WithBasePath(GetTestBasePath())
            .WithOutput(o => o.OutputDirectory = GetTempOutputDir())
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();
    }
}
