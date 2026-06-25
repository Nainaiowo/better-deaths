param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectDir,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $true)]
    [string] $AssemblyName
)

$ErrorActionPreference = "Stop"

function ConvertTo-AbsolutePath {
    param(
        [string] $BasePath,
        [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-DownloadCountSnapshot {
    param([string] $ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        return [pscustomobject]@{
            HasDownloadCount = $false
            DownloadCount = $null
        }
    }

    $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    $hasDownloadCount = $manifest.PSObject.Properties.Name -contains "DownloadCount"
    if (-not $hasDownloadCount) {
        return [pscustomobject]@{
            HasDownloadCount = $false
            DownloadCount = $null
        }
    }

    $downloadCount = [long] $manifest.DownloadCount
    if ($downloadCount -lt 0) {
        throw "DownloadCount must be zero or greater: $ManifestPath"
    }

    return [pscustomobject]@{
        HasDownloadCount = $true
        DownloadCount = $downloadCount
    }
}

function Save-Json {
    param(
        [string] $ManifestPath,
        [object] $Manifest
    )

    $json = ConvertTo-Json -InputObject $Manifest -Depth 20
    [System.IO.File]::WriteAllText($ManifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Sync-DownloadCountSnapshot {
    param(
        [string] $ManifestPath,
        [object] $ExpectedSnapshot
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        return
    }

    $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    $hasDownloadCount = $manifest.PSObject.Properties.Name -contains "DownloadCount"
    if ($ExpectedSnapshot.HasDownloadCount) {
        if ($hasDownloadCount) {
            $manifest.DownloadCount = $ExpectedSnapshot.DownloadCount
        }
        else {
            $manifest | Add-Member -NotePropertyName "DownloadCount" -NotePropertyValue $ExpectedSnapshot.DownloadCount
        }
    }
    elseif ($hasDownloadCount) {
        $manifest.PSObject.Properties.Remove("DownloadCount")
    }
    else {
        return
    }

    Save-Json -ManifestPath $ManifestPath -Manifest $manifest
}

function Assert-MatchingDownloadCountSnapshot {
    param(
        [string] $ManifestPath,
        [object] $ExpectedSnapshot
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        return
    }

    $actualSnapshot = Get-DownloadCountSnapshot -ManifestPath $ManifestPath
    if ($ExpectedSnapshot.HasDownloadCount -and -not $actualSnapshot.HasDownloadCount) {
        throw "Package manifest is missing expected DownloadCount snapshot $($ExpectedSnapshot.DownloadCount): $ManifestPath"
    }

    if (-not $ExpectedSnapshot.HasDownloadCount -and $actualSnapshot.HasDownloadCount) {
        throw "Package manifest has unexpected DownloadCount snapshot $($actualSnapshot.DownloadCount): $ManifestPath"
    }

    if ($ExpectedSnapshot.HasDownloadCount -and $actualSnapshot.DownloadCount -ne $ExpectedSnapshot.DownloadCount) {
        throw "Package manifest DownloadCount $($actualSnapshot.DownloadCount) does not match source snapshot $($ExpectedSnapshot.DownloadCount): $ManifestPath"
    }
}

$projectFullPath = ConvertTo-AbsolutePath -BasePath (Get-Location).Path -Path $ProjectDir
$outputFullPath = ConvertTo-AbsolutePath -BasePath $projectFullPath -Path $OutputPath
$sourceManifestPath = Join-Path $projectFullPath "$AssemblyName.json"
$generatedManifestPath = Join-Path $outputFullPath "$AssemblyName.json"
$packagedManifestPath = Join-Path (Join-Path $outputFullPath $AssemblyName) "$AssemblyName.json"
$expectedDownloadCountSnapshot = Get-DownloadCountSnapshot -ManifestPath $sourceManifestPath

Sync-DownloadCountSnapshot -ManifestPath $generatedManifestPath -ExpectedSnapshot $expectedDownloadCountSnapshot
Sync-DownloadCountSnapshot -ManifestPath $packagedManifestPath -ExpectedSnapshot $expectedDownloadCountSnapshot

Assert-MatchingDownloadCountSnapshot -ManifestPath $generatedManifestPath -ExpectedSnapshot $expectedDownloadCountSnapshot

$zipPath = Join-Path (Join-Path $outputFullPath $AssemblyName) "latest.zip"
if (-not (Test-Path -LiteralPath $zipPath)) {
    return
}

$tempPath = Join-Path ([System.IO.Path]::GetTempPath()) "$AssemblyName-package-validate-$([System.Guid]::NewGuid())"
New-Item -ItemType Directory -Path $tempPath | Out-Null
try {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $tempPath -Force
    Sync-DownloadCountSnapshot -ManifestPath (Join-Path $tempPath "$AssemblyName.json") -ExpectedSnapshot $expectedDownloadCountSnapshot
    Compress-Archive -Path (Join-Path $tempPath "*") -DestinationPath $zipPath -Force
    Assert-MatchingDownloadCountSnapshot -ManifestPath (Join-Path $tempPath "$AssemblyName.json") -ExpectedSnapshot $expectedDownloadCountSnapshot
    if ($expectedDownloadCountSnapshot.HasDownloadCount) {
        Write-Host "Synchronized and validated package manifest with DownloadCount snapshot $($expectedDownloadCountSnapshot.DownloadCount)."
    }
    else {
        Write-Host "Synchronized and validated package manifest without DownloadCount."
    }
}
finally {
    Remove-Item -LiteralPath $tempPath -Recurse -Force -ErrorAction SilentlyContinue
}
