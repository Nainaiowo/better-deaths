param(
    [Parameter(Mandatory = $true)][string]$PullPath,
    [Parameter(Mandatory = $true)][string]$EncounterHistoryPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

function Read-GzipJson([string]$path) {
    $file = [System.IO.File]::OpenRead($path)
    $gzip = [System.IO.Compression.GZipStream]::new($file, [System.IO.Compression.CompressionMode]::Decompress)
    $reader = [System.IO.StreamReader]::new($gzip)
    try {
        return $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
        $gzip.Dispose()
        $file.Dispose()
    }
}

function Write-GzipJson($value, [string]$path, [string[]]$names) {
    $json = ConvertTo-Json -InputObject $value -Depth 100 -Compress
    $orderedNames = $names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique | Sort-Object { $_.Length } -Descending
    $index = 1
    foreach ($name in $orderedNames) {
        $replacement = "Player $index"
        $encoded = (ConvertTo-Json -InputObject $name -Compress).Trim('"')
        $json = $json.Replace($encoded, $replacement).Replace($name, $replacement)
        $index++
    }

    foreach ($name in $orderedNames) {
        if ($json.Contains($name) -or $json.Contains((ConvertTo-Json -InputObject $name -Compress).Trim('"'))) {
            throw "A player name remains in the generated sample."
        }
    }

    $null = [System.Text.Json.JsonDocument]::Parse($json)
    $file = [System.IO.File]::Create($path)
    $gzip = [System.IO.Compression.GZipStream]::new($file, [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = [System.IO.StreamWriter]::new($gzip, [System.Text.UTF8Encoding]::new($false))
    try {
        $writer.Write($json)
    }
    finally {
        $writer.Dispose()
        $gzip.Dispose()
        $file.Dispose()
    }
}

function In-SampleWindow([double]$seconds) {
    foreach ($window in @(@(225, 250), @(345, 390), @(500, 530), @(565, 605), @(645, 690))) {
        if ($seconds -ge $window[0] -and $seconds -le $window[1]) {
            return $true
        }
    }
    return $false
}

$pull = Read-GzipJson $PullPath
$history = Get-Content -LiteralPath $EncounterHistoryPath -Raw | ConvertFrom-Json
$encounter = $history.Encounters |
    Where-Object { [math]::Abs(($_.CapturedAtUtc - $pull.CapturedAtUtc).TotalSeconds) -lt 10 } |
    Select-Object -First 1
if ($null -eq $encounter) {
    throw 'No damage encounter matches the selected pull.'
}

$playerNames = @($pull.Deaths | ForEach-Object MemberName) +
    @($pull.ReplayPositions | Where-Object { $_.ActorKind -eq 'Player' } | ForEach-Object ActorName) +
    @($pull.ReplayMarkers | Where-Object { $_.ActorKind -eq 'Player' } | ForEach-Object ActorName) +
    @($pull.ReplayMitigations | ForEach-Object MemberName) +
    @($pull.ReplayDebuffs | ForEach-Object MemberName) +
    @($encounter.Snapshot.Sources | Where-Object { $_.Source.IsPlayer } | ForEach-Object { $_.Source.Name })
$playerNames = @($playerNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

$samplePositions = [System.Collections.Generic.List[object]]::new()
$seenPositionBins = [System.Collections.Generic.HashSet[string]]::new()
foreach ($position in $pull.ReplayPositions) {
    $bin = if (In-SampleWindow $position.PullElapsedSeconds) {
        "near:$([int][math]::Floor([double]$position.PullElapsedSeconds * 2))"
    }
    else {
        "general:$([int][math]::Floor([double]$position.PullElapsedSeconds))"
    }
    if ($seenPositionBins.Add("$($position.ActorKey):$bin")) {
        $samplePositions.Add($position)
    }
}

$pull.ReplayPositions = @($samplePositions.ToArray())
$pull.PullNumber = 0
$pull.Reason = 'Tutorial sample'
$pull.PullGroupId = ''
$pull.PullGroupColorIndex = -1
$pull.CapturedPluginVersion = 'tutorial'

$snapshot = $encounter.Snapshot
$snapshot.Events = @()
$snapshot.Targets = @()
$snapshot.Sources = @($snapshot.Sources | Where-Object { $_.Source.IsPlayer })
$snapshot.EndReason = 'Tutorial sample'
$snapshot.Diagnostics = $null

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
Write-GzipJson $pull (Join-Path $OutputDirectory 'review-replay-pull.json.gz') $playerNames
Write-GzipJson $snapshot (Join-Path $OutputDirectory 'meter-encounter.json.gz') $playerNames

[pscustomobject]@{
    Deaths = $pull.Deaths.Count
    ReplayPositions = $pull.ReplayPositions.Count
    ReplayMechanics = $pull.ReplayMechanics.Count
    AnalyzerEvents = $pull.ReplayAnalyzerEvents.Count
    MeterPlayers = $snapshot.Sources.Count
    PullSampleBytes = (Get-Item -LiteralPath (Join-Path $OutputDirectory 'review-replay-pull.json.gz')).Length
    MeterSampleBytes = (Get-Item -LiteralPath (Join-Path $OutputDirectory 'meter-encounter.json.gz')).Length
}
