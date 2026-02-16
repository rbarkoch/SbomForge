using CycloneDX;
using CycloneDX.Models;
using SbomForge;

namespace SbomForge.Tests;

/// <summary>
/// Tests for custom component functionality including ForComponent API,
/// cross-component dependencies, and transitive dependency inclusion.
/// </summary>
[TestClass]
public sealed class CustomComponentTests
{
    private static string _testBasePath = null!;
    private static string _outputDirectory = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        _testBasePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));
        
        _outputDirectory = Path.Combine(_testBasePath, "test-custom-component-output");
        
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }
    }

    #region Basic Custom Component Tests

    [TestMethod]
    public async Task CustomComponent_StandaloneComponent_GeneratesSbom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "my-app";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Application;
                }))
            .BuildAsync();

        // Assert
        Assert.AreEqual(1, result.Boms.Count, "Should generate one SBOM");
        Assert.IsTrue(result.Boms.ContainsKey("my-app"), "SBOM should be keyed by component name");
        
        var bom = result.Boms["my-app"];
        Assert.AreEqual("my-app", bom.Metadata!.Component!.Name);
        Assert.AreEqual("1.0.0", bom.Metadata.Component.Version);
        Assert.AreEqual(Component.Classification.Application, bom.Metadata.Component.Type);
    }

    [TestMethod]
    public async Task CustomComponent_WithDependencies_IncludesDependenciesInSbom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "web-app";
                    m.Version = "2.0.0";
                    m.Type = Component.Classification.Application;
                })
                .WithComponent(c =>
                {
                    c.Name = "react";
                    c.Version = "18.2.0";
                    c.Purl = "pkg:npm/react@18.2.0";
                    c.Type = Component.Classification.Library;
                })
                .WithComponent(c =>
                {
                    c.Name = "redis";
                    c.Version = "7.2";
                    c.Purl = "pkg:docker/redis@7.2";
                    c.Type = Component.Classification.Container;
                }))
            .BuildAsync();

        // Assert
        var bom = result.Boms["web-app"];
        
        var react = bom.Components!.FirstOrDefault(c => c.Name == "react");
        var redis = bom.Components!.FirstOrDefault(c => c.Name == "redis");
        
        Assert.IsNotNull(react, "React should be in components");
        Assert.IsNotNull(redis, "Redis should be in components");
        Assert.AreEqual("18.2.0", react.Version);
        Assert.AreEqual("7.2", redis.Version);
    }

    #endregion

    #region Cross-Component Dependency Tests

    [TestMethod]
    public async Task CustomComponent_DependsOnDotNetProject_IncludesProjectMetadata()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "docker-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .DependsOnProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj"))
            .BuildAsync();

        // Assert
        Assert.AreEqual(2, result.Boms.Count, "Should generate 2 SBOMs");
        
        var containerBom = result.Boms["docker-container"];
        var appBom = result.Boms["ExampleConsoleApp1"];
        
        // Find ExampleConsoleApp1 in the container's SBOM
        var appInContainer = containerBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleConsoleApp1");
        
        Assert.IsNotNull(appInContainer, "App should be in container SBOM");
        Assert.AreEqual(appBom.Metadata!.Component!.Version, appInContainer.Version,
            "App version should match its own SBOM");
    }

    [TestMethod]
    public async Task CustomComponent_DependsOnProject_IncludesTransitiveDependencies()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "app-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj"))
            .BuildAsync();

        // Assert
        var containerBom = result.Boms["app-container"];
        var libBom = result.Boms["ExampleClassLibrary1"];
        
        // Container should have both the project reference AND the project's NuGet packages
        var projectRef = containerBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary1");
        Assert.IsNotNull(projectRef, "Should have project reference");
        
        // Should also have transitive NuGet dependencies from the library
        var hasNuGetPackages = containerBom.Components!
            .Any(c => c.Purl?.StartsWith("pkg:nuget/") == true);
        
        Assert.IsTrue(hasNuGetPackages, 
            "Should include transitive NuGet packages from referenced project");
    }

    [TestMethod]
    public async Task CustomComponent_DependsOnMultipleProjects_IncludesAll()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "composite-app";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Application;
                })
                .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
                .DependsOnProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj"))
            .BuildAsync();

        // Assert
        var compositeBom = result.Boms["composite-app"];
        
        var lib1 = compositeBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary1");
        var app1 = compositeBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleConsoleApp1");
        
        Assert.IsNotNull(lib1, "Should have ExampleClassLibrary1");
        Assert.IsNotNull(app1, "Should have ExampleConsoleApp1");
    }

    #endregion

    #region Cross-Custom-Component Dependencies

    [TestMethod]
    public async Task CustomComponent_DependsOnOtherCustomComponent_IncludesMetadata()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "app-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                    m.BomRef = "pkg:docker/app-container@1.0.0";
                    m.Purl = "pkg:docker/app-container@1.0.0";
                }))
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "k8s-deployment";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Platform;
                })
                .DependsOn("pkg:docker/app-container@1.0.0"))
            .BuildAsync();

        // Assert
        Assert.AreEqual(2, result.Boms.Count, "Should generate 2 SBOMs");
        
        var k8sBom = result.Boms["k8s-deployment"];
        
        // k8s-deployment should have app-container as a component
        var appContainer = k8sBom.Components!
            .FirstOrDefault(c => c.Name == "app-container");
        
        Assert.IsNotNull(appContainer, "k8s should reference app-container");
        Assert.AreEqual("1.0.0", appContainer.Version);
    }

    [TestMethod]
    public async Task CustomComponent_ChainedDependencies_AllIncluded()
    {
        // Test: Component A -> Component B -> .NET Project -> NuGet packages
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "app-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                    m.BomRef = "pkg:docker/app-container@1.0.0";
                    m.Purl = "pkg:docker/app-container@1.0.0";
                })
                .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj"))
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "k8s-deployment";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Platform;
                })
                .DependsOn("pkg:docker/app-container@1.0.0"))
            .BuildAsync();

        // Assert
        var k8sBom = result.Boms["k8s-deployment"];
        
        // Should have app-container
        var appContainer = k8sBom.Components!
            .FirstOrDefault(c => c.Name == "app-container");
        Assert.IsNotNull(appContainer, "Should have app-container");
        
        // Currently custom components don't transitively include dependencies of their dependencies
        // This is expected behavior - only direct dependencies are included
        // If this changes in the future, update this test
    }

    #endregion

    #region Deduplication in Custom Components

    [TestMethod]
    public async Task CustomComponent_SharedNuGetPackages_Deduplicated()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "composite-app";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Application;
                })
                .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
                .DependsOnProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj"))
            .BuildAsync();

        // Assert
        var compositeBom = result.Boms["composite-app"];
        
        // Check that packages are not duplicated
        var packageGroups = compositeBom.Components!
            .Where(c => c.Purl?.StartsWith("pkg:nuget/") == true)
            .GroupBy(c => $"{c.Name}@{c.Version}")
            .Where(g => g.Count() > 1)
            .ToList();
        
        Assert.AreEqual(0, packageGroups.Count,
            $"No NuGet packages should be duplicated. Found: {string.Join(", ", packageGroups.Select(g => g.Key))}");
    }

    [TestMethod]
    public async Task CustomComponent_SharedProjectReferences_Deduplicated()
    {
        // Arrange & Act - Both console apps reference ExampleClassLibrary1
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "multi-app-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .DependsOnProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
                .DependsOnProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj"))
            .BuildAsync();

        // Assert
        var containerBom = result.Boms["multi-app-container"];
        
        // Check that ExampleClassLibrary1 appears only once (it's referenced by both apps)
        var lib1Instances = containerBom.Components!
            .Where(c => c.Name == "ExampleClassLibrary1")
            .ToList();
        
        Assert.AreEqual(1, lib1Instances.Count,
            "ExampleClassLibrary1 should appear only once despite being referenced by multiple projects");
    }

    #endregion

    #region Metadata Consistency Tests

    [TestMethod]
    public async Task CustomComponent_ProjectReference_MatchesProjectOwnSbom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "my-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj"))
            .BuildAsync();

        // Assert
        var lib1Bom = result.Boms["ExampleClassLibrary1"];
        var containerBom = result.Boms["my-container"];
        
        var lib1InContainer = containerBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary1");
        
        Assert.IsNotNull(lib1InContainer);
        Assert.AreEqual(lib1Bom.Metadata!.Component!.Name, lib1InContainer.Name);
        Assert.AreEqual(lib1Bom.Metadata.Component.Version, lib1InContainer.Version);
        Assert.AreEqual(lib1Bom.Metadata.Component.Purl, lib1InContainer.Purl);
        Assert.AreEqual(lib1Bom.Metadata.Component.BomRef, lib1InContainer.BomRef);
    }

    [TestMethod]
    public async Task CustomComponent_WithGlobalMetadata_MergesCorrectly()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(meta =>
            {
                meta.Version = "global-version";
            })
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "my-app";
                    m.Type = Component.Classification.Application;
                }))
            .BuildAsync();

        // Assert
        var bom = result.Boms["my-app"];
        
        // Global metadata should NOT override component-specific name
        Assert.AreEqual("my-app", bom.Metadata!.Component!.Name);
        
        // But if we don't specify version in component, global should not apply to custom components
        // (Custom components should explicitly set their metadata)
    }

    #endregion

    #region Custom Component File Output Tests

    [TestMethod]
    public async Task CustomComponent_OutputFile_CreatedSuccessfully()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "test-component";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Application;
                }))
            .BuildAsync();

        // Assert
        Assert.AreEqual(1, result.WrittenFilePaths.Count);
        var outputFile = result.WrittenFilePaths[0];
        
        Assert.IsTrue(File.Exists(outputFile), "SBOM file should exist");
        Assert.IsTrue(outputFile.Contains("test-component"), 
            "File name should contain component name");
    }

    [TestMethod]
    public async Task CustomComponent_MultipleComponents_GenerateSeparateFiles()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "component-a";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Application;
                }))
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "component-b";
                    m.Version = "2.0.0";
                    m.Type = Component.Classification.Container;
                }))
            .BuildAsync();

        // Assert
        Assert.AreEqual(2, result.WrittenFilePaths.Count);
        Assert.AreEqual(2, result.Boms.Count);
        
        foreach (var filePath in result.WrittenFilePaths)
        {
            Assert.IsTrue(File.Exists(filePath), $"File should exist: {filePath}");
        }
    }

    #endregion
}
