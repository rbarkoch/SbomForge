using System.Text.Json;
using CycloneDX.Models;
using SbomForge;

await new SbomBuilder()
    .WithBasePath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."))

    .WithMetadata(meta =>
    {
        meta.Version = "1.0.0";
    })
    .ForProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
    .ForProject("ExampleClassLibrary2/ExampleClassLibrary2.csproj", component =>
    {
        component.WithMetadata(meta =>
        {
            meta.Cpe = "cpe:2.3:a:example:exampleclasslibrary2:1.0.0:*:*:*:*:*:*:*";
        });
    })
    .ForProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj", component => component
        .WithComponent(c =>
        {
            c.Type = Component.Classification.Container;
            c.Name = "redis";
            c.Version = "7.2";
            c.Purl = "pkg:docker/redis@7.2";
            c.Description = "Redis cache";
        })
        .WithComponent(c =>
        {
            c.Type = Component.Classification.Library;
            c.Name = "react";
            c.Version = "18.2.0";
            c.Purl = "pkg:npm/react@18.2.0";
        })
    )
    .ForProject("ExampleConsoleApp2/ExampleConsoleApp2.csproj")

    // Custom component: Docker container that depends on .NET projects
    .ForComponent(comp => comp
        .WithMetadata(m =>
        {
            m.Name = "app-container";
            m.Version = "1.0.0";
            m.Type = Component.Classification.Container;
            m.BomRef = "pkg:docker/myorg/app-container@1.0.0";
            m.Purl = "pkg:docker/myorg/app-container@1.0.0";
            m.Description = "Production Docker container";
        })
        // Depends on .NET projects (cross-SBOM consistency)
        .DependsOnProject("ExampleConsoleApp1/ExampleConsoleApp1.csproj")
        .DependsOnProject("ExampleClassLibrary1/ExampleClassLibrary1.csproj")
        // Container base image
        .WithComponent(c =>
        {
            c.Name = "alpine";
            c.Version = "3.18";
            c.Purl = "pkg:docker/alpine@3.18";
            c.Type = Component.Classification.Container;
        })
    )

    // Custom component: Frontend app
    .ForComponent(comp => comp
        .WithMetadata(m =>
        {
            m.Name = "web-frontend";
            m.Version = "2.1.0";
            m.Type = Component.Classification.Application;
            m.BomRef = "pkg:npm/myorg/web-frontend@2.1.0";
            m.Purl = "pkg:npm/myorg/web-frontend@2.1.0";
        })
        .WithComponent(c =>
        {
            c.Name = "react";
            c.Version = "18.2.0";
            c.Purl = "pkg:npm/react@18.2.0";
        })
    )

    // Custom component: K8s deployment depending on other components
    .ForComponent(comp => comp
        .WithMetadata(m =>
        {
            m.Name = "k8s-deployment";
            m.Version = "1.0.0";
            m.Type = Component.Classification.Platform;
            m.BomRef = "pkg:generic/k8s-deployment@1.0.0";
            m.Purl = "pkg:generic/k8s-deployment@1.0.0";
        })
        // Depends on other custom components via BomRef
        .DependsOn("pkg:docker/myorg/app-container@1.0.0")
        .DependsOn("pkg:npm/myorg/web-frontend@2.1.0")
        .WithComponent(c =>
        {
            c.Name = "kubernetes";
            c.Version = "1.28";
            c.Purl = "pkg:generic/kubernetes@1.28";
            c.Scope = Component.ComponentScope.Excluded;  // Runtime platform
        })
    )

    .BuildAsync();
