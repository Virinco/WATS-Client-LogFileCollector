# Unpack-LogFileCollector.ps1

$zipPath   = "LogFileCollector.zip"
$destPath  = "C:\ProgramData\Virinco\WATS\LogFileCollector"

# Sørg for at mappen finnes
if (-not (Test-Path -LiteralPath $destPath)) {
    New-Item -ItemType Directory -Force -Path $destPath | Out-Null
}

# Pakk ut zip-filen
Expand-Archive -LiteralPath $zipPath -DestinationPath $destPath -Force

Write-Host "LogFileCollector extracted to $destPath"
