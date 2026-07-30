param ($manifestFile, $versionString)

$ErrorActionPreference = "Stop"

$manifest = Get-Content -LiteralPath $manifestFile
$manifest = $manifest -replace '"version_number":\s*"([^"]*)"', "`"version_number`": `"$versionString`""
Set-Content -LiteralPath $manifestFile -Value $manifest
