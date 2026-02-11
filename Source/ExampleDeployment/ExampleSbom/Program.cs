using System.Text.Json;
using SbomForge;

await new SbomBuilder()
    .WithBasePath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."))

    .WithMetadata(meta =>
    {
        meta.Version = "1.0.0";
    })
    
    .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
    .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj")
    .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
    .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")



    .BuildAsync();
