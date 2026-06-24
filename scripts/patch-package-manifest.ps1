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

function Remove-JsonProperty {
    param(
        [pscustomobject] $Target,
        [string] $Name
    )

    if ($Target.PSObject.Properties.Name -notcontains $Name) {
        return $false
    }

    $Target.PSObject.Properties.Remove($Name)
    return $true
}

function Update-Manifest {
    param(
        [string] $TargetManifestPath
    )

    if (-not (Test-Path -LiteralPath $TargetManifestPath)) {
        return $false
    }

    $target = Get-Content -Raw -LiteralPath $TargetManifestPath | ConvertFrom-Json

    $removed = $false
    foreach ($propertyName in @("DownloadCount")) {
        if (Remove-JsonProperty -Target $target -Name $propertyName) {
            $removed = $true
        }
    }

    if (-not $removed) {
        return $false
    }

    $json = ConvertTo-Json -InputObject $target -Depth 20
    [System.IO.File]::WriteAllText($TargetManifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    return $true
}

$projectFullPath = ConvertTo-AbsolutePath -BasePath (Get-Location).Path -Path $ProjectDir
$outputFullPath = ConvertTo-AbsolutePath -BasePath $projectFullPath -Path $OutputPath
$generatedManifestPath = Join-Path $outputFullPath "$AssemblyName.json"

$updatedManifest = Update-Manifest -TargetManifestPath $generatedManifestPath
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
    $updatedZipManifest = Update-Manifest -TargetManifestPath $zipManifestPath
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
