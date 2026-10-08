<#
.SYNOPSIS
    Script di compilazione e packaging per Linux (linux-x64) eseguibile su Windows / PowerShell.
.PARAMETER Version
    Versione del rilascio (default: 0.5.8).
.PARAMETER Configuration
    Configurazione di compilazione (default: Release).
#>
[CmdletBinding()]
param (
    [string]$Version = "0.5.8",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$ProjectFile = Join-Path $RootDir "src/Paddock.UI/Paddock.UI.csproj"
$UpdaterProjectFile = Join-Path $RootDir "src/Paddock.Updater/Paddock.Updater.csproj"
$ArtifactsDir = Join-Path $RootDir "artifacts"
$LinuxTempDir = Join-Path $ArtifactsDir "linux-temp"
$PublishDir = Join-Path $LinuxTempDir "publish"
$ZipFileName = "Paddock-$Version-linux-x64.zip"
$ZipFilePath = Join-Path $ArtifactsDir $ZipFileName

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Paddock - Linux Packaging (linux-x64, via pwsh)" -ForegroundColor Cyan
Write-Host " Versione:       $Version" -ForegroundColor Gray
Write-Host " Configurazione: $Configuration" -ForegroundColor Gray
Write-Host " Root:           $RootDir" -ForegroundColor Gray
Write-Host "====================================================" -ForegroundColor Cyan

# 1. Pulizia output precedenti
if (Test-Path $LinuxTempDir) {
    Write-Host "[1/4] Pulizia directory temporanea Linux..." -ForegroundColor Yellow
    Remove-Item -Path $LinuxTempDir -Recurse -Force
}
if (Test-Path $ZipFilePath) {
    Remove-Item -Path $ZipFilePath -Force
}
if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

# 2. Compilazione e pubblicazione nativa self-contained per linux-x64
Write-Host "[2/4] Esecuzione dotnet publish Paddock.UI (linux-x64, self-contained)..." -ForegroundColor Green
$dotnetArgs = @(
    "publish",
    $ProjectFile,
    "-c", $Configuration,
    "-r", "linux-x64",
    "--self-contained",
    "-p:PublishSingleFile=true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-o", $PublishDir
)

& dotnet @dotnetArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "La pubblicazione di Paddock per linux-x64 e fallita con codice $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "[2b/4] Esecuzione dotnet publish Paddock.Updater (linux-x64, self-contained)..." -ForegroundColor Green
$updaterArgs = @(
    "publish",
    $UpdaterProjectFile,
    "-c", $Configuration,
    "-r", "linux-x64",
    "--self-contained",
    "-p:PublishSingleFile=true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-o", $PublishDir
)

& dotnet @updaterArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "La pubblicazione di Paddock.Updater per linux-x64 e fallita con codice $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 3. Verifica presenza file binari ELF
$ElfPath = Join-Path $PublishDir "Paddock.UI"
if (-not (Test-Path $ElfPath)) {
    Write-Error "Il binario principale Paddock.UI non e stato trovato in $PublishDir."
    exit 1
}

$UpdaterElfPath = Join-Path $PublishDir "Paddock.Updater"
if (-not (Test-Path $UpdaterElfPath)) {
    Write-Error "Il binario Paddock.Updater non e stato trovato in $PublishDir."
    exit 1
}

# 4. Copia asset grafici e desktop entry per Linux
$LogoPng = Join-Path $RootDir "logo.png"
if (Test-Path $LogoPng) {
    Copy-Item $LogoPng (Join-Path $PublishDir "logo.png") -Force
}
$LogoJpg = Join-Path $RootDir "logo.jpg"
if (Test-Path $LogoJpg) {
    Copy-Item $LogoJpg (Join-Path $PublishDir "logo.jpg") -Force
}
$DesktopFile = Join-Path $RootDir "paddock.desktop"
if (Test-Path $DesktopFile) {
    Copy-Item $DesktopFile (Join-Path $PublishDir "paddock.desktop") -Force
}

# 5. Compressione archivio ZIP distribuibile
Write-Host "[3/4] Creazione archivio ZIP Linux: $ZipFileName..." -ForegroundColor Green
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipFilePath -Force

$ZipSizeMb = [math]::Round((Get-Item $ZipFilePath).Length / 1MB, 2)
Write-Host "[4/4] Pacchetto Linux creato con successo!" -ForegroundColor Cyan
Write-Host " Percorso: $ZipFilePath" -ForegroundColor White
Write-Host " Dimensione: $ZipSizeMb MB" -ForegroundColor White
Write-Host "====================================================" -ForegroundColor Cyan

# Pulizia staging
Remove-Item -Path $LinuxTempDir -Recurse -Force

