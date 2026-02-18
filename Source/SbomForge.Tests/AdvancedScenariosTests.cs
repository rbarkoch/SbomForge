using CycloneDX;
using CycloneDX.Models;
using SbomForge;

namespace SbomForge.Tests;

/// <summary>
/// Advanced tests for configuration merging, metadata detection,
/// and complex scenarios involving multiple features together.
/// </summary>
[TestClass]
public sealed class AdvancedScenariosTests
{
    private static string _testBasePath = null!;
    private static string _outputDirectory = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        _testBasePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));
        
        _outputDirectory = Path.Combine(_testBasePath, "test-advanced-output");
        
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

    #region Configuration Merging Tests

    [TestMethod]
    public async Task ConfigMerge_GlobalAndProjectFilters_BothApplied()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithFilters(f => f.ExcludePackagePrefixes = ["Microsoft.Extensions"])
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj", config => config
                .WithFilters(f => f.ExcludePackageIds = ["System.Text.Json"]))
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        // Should exclude packages matching global prefix filter
        var microsoftExtensions = bom.Components!
            .Where(c => c.Name?.StartsWith("Microsoft.Extensions") == true)
            .ToList();
        Assert.IsEmpty(microsoftExtensions, 
            "Global prefix filter should be applied");
        
        // Should also exclude package matching project-specific filter
        var systemTextJson = bom.Components!
            .FirstOrDefault(c => c.Name == "System.Text.Json");
        Assert.IsNull(systemTextJson, "Project-specific filter should be applied");
    }

    [TestMethod]
    public async Task ConfigMerge_OutputConfiguration_PerProjectOverridesGlobal()
    {
        // Arrange
        var globalOutput = Path.Combine(_outputDirectory, "global");
        var project1Output = Path.Combine(_outputDirectory, "project1");
        
        // Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o =>
            {
                o.OutputDirectory = globalOutput;
                o.FileNameTemplate = "global-{ProjectName}.json";
            })
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj", config => config
                .WithOutput(o =>
                {
                    o.OutputDirectory = project1Output;
                    o.FileNameTemplate = "custom-{ProjectName}.json";
                }))
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        // Assert
        var lib1File = result.WrittenFilePaths[0];
        var lib2File = result.WrittenFilePaths[1];
        
        // ExampleClassLibrary1 should use project-specific output
        Assert.StartsWith(project1Output, lib1File, 
            "Project1 should use custom output directory");
        Assert.Contains("custom-ExampleClassLibrary1", lib1File, 
            "Project1 should use custom file name template");
        
        // ExampleClassLibrary2 should use global output
        Assert.StartsWith(globalOutput, lib2File, 
            "Project2 should use global output directory");
        Assert.Contains("global-ExampleClassLibrary2", lib2File, 
            "Project2 should use global file name template");
        
        // Cleanup
        if (Directory.Exists(globalOutput)) Directory.Delete(globalOutput, true);
        if (Directory.Exists(project1Output)) Directory.Delete(project1Output, true);
    }

    [TestMethod]
    public async Task ConfigMerge_MetadataFields_LastWins()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(meta =>
            {
                meta.Version = "1.0.0";
                meta.Description = "Global description";
                meta.Publisher = "Global Publisher";
            })
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj", config => config
                .WithMetadata(meta =>
                {
                    meta.Version = "2.0.0";
                    meta.Description = "Project-specific description";
                    // Publisher not set, should use global
                }))
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var component = bom.Metadata!.Component!;
        
        Assert.AreEqual("2.0.0", component.Version, "Project-specific version should override");
        Assert.AreEqual("Project-specific description", component.Description, 
            "Project-specific description should override");
        Assert.AreEqual("Global Publisher", component.Publisher, 
            "Global publisher should be used when not overridden");
    }

    #endregion

    #region Metadata Auto-Detection Tests

    [TestMethod]
    public async Task MetadataDetection_FromCsproj_AutoDetected()
    {
        // The example projects have various metadata in their .csproj files
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var component = bom.Metadata!.Component!;
        
        Assert.IsNotNull(component.Name, "Name should be auto-detected");
        Assert.IsNotNull(component.Version, "Version should be auto-detected");
        Assert.AreEqual("ExampleClassLibrary1", component.Name, 
            "Name should match project name");
    }

    [TestMethod]
    public async Task MetadataDetection_UserProvidedOverridesAutoDetected()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj", config => config
                .WithMetadata(meta =>
                {
                    meta.Name = "CustomName";
                    meta.Version = "99.0.0";
                }))
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var component = bom.Metadata!.Component!;
        
        Assert.AreEqual("CustomName", component.Name, 
            "User-provided name should override auto-detected");
        Assert.AreEqual("99.0.0", component.Version, 
            "User-provided version should override auto-detected");
    }

    #endregion

    #region Complex Multi-Feature Tests

    [TestMethod]
    public async Task Complex_FilteredProjectsWithCustomComponents_WorksTogether()
    {
        // This tests filtering + custom components + cross-SBOM consistency all together
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithFilters(f => f.ExcludePackagePrefixes = ["Microsoft.Extensions"])
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForComponent(comp => comp
                .WithMetadata(m =>
                {
                    m.Name = "container";
                    m.Version = "1.0.0";
                    m.Type = Component.Classification.Container;
                })
                .DependsOnProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
                .WithComponent(c =>
                {
                    c.Name = "redis";
                    c.Version = "7.0";
                    c.Type = Component.Classification.Container;
                }))
            .BuildAsync();

        // Assert
        #pragma warning disable MSTEST0037
        Assert.AreEqual(3, result.Boms.Count, "Should generate 3 SBOMs");
        #pragma warning restore MSTEST0037
        
        var lib1Bom = result.Boms["ExampleClassLibrary1"];
        var app1Bom = result.Boms["ExampleConsoleApp1"];
        var containerBom = result.Boms["container"];
        
        // Verify filtering was applied to .NET projects
        var microsoftExtInLib1 = lib1Bom.Components!
            .Any(c => c.Name?.StartsWith("Microsoft.Extensions") == true);
        Assert.IsFalse(microsoftExtInLib1, "Filtering should apply to library");
        
        var microsoftExtInApp1 = app1Bom.Components!
            .Any(c => c.Name?.StartsWith("Microsoft.Extensions") == true);
        Assert.IsFalse(microsoftExtInApp1, "Filtering should apply to app");
        
        // Verify custom component has its dependencies
        var redis = containerBom.Components!.FirstOrDefault(c => c.Name == "redis");
        Assert.IsNotNull(redis, "Custom component should be included");
        
        var app1InContainer = containerBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleConsoleApp1");
        Assert.IsNotNull(app1InContainer, "Project reference should be included");
        
        // Verify filtering was also applied to transitive dependencies in custom component
        var microsoftExtInContainer = containerBom.Components!
            .Any(c => c.Name?.StartsWith("Microsoft.Extensions") == true);
        Assert.IsFalse(microsoftExtInContainer, 
            "Filtering should apply to transitive dependencies in custom components");
    }

    [TestMethod]
    public async Task Complex_MultiLevelDependenciesWithFiltering_CorrectGraph()
    {
        // Test: App1 -> Lib1 -> Lib2 with filtering at different levels
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithFilters(f => f.ExcludePackagePrefixes = ["System."])
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", config => config
                .WithFilters(f => f.ExcludePackagePrefixes = ["Microsoft."]))
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        var app1Bom = result.Boms["ExampleConsoleApp1"];
        var lib1Bom = result.Boms["ExampleClassLibrary1"];
        var lib2Bom = result.Boms["ExampleClassLibrary2"];
        
        // Verify cross-project references still work with filtering
        var lib1InApp = app1Bom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary1");
        Assert.IsNotNull(lib1InApp, "Project references should work with filtering");
        
        var lib2InLib1 = lib1Bom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary2");
        Assert.IsNotNull(lib2InLib1, "Nested project references should work");
        
        // Verify filters are applied correctly at each level
        var systemPackagesInLib2 = lib2Bom.Components!
            .Where(c => c.Name?.StartsWith("System.") == true)
            .ToList();
        Assert.IsEmpty(systemPackagesInLib2, 
            "Global filter should apply to Lib2");
        
        var microsoftPackagesInLib2 = lib2Bom.Components!
            .Where(c => c.Name?.StartsWith("Microsoft.") == true)
            .ToList();
        Assert.IsEmpty(microsoftPackagesInLib2, 
            "Project-specific filter should also apply to Lib2");
    }

    #endregion

    #region Dependency Graph Validation Tests

    [TestMethod]
    public async Task DependencyGraph_TransitiveClosureCorrect()
    {
        // Verify that the dependency graph correctly represents all transitive relationships
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleConsoleApp1"];
        
        // Build a lookup of all dependency relationships
        var depLookup = bom.Dependencies!.ToDictionary(d => d.Ref!, d => d);
        
        // Root should be the main component
        var root = depLookup[bom.Metadata!.Component!.BomRef!];
        Assert.IsNotNull(root, "Should have root dependency");
        
        // All components should be reachable from root
        var reachable = new HashSet<string>();
        void Visit(Dependency dep)
        {
            if (!reachable.Add(dep.Ref!)) return;
            if (dep.Dependencies != null)
            {
                foreach (var child in dep.Dependencies)
                {
                    if (depLookup.TryGetValue(child.Ref!, out var childDep))
                    {
                        Visit(childDep);
                    }
                }
            }
        }
        
        Visit(root);
        
        // All components (except root) should be reachable
        foreach (var component in bom.Components!)
        {
            if (component.BomRef != bom.Metadata.Component.BomRef)
            {
                Assert.IsTrue(reachable.Contains(component.BomRef!) || 
                    depLookup.ContainsKey(component.BomRef!),
                    $"Component {component.Name} should be in dependency graph");
            }
        }
    }

    [TestMethod]
    public async Task DependencyGraph_NoCycles_InDirectDependencies()
    {
        // While NuGet doesn't allow cycles, verify our graph doesn't have any
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var depLookup = bom.Dependencies!.ToDictionary(d => d.Ref!, d => d);
        
        // Check for cycles using DFS
        bool HasCycle(string nodeRef, HashSet<string> visited, HashSet<string> recursionStack)
        {
            if (recursionStack.Contains(nodeRef))
                return true;
            
            if (visited.Contains(nodeRef))
                return false;
            
            visited.Add(nodeRef);
            recursionStack.Add(nodeRef);
            
            if (depLookup.TryGetValue(nodeRef, out var node) && node.Dependencies != null)
            {
                foreach (var child in node.Dependencies)
                {
                    if (HasCycle(child.Ref!, visited, recursionStack))
                        return true;
                }
            }
            
            recursionStack.Remove(nodeRef);
            return false;
        }
        
        foreach (var dep in bom.Dependencies!)
        {
            Assert.IsFalse(HasCycle(dep.Ref!, new HashSet<string>(), new HashSet<string>()),
                $"Dependency graph should not have cycles (found at {dep.Ref})");
        }
    }

    #endregion

    #region BomRef and Purl Consistency Tests

    [TestMethod]
    public async Task BomRef_ConsistentWithPurl()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        // For components with PURL, BomRef should match PURL
        foreach (var component in bom.Components!)
        {
            if (!string.IsNullOrEmpty(component.Purl))
            {
                Assert.AreEqual(component.Purl, component.BomRef,
                    $"BomRef should match PURL for component {component.Name}");
            }
        }
        
        // Metadata component should also have consistent BomRef/Purl
        var metaComponent = bom.Metadata!.Component!;
        if (!string.IsNullOrEmpty(metaComponent.Purl))
        {
            Assert.AreEqual(metaComponent.Purl, metaComponent.BomRef,
                "Metadata component BomRef should match PURL");
        }
    }

    [TestMethod]
    public async Task Purl_CorrectFormat_ForDifferentComponentTypes()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj", config => config
                .WithComponent(c =>
                {
                    c.Name = "redis";
                    c.Version = "7.2";
                    c.Purl = "pkg:docker/redis@7.2";
                    c.Type = Component.Classification.Container;
                })
                .WithComponent(c =>
                {
                    c.Name = "react";
                    c.Version = "18.2.0";
                    c.Purl = "pkg:npm/react@18.2.0";
                    c.Type = Component.Classification.Library;
                }))
            .BuildAsync();

        // Assert
        var lib1Bom = result.Boms["ExampleClassLibrary1"];
        var app1Bom = result.Boms["ExampleConsoleApp1"];
        
        // NuGet packages should have pkg:nuget PURLs
        var nugetPackages = lib1Bom.Components!
            .Where(c => c.Purl?.StartsWith("pkg:nuget/") == true)
            .ToList();
        Assert.IsNotEmpty(nugetPackages, "Should have NuGet packages with pkg:nuget PURLs");
        
        // Custom Docker component should have pkg:docker PURL
        var redis = app1Bom.Components!.FirstOrDefault(c => c.Name == "redis");
        Assert.IsNotNull(redis);
        Assert.IsTrue(redis.Purl?.StartsWith("pkg:docker/"), 
            "Docker component should have pkg:docker PURL");
        
        // Custom npm component should have pkg:npm PURL
        var react = app1Bom.Components!.FirstOrDefault(c => c.Name == "react");
        Assert.IsNotNull(react);
        Assert.IsTrue(react.Purl?.StartsWith("pkg:npm/"), 
            "npm component should have pkg:npm PURL");
    }

    #endregion

    #region Tool Metadata Tests

    [TestMethod]
    public async Task ToolMetadata_IncludedInAllSboms()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        // Assert
        foreach (var bom in result.Boms.Values)
        {
            Assert.IsNotNull(bom.Metadata, "Metadata should exist");
            Assert.IsNotNull(bom.Metadata.Tools, "Tools should exist");
            
            var toolComponents = bom.Metadata.Tools.Components;
            Assert.IsNotNull(toolComponents, "Tool components should exist");
            Assert.IsNotEmpty(toolComponents, "Should have at least one tool");
            
            var sbomForge = toolComponents.FirstOrDefault(t => t.Name == "SbomForge");
            Assert.IsNotNull(sbomForge, "SbomForge should be listed as a tool");
            Assert.IsNotNull(sbomForge.Version, "Tool should have version");
        }
    }

    [TestMethod]
    public async Task ToolMetadata_CustomToolInfo_CanBeSet()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithTool(tool =>
            {
                tool.Version = "99.0.0-custom";
                tool.Publisher = "Custom Publisher";
            })
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var toolComponents = bom.Metadata!.Tools!.Components;
        var sbomForge = toolComponents!.FirstOrDefault(t => t.Name == "SbomForge");
        
        Assert.IsNotNull(sbomForge);
        Assert.AreEqual("99.0.0-custom", sbomForge.Version, 
            "Custom tool version should be used");
        Assert.AreEqual("Custom Publisher", sbomForge.Publisher, 
            "Custom tool publisher should be used");
    }

    #endregion
}
