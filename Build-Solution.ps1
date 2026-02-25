param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

Write-Host "Building SbomForge solution with configuration: $Configuration" -ForegroundColor Cyan

$SolutionPath = Join-Path $PSScriptRoot 'Source' 'SbomForge.slnx'

# Build the solution
dotnet build $SolutionPath --configuration $Configuration --no-incremental

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Build completed successfully!" -ForegroundColor Green
