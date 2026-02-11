param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

Write-Host "Publishing SbomForge with configuration: $Configuration, version: $Version" -ForegroundColor Cyan

# Define paths
$PublishDir = Join-Path $PSScriptRoot 'Publish'
$SbomForgeProject = Join-Path $PSScriptRoot 'Source' 'SbomForge' 'SbomForge.csproj'
$SbomToolProject = Join-Path $PSScriptRoot 'Source' 'SbomForge.Sbom' 'SbomForge.Sbom.csproj'

# Create Publish directory if it doesn't exist
if (-not (Test-Path $PublishDir)) {
    Write-Host "Creating Publish directory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $PublishDir | Out-Null
}

# Pack the SbomForge project
Write-Host "Packing SbomForge NuGet package..." -ForegroundColor Cyan
dotnet pack $SbomForgeProject `
    --configuration $Configuration `
    --output $PublishDir `
    /p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Error "Pack failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Build the SbomForge.Sbom tool
Write-Host "Building SbomForge.Sbom tool..." -ForegroundColor Cyan
dotnet build $SbomToolProject --configuration $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build of SbomForge.Sbom failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Run the SbomForge.Sbom tool to generate SBOM
Write-Host "Generating SBOM..." -ForegroundColor Cyan
$SbomToolProj = Join-Path $PSScriptRoot 'Source' 'SbomForge.Sbom' 'SbomForge.Sbom.csproj'
dotnet run --project $SbomToolProj -- $Version

if ($LASTEXITCODE -ne 0) {
    Write-Error "SBOM generation failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Publish completed successfully!" -ForegroundColor Green
Write-Host "Output directory: $PublishDir" -ForegroundColor Green
