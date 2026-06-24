param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectDir,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $true)]
    [string] $AssemblyName,

    [string] $RepoJsonUrl = "https://raw.githubusercontent.com/Nainaiowo/IMakeSillyThings/refs/heads/main/repo.json"
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

function Set-JsonProperty {
    param(
        [pscustomobject] $Target,
        [string] $Name,
        [object] $Value
    )

    if ($Target.PSObject.Properties.Name -contains $Name) {
        $Target.$Name = $Value
        return
    }

    $Target | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
}

function Get-RepoDownloadCount {
    param(
        [string] $InternalName,
        [string] $Url
    )

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $null
    }

    try {
        $repo = Invoke-RestMethod -Uri $Url -Headers @{ "Cache-Control" = "no-cache" } -Method Get
        foreach ($plugin in @($repo)) {
            if ($plugin.InternalName -eq $InternalName -and $null -ne $plugin.DownloadCount) {
                return [int] $plugin.DownloadCount
            }
        }
    }
    catch {
        Write-Warning "Could not read DownloadCount from $Url`: $($_.Exception.Message)"
    }

    return $null
}

function Update-Manifest {
    param(
        [string] $TargetManifestPath,
        [Nullable[int]] $DownloadCount
    )

    if (-not (Test-Path -LiteralPath $TargetManifestPath)) {
        return $false
    }

    $target = Get-Content -Raw -LiteralPath $TargetManifestPath | ConvertFrom-Json

    if ($null -eq $DownloadCount) {
        return $false
    }

    Set-JsonProperty -Target $target -Name "DownloadCount" -Value $DownloadCount

    $json = ConvertTo-Json -InputObject $target -Depth 20
    [System.IO.File]::WriteAllText($TargetManifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    return $true
}

$projectFullPath = ConvertTo-AbsolutePath -BasePath (Get-Location).Path -Path $ProjectDir
$outputFullPath = ConvertTo-AbsolutePath -BasePath $projectFullPath -Path $OutputPath
$generatedManifestPath = Join-Path $outputFullPath "$AssemblyName.json"
$downloadCount = Get-RepoDownloadCount -InternalName $AssemblyName -Url $RepoJsonUrl

if ($null -eq $downloadCount) {
    Write-Warning "DownloadCount unavailable for $AssemblyName; package manifest will keep its generated value."
}
else {
    Write-Host "Using DownloadCount $downloadCount from $RepoJsonUrl"
}

$updatedManifest = Update-Manifest -TargetManifestPath $generatedManifestPath -DownloadCount $downloadCount
if ($updatedManifest) {
    Write-Host "Patched $generatedManifestPath"
}

$zipPath = Join-Path (Join-Path $outputFullPath $AssemblyName) "latest.zip"
if (-not (Test-Path -LiteralPath $zipPath)) {
    return
}

$tempPath = Join-Path ([System.IO.Path]::GetTempPath()) "$AssemblyName-package-$([System.Guid]::NewGuid())"
New-Item -ItemType Directory -Path $tempPath | Out-Null
try {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $tempPath -Force
    $zipManifestPath = Join-Path $tempPath "$AssemblyName.json"
    $updatedZipManifest = Update-Manifest -TargetManifestPath $zipManifestPath -DownloadCount $downloadCount
    if (-not $updatedZipManifest) {
        return
    }

    Remove-Item -LiteralPath $zipPath -Force
    Compress-Archive -Path (Join-Path $tempPath "*") -DestinationPath $zipPath -Force
    Write-Host "Patched $zipPath"
}
finally {
    Remove-Item -LiteralPath $tempPath -Recurse -Force -ErrorAction SilentlyContinue
}
