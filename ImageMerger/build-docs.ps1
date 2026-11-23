# Builds the app (so ImageMerger.dll and ImageMerger.xml are up to date), then builds the docs.
# Usage:
#   .\build-docs.ps1          # build app + docs
#   .\build-docs.ps1 -Serve   # build app + docs, then preview at http://localhost:8080

param(
    [switch]$Serve
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "==> Building ImageMerger (Debug, x64)..." -ForegroundColor Cyan
dotnet build ImageMerger/ImageMerger.csproj -c Debug -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "App build failed. Docs were not generated." }

Write-Host "==> Building docs..." -ForegroundColor Cyan
if ($Serve) {
    docfx docfx.json --serve
} else {
    docfx docfx.json
}
if ($LASTEXITCODE -ne 0) { throw "docfx failed." }
