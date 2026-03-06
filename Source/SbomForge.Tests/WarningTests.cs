using SbomForge;

namespace SbomForge.Tests;

/// <summary>
/// Tests that the warning system correctly reports issues
/// that would otherwise be silently swallowed.
/// </summary>
[TestClass]
public sealed class WarningTests
{
    private static string _testBasePath = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext context)
    {
        _testBasePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "ExampleDeployment"));
    }

    [TestMethod]
    public async Task Warning_StandardBuild_NoUnexpectedWarnings()
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // We expect zero validation-level warnings for well-formed projects.
        // Filter out nuspec cache misses (informational).
        var nonInfoWarnings = result.Warnings
            .Where(w => !w.Contains("nuspec", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.AreEqual(0, nonInfoWarnings.Count,
            $"Expected no warnings for standard project but got:\n{string.Join("\n", nonInfoWarnings)}");
    }

    [TestMethod]
    public async Task Warning_WarningsListIsAccessible()
    {
        string outputDir = GetTempOutputDir();

        var result = await new SbomBuilder()
            .WithBasePath(_testBasePath)
            .WithOutput(o => o.OutputDirectory = outputDir)
            .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
            .BuildAsync();

        // Warnings should be an accessible list (even if empty).
        Assert.IsNotNull(result.Warnings);
    }

    [TestMethod]
    public async Task Warning_ExternalSbomMissing_ThrowsFileNotFoundException()
    {
        string outputDir = GetTempOutputDir();

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await new SbomBuilder()
                .WithBasePath(_testBasePath)
                .WithOutput(o => o.OutputDirectory = outputDir)
                .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
                .WithExternal("nonexistent-sbom.json")
                .BuildAsync();
        });
    }

    [TestMethod]
    public async Task Warning_ProjectNotFound_ThrowsFileNotFoundException()
    {
        string outputDir = GetTempOutputDir();

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await new SbomBuilder()
                .WithBasePath(_testBasePath)
                .WithOutput(o => o.OutputDirectory = outputDir)
                .ForProject("NonExistentProject/NonExistent.csproj")
                .BuildAsync();
        });
    }

    private static string GetTempOutputDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SbomForge-Warning-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
