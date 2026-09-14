<#
.SYNOPSIS
  Copy selected models from ModelsLibrary/ into the MAUI asset dir (Resources/Models) for bundling.
.DESCRIPTION
  Reads the pack list (model Ids) from models.pack.json and copies each model from
  ModelsLibrary/<relpath> to src/VoiceTodo.Maui/Resources/Models/<relpath> (including .filelist
  and subdirs). Models not listed are removed from Resources/Models so they are excluded from the
  app package, keeping the package size down while still switchable later.
.PARAMETER PackJson
  Path to models.pack.json (default: scripts/../models.pack.json).
.PARAMETER All
  Ignore the pack list and bundle every model found in ModelsLibrary.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/pack-models.ps1
  powershell -ExecutionPolicy Bypass -File scripts/pack-models.ps1 -All
#>
param(
    [string]$PackJson     = "$PSScriptRoot/../models.pack.json",
    [string]$ManifestPath = "$PSScriptRoot/../src/VoiceTodo.Maui/Resources/Models/manifest.json",
    [string]$LibraryRoot  = "$PSScriptRoot/../ModelsLibrary",
    [string]$ModelsRoot   = "$PSScriptRoot/../src/VoiceTodo.Maui/Resources/Models",
    [switch]$All
)

$ErrorActionPreference = 'Stop'

$json = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json

$packIds = @()
if ($All) {
    $packIds = $json.Entries | ForEach-Object { $_.Id }
} else {
    if (-not (Test-Path $PackJson)) { Write-Error "Pack list not found: $PackJson"; exit 1 }
    $pack = Get-Content -Raw -Path $PackJson | ConvertFrom-Json
    $packIds = $pack.pack
}

foreach ($entry in $json.Entries) {
    $rel = $entry.RelativePath
    $src = Join-Path $LibraryRoot $rel
    $dst = Join-Path $ModelsRoot $rel

    if ($packIds -notcontains $entry.Id) {
        if (Test-Path $dst) { Remove-Item -Recurse -Force $dst; Write-Host "[drop] excluded from bundle: $rel" }
        continue
    }
    if (-not (Test-Path $src)) {
        Write-Warning "Model not downloaded; skip bundling: $rel (run scripts/download-models.ps1 first)"
        continue
    }
    if (Test-Path $dst) { Remove-Item -Recurse -Force $dst }
    Copy-Item -Recurse -Force -Path $src -Destination $dst
    Write-Host "[pack] $($entry.Id) -> Resources/Models/$rel"
}

Write-Host ""
Write-Host "Done. Selected models bundled into: $ModelsRoot"
Write-Host "Rebuild the MAUI app to apply."
