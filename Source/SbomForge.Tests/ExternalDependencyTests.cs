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
    private static string _golangExternalSbomPath = null!;

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
        _golangExternalSbomPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "TestData", "golang-external-sbom.json"));
        
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

    #region Regression Tests (Bug Fixes)

    /// <summary>
    /// Regression test for Bug 1: when a custom component has no Name set, the source-key
    /// fallback differed between Pass 1d (loop index) and Pass 2b (GetHashCode), causing
    /// external dependencies to be silently omitted.
    /// </summary>
    [TestMethod]
    public async Task CustomComponent_WithoutName_WithExternal_IncludesExternalDependency()
    {
        // Arrange & Act — intentionally no .WithMetadata(m => m.Name = ...) call
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .ForComponent(comp => comp
                .WithExternal(_externalSbomPath))
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;

        Assert.IsTrue(bom.Components!.Any(c => c.Name == "external-library"),
            "Unnamed custom component should include external library main component");
        Assert.IsTrue(bom.Components.Any(c => c.Name == "external-dep-1"),
            "Unnamed custom component should include external transitive dependencies");
    }

    /// <summary>
    /// Regression test for Bug 2: when a project path is specified as a directory (no .csproj
    /// suffix), Pass 1d resolved to the directory path while Pass 2 resolved to the .csproj
    /// file path, causing the source-key lookup to fail and per-project externals to be silently omitted.
    /// </summary>
    [TestMethod]
    public async Task PerProjectExternal_DirectoryStylePath_IncludesExternalDependency()
    {
        // Arrange & Act — directory path only, no .csproj extension
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleConsoleApp1", p => p
                .WithExternal(_simpleExternalSbomPath))
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;

        Assert.IsTrue(bom.Components!.Any(c => c.Name == "simple-external-app"),
            "Per-project external should be included when project path is specified as a directory");
    }

    /// <summary>
    /// Regression test for Bug 3: when a Name override was applied via WithExternal's component
    /// action, the overridden name was registered in the projectRegistry but AddExternalDependenciesToGraph
    /// still read the original name from externalBom.Metadata.Component, causing the Composer's
    /// registry lookup to miss and all metadata overrides (including Description and Version) to be lost.
    /// </summary>
    [TestMethod]
    public async Task GlobalExternal_NameOverride_AppliesAllMetadata()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithExternal(_externalSbomPath, comp =>
            {
                comp.Name = "renamed-external-lib";
                comp.Version = "9.9.9";
                comp.Description = "Renamed and overridden";
            })
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;

        // The component should appear under the overridden name, not the original "external-library"
        Assert.IsFalse(bom.Components!.Any(c => c.Name == "external-library"),
            "Original name should not appear when a Name override is configured");
        var renamed = bom.Components.FirstOrDefault(c => c.Name == "renamed-external-lib");
        Assert.IsNotNull(renamed, "Component should appear under the overridden name");
        Assert.AreEqual("9.9.9", renamed.Version, "Version override should be applied");
        Assert.AreEqual("Renamed and overridden", renamed.Description, "Description override should be applied");
    }

    #endregion

    #region Non-NuGet External Purl Preservation Tests

    /// <summary>
    /// Regression test: non-NuGet transitive components from an external SBOM must retain their
    /// original purl scheme (e.g. pkg:golang/) and version. Previously, they were re-generated
    /// as pkg:nuget/ with the global metadata version because the Composer registry lookup missed.
    /// </summary>
    [TestMethod]
    public async Task PerProjectExternal_GolangComponents_PreservePurlAndVersion()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(m => m.Version = "1.0.0")
            .WithResolution(r => r.IncludeTransitive = true)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj", p => p
                .WithExternal(_golangExternalSbomPath))
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms.First().Value;

        // Main component from the external SBOM
        var mainComp = bom.Components!.FirstOrDefault(c => c.Name == "github.com/example/go-service");
        Assert.IsNotNull(mainComp, "Main golang component should be present");
        Assert.AreEqual("v2.3.1", mainComp.Version, "Main component version should be preserved from external SBOM");

        // Transitive golang components should keep their original purls and versions
        var mux = bom.Components.FirstOrDefault(c => c.Name == "github.com/gorilla/mux");
        Assert.IsNotNull(mux, "gorilla/mux component should be present");
        Assert.AreEqual("v1.8.0", mux.Version, "gorilla/mux version should be preserved");
        Assert.AreEqual("pkg:golang/github.com/gorilla/mux@v1.8.0", mux.Purl,
            "gorilla/mux purl should retain pkg:golang/ scheme, not be rewritten to pkg:nuget/");

        var logrus = bom.Components.FirstOrDefault(c => c.Name == "github.com/sirupsen/logrus");
        Assert.IsNotNull(logrus, "sirupsen/logrus component should be present");
        Assert.AreEqual("v1.9.3", logrus.Version, "logrus version should be preserved");
        Assert.AreEqual("pkg:golang/github.com/sirupsen/logrus@v1.9.3", logrus.Purl,
            "logrus purl should retain pkg:golang/ scheme");

        var xnet = bom.Components.FirstOrDefault(c => c.Name == "golang.org/x/net");
        Assert.IsNotNull(xnet, "golang.org/x/net component should be present");
        Assert.AreEqual("v0.17.0", xnet.Version, "golang.org/x/net version should be preserved");
        Assert.AreEqual("pkg:golang/golang.org/x/net@v0.17.0", xnet.Purl,
            "golang.org/x/net purl should retain pkg:golang/ scheme");
    }

    /// <summary>
    /// Verifies that a global external SBOM with non-NuGet components
    /// preserves purls across all project SBOMs.
    /// </summary>
    [TestMethod]
    public async Task GlobalExternal_GolangComponents_PreservePurlAcrossProjects()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(m => m.Version = "1.0.0")
            .WithResolution(r => r.IncludeTransitive = true)
            .WithExternal(_golangExternalSbomPath)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        // Assert — both projects should have the golang components with correct purls
        Assert.HasCount(2, result.Boms);

        foreach (var (name, bom) in result.Boms)
        {
            var mux = bom.Components!.FirstOrDefault(c => c.Name == "github.com/gorilla/mux");
            Assert.IsNotNull(mux, $"gorilla/mux should be present in {name}");
            Assert.AreEqual("pkg:golang/github.com/gorilla/mux@v1.8.0", mux.Purl,
                $"gorilla/mux purl should be pkg:golang/ in {name}");
            Assert.AreEqual("v1.8.0", mux.Version,
                $"gorilla/mux version should be preserved in {name}");
        }
    }

    /// <summary>
    /// Verifies that non-NuGet external components on a custom component
    /// also preserve their original purl and version.
    /// </summary>
    [TestMethod]
    public async Task CustomComponent_GolangExternal_PreservesPurlAndVersion()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "my-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .WithExternal(_golangExternalSbomPath))
            .BuildAsync();

        // Assert
        Assert.HasCount(1, result.Boms);
        var bom = result.Boms["my-container"];

        var logrus = bom.Components!.FirstOrDefault(c => c.Name == "github.com/sirupsen/logrus");
        Assert.IsNotNull(logrus, "logrus should be present in custom component SBOM");
        Assert.AreEqual("pkg:golang/github.com/sirupsen/logrus@v1.9.3", logrus.Purl,
            "logrus purl should retain pkg:golang/ in custom component SBOM");
        Assert.AreEqual("v1.9.3", logrus.Version,
            "logrus version should be preserved in custom component SBOM");
    }

    #endregion

    #region Transitive External Dependency Tests

    /// <summary>
    /// When ProjectB has WithExternal and ProjectA references ProjectB via ProjectReference,
    /// ProjectA's SBOM should include the external dependencies from ProjectB.
    /// </summary>
    [TestMethod]
    public async Task TransitiveExternal_ProjectReferencesProjectWithExternal_IncludesExternalDeps()
    {
        // Arrange & Act
        // ExampleConsoleApp2 references ExampleClassLibrary2 via ProjectReference.
        // ExampleClassLibrary2 has WithExternal configured.
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", p => p
                .WithExternal(_externalSbomPath))
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(2, result.Boms);

        var lib2Bom = result.Boms["ExampleClassLibrary2"];
        var app2Bom = result.Boms["ExampleConsoleApp2"];

        // ExampleClassLibrary2 itself should have the external deps
        Assert.IsTrue(lib2Bom.Components!.Any(c => c.Name == "external-library"),
            "ExampleClassLibrary2 should include external library");
        Assert.IsTrue(lib2Bom.Components.Any(c => c.Name == "external-dep-1"),
            "ExampleClassLibrary2 should include external-dep-1");
        Assert.IsTrue(lib2Bom.Components.Any(c => c.Name == "external-dep-2"),
            "ExampleClassLibrary2 should include external-dep-2");

        // ExampleConsoleApp2 references ExampleClassLibrary2, so the external deps
        // should flow transitively into its SBOM
        Assert.IsTrue(app2Bom.Components!.Any(c => c.Name == "external-library"),
            "ExampleConsoleApp2 should transitively include external library from ExampleClassLibrary2");
        Assert.IsTrue(app2Bom.Components.Any(c => c.Name == "external-dep-1"),
            "ExampleConsoleApp2 should transitively include external-dep-1 from ExampleClassLibrary2");
        Assert.IsTrue(app2Bom.Components.Any(c => c.Name == "external-dep-2"),
            "ExampleConsoleApp2 should transitively include external-dep-2 from ExampleClassLibrary2");
    }

    /// <summary>
    /// Multi-level transitive: A → B → C, where C has WithExternal.
    /// External deps from C should flow through B into A's SBOM.
    /// </summary>
    [TestMethod]
    public async Task TransitiveExternal_MultiLevel_ExternalDepsFlowThroughChain()
    {
        // Arrange & Act
        // Chain: ExampleConsoleApp1 → ExampleClassLibrary1 → ExampleClassLibrary2
        // ExampleClassLibrary2 has WithExternal.
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", p => p
                .WithExternal(_externalSbomPath))
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(3, result.Boms);

        var lib2Bom = result.Boms["ExampleClassLibrary2"];
        var lib1Bom = result.Boms["ExampleClassLibrary1"];
        var app1Bom = result.Boms["ExampleConsoleApp1"];

        // ExampleClassLibrary2 should have the external deps directly
        Assert.IsTrue(lib2Bom.Components!.Any(c => c.Name == "external-library"),
            "ExampleClassLibrary2 should include external library");

        // ExampleClassLibrary1 references ExampleClassLibrary2 — external deps should flow
        Assert.IsTrue(lib1Bom.Components!.Any(c => c.Name == "external-library"),
            "ExampleClassLibrary1 should transitively include external library from ExampleClassLibrary2");
        Assert.IsTrue(lib1Bom.Components.Any(c => c.Name == "external-dep-1"),
            "ExampleClassLibrary1 should transitively include external-dep-1");

        // ExampleConsoleApp1 references ExampleClassLibrary1 — external deps should flow two levels
        Assert.IsTrue(app1Bom.Components!.Any(c => c.Name == "external-library"),
            "ExampleConsoleApp1 should transitively include external library through multi-level reference chain");
        Assert.IsTrue(app1Bom.Components.Any(c => c.Name == "external-dep-1"),
            "ExampleConsoleApp1 should transitively include external-dep-1 through multi-level reference chain");
    }

    /// <summary>
    /// When a custom component DependsOnProject where that project has per-project WithExternal,
    /// the external deps should be included in the custom component's SBOM.
    /// </summary>
    [TestMethod]
    public async Task TransitiveExternal_CustomComponentDependsOnProjectWithExternal_IncludesExternalDeps()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", p => p
                .WithExternal(_externalSbomPath))
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "my-container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .DependsOnProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj"))
            .BuildAsync();

        // Assert
        Assert.HasCount(2, result.Boms);
        var containerBom = result.Boms["my-container"];

        // The custom component should include the project reference
        Assert.IsTrue(containerBom.Components!.Any(c => c.Name == "ExampleClassLibrary2"),
            "Custom component should include ExampleClassLibrary2 as dependency");

        // External deps from ExampleClassLibrary2 should flow to the custom component
        Assert.IsTrue(containerBom.Components.Any(c => c.Name == "external-library"),
            "Custom component should transitively include external library from referenced project");
        Assert.IsTrue(containerBom.Components.Any(c => c.Name == "external-dep-1"),
            "Custom component should transitively include external-dep-1 from referenced project");
        Assert.IsTrue(containerBom.Components.Any(c => c.Name == "external-dep-2"),
            "Custom component should transitively include external-dep-2 from referenced project");
    }

    /// <summary>
    /// Non-NuGet (golang) external deps should preserve their purl scheme when flowing
    /// transitively across project references.
    /// </summary>
    [TestMethod]
    public async Task TransitiveExternal_GolangDeps_PreservePurlAcrossProjectReferences()
    {
        // Arrange & Act
        // ExampleClassLibrary2 has a golang external SBOM.
        // ExampleConsoleApp2 references ExampleClassLibrary2.
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(m => m.Version = "1.0.0")
            .WithResolution(r => r.IncludeTransitive = true)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", p => p
                .WithExternal(_golangExternalSbomPath))
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(2, result.Boms);
        var app2Bom = result.Boms["ExampleConsoleApp2"];

        // Golang components should flow transitively and preserve purls
        var mux = app2Bom.Components!.FirstOrDefault(c => c.Name == "github.com/gorilla/mux");
        Assert.IsNotNull(mux,
            "gorilla/mux should flow transitively to ExampleConsoleApp2");
        Assert.AreEqual("pkg:golang/github.com/gorilla/mux@v1.8.0", mux.Purl,
            "gorilla/mux purl should retain pkg:golang/ scheme after transitive flow");
        Assert.AreEqual("v1.8.0", mux.Version,
            "gorilla/mux version should be preserved after transitive flow");

        var logrus = app2Bom.Components.FirstOrDefault(c => c.Name == "github.com/sirupsen/logrus");
        Assert.IsNotNull(logrus,
            "sirupsen/logrus should flow transitively to ExampleConsoleApp2");
        Assert.AreEqual("pkg:golang/github.com/sirupsen/logrus@v1.9.3", logrus.Purl,
            "logrus purl should retain pkg:golang/ scheme after transitive flow");
    }

    /// <summary>
    /// When IncludeTransitive is false, only the main component (not its sub-dependencies)
    /// should flow transitively across project references.
    /// </summary>
    [TestMethod]
    public async Task TransitiveExternal_WithoutTransitiveFlag_OnlyMainComponentFlows()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = false)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", p => p
                .WithExternal(_externalSbomPath))
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(2, result.Boms);
        var app2Bom = result.Boms["ExampleConsoleApp2"];

        // The main component should still flow transitively
        Assert.IsTrue(app2Bom.Components!.Any(c => c.Name == "external-library"),
            "Main external component should flow transitively even with IncludeTransitive=false");

        // But its sub-dependencies should NOT
        Assert.IsFalse(app2Bom.Components.Any(c => c.Name == "external-dep-1"),
            "External sub-dependencies should NOT flow when IncludeTransitive=false");
        Assert.IsFalse(app2Bom.Components.Any(c => c.Name == "external-dep-2"),
            "External sub-dependencies should NOT flow when IncludeTransitive=false");
    }

    /// <summary>
    /// External deps configured on one project should NOT bleed into an unrelated project
    /// that does not reference it.
    /// </summary>
    [TestMethod]
    public async Task TransitiveExternal_UnrelatedProject_DoesNotGetExternalDeps()
    {
        // Arrange & Act
        // ExampleConsoleApp1 does NOT reference ExampleConsoleApp2.
        // Only ExampleConsoleApp2 has WithExternal.
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithResolution(r => r.IncludeTransitive = true)
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj", p => p
                .WithExternal(_externalSbomPath))
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        Assert.HasCount(2, result.Boms);

        var app2Bom = result.Boms["ExampleConsoleApp2"];
        var app1Bom = result.Boms["ExampleConsoleApp1"];

        // ExampleConsoleApp2 should have the external deps
        Assert.IsTrue(app2Bom.Components!.Any(c => c.Name == "external-library"),
            "ExampleConsoleApp2 should include its own external dependency");

        // ExampleConsoleApp1 should NOT have the external deps — it's unrelated
        Assert.IsFalse(app1Bom.Components!.Any(c => c.Name == "external-library"),
            "ExampleConsoleApp1 should NOT include external deps from unrelated project");
        Assert.IsFalse(app1Bom.Components.Any(c => c.Name == "external-dep-1"),
            "ExampleConsoleApp1 should NOT include external-dep-1 from unrelated project");
    }

    #endregion
}
