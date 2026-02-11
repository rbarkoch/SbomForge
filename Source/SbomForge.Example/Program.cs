using CycloneDX.Models;
using SbomForge;

// ---------------------------------------------------------------------------
// Scenario 1: Single project — simplest usage
//
// AddBasePath() sets the root directory, then AddProject() with a relative
// path declares the project and returns a ProjectBuilder for configuration.
// ---------------------------------------------------------------------------

Console.WriteLine("=== Scenario 1: Single Project SBOM ===\n");

var basePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

var singleResult = await SbomBuilder
    .AddBasePath(basePath)
    .AddProject("SbomForge/SbomForge.csproj")
        .WithVersion("1.0.0")
        .WithMetadata(meta =>
        {
            meta.BomRef = "pkg:nuget/SbomForge@1.0.0";
            meta.Purl = "pkg:nuget/SbomForge@1.0.0";
            meta.Supplier = new OrganizationalEntity { Name = "SbomForge Contributors" };
            meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} SbomForge Contributors";
            meta.Type = Component.Classification.Library;
        })
    .WithResolution(res =>
    {
        res.IncludeTransitive = true;
    })
    .WithOutput(o =>
    {
        o.OutputDirectory = Path.Combine(AppContext.BaseDirectory, "sbom-output");
        o.Format = SbomFormat.CycloneDxJson;
        o.FileNameTemplate = "{ProjectName}.sbom.json";
    })
    .BuildAsync();

Console.WriteLine($"  Written files:");
foreach (var path in singleResult.WrittenFilePaths)
    Console.WriteLine($"    {path}");

var bom = singleResult.Boms.Values.First();
Console.WriteLine($"  Components: {bom.Components?.Count ?? 0}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Scenario 2: Multiple projects with per-project metadata and filters
//
// Each AddProject() returns a ProjectBuilder, allowing per-project version
// and metadata. When projects reference each other, the generated SBOMs
// use the configured BomRef and Purl for cross-references.
// ---------------------------------------------------------------------------

Console.WriteLine("=== Scenario 2: Multiple Projects with Filters ===\n");

var multiResult = await SbomBuilder
    .AddBasePath(basePath)
    .AddProject("SbomForge/SbomForge.csproj")
        .WithVersion("1.0.0")
        .WithMetadata(meta =>
        {
            meta.BomRef = "pkg:nuget/SbomForge@1.0.0";
            meta.Purl = "pkg:nuget/SbomForge@1.0.0";
            meta.Supplier = new OrganizationalEntity { Name = "Acme Corp" };
            meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Acme Corp";
            meta.Type = Component.Classification.Library;
        })
    .AddProject("SbomForge.Sbom/SbomForge.Sbom.csproj")
        .WithVersion("1.0.0")
        .WithMetadata(meta =>
        {
            meta.BomRef = "pkg:nuget/SbomForge.Sbom@1.0.0";
            meta.Purl = "pkg:nuget/SbomForge.Sbom@1.0.0";
            meta.Supplier = new OrganizationalEntity { Name = "Acme Corp" };
            meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Acme Corp";
            meta.Type = Component.Classification.Application;
        })
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
        o.FileNameTemplate = "{ProjectName}-{Version}-sbom.json";
    })
    .BuildAsync();

Console.WriteLine($"  Written files:");
foreach (var path in multiResult.WrittenFilePaths)
    Console.WriteLine($"    {path}");

foreach (var (name, execBom) in multiResult.Boms)
    Console.WriteLine($"  {name}: {execBom.Components?.Count ?? 0} components");

Console.WriteLine();
Console.WriteLine("Done.");
