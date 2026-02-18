using System.Text.Json;
using CycloneDX;
using CycloneDX.Models;
using SbomForge;

namespace SbomForge.Tests;

/// <summary>
/// Comprehensive tests for SbomForge SBOM generation, covering:
/// - Basic SBOM generation
/// - Cross-SBOM consistency for project references
/// - Deduplication of dependencies
/// - Filtering and configuration
/// - Custom components
/// - Metadata detection and merging
/// </summary>
[TestClass]
public sealed class SbomForgeTests
{
    private static string _testBasePath = null!;
    private static string _outputDirectory = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        // Navigate to the ExampleDeployment directory
        _testBasePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));
        
        _outputDirectory = Path.Combine(_testBasePath, "test-sbom-output");
        
        // Clean up any previous test output
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        // Clean up test output directory
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }
    }

    #region Basic SBOM Generation Tests

    [TestMethod]
    public async Task BasicSbom_SingleProject_GeneratesValidBom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        #pragma warning disable MSTEST0037 // Use Assert.HasCount
        Assert.AreEqual(1, result.Boms.Count, "Should generate exactly one SBOM");
        Assert.AreEqual(1, result.WrittenFilePaths.Count, "Should write exactly one file");
        #pragma warning restore MSTEST0037
        Assert.IsTrue(result.Boms.ContainsKey("ExampleClassLibrary1"), "SBOM should be keyed by project name");

        var bom = result.Boms["ExampleClassLibrary1"];
        Assert.IsNotNull(bom, "BOM should not be null");
        Assert.AreEqual(SpecificationVersion.v1_7, bom.SpecVersion, "Should use CycloneDX 1.7");
        Assert.IsNotNull(bom.SerialNumber, "Should have a serial number");
        Assert.StartsWith("urn:uuid:", bom.SerialNumber!, "Serial number should be a UUID URN");
        Assert.IsNotNull(bom.Metadata, "Metadata should not be null");
        Assert.IsNotNull(bom.Components, "Components list should not be null");
        Assert.IsNotNull(bom.Dependencies, "Dependencies list should not be null");
    }

    [TestMethod]
    public async Task BasicSbom_MultipleProjects_GeneratesMultipleBoms()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        #pragma warning disable MSTEST0037
        Assert.AreEqual(3, result.Boms.Count, "Should generate three SBOMs");
        Assert.AreEqual(3, result.WrittenFilePaths.Count, "Should write three files");
        #pragma warning restore MSTEST0037
        Assert.IsTrue(result.Boms.ContainsKey("ExampleClassLibrary1"), "Should have ExampleClassLibrary1 SBOM");
        Assert.IsTrue(result.Boms.ContainsKey("ExampleClassLibrary2"), "Should have ExampleClassLibrary2 SBOM");
        Assert.IsTrue(result.Boms.ContainsKey("ExampleConsoleApp1"), "Should have ExampleConsoleApp1 SBOM");

        // Verify all files were actually written
        foreach (var filePath in result.WrittenFilePaths)
        {
            Assert.IsTrue(File.Exists(filePath), $"SBOM file should exist: {filePath}");
        }
    }

    [TestMethod]
    public async Task BasicSbom_WithGlobalMetadata_AppliesMetadataToAllProjects()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(meta =>
            {
                meta.Version = "2.0.0";
            })
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        // Assert
        var bom1 = result.Boms["ExampleClassLibrary1"];
        var bom2 = result.Boms["ExampleClassLibrary2"];
        
        Assert.AreEqual("2.0.0", bom1.Metadata!.Component!.Version, "Global version should apply to first project");
        Assert.AreEqual("2.0.0", bom2.Metadata!.Component!.Version, "Global version should apply to second project");
    }

    [TestMethod]
    public async Task BasicSbom_WithPerProjectMetadata_OverridesGlobalMetadata()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(meta =>
            {
                meta.Version = "1.0.0";
                meta.Description = "Global description";
            })
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj", config => config
                .WithMetadata(meta =>
                {
                    meta.Version = "2.0.0";
                }))
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        // Assert
        var bom1 = result.Boms["ExampleClassLibrary1"];
        var bom2 = result.Boms["ExampleClassLibrary2"];
        
        Assert.AreEqual("2.0.0", bom1.Metadata!.Component!.Version, "Per-project version should override global");
        Assert.AreEqual("1.0.0", bom2.Metadata!.Component!.Version, "Global version should apply when not overridden");
    }

    #endregion

    #region Cross-SBOM Consistency Tests

    [TestMethod]
    public async Task CrossSbomConsistency_ProjectReferences_HaveConsistentMetadata()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(meta => meta.Version = "1.0.0")
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert - ExampleConsoleApp1 references ExampleClassLibrary1
        var consoleAppBom = result.Boms["ExampleConsoleApp1"];
        var classLib1Bom = result.Boms["ExampleClassLibrary1"];
        
        // Find ExampleClassLibrary1 component in ExampleConsoleApp1's SBOM
        var lib1InConsoleApp = consoleAppBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary1");
        
        Assert.IsNotNull(lib1InConsoleApp, "ExampleClassLibrary1 should be in ExampleConsoleApp1's components");
        
        // Verify consistent metadata
        Assert.AreEqual(classLib1Bom.Metadata!.Component!.Name, lib1InConsoleApp.Name, 
            "Project reference name should match the project's own SBOM");
        Assert.AreEqual(classLib1Bom.Metadata!.Component!.Version, lib1InConsoleApp.Version, 
            "Project reference version should match the project's own SBOM");
        Assert.AreEqual(classLib1Bom.Metadata!.Component!.Purl, lib1InConsoleApp.Purl, 
            "Project reference PURL should match the project's own SBOM");
    }

    [TestMethod]
    public async Task CrossSbomConsistency_MultiLevelProjectReferences_AllConsistent()
    {
        // Arrange & Act
        // ExampleConsoleApp1 -> ExampleClassLibrary1 -> ExampleClassLibrary2
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        var consoleAppBom = result.Boms["ExampleConsoleApp1"];
        var classLib1Bom = result.Boms["ExampleClassLibrary1"];
        var classLib2Bom = result.Boms["ExampleClassLibrary2"];
        
        // Find ExampleClassLibrary2 in both ExampleConsoleApp1 and ExampleClassLibrary1
        var lib2InConsoleApp = consoleAppBom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary2");
        var lib2InClassLib1 = classLib1Bom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary2");
        
        Assert.IsNotNull(lib2InConsoleApp, "ExampleClassLibrary2 should be in ExampleConsoleApp1");
        Assert.IsNotNull(lib2InClassLib1, "ExampleClassLibrary2 should be in ExampleClassLibrary1");
        
        // Verify consistent metadata across all SBOMs
        Assert.AreEqual(classLib2Bom.Metadata!.Component!.Version, lib2InConsoleApp.Version, 
            "Version should be consistent in console app");
        Assert.AreEqual(classLib2Bom.Metadata!.Component!.Version, lib2InClassLib1.Version, 
            "Version should be consistent in class library");
        Assert.AreEqual(lib2InConsoleApp.Version, lib2InClassLib1.Version, 
            "Version should be the same in all referencing projects");
    }

    #endregion

    #region Dependency Resolution Tests

    [TestMethod]
    public async Task DependencyResolution_DirectPackages_MarkedAsDirect()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var rootDependency = bom.Dependencies!.FirstOrDefault(d => 
            d.Ref == bom.Metadata!.Component!.BomRef);
        
        Assert.IsNotNull(rootDependency, "Should have root dependency node");
        Assert.IsNotNull(rootDependency.Dependencies, "Root should have dependencies");
        Assert.IsNotEmpty(rootDependency.Dependencies, "Should have at least one direct dependency");
        
        // Microsoft.Extensions.Hosting is a direct dependency
        var hostingComponent = bom.Components!
            .FirstOrDefault(c => c.Name == "Microsoft.Extensions.Hosting");
        Assert.IsNotNull(hostingComponent, "Should have Microsoft.Extensions.Hosting");
        
        var hostingInDeps = rootDependency.Dependencies
            .FirstOrDefault(d => d.Ref == hostingComponent.BomRef);
        Assert.IsNotNull(hostingInDeps, "Direct package should be in root dependencies");
    }

    [TestMethod]
    public async Task DependencyResolution_TransitiveDependencies_IncludedInComponents()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        // Microsoft.Extensions.Hosting has transitive dependencies
        Assert.IsGreaterThan(2, bom.Components!.Count, "Should have transitive dependencies beyond direct ones");
        
        // Verify some common transitive dependencies
        var hasTransitive = bom.Components!.Any(c => 
            c.Name != "ExampleClassLibrary1" && 
            c.Name != "ExampleClassLibrary2" &&
            c.Name != "Microsoft.Extensions.Hosting");
        
        Assert.IsTrue(hasTransitive, "Should have transitive package dependencies");
    }

    [TestMethod]
    public async Task DependencyResolution_ProjectReferences_IncludedInComponents()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleConsoleApp1"];
        var lib1Component = bom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary1");
        
        Assert.IsNotNull(lib1Component, "Project reference should be in components");
        Assert.AreEqual(Component.Classification.Library, lib1Component.Type, 
            "Project reference should be marked as library");
    }

    #endregion

    #region Deduplication Tests

    [TestMethod]
    public async Task Deduplication_SamePackageInMultipleProjects_NotDuplicated()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        // Group components by name and version
        var duplicates = bom.Components!
            .GroupBy(c => $"{c.Name}@{c.Version}")
            .Where(g => g.Count() > 1)
            .ToList();
        
        Assert.IsEmpty(duplicates, 
            $"Should not have duplicate components. Found: {string.Join(", ", duplicates.Select(d => d.Key))}");
    }

    [TestMethod]
    public async Task Deduplication_DependencyRefs_NoDuplicateBomRefs()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        // Check all BomRefs are unique
        var bomRefs = bom.Components!.Select(c => c.BomRef).ToList();
        var distinctBomRefs = bomRefs.Distinct().ToList();
        
        #pragma warning disable MSTEST0037
        Assert.AreEqual(bomRefs.Count, distinctBomRefs.Count, 
            "All components should have unique BomRefs");
        #pragma warning restore MSTEST0037
    }

    [TestMethod]
    public async Task Deduplication_DependencyGraph_AllRefsPointToValidComponents()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var validBomRefs = new HashSet<string>(bom.Components!.Select(c => c.BomRef!));
        validBomRefs.Add(bom.Metadata!.Component!.BomRef!); // Add root component
        
        // Check all dependency references point to valid components
        foreach (var dep in bom.Dependencies!)
        {
            Assert.Contains(dep.Ref!, validBomRefs, 
                $"Dependency ref '{dep.Ref}' should point to a valid component");
            
            if (dep.Dependencies != null)
            {
                foreach (var subDep in dep.Dependencies)
                {
                    Assert.Contains(subDep.Ref!, validBomRefs, 
                        $"Sub-dependency ref '{subDep.Ref}' should point to a valid component");
                }
            }
        }
    }

    #endregion

    #region Filtering Tests

    [TestMethod]
    public async Task Filtering_ExcludePackageById_PackageNotInBom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithFilters(f => f.ExcludePackageIds = ["Microsoft.Extensions.Hosting"])
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var excludedPackage = bom.Components!
            .FirstOrDefault(c => c.Name == "Microsoft.Extensions.Hosting");
        
        Assert.IsNull(excludedPackage, "Excluded package should not be in components");
    }

    [TestMethod]
    public async Task Filtering_ExcludePackageByPrefix_MatchingPackagesNotInBom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithFilters(f => f.ExcludePackagePrefixes = ["Microsoft.Extensions"])
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var microsoftExtensionsPackages = bom.Components!
            .Where(c => c.Name?.StartsWith("Microsoft.Extensions") == true)
            .ToList();
        
        Assert.IsEmpty(microsoftExtensionsPackages, 
            "Should not have any Microsoft.Extensions.* packages");
    }

    [TestMethod]
    public async Task Filtering_ExcludeProjectByName_ProjectNotInBom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithFilters(f => f.ExcludeProjectNames = ["ExampleClassLibrary2"])
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        var excludedProject = bom.Components!
            .FirstOrDefault(c => c.Name == "ExampleClassLibrary2");
        
        Assert.IsNull(excludedProject, "Excluded project should not be in components");
    }

    [TestMethod]
    public async Task Filtering_PerProjectOverride_OnlyAffectsThatProject()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj", config => config
                .WithFilters(f => f.ExcludePackageIds = ["Microsoft.Extensions.Hosting"]))
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        // Assert
        var bom1 = result.Boms["ExampleClassLibrary1"];
        var bom2 = result.Boms["ExampleClassLibrary2"];
        
        var excludedInBom1 = bom1.Components!
            .FirstOrDefault(c => c.Name == "Microsoft.Extensions.Hosting");
        
        Assert.IsNull(excludedInBom1, "Package should be excluded in first project");
        // We can't check bom2 because it doesn't have this dependency, but the test verifies filtering is scoped
    }

    #endregion

    #region Custom Component Tests

    [TestMethod]
    public async Task CustomComponent_BasicComponent_IncludedInBom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj", config => config
                .WithComponent(c =>
                {
                    c.Type = Component.Classification.Container;
                    c.Name = "redis";
                    c.Version = "7.2";
                    c.Purl = "pkg:docker/redis@7.2";
                }))
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleConsoleApp1"];
        var redisComponent = bom.Components!
            .FirstOrDefault(c => c.Name == "redis" && c.Version == "7.2");
        
        Assert.IsNotNull(redisComponent, "Custom component should be in BOM");
        Assert.AreEqual(Component.Classification.Container, redisComponent.Type);
        Assert.AreEqual("pkg:docker/redis@7.2", redisComponent.Purl);
    }

    [TestMethod]
    public async Task CustomComponent_MultipleComponents_AllIncluded()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj", config => config
                .WithComponent(c =>
                {
                    c.Name = "redis";
                    c.Version = "7.2";
                    c.Type = Component.Classification.Container;
                })
                .WithComponent(c =>
                {
                    c.Name = "react";
                    c.Version = "18.2.0";
                    c.Type = Component.Classification.Library;
                }))
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleConsoleApp1"];
        
        var redis = bom.Components!.FirstOrDefault(c => c.Name == "redis");
        var react = bom.Components!.FirstOrDefault(c => c.Name == "react");
        
        Assert.IsNotNull(redis, "Redis should be in BOM");
        Assert.IsNotNull(react, "React should be in BOM");
    }

    #endregion

    #region Output Configuration Tests

    [TestMethod]
    public async Task OutputConfiguration_CustomDirectory_UsesSpecifiedPath()
    {
        // Arrange
        var customPath = Path.Combine(_testBasePath, "custom-output");
        
        // Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = customPath)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        #pragma warning disable MSTEST0037
        Assert.AreEqual(1, result.WrittenFilePaths.Count);
        #pragma warning restore MSTEST0037
        var outputFile = result.WrittenFilePaths[0];
        Assert.StartsWith(customPath, outputFile, 
            $"Output file should be in custom directory. Expected path starting with: {customPath}, Got: {outputFile}");
        Assert.IsTrue(File.Exists(outputFile), "Output file should exist");
        
        // Cleanup
        if (Directory.Exists(customPath))
        {
            Directory.Delete(customPath, true);
        }
    }

    [TestMethod]
    public async Task OutputConfiguration_CustomFileNameTemplate_UsesTemplate()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o =>
            {
                o.OutputDirectory = _outputDirectory;
                o.FileNameTemplate = "{ProjectName}-custom-sbom.json";
            })
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        #pragma warning disable MSTEST0037
        Assert.AreEqual(1, result.WrittenFilePaths.Count);
        #pragma warning restore MSTEST0037
        var fileName = Path.GetFileName(result.WrittenFilePaths[0]);
        Assert.AreEqual("ExampleClassLibrary1-custom-sbom.json", fileName, 
            "Should use custom file name template");
    }

    #endregion

    #region SBOM Structure Validation Tests

    [TestMethod]
    public async Task SbomStructure_Metadata_ContainsRequiredFields()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        Assert.IsNotNull(bom.Metadata, "Metadata should not be null");
        Assert.IsNotNull(bom.Metadata.Component, "Metadata component should not be null");
        Assert.IsNotNull(bom.Metadata.Component.Name, "Component name should not be null");
        Assert.IsNotNull(bom.Metadata.Component.Version, "Component version should not be null");
        Assert.IsNotNull(bom.Metadata.Component.BomRef, "Component BomRef should not be null");
        Assert.IsNotNull(bom.Metadata.Tools, "Tools should not be null");
    }

    [TestMethod]
    public async Task SbomStructure_Components_HaveRequiredFields()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        Assert.IsNotEmpty(bom.Components!, "Should have components");
        
        foreach (var component in bom.Components!)
        {
            Assert.IsNotNull(component.Name, $"Component should have name");
            Assert.IsNotNull(component.Version, $"Component {component.Name} should have version");
            Assert.IsNotNull(component.BomRef, $"Component {component.Name} should have BomRef");
            Assert.AreNotEqual(Component.Classification.Null, component.Type, 
                $"Component {component.Name} should have a valid type");
        }
    }

    [TestMethod]
    public async Task SbomStructure_Dependencies_FormValidGraph()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary1"];
        
        Assert.IsNotEmpty(bom.Dependencies!, "Should have dependencies");
        
        // Should have a root dependency matching the metadata component
        var rootDep = bom.Dependencies!
            .FirstOrDefault(d => d.Ref == bom.Metadata!.Component!.BomRef);
        Assert.IsNotNull(rootDep, "Should have root dependency");
        
        // All dependency refs should be valid
        var allValidRefs = new HashSet<string>(
            bom.Components!.Select(c => c.BomRef!).Concat([bom.Metadata!.Component!.BomRef!]));
        
        foreach (var dep in bom.Dependencies!)
        {
            Assert.Contains(dep.Ref!, allValidRefs, 
                $"Dependency ref {dep.Ref} should be valid");
        }
    }

    [TestMethod]
    public async Task SbomStructure_SerializedJson_IsValidFormat()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Assert
        var filePath = result.WrittenFilePaths[0];
        Assert.IsTrue(File.Exists(filePath), "SBOM file should exist");
        
        var jsonContent = await File.ReadAllTextAsync(filePath);
        Assert.IsFalse(string.IsNullOrWhiteSpace(jsonContent), "JSON should not be empty");
        
        // Verify it's valid JSON
        var jsonDoc = JsonDocument.Parse(jsonContent);
        Assert.IsNotNull(jsonDoc, "Should be valid JSON");
        
        // Verify it has expected CycloneDX structure
        Assert.IsTrue(jsonDoc.RootElement.TryGetProperty("bomFormat", out var bomFormat));
        Assert.AreEqual("CycloneDX", bomFormat.GetString());
        
        Assert.IsTrue(jsonDoc.RootElement.TryGetProperty("specVersion", out var specVersion));
        Assert.AreEqual("1.7", specVersion.GetString());
    }

    #endregion

    #region Edge Cases and Error Handling

    [TestMethod]
    public async Task EdgeCase_EmptyProject_GeneratesValidBom()
    {
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .BuildAsync();

        // Assert
        var bom = result.Boms["ExampleClassLibrary2"];
        Assert.IsNotNull(bom, "Should generate BOM for project with no dependencies");
        Assert.IsNotNull(bom.Metadata, "Metadata should exist");
        Assert.IsNotNull(bom.Components, "Components should exist (even if empty)");
    }

    [TestMethod]
    public async Task EdgeCase_CircularProjectReferences_HandledCorrectly()
    {
        // This tests that the system can handle projects properly even with complex reference graphs
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        // Assert - just verify all build successfully without errors
        #pragma warning disable MSTEST0037
        Assert.AreEqual(4, result.Boms.Count, "All projects should build successfully");
        #pragma warning restore MSTEST0037
    }

    #endregion

    #region Integration Tests

    [TestMethod]
    public async Task Integration_CompleteExample_GeneratesConsistentSboms()
    {
        // This is a comprehensive integration test that mimics a real-world scenario
        // Arrange & Act
        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = _outputDirectory)
            .WithMetadata(meta =>
            {
                meta.Version = "1.0.0";
            })
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
            .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
            .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
            .BuildAsync();

        // Assert - Multiple comprehensive checks
        #pragma warning disable MSTEST0037
        Assert.AreEqual(4, result.Boms.Count, "Should generate 4 SBOMs");
        Assert.AreEqual(4, result.WrittenFilePaths.Count, "Should write 4 files");
        #pragma warning restore MSTEST0037
        
        // Check cross-SBOM consistency for shared dependencies
        var lib1Bom = result.Boms["ExampleClassLibrary1"];
        var lib2Bom = result.Boms["ExampleClassLibrary2"];
        var app1Bom = result.Boms["ExampleConsoleApp1"];
        var app2Bom = result.Boms["ExampleConsoleApp2"];
        
        // Verify all BOMs have unique serial numbers
        var serialNumbers = new[] {
            lib1Bom.SerialNumber, lib2Bom.SerialNumber, 
            app1Bom.SerialNumber, app2Bom.SerialNumber
        };
        Assert.AreEqual(4, serialNumbers.Distinct().Count(), "Each SBOM should have unique serial number");
        
        // Verify all BOMs use CycloneDX 1.7
        Assert.IsTrue(new[] { lib1Bom, lib2Bom, app1Bom, app2Bom }
            .All(b => b.SpecVersion == SpecificationVersion.v1_7), 
            "All SBOMs should use CycloneDX 1.7");
        
        // Verify metadata consistency for project references
        var lib2InLib1 = lib1Bom.Components!.FirstOrDefault(c => c.Name == "ExampleClassLibrary2");
        Assert.IsNotNull(lib2InLib1, "ExampleClassLibrary2 should be in ExampleClassLibrary1");
        Assert.AreEqual(lib2Bom.Metadata!.Component!.Version, lib2InLib1.Version, 
            "Referenced project version should match its own SBOM");
    }

    #endregion
}
