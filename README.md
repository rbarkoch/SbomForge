# SbomForge

SbomForge is a .NET library for generating Software Bill of Materials (SBOM) files in CycloneDX 1.7 format. It automatically analyzes your .NET projects and their dependencies to create comprehensive, standards-compliant SBOM documents.

## Features

- 🔄 **Cross-SBOM Consistency** - When generating multiple SBOMs for projects that reference each other, project metadata remains consistent across all generated SBOMs. This ensures that ProjectA's SBOM references ProjectB with the same metadata that appears in ProjectB's own SBOM.
- 🎯 **CycloneDX 1.7** - Industry-standard SBOM format
- 📦 **NuGet Package Resolution** - Automatic detection of direct and transitive dependencies
- 🔗 **Project-to-Project References** - Tracks inter-project dependencies in multi-project solutions
- 🏗️ **Directory.Build.props Support** - Respects MSBuild property hierarchy for metadata discovery
- ⚙️ **Flexible Configuration** - Fine-grained control over SBOM generation
- 🎨 **Customizable Output** - Control output location and file naming
- 🚫 **Advanced Filtering** - Exclude specific packages, projects, or test dependencies
- 📊 **Multi-Framework Support** - Works with all SDK-style .NET projects (.csproj, .fsproj, .vbproj)

## Installation

Add SbomForge to your solution by creating a new console project or adding it to an existing one:

```bash
dotnet new console -n MyProjectSbom
dotnet add MyProjectSbom package SbomForge
```

Alternatively, add the package reference directly to your `.csproj` file:

```xml
<ItemGroup>
  <PackageReference Include="SbomForge" Version="*" />
</ItemGroup>
```

## Getting Started

### Basic Usage

Create a simple SBOM generator for a single project:

```csharp
using SbomForge;

await new SbomBuilder()
    .WithBasePath(Directory.GetCurrentDirectory())
    .ForProject("MyApp/MyApp.csproj")
    .BuildAsync();
```

This will:
1. Analyze the project and its dependencies
2. Generate a CycloneDX SBOM
3. Save it to `./sbom-output/MyApp-sbom.json`

### Multi-Project Solutions

Generate SBOMs for multiple projects in your solution:

```csharp
await new SbomBuilder()
    .WithBasePath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
    
    .ForProject("MyLibrary/MyLibrary.csproj")
    .ForProject("MyWebApi/MyWebApi.csproj")
    .ForProject("MyConsoleApp/MyConsoleApp.csproj")
    
    .BuildAsync();
```

## Basic Configuration

### Setting Global Metadata

Apply metadata to all generated SBOMs:

```csharp
await new SbomBuilder()
    .WithBasePath(Directory.GetCurrentDirectory())
    
    .WithMetadata(meta =>
    {
        meta.Name = "My Application Suite";
        meta.Version = "2.1.0";
        meta.Description = "Enterprise application suite";
        meta.Author = "Your Company";
    })
    
    .ForProject("MyApp.csproj")
    .BuildAsync();
```

### Per-Project Configuration

Override settings for specific projects:

```csharp
await new SbomBuilder()
    .WithBasePath(Directory.GetCurrentDirectory())
    
    .ForProject("MyLibrary.csproj", config => config
        .WithMetadata(meta =>
        {
            meta.Version = "1.5.0";
            meta.Description = "Core library components";
        })
        .WithOutput(output =>
        {
            output.OutputDirectory = "./artifacts/sboms";
            output.FileNameTemplate = "{ProjectName}-v{Version}-sbom.json";
        })
    )
    
    .BuildAsync();
```

### Filtering Dependencies

Exclude specific packages, prefixes, or test projects:

```csharp
await new SbomBuilder()
    .WithBasePath(Directory.GetCurrentDirectory())
    
    .WithFilters(filters =>
    {
        // Exclude specific package IDs
        filters.ExcludePackageIds.Add("Microsoft.Extensions.Logging.Abstractions");
        filters.ExcludePackageIds.Add("System.Text.Json");
        
        // Exclude all packages with certain prefixes
        filters.ExcludePackagePrefixes.Add("Microsoft.Extensions.");
        filters.ExcludePackagePrefixes.Add("Internal.");
        
        // Exclude specific projects by name
        filters.ExcludeProjectNames.Add("TestUtilities");
        
        // Exclude test projects (enabled by default)
        filters.ExcludeTestProjects = true;
    })
    
    .ForProject("MyApp.csproj")
    .BuildAsync();
```

### Resolution Configuration

Control how dependencies are resolved:

```csharp
await new SbomBuilder()
    .WithBasePath(Directory.GetCurrentDirectory())
    
    .WithResolution(resolution =>
    {
        // Include both direct and transitive (indirect) dependencies
        resolution.IncludeTransitive = true;
        
        // For multi-targeted projects, specify which framework to use
        resolution.TargetFramework = "net8.0";
    })
    
    .ForProject("MyApp.csproj")
    .BuildAsync();
```

### Output Configuration

Customize where and how SBOMs are written:

```csharp
await new SbomBuilder()
    .WithBasePath(Directory.GetCurrentDirectory())
    
    .WithOutput(output =>
    {
        // Custom output directory
        output.OutputDirectory = "./build/sboms";
        
        // Custom file naming with tokens: {ProjectName}, {ExecutableName}, {Version}
        output.FileNameTemplate = "{ProjectName}-{Version}-sbom.json";
    })
    
    .ForProject("MyApp.csproj")
    .BuildAsync();
```

## Complete Example

Here's a comprehensive example combining multiple configuration options:

```csharp
using SbomForge;

var result = await new SbomBuilder()
    .WithBasePath(Directory.GetCurrentDirectory())
    
    // Global settings
    .WithMetadata(meta =>
    {
        meta.Version = "3.0.0";
        meta.Author = "Acme Corporation";
    })
    
    .WithResolution(resolution =>
    {
        resolution.IncludeTransitive = true;
        resolution.TargetFramework = "net8.0";
    })
    
    .WithFilters(filters =>
    {
        filters.ExcludePackagePrefixes.Add("Microsoft.Extensions.");
        filters.ExcludeTestProjects = true;
    })
    
    .WithOutput(output =>
    {
        output.OutputDirectory = "./artifacts/sboms";
    })
    
    // Individual projects with optional overrides
    .ForProject("CoreLibrary/CoreLibrary.csproj")
    
    .ForProject("WebApi/WebApi.csproj", config => config
        .WithMetadata(meta => meta.Description = "REST API Service")
    )
    
    .ForProject("Worker/Worker.csproj", config => config
        .WithOutput(output => output.FileNameTemplate = "worker-service-sbom.json")
    )
    
    .BuildAsync();

// Access generated SBOMs
Console.WriteLine($"Generated {result.WrittenFilePaths.Count} SBOM files:");
foreach (var path in result.WrittenFilePaths)
{
    Console.WriteLine($"  - {path}");
}
```

## Project Structure

A typical SBOM generation project structure:

```
MySolution/
├── MySolution.sln
├── MyLibrary/
│   └── MyLibrary.csproj
├── MyWebApi/
│   └── MyWebApi.csproj
└── SbomGenerator/              # SBOM generation project
    ├── SbomGenerator.csproj
    └── Program.cs              # SbomBuilder configuration
```

## How It Works

1. **Restore**: Ensures all projects have been restored (runs `dotnet restore` if needed)
2. **Metadata Discovery**: Reads project metadata from `.csproj` files and respects the `Directory.Build.props` hierarchy, walking up the directory tree to merge properties
3. **Resolve**: Analyzes `project.assets.json` to identify all NuGet packages and project references
4. **Filter**: Applies configured filters to exclude unwanted dependencies
5. **Compose**: Builds CycloneDX SBOM documents with complete dependency graphs
6. **Output**: Writes JSON SBOM files to the configured output directory

## Requirements

- .NET SDK (compatible with SDK-style projects)
- Projects must use `PackageReference` format (not `packages.config`)

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE.txt](LICENSE.txt) file for details.

## ⚠️ AI-Generated Software Disclaimer and Disclosure

**Portions of this software were written by AI.**

The idea and API was constructed by humans. Much of the implementation details and documentation were written by AI with human bug-fixing and intervention.

Please be aware:

- **Bugs and security vulnerabilities** may exist that would typically be caught by human developers
- **Use at your own risk** — especially for sensitive or production data
- **Test thoroughly** before relying on this software

This tool is relatively simple and imposes little risk to use as is. However, this tool generates documentation which is primarily stored for record keeping and identifying important vulnerabilities in other software. Users are encouraged to check that the output of this software is accurate for their use case. Users are additionally encouraged to review the code themselves before using. 

This transparency notice is provided so you can make informed decisions the use of this software.
