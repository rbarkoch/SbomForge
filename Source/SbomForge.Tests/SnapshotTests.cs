using CycloneDX.Json;
using CycloneDX.Models;
using SbomForge;

namespace SbomForge.Tests;

/// <summary>
/// Snapshot (golden-file) tests that detect any regression in generated SBOM output
/// by comparing the full SBOM JSON against a checked-in reference file.
/// 
/// If a golden file does not yet exist, the first test run will create it and fail.
/// Review the created file, then re-run. Any SBOM structural change will fail the test
/// with a line-level diff.
/// </summary>
[TestClass]
public sealed class SnapshotTests
{
    private static string _testBasePath = null!;
    private static string _outputDirectory = null!;
    private static string _goldenDir = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        _testBasePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));

        _outputDirectory = Path.Combine(Path.GetTempPath(), "SbomForge-Snapshot-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDirectory);

        _goldenDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "TestData", "GoldenFiles"));
        Directory.CreateDirectory(_goldenDir);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(_outputDirectory))
            Directory.Delete(_outputDirectory, true);
    }

    // ──────────────────────── .NET Project Snapshots ────────────────────────

    [TestMethod]
    public async Task Snapshot_ExampleClassLibrary1_MatchesGoldenFile()
    {
        var result = await BuildStandardSbom();
        await AssertBomMatchesGolden(result, "ExampleClassLibrary1");
    }

    [TestMethod]
    public async Task Snapshot_ExampleClassLibrary2_MatchesGoldenFile()
    {
        var result = await BuildStandardSbom();
        await AssertBomMatchesGolden(result, "ExampleClassLibrary2");
    }

    [TestMethod]
    public async Task Snapshot_ExampleConsoleApp1_MatchesGoldenFile()
    {
        var result = await BuildStandardSbom();
        await AssertBomMatchesGolden(result, "ExampleConsoleApp1");
    }

    [TestMethod]
    public async Task Snapshot_ExampleConsoleApp2_MatchesGoldenFile()
    {
        var result = await BuildStandardSbom();
        await AssertBomMatchesGolden(result, "ExampleConsoleApp2");
    }

    // ──────────────────────── Custom Component Snapshots ────────────────────────

    [TestMethod]
    public async Task Snapshot_AppContainer_MatchesGoldenFile()
    {
        var result = await BuildStandardSbom();
        await AssertBomMatchesGolden(result, "app-container");
    }

    [TestMethod]
    public async Task Snapshot_WebFrontend_MatchesGoldenFile()
    {
        var result = await BuildStandardSbom();
        await AssertBomMatchesGolden(result, "web-frontend");
    }

    [TestMethod]
    public async Task Snapshot_K8sDeployment_MatchesGoldenFile()
    {
        var result = await BuildStandardSbom();
        await AssertBomMatchesGolden(result, "k8s-deployment");
    }

    // ──────────────────────── Zero Warnings ────────────────────────

    [TestMethod]
    public async Task Snapshot_StandardBuild_ProducesNoValidationWarnings()
    {
        var result = await BuildStandardSbom();

        // Filter to only validation-related warnings (not nuspec cache misses for external packages).
        var validationWarnings = result.Warnings
            .Where(w => !w.Contains("nuspec", StringComparison.OrdinalIgnoreCase)
                     && !w.Contains("metadata from project file", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.AreEqual(0, validationWarnings.Count,
            $"Expected zero validation warnings but got:\n{string.Join("\n", validationWarnings)}");
    }

    // ──────────────────────── Helpers ────────────────────────

    /// <summary>
    /// Builds the standard example deployment SBOMs using the same configuration
    /// as the ExampleSbom program.
    /// </summary>
    private async Task<SbomBuildResult> BuildStandardSbom()
    {
        return await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithMetadata(meta =>
            {
                meta.Version = "1.0.0";
            })
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", component =>
            {
                component.WithMetadata(meta =>
                {
                    meta.Cpe = "cpe:2.3:a:example:exampleclasslibrary2:1.0.0:*:*:*:*:*:*:*";
                });
                component.WithExternal("ExampleSbom/ExampleExternal.sbom.json");
            })
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj", component => component
                .WithComponent(c =>
                {
                    c.Type = Component.Classification.Container;
                    c.Name = "redis";
                    c.Version = "7.2";
                    c.Purl = "pkg:docker/redis@7.2";
                    c.Description = "Redis cache";
                })
                .WithComponent(c =>
                {
                    c.Type = Component.Classification.Library;
                    c.Name = "react";
                    c.Version = "18.2.0";
                    c.Purl = "pkg:npm/react@18.2.0";
                })
            )
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "app-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                    m.BomRef = "pkg:docker/myorg/app-container@1.0.0";
                    m.Purl = "pkg:docker/myorg/app-container@1.0.0";
                    m.Description = "Production Docker container";
                })
                .DependsOnProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
                .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
                .WithComponent(c =>
                {
                    c.Name = "alpine";
                    c.Version = "3.18";
                    c.Purl = "pkg:docker/alpine@3.18";
                    c.Type = Component.Classification.Container;
                })
            )
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "web-frontend";
                    m.Version = "2.1.0";
                    m.Type = Component.Classification.Application;
                    m.BomRef = "pkg:npm/myorg/web-frontend@2.1.0";
                    m.Purl = "pkg:npm/myorg/web-frontend@2.1.0";
                })
                .WithComponent(c =>
                {
                    c.Name = "react";
                    c.Version = "18.2.0";
                    c.Purl = "pkg:npm/react@18.2.0";
                })
            )
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "k8s-deployment";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Platform;
                    m.BomRef = "pkg:generic/k8s-deployment@1.0.0";
                    m.Purl = "pkg:generic/k8s-deployment@1.0.0";
                })
                .DependsOn("pkg:docker/myorg/app-container@1.0.0")
                .DependsOn("pkg:npm/myorg/web-frontend@2.1.0")
                .WithComponent(c =>
                {
                    c.Name = "kubernetes";
                    c.Version = "1.28";
                    c.Purl = "pkg:generic/kubernetes@1.28";
                    c.Scope = Component.ComponentScope.Excluded;
                })
            )
            .BuildAsync();
    }

    private static Task AssertBomMatchesGolden(SbomBuildResult result, string bomKey)
    {
        Assert.IsTrue(result.Boms.ContainsKey(bomKey), $"BOM '{bomKey}' not found in result.");
        var bom = result.Boms[bomKey];
        string json = Serializer.Serialize(bom);
        string goldenPath = Path.Combine(_goldenDir, $"{bomKey}.approved.json");
        SnapshotTestHelper.AssertMatchesGoldenFile(json, goldenPath);
        return Task.CompletedTask;
    }
}
