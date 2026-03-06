using CycloneDX;
using CycloneDX.Models;
using SbomForge;

namespace SbomForge.Tests;

/// <summary>
/// Edge case and regression tests targeting specific risk areas:
/// BomRef collision handling, dependency graph integrity, and
/// structural completeness under non-trivial configurations.
/// </summary>
[TestClass]
public sealed class EdgeCaseTests
{
    private static string _testBasePath = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        _testBasePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));
    }

    #region Dependency Graph Integrity

    [TestMethod]
    public async Task DependencyGraph_AllDependsOnRefsAreValid()
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleConsoleApp1"];

        // Build set of all known BomRefs.
        HashSet<string> knownRefs = new(StringComparer.OrdinalIgnoreCase);
        if (bom.Metadata?.Component?.BomRef is not null)
            knownRefs.Add(bom.Metadata.Component.BomRef);
        foreach (var c in bom.Components!)
            if (c.BomRef is not null) knownRefs.Add(c.BomRef);

        // Every ref and dependsOn entry must point to a real component.
        foreach (var dep in bom.Dependencies!)
        {
            Assert.IsTrue(knownRefs.Contains(dep.Ref!),
                $"Dependency ref '{dep.Ref}' does not correspond to any component.");

            if (dep.Dependencies is not null)
            {
                foreach (var child in dep.Dependencies)
                {
                    Assert.IsTrue(knownRefs.Contains(child.Ref!),
                        $"dependsOn ref '{child.Ref}' under '{dep.Ref}' does not correspond to any component.");
                }
            }
        }
    }

    [TestMethod]
    public async Task DependencyGraph_NoDuplicateBomRefsInComponents()
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleConsoleApp1"];
        var bomRefs = bom.Components!.Select(c => c.BomRef).ToList();
        var unique = bomRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.AreEqual(unique.Count, bomRefs.Count,
            $"Found duplicate BomRefs: {string.Join(", ", bomRefs.GroupBy(r => r, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key))}");
    }

    [TestMethod]
    public async Task DependencyGraph_EveryComponentHasDependencyEntry()
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleClassLibrary1"];
        var depRefs = bom.Dependencies!.Select(d => d.Ref).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var component in bom.Components!)
        {
            Assert.IsTrue(depRefs.Contains(component.BomRef!),
                $"Component '{component.Name}' (BomRef={component.BomRef}) has no entry in the dependencies array.");
        }
    }

    #endregion

    #region Cross-SBOM Consistency

    [TestMethod]
    public async Task CrossSbom_ProjectReferenceVersionConsistent()
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithMetadata(m => m.Version = "1.0.0")
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // ExampleConsoleApp1 references ExampleClassLibrary1.
        // The version/PURL for ExampleClassLibrary1 should be the same in both:
        // 1. ExampleClassLibrary1's own BOM metadata
        // 2. ExampleConsoleApp1's BOM component entry for ExampleClassLibrary1

        var lib1Bom = result.Boms["ExampleClassLibrary1"];
        string lib1Version = lib1Bom.Metadata!.Component!.Version!;
        string lib1Purl = lib1Bom.Metadata.Component.Purl!;

        var app1Bom = result.Boms["ExampleConsoleApp1"];
        var lib1InApp = app1Bom.Components!.FirstOrDefault(
            c => c.Name == "ExampleClassLibrary1");

        Assert.IsNotNull(lib1InApp, "ExampleClassLibrary1 should appear as a component in ExampleConsoleApp1's BOM");
        Assert.AreEqual(lib1Version, lib1InApp!.Version,
            "Version mismatch: ExampleClassLibrary1 version differs between its own BOM and ExampleConsoleApp1's BOM");
        Assert.AreEqual(lib1Purl, lib1InApp.Purl,
            "PURL mismatch: ExampleClassLibrary1 PURL differs between its own BOM and ExampleConsoleApp1's BOM");
    }

    #endregion

    #region Custom Component Deps Include Transitive

    [TestMethod]
    public async Task CustomComponent_IncludesTransitiveNuGetFromProject()
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithMetadata(m => m.Version = "1.0.0")
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "test-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            )
            .BuildAsync();

        var containerBom = result.Boms["test-container"];
        var lib1Bom = result.Boms["ExampleClassLibrary1"];

        // The container should include all NuGet packages from ExampleClassLibrary1.
        int lib1PackageCount = lib1Bom.Components!.Count(c =>
            c.Purl is not null && c.Purl.StartsWith("pkg:nuget/"));

        int containerNuGetCount = containerBom.Components!.Count(c =>
            c.Purl is not null && c.Purl.StartsWith("pkg:nuget/"));

        Assert.IsTrue(containerNuGetCount >= lib1PackageCount,
            $"Container should include at least {lib1PackageCount} NuGet packages from ExampleClassLibrary1, but only has {containerNuGetCount}");
    }

    #endregion

    #region Empty/Minimal Projects

    [TestMethod]
    public async Task EmptyProject_ProducesValidBom()
    {
        string outputDir = GetTempOutputDir();

        // ExampleConsoleApp2 is the simplest — fewest dependencies.
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleConsoleApp2"];
        Assert.IsNotNull(bom.Metadata?.Component?.Name);
        Assert.IsNotNull(bom.Metadata?.Component?.Version);
        Assert.IsNotNull(bom.Dependencies);

        // Should have zero validation errors.
        var validationWarnings = result.Warnings
            .Where(w => w.Contains("Duplicate") || w.Contains("dangling") || w.Contains("missing"))
            .ToList();
        Assert.AreEqual(0, validationWarnings.Count,
            $"Validation warnings: {string.Join("\n", validationWarnings)}");
    }

    #endregion

    #region Spec Version

    [TestMethod]
    [DataRow(SpecificationVersion.v1_4)]
    [DataRow(SpecificationVersion.v1_5)]
    [DataRow(SpecificationVersion.v1_6)]
    [DataRow(SpecificationVersion.v1_7)]
    public async Task SpecVersion_ProducesValidOutput(SpecificationVersion version)
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o =>
            {
                o.OutputDirectory = outputDir;
                o.SpecVersion = version;
            })
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        var bom = result.Boms["ExampleClassLibrary2"];
        Assert.AreEqual(version, bom.SpecVersion);
        Assert.IsNotNull(bom.Metadata?.Component);
        Assert.IsNotNull(bom.Components);

        // Should serialize without error.
        string json = CycloneDX.Json.Serializer.Serialize(bom);
        Assert.IsTrue(json.Length > 100, "Serialized JSON should be non-trivial");
    }

    #endregion

    #region Filtering Doesn't Break Graph

    [TestMethod]
    public async Task Filtering_ExcludedPackages_RemovedFromDependsOnLists()
    {
        string outputDir = GetTempOutputDir();

        // First, build without filters to find a package to exclude.
        var unfilteredResult = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        var unfilteredBom = unfilteredResult.Boms["ExampleClassLibrary1"];
        var transitivePkg = unfilteredBom.Components!
            .FirstOrDefault(c => c.Scope == Component.ComponentScope.Optional && c.Purl?.StartsWith("pkg:nuget/") == true);

        if (transitivePkg is null)
            Assert.Inconclusive("No transitive NuGet package found to test filtering.");

        // Now exclude it and verify the graph remains valid.
        string outputDir2 = GetTempOutputDir();
        var filteredResult = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir2)
            .WithFilters(f => f.ExcludePackageIds.Add(transitivePkg.Name!))
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        var filteredBom = filteredResult.Boms["ExampleClassLibrary1"];

        // The excluded package should not appear in components.
        Assert.IsFalse(filteredBom.Components!.Any(c => c.Name == transitivePkg.Name),
            $"Excluded package '{transitivePkg.Name}' should not appear in components.");

        // No dependency should still reference the excluded package by its exact bom-ref or PURL.
        string excludedPurl = transitivePkg.Purl!;
        string excludedBomRef = transitivePkg.BomRef!;
        foreach (var dep in filteredBom.Dependencies!)
        {
            if (dep.Dependencies is not null)
            {
                Assert.IsFalse(dep.Dependencies.Any(d =>
                    d.Ref == excludedPurl || d.Ref == excludedBomRef),
                    $"Dependency ref to excluded package '{transitivePkg.Name}' still present in dependsOn of '{dep.Ref}'.");
            }
        }
    }

    #endregion

    private static string GetTempOutputDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SbomForge-Edge-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
