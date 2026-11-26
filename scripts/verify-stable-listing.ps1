param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,
    [string] $RepositoryUrl = 'https://puni.sh/api/repository/nainai',
    [ValidateRange(1, 10)][int] $Attempts = 6,
    [ValidateRange(0, 60)][int] $DelaySeconds = 10
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $reader = [System.IO.StreamReader]::new($zip.GetEntry('BetterDeaths.json').Open())
    try { $expected = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
} finally { $zip.Dispose() }
if (!$expected.AssemblyVersion -or !$expected.Changelog) { throw 'Stable ZIP is missing its version or notes.' }

for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    try {
        $repo = Invoke-RestMethod -Uri $RepositoryUrl -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 15
        $entries = @($repo | Where-Object { $_.InternalName -ceq $expected.InternalName })
        if ($entries.Count -ne 1) { throw 'Expected one Better Deaths entry in the repository.' }
        $live = $entries[0]
        if ($live.AssemblyVersion -cne $expected.AssemblyVersion) {
            throw "Listing shows version '$($live.AssemblyVersion)', expected '$($expected.AssemblyVersion)'."
        }
        if (([string] $live.Changelog).Replace("`r`n", "`n").Trim() -cne $expected.Changelog.Replace("`r`n", "`n").Trim()) {
            throw "Listing changelog does not match the stable ZIP for $($expected.AssemblyVersion)."
        }
        Write-Host "Verified public stable version and changelog for $($expected.AssemblyVersion)."
        return
    } catch {
        Write-Warning "Check $attempt of ${Attempts}: $($_.Exception.Message)"
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
}
throw 'The public listing was not verified. Check Puni metadata, then retry only "Verify Puni stable listing" or choose "Re-run failed jobs". Do not rerun the publish job.'
