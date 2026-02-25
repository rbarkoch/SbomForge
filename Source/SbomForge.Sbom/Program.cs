using SbomForge;

Console.WriteLine("Generating SBOM for SbomForge...");

string version = "1.0.0";
if(args.Length >= 1)
{
    version = args.First();
}

var result = await new SbomBuilder()
    .WithBasePathFromSolution("SbomForge.slnx")
    .ForProject("SbomForge/SbomForge.csproj", component =>
    {
        component.WithMetadata(meta => 
        {
            meta.Version = version;
            meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Ronnie Bar-Kochba";
        });
    })
    .WithTool(tool =>
    {
        tool.Version = version;
    })
    .WithOutput(output =>
    {
        output.OutputDirectory = "../Publish";
    })
    .BuildAsync();

foreach(var sbom in result.WrittenFilePaths)
{
    Console.WriteLine($"SBOM written to '{sbom}'.");
}