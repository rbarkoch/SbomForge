using CycloneDX.Models;
using SbomForge;

// ---------------------------------------------------------------------------
// Scenario 1: Single project — simplest usage
//
// ForProject() automatically treats the .csproj as a single executable.
// We just set metadata and output options, then build.
// ---------------------------------------------------------------------------

Console.WriteLine("=== Scenario 1: Single Project SBOM ===\n");

var singleResult = await SbomBuilder
    .ForProject(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SbomForge", "SbomForge.csproj"))
    .WithMetadata(m =>
    {
        m.Supplier = new OrganizationalEntity { Name = "SbomForge Contributors" };
        m.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} SbomForge Contributors";
    })
    .WithOutput(o =>
    {
        o.OutputDirectory = Path.Combine(AppContext.BaseDirectory, "sbom-output");
        o.Format = SbomFormat.CycloneDxJson;
    })
    .BuildAsync();

Console.WriteLine($"  Written files:");
foreach (var path in singleResult.WrittenFilePaths)
    Console.WriteLine($"    {path}");

var bom = singleResult.Boms.Values.First();
Console.WriteLine($"  Components: {bom.Components?.Count ?? 0}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Scenario 2: Multi-executable with filters
//
// Demonstrates AddExecutable() with IncludesProject(), per-executable
// metadata overrides, and package exclusion filters.
// ---------------------------------------------------------------------------

Console.WriteLine("=== Scenario 2: Multi-Executable with Filters ===\n");

// For this demo we use the same project twice to simulate two executables
var projectPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SbomForge", "SbomForge.csproj"));

var multiResult = await SbomBuilder
    .ForSolution(Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SbomForge.slnx")))
    .WithMetadata(meta =>
    {
        meta.Supplier = new OrganizationalEntity { Name = "Acme Corp" };
        meta.Manufacturer = new OrganizationalEntity { Name = "Acme Corp" };
        meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Acme Corp";
    })
    .AddExecutable("LibrarySbom", exec => exec
        .FromProject("SbomForge/SbomForge.csproj")
        .WithVersion("1.0.0"))
    .WithFilters(filter =>
    {
        filter.ExcludeTestProjects = true;
        filter.ExcludePackagePrefixes.Add("Microsoft.NET.Test");
    })
    .WithResolution(res =>
    {
        res.IncludeTransitive = true;
    })
    .WithOutput(o =>
    {
        o.OutputDirectory = Path.Combine(AppContext.BaseDirectory, "sbom-output");
        o.Format = SbomFormat.CycloneDxJson;
        o.Scope = SbomScope.Both;
        o.FileNameTemplate = "{ExecutableName}-{Version}-sbom.json";
    })
    .BuildAsync();

Console.WriteLine($"  Written files:");
foreach (var path in multiResult.WrittenFilePaths)
    Console.WriteLine($"    {path}");

foreach (var (name, execBom) in multiResult.Boms)
    Console.WriteLine($"  {name}: {execBom.Components?.Count ?? 0} components");

if (multiResult.SolutionBom is not null)
    Console.WriteLine($"  Solution BOM: {multiResult.SolutionBom.Components?.Count ?? 0} components");

Console.WriteLine();
Console.WriteLine("Done.");
