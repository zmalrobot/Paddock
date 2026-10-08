<#
.SYNOPSIS
    Script di compilazione e packaging per Windows (win-x64).
.PARAMETER Version
    Versione del rilascio (default: 0.5.5).
.PARAMETER Configuration
    Configurazione di compilazione (default: Release).
#>
[CmdletBinding()]
param (
    [string]$Version = "0.5.5",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$ProjectFile = Join-Path $RootDir "src/Paddock.UI/Paddock.UI.csproj"
$UpdaterProjectFile = Join-Path $RootDir "src/Paddock.Updater/Paddock.Updater.csproj"
$ArtifactsDir = Join-Path $RootDir "artifacts"
$WindowsTempDir = Join-Path $ArtifactsDir "windows-temp"
$PublishDir = Join-Path $WindowsTempDir "publish"
$ZipFileName = "Paddock-$Version-windows-x64.zip"
$ZipFilePath = Join-Path $ArtifactsDir $ZipFileName

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Paddock - Windows Packaging (win-x64)" -ForegroundColor Cyan
Write-Host " Versione:       $Version" -ForegroundColor Gray
Write-Host " Configurazione: $Configuration" -ForegroundColor Gray
Write-Host " Root:           $RootDir" -ForegroundColor Gray
Write-Host "====================================================" -ForegroundColor Cyan

# 1. Pulizia output precedenti
if (Test-Path $WindowsTempDir) {
    Write-Host "[1/4] Pulizia directory temporanea Windows..." -ForegroundColor Yellow
    Remove-Item -Path $WindowsTempDir -Recurse -Force
}
if (Test-Path $ZipFilePath) {
    Remove-Item -Path $ZipFilePath -Force
}
if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

# 2. Compilazione e pubblicazione nativa self-contained
Write-Host "[2/4] Esecuzione dotnet publish Paddock.UI (win-x64, self-contained)..." -ForegroundColor Green
$dotnetArgs = @(
    "publish",
    $ProjectFile,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained",
    "-p:PublishSingleFile=true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-o", $PublishDir
)

& dotnet @dotnetArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "La pubblicazione di Paddock per win-x64 e fallita con codice $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "[2b/4] Esecuzione dotnet publish Paddock.Updater (win-x64, self-contained)..." -ForegroundColor Green
$updaterArgs = @(
    "publish",
    $UpdaterProjectFile,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained",
    "-p:PublishSingleFile=true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-o", $PublishDir
)

& dotnet @updaterArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "La pubblicazione di Paddock.Updater per win-x64 e fallita con codice $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 3. Verifica presenza file eseguibili e librerie native
$ExePath = Join-Path $PublishDir "Paddock.UI.exe"
if (-not (Test-Path $ExePath)) {
    Write-Error "L eseguibile principale Paddock.UI.exe non e stato trovato in $PublishDir."
    exit 1
}

$UpdaterExePath = Join-Path $PublishDir "Paddock.Updater.exe"
if (-not (Test-Path $UpdaterExePath)) {
    Write-Error "L eseguibile Paddock.Updater.exe non e stato trovato in $PublishDir."
    exit 1
}

# 4. Compressione archivio ZIP distribuibile
Write-Host "[3/4] Creazione archivio ZIP: $ZipFileName..." -ForegroundColor Green
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipFilePath -Force

$ZipSizeMb = [math]::Round((Get-Item $ZipFilePath).Length / 1MB, 2)
Write-Host "[4/4] Pacchetto creato con successo!" -ForegroundColor Cyan
Write-Host " Percorso: $ZipFilePath" -ForegroundColor White
Write-Host " Dimensione: $ZipSizeMb MB" -ForegroundColor White
Write-Host "====================================================" -ForegroundColor Cyan

# Rimuove la cartella temporanea di staging per lasciare solo lo zip
Remove-Item -Path $WindowsTempDir -Recurse -Force

