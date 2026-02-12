using CycloneDX.Models;
using SbomForge;

var result = await SbomBuilder
    .AddBasePath(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")))
    .AddProject("SbomForge/SbomForge.csproj")
        .WithMetadata(meta =>
        {
            meta.Version = "1.0.0";
            meta.BomRef = "pkg:nuget/SbomForge@1.0.0";
            meta.Purl = "pkg:nuget/SbomForge@1.0.0";
            meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Ronnie Bar-Kochba";
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

foreach (var path in result.WrittenFilePaths)
    Console.WriteLine($"Written: {path}");

var bom = result.Boms.Values.First();
Console.WriteLine($"Components: {bom.Components?.Count ?? 0}");
