# SbomForge Design Documentation

This document describes the internal architecture and design of SbomForge for developers working on or extending the library.

## Architecture Overview

SbomForge follows a **two-pass pipeline architecture** to generate CycloneDX SBOM documents:

```
┌─────────────────────────────────────────────────────────────────┐
│ Pass 1: Resolution Phase (parallel per project)                │
│ ─────────────────────────────────────────────────────────────── │
│  Project → DependencyResolver → DependencyGraph                │
│                                                                 │
│  • Read project.assets.json (NuGet lock file)                   │
│  • Build dependency graph (packages + project references)       │
│  • Extract metadata from .csproj + Directory.Build.props        │
│  • Register projects in cross-SBOM registry                     │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ Pass 2: Composition Phase (per project)                        │
│ ─────────────────────────────────────────────────────────────── │
│  DependencyGraph → Composer → CycloneDX Bom → JSON File        │
│                                                                 │
│  • Apply filters (exclude packages/projects)                    │
│  • Build CycloneDX components and dependency relationships      │
│  • Resolve project refs using shared registry                  │
│  • Serialize to JSON and write to disk                          │
└─────────────────────────────────────────────────────────────────┘
```

The two-pass design ensures consistent metadata for project references across multiple SBOMs and enables future parallel processing optimizations.

## Core Components

### 1. Builder Layer

**Purpose**: Provides fluent API for configuring SBOM generation.

#### `SbomBuilder`
The main entry point for users. Orchestrates the entire SBOM generation pipeline.

**Key Responsibilities:**
- Collect project configurations via `.ForProject()`
- Merge global and per-project configurations
- Execute two-pass resolution and composition pipeline
- Maintain project registry for cross-SBOM consistency
- Return `SbomBuildResult` with all generated SBOMs

**Important Methods:**
- `WithBasePath(string)` - Set working directory for relative paths
- `ForProject(string, Action<ComponentBuilder>?)` - Add project to SBOM generation queue
- `WithMetadata()`, `WithFilters()`, `WithResolution()`, `WithOutput()` - Configure global settings
- `BuildAsync()` - Execute SBOM generation pipeline

#### `ComponentBuilder`
Provides per-project configuration overrides.

**Key Responsibilities:**
- Expose fluent API for project-specific settings
- Build `SbomConfiguration` consumed by `SbomBuilder.ForProject()`
- Support same configuration methods as `SbomBuilder` but scoped to one project

#### `BuilderBase<T>`
Abstract base class defining the configuration API contract.

**Methods:**
- `WithMetadata()` - Component metadata (name, version, description, etc.)
- `WithFilters()` - Filtering rules for dependencies
- `WithResolution()` - Dependency resolution settings
- `WithOutput()` - Output path and file naming
- `WithExternal()` - External component references (not yet implemented)
- `WithComponent()` - Additional component configuration (not yet implemented)

### 2. Configuration Layer

**Namespace**: `SbomForge.Configuration`

Immutable configuration objects passed through the pipeline.

#### `SbomConfiguration`
Root configuration container with four sub-configurations:

```csharp
public class SbomConfiguration
{
    public ComponentConfiguration Component { get; }    // Metadata
    public ResolutionConfiguration Resolution { get; }  // Dependency resolution
    public FiltersConfiguration Filters { get; }        // Filtering rules
    public OutputConfiguration Output { get; }          // Output settings
}
```

Supports **merging** for combining global + per-project overrides via `Merge()` method.

#### `ComponentConfiguration`
Extends CycloneDX `Component` class to provide metadata properties:
- `Name`, `Version`, `Description`, `Author`
- `Type` (e.g., application, library)
- License information
- Other CycloneDX component fields

#### `ResolutionConfiguration`
Controls dependency analysis behavior:
- `IncludeTransitive` (bool) - Include indirect dependencies (default: true)
- `TargetFramework` (string?) - Target framework for multi-targeted projects

#### `FiltersConfiguration`
Defines exclusion rules:
- `ExcludePackageIds` (List<string>) - Exact package IDs to exclude
- `ExcludePackagePrefixes` (List<string>) - Package ID prefixes to exclude
- `ExcludeProjectNames` (List<string>) - Project names to exclude
- `ExcludeTestProjects` (bool) - Auto-exclude test projects (default: true)

#### `OutputConfiguration`
Controls SBOM file output:
- `OutputDirectory` (string) - Target directory (default: `./sbom-output`)
- `FileNameTemplate` (string) - File naming pattern with tokens: `{ProjectName}`, `{ExecutableName}`, `{Version}`

#### `ProjectConfiguration`
Internal configuration linking a project path to its SBOM configuration:
- `ProjectPath` (string) - Relative or absolute path to `.csproj`
- `Sbom` (SbomConfiguration) - Effective configuration for this project

#### `ExternalComponentConfiguration`
Placeholder for external component references (future feature).

### 3. Resolver Layer

**Namespace**: `SbomForge.Resolver`

Analyzes .NET projects to extract dependency information.

#### `DependencyResolver`
Reads NuGet lock files (`project.assets.json`) to build a complete dependency graph.

**Key Responsibilities:**
- Locate and load `project.assets.json` (NuGet lock file)
- Run `dotnet restore` if lock file is missing
- Select appropriate target framework from lock file
- Identify direct vs. transitive dependencies
- Extract package metadata (version, SHA512 hash, file paths)
- Identify project-to-project references
- Build `DependencyGraph` data structure

**Process Flow:**
1. Resolve project path (handle directories by finding `.csproj` inside)
2. Ensure project is restored (check for `obj/project.assets.json`)
3. Load and parse NuGet lock file using `NuGet.ProjectModel`
4. Select target framework (user-specified or first available)
5. Extract direct dependency IDs from lock file spec
6. Build library lookup for metadata (SHA512, paths)
7. Iterate target libraries and build `ResolvedPackage` or `ResolvedProjectReference` objects
8. Return `DependencyGraph`

**NuGet Integration:**
Uses NuGet SDK libraries (`NuGet.ProjectModel`) to parse lock files:
- `LockFile` - Represents `project.assets.json`
- `LockFileTarget` - Target framework-specific dependency resolution
- `LockFileTargetLibrary` - Resolved dependency in target
- `LockFileLibrary` - Library-level metadata (hashes, paths)

#### `DependencyGraph`
Immutable data structure representing all resolved dependencies for a single project.

**Properties:**
- `ProjectName` (string) - Name of the analyzed project
- `SourceProjectPath` (string) - Absolute path to `.csproj`
- `Packages` (List<ResolvedPackage>) - NuGet package dependencies
- `ProjectReferences` (List<ResolvedProjectReference>) - Project-to-project references

#### `ResolvedPackage`
Represents a single NuGet package dependency.

**Properties:**
- `Id` (string) - NuGet package identifier
- `Version` (string) - Package version
- `IsDirect` (bool) - Whether this is a direct or transitive dependency
- `Sha512` (string?) - Package content hash for integrity verification
- `NuspecPath` (string?) - Path to `.nuspec` metadata file
- `DependsOn` (List<string>) - List of dependency keys this package depends on
- `Type` (PackageType enum) - Package type (NuGet, ProjectReference)

#### `ResolvedProjectReference`
Represents a project-to-project reference.

**Properties:**
- `Name` (string) - Referenced project name
- `AbsolutePath` (string) - Absolute path to referenced `.csproj`
- `DependsOn` (List<string>) - Dependencies of the referenced project

#### `NuspecMetadata`
Parser for `.nuspec` files to extract additional package metadata.

**Extracted Fields:**
- Package description
- Authors
- License information
- Project URLs
- Copyright

### 4. Composer Layer

**Namespace**: `SbomForge.Composer`

Transforms `DependencyGraph` into CycloneDX SBOM documents.

#### `Composer`
Builds CycloneDX `Bom` objects from dependency graphs.

**Key Responsibilities:**
- Apply filters to dependency graph
- Convert `ResolvedPackage` → CycloneDX `Component`
- Convert `ResolvedProjectReference` → CycloneDX `Component`
- Build dependency relationships using `dependency.Ref`
- Resolve project metadata from cross-SBOM registry
- Serialize SBOM to JSON using `CycloneDX.Json`
- Write output file to configured directory

**Process Flow:**
1. **Filter**: Apply exclusion rules to create filtered `DependencyGraph`
2. **Build**: Convert graph to CycloneDX `Bom` structure
   - Create metadata component (main project)
   - Map packages to components with `purl` identifiers
   - Map project references to components (resolved via registry)
   - Build `Dependency` objects linking components
3. **Serialize**: Convert `Bom` to JSON using `Serializer.Serialize()`
4. **Write**: Save JSON to file with configured naming template
5. **Return**: `ComposerResult` with `Bom` and output path

**CycloneDX Integration:**
Uses `CycloneDX.Core` and `CycloneDX.Json` libraries:
- `Bom` - Root SBOM document (CycloneDX 1.7 schema)
- `Component` - Software component (package or project)
- `Dependency` - Represents dependency relationship between components
- `Metadata` - SBOM metadata (timestamp, tools, authors)

**PURL Format:**
Package URLs follow the pattern: `pkg:nuget/{PackageId}@{Version}`

#### `ComposerResult`
Output of composer containing the generated SBOM and file path.

**Properties:**
- `Bom` (CycloneDX.Models.Bom) - Generated SBOM object
- `OutputPath` (string) - Absolute path to written SBOM file

### 5. Utilities Layer

**Namespace**: `SbomForge.Utilities`

Supporting utilities for metadata extraction and common operations.

#### `ProjectMetadataReader`
Reads metadata from `.csproj` and `Directory.Build.props`.

**Extracted Metadata:**
- Assembly name
- Version
- Company
- Authors
- Description
- Copyright
- Product name

**Process:**
1. Parse `.csproj` as XML
2. Walk up directory tree looking for `Directory.Build.props`
3. Merge properties with project-level properties taking precedence
4. Return `ProjectMetadata` object

#### `ProjectMetadata`
Data transfer object for project-level metadata.

#### `Extensions`
Extension methods for configuration merging and utility operations.

**Key Methods:**
- `Merge(this SbomConfiguration)` - Merge two configurations (per-project overrides global)

### 6. Result Layer

#### `SbomBuildResult`
Final output returned by `SbomBuilder.BuildAsync()`.

**Properties:**
- `WrittenFilePaths` (List<string>) - Paths to all written SBOM files
- `Boms` (Dictionary<string, Bom>) - Generated SBOMs keyed by project name

## Data Flow

### Configuration Inheritance

```
Global Config (SbomBuilder)
    ↓
    Merge with Per-Project Config (ComponentBuilder)
    ↓
Effective Configuration (passed to Resolver & Composer)
```

Merge strategy: Per-project settings override global settings, but both are additive for collections (filters, etc.).

### Dependency Resolution

```
.csproj
    ↓
dotnet restore (if needed)
    ↓
project.assets.json (NuGet lock file)
    ↓
DependencyResolver.ResolveAsync()
    ↓
DependencyGraph
    ↓
Composer.ApplyFilters()
    ↓
Filtered DependencyGraph
```

### SBOM Generation

```
DependencyGraph + SbomConfiguration
    ↓
Composer.BuildBom()
    ↓
CycloneDX Bom (in-memory)
    ↓
Serializer.Serialize() (CycloneDX.Json)
    ↓
JSON string
    ↓
File.WriteAllTextAsync()
    ↓
SBOM file on disk
```

## Project Registry

The **project registry** is a critical component for cross-SBOM consistency.

**Purpose**: Ensure project references have consistent metadata across multiple SBOMs.

**Structure**:
```csharp
Dictionary<string, ComponentConfiguration>
```

**Keying Strategy**:
- Primary key: Absolute project path
- Secondary key: Project name

**Usage**:
1. During Pass 1, each project's `ComponentConfiguration` is registered
2. During Pass 2, when composing project references, the registry is consulted
3. If found, metadata from the registry is used instead of minimal defaults

This ensures that if `ProjectA` references `ProjectB`, and both have SBOMs generated, the metadata for `ProjectB` in `ProjectA`'s SBOM matches `ProjectB`'s own SBOM metadata.

## Extension Points

### Adding New Configuration Options

1. Add property to appropriate `*Configuration` class
2. Add fluent method to `BuilderBase<T>`
3. Implement in `SbomBuilder` and `ComponentBuilder`
4. Use configuration in `DependencyResolver` or `Composer`

### Adding New Filters

1. Add property to `FiltersConfiguration`
2. Implement filter logic in `Composer.ApplyFilters()`
3. Update documentation

### Supporting New Project Types

1. Add file extension to `DependencyResolver.ProjectFileExtensions`
2. Ensure NuGet lock file format compatibility
3. Test with target project type

## Dependencies

**External Libraries:**
- **CycloneDX.Core** (v11.0.0) - CycloneDX SBOM object model
- **CycloneDX.Json** - JSON serialization for CycloneDX
- **NuGet.ProjectModel** (v7.3.0) - NuGet lock file parsing

**Target Framework:** .NET 10.0 (with implicit usings and nullable reference types enabled)

## Design Principles

1. **Fluent API** - Builder pattern with method chaining for intuitive configuration
2. **Immutability** - Configuration objects are immutable after creation
3. **Separation of Concerns** - Clear boundaries between resolution, composition, and output
4. **Two-Pass Processing** - Ensures consistency and enables future optimizations
5. **Convention over Configuration** - Sensible defaults, minimal required configuration
6. **Standards Compliance** - Full CycloneDX 1.7 specification support
7. **Extensibility** - Abstract base classes and configuration composition allow extension

## Future Enhancements

Potential areas for expansion:

- **External Components**: Support for manually-added components via `WithExternal()`
- **Parallel Processing**: Leverage two-pass design to parallelize Pass 1 resolution
- **Additional Formats**: Support SPDX, SWID, or other SBOM formats
- **Vulnerability Scanning**: Integrate with vulnerability databases
- **License Analysis**: Enhanced license detection and compliance checking
- **Build Integration**: MSBuild tasks for automatic SBOM generation during builds
- **Incremental Generation**: Only regenerate SBOMs for changed projects
