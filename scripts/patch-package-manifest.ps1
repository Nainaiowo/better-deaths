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

function Update-Manifest {
    param(
        [string] $SourceManifestPath,
        [string] $TargetManifestPath
    )

    if (-not (Test-Path -LiteralPath $SourceManifestPath) -or -not (Test-Path -LiteralPath $TargetManifestPath)) {
        return $false
    }

    $source = Get-Content -Raw -LiteralPath $SourceManifestPath | ConvertFrom-Json
    $target = Get-Content -Raw -LiteralPath $TargetManifestPath | ConvertFrom-Json

    $copied = $false
    foreach ($propertyName in @("DownloadCount")) {
        $property = $source.PSObject.Properties[$propertyName]
        if ($null -eq $property) {
            continue
        }

        Set-JsonProperty -Target $target -Name $propertyName -Value $property.Value
        $copied = $true
    }

    if (-not $copied) {
        return $false
    }

    $json = ConvertTo-Json -InputObject $target -Depth 20
    [System.IO.File]::WriteAllText($TargetManifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    return $true
}

$projectFullPath = ConvertTo-AbsolutePath -BasePath (Get-Location).Path -Path $ProjectDir
$outputFullPath = ConvertTo-AbsolutePath -BasePath $projectFullPath -Path $OutputPath
$sourceManifestPath = Join-Path $projectFullPath "$AssemblyName.json"
$generatedManifestPath = Join-Path $outputFullPath "$AssemblyName.json"

$updatedManifest = Update-Manifest -SourceManifestPath $sourceManifestPath -TargetManifestPath $generatedManifestPath
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
    $updatedZipManifest = Update-Manifest -SourceManifestPath $sourceManifestPath -TargetManifestPath $zipManifestPath
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
