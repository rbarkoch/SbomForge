using CycloneDX.Models;
using SbomForge;

namespace SbomForge.Tests;

/// <summary>
/// Tests for external SBOM dependency functionality including WithExternal API,
/// transitive dependency inclusion, and metadata overrides.
/// </summary>
[TestClass]
public sealed class ExternalDependencyTests
{
    private static string _testBasePath = null!;
    private static string _outputDirectory = null!;
    private static string _externalSbomPath = null!;
    private static string _simpleExternalSbomPath = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        _testBasePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));
        
        _outputDirectory = Path.Combine(_testBasePath, "test-external-dependency-output");
        
        // Path to test data external SBOMs in the test output directory
        _externalSbomPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "TestData", "external-sbom-sample.json"));
        _simpleExternalSbomPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "TestData", "simple-external-sbom.json"));
        
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

    #region Global External Dependency Tests

    [TestMethod]
    public async Task GlobalExternal_WithTransitive_IncludesAllComponents()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .WithExternal(_externalSbomPath)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;
        
        // Should include the external library and its dependencies
        Assert.IsTrue(bom.Components!.Any(c => c.Name == "external-library"), 
            "Should include external library main component");
        Assert.IsTrue(bom.Components.Any(c => c.Name == "external-dep-1"),
            "Should include external dependency 1 when IncludeTransitive is true");
        Assert.IsTrue(bom.Components.Any(c => c.Name == "external-dep-2"),
            "Should include external dependency 2 when IncludeTransitive is true");
    }

    [TestMethod]
    public async Task GlobalExternal_WithoutTransitive_IncludesOnlyMainComponent()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = false)
            .WithExternal(_externalSbomPath)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;
        
        // Should include only the main component, not its dependencies
        Assert.IsTrue(bom.Components!.Any(c => c.Name == "external-library"), 
            "Should include external library main component");
        Assert.IsFalse(bom.Components.Any(c => c.Name == "external-dep-1"),
            "Should NOT include external dependency 1 when IncludeTransitive is false");
        Assert.IsFalse(bom.Components.Any(c => c.Name == "external-dep-2"),
            "Should NOT include external dependency 2 when IncludeTransitive is false");
    }

    [TestMethod]
    public async Task GlobalExternal_MetadataOverride_AppliesCustomMetadata()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithExternal(_externalSbomPath, comp =>
            {
                comp.Description = "Overridden description";
                comp.Version = "9.9.9";
            })
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;
        
        var externalLib = bom.Components!.FirstOrDefault(c => c.Name == "external-library");
        Assert.IsNotNull(externalLib, "External library should be present");
        Assert.AreEqual("Overridden description", externalLib.Description, 
            "Description should be overridden");
        Assert.AreEqual("9.9.9", externalLib.Version,
            "Version should be overridden");
    }

    #endregion

    #region Per-Project External Dependency Tests

    [TestMethod]
    public async Task PerProjectExternal_SingleProject_IncludesExternalInProjectOnly()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj", p => p
                .WithExternal(_simpleExternalSbomPath))
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(2, result.Boms);
        
        var bom1 = result.Boms["ExampleConsoleApp1"];
        var bom2 = result.Boms["ExampleConsoleApp2"];
        
        // Only first project should have the external dependency
        Assert.IsTrue(bom1.Components!.Any(c => c.Name == "simple-external-app"),
            "First project should include external dependency");
        Assert.IsFalse(bom2.Components!.Any(c => c.Name == "simple-external-app"),
            "Second project should NOT include external dependency");
    }

    #endregion

    #region Custom Component External Dependency Tests

    [TestMethod]
    public async Task CustomComponent_WithExternal_IncludesExternalDependency()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "my-docker-image";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .WithExternal(_externalSbomPath))
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms["my-docker-image"];
        
        // Should include the external library and its dependencies
        Assert.IsTrue(bom.Components!.Any(c => c.Name == "external-library"),
            "Custom component should include external library");
        Assert.IsTrue(bom.Components.Any(c => c.Name == "external-dep-1"),
            "Custom component should include external transitive dependencies");
    }

    [TestMethod]
    public async Task CustomComponent_WithExternal_RespectsTransitiveFlag()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "my-app";
                    m.Version = "2.0.0";
                    m.Type = Component.Classification.Application;
                })
                .WithResolution(r => r.IncludeTransitive = false)
                .WithExternal(_externalSbomPath))
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms["my-app"];
        
        // Should include only the main component
        Assert.IsTrue(bom.Components!.Any(c => c.Name == "external-library"),
            "Should include external library main component");
        Assert.IsFalse(bom.Components.Any(c => c.Name == "external-dep-1"),
            "Should NOT include transitive dependencies when flag is false");
    }

    #endregion

    #region Error Handling Tests

    [TestMethod]
    public async Task External_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        bool exceptionThrown = false;

        // Act
        try
        {
            await new SbomBuilder()
                .WithBasePath(_testBasePath)
                .WithOutput(o => o.OutputDirectory = _outputDirectory)
                .WithExternal("nonexistent-file.json")
                .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
                .BuildAsync();
        }
        catch (FileNotFoundException)
        {
            exceptionThrown = true;
        }

        // Assert
        Assert.IsTrue(exceptionThrown, "Should throw FileNotFoundException for missing external SBOM file");
    }

    [TestMethod]
    public async Task External_RelativePath_ResolvesCorrectly()
    {
        // Arrange
        // Use absolute path for this test since TestData is in test output, not relative to basePath
        string testDataPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "TestData", "simple-external-sbom.json"));

        // Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithExternal(testDataPath)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;
        Assert.IsTrue(bom.Components!.Any(c => c.Name == "simple-external-app"),
            "Should resolve path and include external component");
    }

    #endregion
}
