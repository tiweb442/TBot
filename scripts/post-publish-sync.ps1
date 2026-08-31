# Sync local instance settings into publish/win64 after dotnet publish.
# dotnet publish can overwrite publish/win64/settings.json with the single-instance
# project template; authoritative copies live in .tbot-secrets (gitignored).

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SecretsDir = Join-Path $RepoRoot '.tbot-secrets'
$PublishDir = Join-Path $RepoRoot 'publish\win64'

if (-not (Test-Path -LiteralPath $SecretsDir -PathType Container)) {
    Write-Error "Secrets directory not found: $SecretsDir"
}

if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) {
    Write-Error "Publish directory not found: $PublishDir (run dotnet publish first)"
}

function Sync-FileToPublish {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $SourceFile
    )

    $dest = Join-Path $PublishDir $SourceFile.Name
    Copy-Item -LiteralPath $SourceFile.FullName -Destination $dest -Force
    $srcHash = (Get-FileHash -LiteralPath $SourceFile.FullName -Algorithm SHA256).Hash
    $destHash = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
    if ($srcHash -ne $destHash) {
        Write-Error "Hash mismatch after copy: $($SourceFile.Name)"
    }
    Write-Host ("Synced {0} ({1:N0} bytes)" -f $SourceFile.Name, $SourceFile.Length)
}

$syncedCount = 0

$settingsSrc = Join-Path $SecretsDir 'settings.json'
if (Test-Path -LiteralPath $settingsSrc -PathType Leaf) {
    Sync-FileToPublish -SourceFile (Get-Item -LiteralPath $settingsSrc)
    $syncedCount++
}

$localFiles = @(Get-ChildItem -LiteralPath $SecretsDir -Filter '*.local.json' -File)
if ($syncedCount -eq 0 -and $localFiles.Count -eq 0) {
    Write-Error "No settings.json or *.local.json files in $SecretsDir"
}

foreach ($src in $localFiles) {
    Sync-FileToPublish -SourceFile $src
    $syncedCount++
}

Write-Host "Post-publish sync complete: $syncedCount file(s) -> $PublishDir"
