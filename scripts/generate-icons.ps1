<#
.SYNOPSIS
    Genera logo.ico (multi-risoluzione per Windows) e logo.png a partire da logo.jpg.
#>
[CmdletBinding()]
param ()

$ErrorActionPreference = "Stop"
$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$SourceJpg = Join-Path $RootDir "logo.jpg"

if (-not (Test-Path $SourceJpg)) {
    Write-Error "File sorgente logo.jpg non trovato in $SourceJpg"
    exit 1
}

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile($SourceJpg)

try {
    # 1. Genera logo.png (512x512)
    $destPng = Join-Path $RootDir "logo.png"
    $png512 = New-Object System.Drawing.Bitmap 512, 512
    $g512 = [System.Drawing.Graphics]::FromImage($png512)
    $g512.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g512.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g512.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g512.DrawImage($img, 0, 0, 512, 512)
    $g512.Dispose()
    $png512.Save($destPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $png512.Dispose()
    Write-Host "Creato: $destPng" -ForegroundColor Green

    # 2. Genera logo.ico multi-risoluzione (16, 24, 32, 48, 64, 128, 256)
    $destIco = Join-Path $RootDir "logo.ico"
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $pngFrames = @()

    foreach ($s in $sizes) {
        $resized = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($resized)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($img, 0, 0, $s, $s)
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $resized.Dispose()
        $pngFrames += ,@($s, $ms.ToArray())
        $ms.Dispose()
    }

    $msIco = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $msIco

    # Header: reserved (0), type (1), count
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$pngFrames.Count)

    $offset = 6 + (16 * $pngFrames.Count)

    foreach ($entry in $pngFrames) {
        $s = $entry[0]
        $data = $entry[1]
        $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $h = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $bw.Write($w)
        $bw.Write($h)
        $bw.Write([byte]0) # ColorCount
        $bw.Write([byte]0) # Reserved
        $bw.Write([uint16]1) # Planes
        $bw.Write([uint16]32) # BitCount
        $bw.Write([uint32]$data.Length) # BytesInRes
        $bw.Write([uint32]$offset) # ImageOffset
        $offset += $data.Length
    }

    foreach ($entry in $pngFrames) {
        $data = $entry[1]
        $bw.Write($data)
    }
    $bw.Flush()

    $icoBytes = $msIco.ToArray()
    $msIco.Dispose()

    [System.IO.File]::WriteAllBytes($destIco, $icoBytes)
    Write-Host "Creato: $destIco ($($icoBytes.Length) byte)" -ForegroundColor Green

    # 3. Copia nelle cartelle Assets dei progetti
    $uiAssets = Join-Path $RootDir "src/Paddock.UI/Assets"
    $updaterAssets = Join-Path $RootDir "src/Paddock.Updater/Assets"

    if (-not (Test-Path $uiAssets)) {
        New-Item -ItemType Directory -Path $uiAssets -Force | Out-Null
    }
    if (-not (Test-Path $updaterAssets)) {
        New-Item -ItemType Directory -Path $updaterAssets -Force | Out-Null
    }

    Copy-Item $destIco (Join-Path $uiAssets "logo.ico") -Force
    Copy-Item $destPng (Join-Path $uiAssets "logo.png") -Force

    Copy-Item $destIco (Join-Path $updaterAssets "logo.ico") -Force
    Copy-Item $destPng (Join-Path $updaterAssets "logo.png") -Force
    Copy-Item $SourceJpg (Join-Path $updaterAssets "logo.jpg") -Force

    Write-Host "Risorse copiate con successo in Paddock.UI/Assets e Paddock.Updater/Assets." -ForegroundColor Cyan
}
finally {
    $img.Dispose()
}

