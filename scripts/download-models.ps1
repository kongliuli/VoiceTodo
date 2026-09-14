<#
.SYNOPSIS
  Download on-device speech models (sherpa-onnx ASR / TTS) into the local ModelsLibrary/.
.DESCRIPTION
  Reads manifest.json and downloads/unpacks each candidate model into
  <repo>/ModelsLibrary/<RelativePath>/. All models are kept side-by-side so you can
  switch and A/B test them later in the app settings. Also writes a .filelist per model
  (recursive file list, including subdirs like espeak-ng-data/) used for packing/extraction.
  A single model failure only warns and continues; it does not abort the others.
.PARAMETER ManifestPath
  Path to manifest.json (default: scripts/../src/VoiceTodo.Maui/Resources/Models/manifest.json).
.PARAMETER LibraryRoot
  Local model library root (default: repo root ModelsLibrary/).
.PARAMETER Force
  Re-download even if files already exist.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/download-models.ps1
#>
param(
    [string]$ManifestPath = "$PSScriptRoot/../src/VoiceTodo.Maui/Resources/Models/manifest.json",
    [string]$LibraryRoot  = "$PSScriptRoot/../ModelsLibrary",
    [switch]$Force
)

$ErrorActionPreference = 'Continue'

# ---- helper functions (must be defined before use) ----

function Get-Archive {
    param([string]$Url, [string]$DestDir, [switch]$Force)
    $tmp = Join-Path $env:TEMP ("sherpa_" + [guid]::NewGuid().ToString("N") + ".dl")
    try {
        if ((Test-Path $DestDir) -and -not $Force) {
            Write-Host "[skip] already exists: $DestDir"
            return $true
        }
        Write-Host "[get ] $Url"
        Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
        New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
        $lower = $Url.ToLowerInvariant()
        if ($lower.EndsWith(".zip")) {
            Expand-Archive -Path $tmp -DestinationPath $DestDir -Force
        } elseif ($lower.EndsWith(".tar.bz2") -or $lower.EndsWith(".tbz2")) {
            tar -xjf $tmp -C $DestDir
        } elseif ($lower.EndsWith(".tar.gz") -or $lower.EndsWith(".tgz")) {
            tar -xzf $tmp -C $DestDir
        } elseif ($lower.EndsWith(".tar")) {
            tar -xf $tmp -C $DestDir
        } else {
            Write-Warning "Unsupported archive format: $Url"
            return $false
        }
        return $true
    } catch {
        Write-Warning "Unpack failed: $_"
        return $false
    } finally {
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
    }
}

function Get-Files {
    param($Entry, [string]$DestDir, [switch]$Force)
    $base = $Entry.DownloadBaseUrl.TrimEnd("/")
    $allOk = $true
    foreach ($file in $Entry.Files) {
        $dest = Join-Path $DestDir $file
        if ((Test-Path $dest) -and -not $Force) { Write-Host "[skip] exists: $dest"; continue }
        $url = "$base/$file"
        try {
            Write-Host "[get ] $url"
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
        } catch {
            Write-Warning "Download failed: $url ($_)"
            $allOk = $false
        }
    }
    return $allOk
}

function New-FileList {
    param([string]$ModelDir)
    $files = Get-ChildItem -Path $ModelDir -Recurse -File
    $lines = $files | ForEach-Object {
        $rel = $_.FullName.Substring($ModelDir.Length)
        $rel = $rel.TrimStart("\", "/")
        $rel.Replace("\", "/")
    }
    $out = Join-Path $ModelDir ".filelist"
    Set-Content -Path $out -Value ($lines -join "`n") -Encoding utf8
    Write-Host "[list] wrote .filelist ($($lines.Count) files)"
}

# ---- main ----

if (-not (Test-Path $ManifestPath)) { Write-Error "Manifest not found: $ManifestPath"; exit 1 }

$json = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json

if (-not (Test-Path $LibraryRoot)) { New-Item -ItemType Directory -Force -Path $LibraryRoot | Out-Null }
$LibraryRoot = Resolve-Path $LibraryRoot

foreach ($entry in $json.Entries) {
    $rel = $entry.RelativePath
    $destDir = Join-Path $LibraryRoot $rel
    Write-Host ""
    Write-Host "=== $($entry.Kind)/$rel ($($entry.Id)) ==="

    $ok = $false
    if (-not [string]::IsNullOrWhiteSpace($entry.ArchiveUrl)) {
        $ok = Get-Archive -Url $entry.ArchiveUrl -DestDir $destDir -Force:$Force
    }
    elseif (-not [string]::IsNullOrWhiteSpace($entry.DownloadBaseUrl)) {
        $ok = Get-Files -Entry $entry -DestDir $destDir -Force:$Force
    }
    else {
        Write-Warning "Entry $rel has neither ArchiveUrl nor DownloadBaseUrl; skipped."
        continue
    }

    if (-not $ok) {
        Write-Warning "Entry $rel download/unpack failed; skipped (others unaffected). Check ArchiveUrl."
        continue
    }

    New-FileList -ModelDir $destDir
    Write-Host "[ok ] ready: $destDir"
}

Write-Host ""
Write-Host "Done. All models kept side-by-side at: $LibraryRoot"
Write-Host "Next: edit models.pack.json to choose which to bundle, then run scripts/pack-models.ps1."
