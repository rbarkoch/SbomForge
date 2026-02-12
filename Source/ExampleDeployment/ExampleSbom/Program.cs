using CycloneDX.Models;
using SbomForge;

Console.WriteLine("=== Generating SBOMs for Example Deployment Projects ===\n");

// Set up the base path to the ExampleDeployment folder
var basePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

Console.WriteLine($"Base path: {basePath}\n");

// Build SBOMs for all example projects
var result = await SbomBuilder
    .AddBasePath(basePath)

    // ── Global external BOM reference ──
    // This BOM is merged into every project's SBOM.
    // Useful for shared vendor or third-party component inventories.
    // .AddExternalBom("shared-vendor/vendor-components.sbom.json")

    // ── Global additional component ──
    // Added to every project's SBOM. Use for components present at runtime
    // but not tracked by NuGet (native libs, vendored code, etc.).
    .AddComponent(c =>
    {
        c.Type = Component.Classification.Library;
        c.Name = "example-native-runtime";
        c.Version = "2.0.0";
        c.Purl = "pkg:generic/example-native-runtime@2.0.0";
        c.Description = "Example native runtime library included in all deployments";
    })

    // Add ExampleClassLibrary1
    // Version is from .csproj, Copyright and Company are from Directory.Build.props
    .AddProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
        .WithMetadata(meta =>
        {
            // Only override what's needed - Version, Copyright, and Supplier come from project files
        })

    // Add ExampleClassLibrary2
    // Version, Copyright, Company, and Description are automatically read from the project file
    .AddProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
        .WithMetadata(meta =>
        {
            // No need to set Version, Copyright, or Supplier - they come from the .csproj
        })

    // Add ExampleConsoleApp1
    // For executables, Purl defaults to pkg:generic/... instead of pkg:nuget/...
    .AddProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
        .WithMetadata(meta =>
        {
            meta.Version = "1.0.0";
            meta.Supplier = new OrganizationalEntity { Name = "Example Corp" };
            meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Example Corp";
        })
        // ── Per-project external BOM reference ──
        // Merge an external BOM into only this project's SBOM.
        // .AddExternalBom("ExampleConsoleApp1/vendor-bom.json")
        //
        // ── Per-project additional component ──
        // Include a component only in this project's SBOM.
        .AddComponent(c =>
        {
            c.Type = Component.Classification.Library;
            c.Name = "app1-embedded-resource";
            c.Version = "1.0.0";
            c.Purl = "pkg:generic/app1-embedded-resource@1.0.0";
            c.Description = "Embedded resource specific to ExampleConsoleApp1";
        })

    // Add ExampleConsoleApp2
    .AddProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")
        .WithMetadata(meta =>
        {
            meta.Version = "1.0.0";
            meta.Supplier = new OrganizationalEntity { Name = "Example Corp" };
            meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Example Corp";
        })

    // Configure filters
    .WithFilters(filter =>
    {
        filter.ExcludeTestProjects = true;
    })

    // Configure dependency resolution
    .WithResolution(res =>
    {
        res.IncludeTransitive = true;
    })

    // Configure output
    .WithOutput(o =>
    {
        o.OutputDirectory = Path.Combine(basePath, "sbom-output");
        o.Format = SbomFormat.CycloneDxJson;
        o.FileNameTemplate = "{ProjectName}-v{Version}.sbom.json";
    })
    .BuildAsync();

// Display results
Console.WriteLine("\nSBOM Generation Complete!");
Console.WriteLine($"\nWritten files:");
foreach (var path in result.WrittenFilePaths)
{
    Console.WriteLine($"  ✓ {Path.GetFileName(path)}");
}

Console.WriteLine("\nComponent Summary:");
foreach (var (name, bom) in result.Boms)
{
    var componentCount = bom.Components?.Count ?? 0;
    var dependencyCount = bom.Dependencies?.Count ?? 0;
    Console.WriteLine($"  {name}:");
    Console.WriteLine($"    - Components: {componentCount}");
    Console.WriteLine($"    - Dependencies: {dependencyCount}");
}

Console.WriteLine("\nDone!");