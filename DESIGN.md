# Manifold — SBOM Orchestration Library for .NET

## Overview

Manifold is a .NET library that provides a fluent, code-driven API for generating Software Bills of Materials (SBOMs) from .NET solutions and projects. It is designed as a replacement for the configuration complexity of the `dotnet-cyclonedx` CLI tool, giving developers full programmatic control over how SBOMs are assembled, filtered, and composed — particularly in multi-executable solutions.

The primary pain point being solved is this: `dotnet-cyclonedx` is a rigid CLI tool that does not compose well across solutions with multiple executable outputs. There is no good way to declare which projects feed into which executables, apply per-executable metadata, merge shared dependencies correctly, or produce both per-executable and solution-level SBOMs from a single configuration. Manifold solves all of this through a C# API that a developer configures in a standard console project.

---

## Usage Model

A developer creates a C# console project, adds the Manifold NuGet package, and writes a configuration file in C#. Running the project with `dotnet run` produces the SBOM output. No CLI flags, no MSBuild properties, no shell scripts.

A typical configuration looks like this:

```csharp
var result = await SbomBuilder
    .ForSolution("path/to/MySolution.sln")
    .WithMetadata(meta =>
    {
        meta.Supplier = "Acme Corp";
        meta.Manufacturer = "Acme Corp";
        meta.Copyright = $"Copyright (c) {DateTime.UtcNow.Year} Acme Corp";
    })
    .AddExecutable("ServerA", exec => exec
        .FromProject("src/Servers/ServerA/ServerA.csproj")
        .WithVersion("3.2.1")
        .IncludesProject("src/Shared/Core.csproj")
        .IncludesProject("src/Shared/Protocol.csproj"))
    .AddExecutable("ServerB", exec => exec
        .FromProject("src/Servers/ServerB/ServerB.csproj")
        .WithVersion("3.2.1")
        .IncludesProject("src/Shared/Core.csproj"))
    .WithFilters(filter =>
    {
        filter.ExcludeTestProjects = true;
        filter.ExcludePackagePrefixes.Add("Microsoft.NET.Test");
    })
    .WithResolution(res =>
    {
        res.IncludeTransitive = true;
        res.TargetFramework = "net8.0";
    })
    .WithOutput(out_ =>
    {
        out_.OutputDirectory = "./sbom-output";
        out_.Format = SbomFormat.CycloneDxJson;
        out_.Scope = SbomScope.Both;
        out_.FileNameTemplate = "{ExecutableName}-{Version}-sbom.json";
    })
    .BuildAsync();
```

---

## Core Concepts

### Executable Definitions

The central abstraction is the `ExecutableDefinition` — a declaration of a deployable artifact and which projects compose it. This is the key feature that `dotnet-cyclonedx` lacks. A single executable can include dependencies from multiple projects (e.g. shared libraries, plugins, runtime-loaded modules) so that its SBOM accurately reflects everything it ships with.

### Dependency Resolution

Manifold reads `project.assets.json` — the NuGet lock file written by `dotnet restore` — to resolve the full dependency graph without invoking MSBuild at runtime. This means the user must run `dotnet restore` before running Manifold, but it avoids the complexity and fragility of runtime MSBuild invocation.

When a single executable is composed from multiple projects, Manifold merges their dependency graphs and deduplicates shared packages, keeping the higher version on conflict.

### Metadata Inheritance

Global metadata (supplier, manufacturer, copyright, custom properties) is declared once and inherited by all executables. Individual executables can override any field. The merge strategy is: per-executable values win over global defaults, but null/empty per-executable values fall back to the global value.

### Output Scopes

Manifold supports three output scopes:

- `PerExecutable` — one SBOM file per declared executable
- `Solution` — a single merged SBOM covering all executables, with shared packages deduplicated
- `Both` — produces both of the above

### Auto-Discovery

As a convenience, `DiscoverExecutables()` can be called instead of `AddExecutable()` to automatically find all projects in the solution with `OutputType = Exe` or `WinExe`. This is useful for initial exploration but offers less control than explicit declaration.

---

## API Reference

### Entry Points

```csharp
SbomBuilder.ForSolution(string solutionPath)
SbomBuilder.ForProject(string projectPath)
```

`ForProject` is a convenience method for single-project use. The project path is a path to a `.csproj` file, relative to the working directory of the running application.

### Builder Methods

| Method | Description |
|---|---|
| `WithMetadata(Action<ComponentMetadata>)` | Set global metadata applied to all executables |
| `AddExecutable(string name, Action<ExecutableBuilder>)` | Declare an executable and its constituent projects |
| `DiscoverExecutables()` | Auto-discover executable projects from the solution file |
| `WithFilters(Action<ComponentFilter>)` | Configure package and project exclusion rules |
| `WithResolution(Action<DependencyResolutionOptions>)` | Configure how dependencies are resolved |
| `WithOutput(Action<OutputOptions>)` | Configure output format, directory, and scope |
| `BuildAsync()` | Execute the pipeline and return an `SbomResult` |

### ExecutableBuilder Methods

| Method | Description |
|---|---|
| `FromProject(string projectPath)` | The primary `.csproj` for this executable |
| `WithVersion(string version)` | Version string written to the SBOM metadata |
| `IncludesProject(string projectPath)` | Add an additional project whose dependencies are merged in |
| `WithMetadata(Action<ComponentMetadata>)` | Override global metadata for this executable only |

### ComponentFilter Properties

| Property | Type | Description |
|---|---|---|
| `ExcludePackageIds` | `List<string>` | Exact package IDs to exclude |
| `ExcludePackagePrefixes` | `List<string>` | Exclude all packages starting with this prefix |
| `ExcludeProjectNames` | `List<string>` | Exclude projects by name |
| `ExcludeTestProjects` | `bool` | Automatically exclude test projects (default: true) |

### DependencyResolutionOptions Properties

| Property | Type | Description |
|---|---|---|
| `IncludeTransitive` | `bool` | Include indirect dependencies (default: true) |
| `DeduplicateSharedPackages` | `bool` | Deduplicate across projects in a merged SBOM (default: true) |
| `TargetFramework` | `string` | Pin to a specific TFM when projects are multi-targeted |

### OutputOptions Properties

| Property | Type | Description |
|---|---|---|
| `OutputDirectory` | `string` | Directory to write SBOM files (default: `./sbom-output`) |
| `Format` | `SbomFormat` | `CycloneDxJson`, `CycloneDxXml`, or `SpdxJson` |
| `Scope` | `SbomScope` | `PerExecutable`, `Solution`, or `Both` |
| `FileNameTemplate` | `string` | Supports `{ExecutableName}` and `{Version}` tokens |

---

## Internal Architecture

The pipeline consists of four components that are composed inside `BuildAsync()`:

**`DependencyResolver`** reads `project.assets.json` for each project path associated with an executable and constructs a `DependencyGraph`. When multiple projects feed into one executable, their graphs are merged. Marks each package as direct or transitive.

**`SbomComposer`** takes a `DependencyGraph` and an `ExecutableDefinition` and produces a `CycloneDxBom` object. Applies filtering, constructs package URLs (purls) in the format `pkg:nuget/{id}@{version}`, and builds the `dependencies` section of the BOM. Also exposes a `Merge()` method that combines multiple per-executable BOMs into a single solution-level BOM with deduplication.

**`SbomWriter`** serializes `CycloneDxBom` objects to disk based on the `OutputOptions`. Handles file naming from the template and creates the output directory if it does not exist.

**`SolutionResolver`** (used only by `DiscoverExecutables()`) loads the `.sln` file and inspects each referenced `.csproj` for `OutputType`. Requires `Microsoft.Build` and `Microsoft.Build.Locator`.

---

## NuGet Dependencies

| Package | Purpose |
|---|---|
| `NuGet.ProjectModel` | Reading and parsing `project.assets.json` |
| `CycloneDX.Models` | CycloneDX BOM object model and serialization |
| `Microsoft.Build` | Solution and project file inspection (optional, for `DiscoverExecutables()`) |
| `Microsoft.Build.Locator` | Locating the MSBuild installation at runtime (optional) |

The `Microsoft.Build` packages are only required if `DiscoverExecutables()` is used. They can be made an optional dependency or separated into a companion package to keep the core dependency footprint small.

---

## Prototype Code

A working prototype of the full API surface has been produced and is available alongside this document. The prototype covers all classes described above and includes a worked example (`Example.cs`) demonstrating the three main usage scenarios. The lightweight CycloneDX model classes in the prototype (`CycloneDxBom`, `BomComponent`, etc.) should be replaced with the `CycloneDX.Models` NuGet package in the real implementation.

Files included in the prototype:

- `Models.cs` — all configuration and options types
- `SbomBuilder.cs` — the main builder and `ExecutableBuilder`
- `DependencyResolver.cs` — `project.assets.json` parsing and graph construction
- `SbomComposer.cs` — BOM composition, merging, writing, and lightweight CycloneDX models
- `Extensions.cs` — metadata merge/clone helpers and `SolutionResolver` stub
- `Example.cs` — three annotated usage scenarios

---

## What Is Out of Scope

- DependencyTrack integration — uploading, vulnerability tracking, and policy management are intentionally excluded. Manifold's responsibility ends at producing well-formed SBOM files on disk.
- SBOM validation — the library does not validate the produced SBOM against the CycloneDX schema. This can be done separately with existing tooling.
- Signing — SBOM signing is not in scope for the initial implementation.