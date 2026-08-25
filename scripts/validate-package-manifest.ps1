param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectDir,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $true)]
    [string] $AssemblyName,

    [string] $RepositoryUrl = "https://puni.sh/api/repository/nainai"
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

function Get-LiveDownloadCount {
    param(
        [string] $InternalName,
        [string] $Url
    )

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $null
    }

    try {
        $plugins = Invoke-RestMethod `
            -Uri $Url `
            -Headers @{
                "Cache-Control" = "no-cache"
                "User-Agent" = "BetterDeaths-Packager"
            } `
            -Method Get `
            -TimeoutSec 15
        $plugin = @($plugins) |
            Where-Object { $_.InternalName -eq $InternalName } |
            Select-Object -First 1
        if ($null -eq $plugin -or $null -eq $plugin.DownloadCount) {
            Write-Warning "Could not find DownloadCount for $InternalName in $Url."
            return $null
        }

        $downloadCount = [long] $plugin.DownloadCount
        if ($downloadCount -lt 0) {
            Write-Warning "Puni returned an invalid DownloadCount for ${InternalName}: $downloadCount."
            return $null
        }

        return $downloadCount
    }
    catch {
        Write-Warning "Could not read DownloadCount from $Url`: $($_.Exception.Message)"
        return $null
    }
}

function Resolve-PackagedDownloadCountSnapshot {
    param(
        [string] $InternalName,
        [string] $Url,
        [object] $LegacySnapshot
    )

    $liveDownloadCount = Get-LiveDownloadCount -InternalName $InternalName -Url $Url
    if ($null -eq $liveDownloadCount) {
        if ($LegacySnapshot.HasDownloadCount) {
            Write-Host "Using legacy DownloadCount fallback $($LegacySnapshot.DownloadCount)."
        }

        return $LegacySnapshot
    }

    if ($LegacySnapshot.HasDownloadCount -and $liveDownloadCount -lt $LegacySnapshot.DownloadCount) {
        Write-Warning "Puni DownloadCount $liveDownloadCount is below legacy baseline $($LegacySnapshot.DownloadCount); using the legacy fallback."
        return $LegacySnapshot
    }

    # Puni's live total already includes the imported legacy baseline; only subtract for reporting.
    if ($LegacySnapshot.HasDownloadCount) {
        $puniDownloads = $liveDownloadCount - $LegacySnapshot.DownloadCount
        Write-Host "Using combined DownloadCount $liveDownloadCount (legacy $($LegacySnapshot.DownloadCount) + Puni $puniDownloads)."
    }
    else {
        Write-Host "Using Puni DownloadCount $liveDownloadCount."
    }

    return [pscustomobject]@{
        HasDownloadCount = $true
        DownloadCount = $liveDownloadCount
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
        throw "Package manifest DownloadCount $($actualSnapshot.DownloadCount) does not match expected snapshot $($ExpectedSnapshot.DownloadCount): $ManifestPath"
    }
}

function Assert-JsonPropertyEquals {
    param(
        [string] $ManifestPath,
        [string] $PropertyName,
        [object] $ExpectedValue
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "Required manifest does not exist: $ManifestPath"
    }

    $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    $property = $manifest.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        throw "Manifest is missing required property ${PropertyName}: $ManifestPath"
    }

    if ([string] $property.Value -ne [string] $ExpectedValue) {
        throw "Manifest ${PropertyName} '$($property.Value)' does not match expected value '$ExpectedValue': $ManifestPath"
    }
}

function Assert-PackageManifest {
    param(
        [string] $ManifestPath,
        [string] $ExpectedInternalName,
        [string] $ExpectedVersion,
        [int] $ExpectedDalamudApiLevel,
        [object] $ExpectedDownloadCountSnapshot
    )

    Assert-JsonPropertyEquals -ManifestPath $ManifestPath -PropertyName "InternalName" -ExpectedValue $ExpectedInternalName
    Assert-JsonPropertyEquals -ManifestPath $ManifestPath -PropertyName "AssemblyVersion" -ExpectedValue $ExpectedVersion
    Assert-JsonPropertyEquals -ManifestPath $ManifestPath -PropertyName "DalamudApiLevel" -ExpectedValue $ExpectedDalamudApiLevel
    Assert-MatchingDownloadCountSnapshot -ManifestPath $ManifestPath -ExpectedSnapshot $ExpectedDownloadCountSnapshot
}

function Assert-ExactPackageFiles {
    param(
        [string] $DirectoryPath,
        [string] $ExpectedAssemblyName
    )

    $rootPath = [System.IO.Path]::GetFullPath($DirectoryPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $expectedFiles = @(
        "$ExpectedAssemblyName.deps.json",
        "$ExpectedAssemblyName.dll",
        "$ExpectedAssemblyName.json",
        "THIRD_PARTY_NOTICES.md"
    ) | Sort-Object
    $actualFiles = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File | ForEach-Object {
        $_.FullName.Substring($rootPath.Length).TrimStart(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar).Replace("\", "/")
    } | Sort-Object)
    $difference = @(Compare-Object -ReferenceObject $expectedFiles -DifferenceObject $actualFiles)
    if ($difference.Count -gt 0) {
        throw "Package files must be exactly [$($expectedFiles -join ', ')], found [$($actualFiles -join ', ')]."
    }
}

$projectFullPath = ConvertTo-AbsolutePath -BasePath (Get-Location).Path -Path $ProjectDir
$outputFullPath = ConvertTo-AbsolutePath -BasePath $projectFullPath -Path $OutputPath
$sourceManifestPath = Join-Path $projectFullPath "$AssemblyName.json"
$generatedManifestPath = Join-Path $outputFullPath "$AssemblyName.json"
$packagedDirectoryPath = Join-Path $outputFullPath $AssemblyName
$packagedManifestPath = Join-Path $packagedDirectoryPath "$AssemblyName.json"
$thirdPartyNoticeSourcePath = Join-Path (Split-Path -Parent $projectFullPath) "THIRD_PARTY_NOTICES.md"
$packagedThirdPartyNoticePath = Join-Path $packagedDirectoryPath "THIRD_PARTY_NOTICES.md"
$projectFilePath = Join-Path $projectFullPath "$AssemblyName.csproj"
if (-not (Test-Path -LiteralPath $projectFilePath)) {
    throw "Project file does not exist: $projectFilePath"
}

if (-not (Test-Path -LiteralPath $thirdPartyNoticeSourcePath)) {
    throw "Third-party notice does not exist: $thirdPartyNoticeSourcePath"
}

Copy-Item -LiteralPath $thirdPartyNoticeSourcePath -Destination $packagedThirdPartyNoticePath -Force

[xml] $project = Get-Content -Raw -LiteralPath $projectFilePath
$versionNode = $project.SelectSingleNode("/Project/PropertyGroup/Version")
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "Project Version is missing: $projectFilePath"
}

$expectedVersion = $versionNode.InnerText.Trim()
$projectSdk = [string] $project.Project.Sdk
if ($projectSdk -notmatch "^Dalamud\.NET\.Sdk/(?<ApiLevel>\d+)(?:\.|$)") {
    throw "Could not derive Dalamud API level from project SDK '$projectSdk': $projectFilePath"
}

$expectedDalamudApiLevel = [int] $Matches.ApiLevel
$legacyDownloadCountSnapshot = Get-DownloadCountSnapshot -ManifestPath $sourceManifestPath
$packagedDownloadCountSnapshot = Resolve-PackagedDownloadCountSnapshot `
    -InternalName $AssemblyName `
    -Url $RepositoryUrl `
    -LegacySnapshot $legacyDownloadCountSnapshot
Assert-JsonPropertyEquals -ManifestPath $sourceManifestPath -PropertyName "InternalName" -ExpectedValue $AssemblyName

Sync-DownloadCountSnapshot -ManifestPath $generatedManifestPath -ExpectedSnapshot $packagedDownloadCountSnapshot
Sync-DownloadCountSnapshot -ManifestPath $packagedManifestPath -ExpectedSnapshot $packagedDownloadCountSnapshot

Assert-PackageManifest `
    -ManifestPath $generatedManifestPath `
    -ExpectedInternalName $AssemblyName `
    -ExpectedVersion $expectedVersion `
    -ExpectedDalamudApiLevel $expectedDalamudApiLevel `
    -ExpectedDownloadCountSnapshot $packagedDownloadCountSnapshot
Assert-PackageManifest `
    -ManifestPath $packagedManifestPath `
    -ExpectedInternalName $AssemblyName `
    -ExpectedVersion $expectedVersion `
    -ExpectedDalamudApiLevel $expectedDalamudApiLevel `
    -ExpectedDownloadCountSnapshot $packagedDownloadCountSnapshot

$zipPath = Join-Path (Join-Path $outputFullPath $AssemblyName) "latest.zip"
if (-not (Test-Path -LiteralPath $zipPath)) {
    return
}

$tempPath = Join-Path ([System.IO.Path]::GetTempPath()) "$AssemblyName-package-validate-$([System.Guid]::NewGuid())"
New-Item -ItemType Directory -Path $tempPath | Out-Null
try {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $tempPath -Force
    Copy-Item -LiteralPath $thirdPartyNoticeSourcePath -Destination (Join-Path $tempPath "THIRD_PARTY_NOTICES.md") -Force
    Sync-DownloadCountSnapshot -ManifestPath (Join-Path $tempPath "$AssemblyName.json") -ExpectedSnapshot $packagedDownloadCountSnapshot
    Compress-Archive -Path (Join-Path $tempPath "*") -DestinationPath $zipPath -Force
    Assert-ExactPackageFiles -DirectoryPath $tempPath -ExpectedAssemblyName $AssemblyName
    Assert-PackageManifest `
        -ManifestPath (Join-Path $tempPath "$AssemblyName.json") `
        -ExpectedInternalName $AssemblyName `
        -ExpectedVersion $expectedVersion `
        -ExpectedDalamudApiLevel $expectedDalamudApiLevel `
        -ExpectedDownloadCountSnapshot $packagedDownloadCountSnapshot
    if ($packagedDownloadCountSnapshot.HasDownloadCount) {
        Write-Host "Validated Better Deaths package invariants with DownloadCount snapshot $($packagedDownloadCountSnapshot.DownloadCount)."
    }
    else {
        Write-Host "Validated Better Deaths package invariants without DownloadCount."
    }
}
finally {
    Remove-Item -LiteralPath $tempPath -Recurse -Force -ErrorAction SilentlyContinue
}
