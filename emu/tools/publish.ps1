# Builds self-contained packages (no .NET install needed on the target PC).
#   pwsh tools/publish.ps1                       -> Windows x64 + Linux x64
#   pwsh tools/publish.ps1 -Runtimes osx-arm64   -> other targets
param(
    [string[]]$Runtimes = @("win-x64", "linux-x64"),
    [string]$Version = "0.1.0"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

foreach ($rid in $Runtimes) {
    $name = "RetroEmu-$Version-$rid"
    $out = Join-Path $dist $name
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    Write-Host "== $name"
    foreach ($proj in "src/Retro.App", "src/Retro.Cli") {
        dotnet publish (Join-Path $root $proj) -c Release -r $rid --self-contained true -o $out `
            -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló para $proj ($rid)" }
    }
    New-Item -ItemType Directory -Force (Join-Path $out "roms") | Out-Null
    Copy-Item (Join-Path $root "roms/48.rom") (Join-Path $out "roms/48.rom")
    # free C64 firmware (Open ROMs, LGPL-3) with its license
    Copy-Item -Recurse -Force (Join-Path $root "roms/c64") (Join-Path $out "roms/c64")
    # the folders the player fills: games (never included) and fusion games
    New-Item -ItemType Directory -Force (Join-Path $out "ROMs") | Out-Null
    "Poné acá tus juegos (sueltos o en .zip). El emulador reconoce solo para qué máquina es cada uno." |
        Set-Content (Join-Path $out "ROMs/LEEME.txt") -Encoding utf8
    New-Item -ItemType Directory -Force (Join-Path $out "Juegos fusionados") | Out-Null
    Copy-Item (Join-Path $root "README.md") (Join-Path $out "README.md")
    Copy-Item (Join-Path $root "docs/ARQUITECTURA.md") (Join-Path $out "ARQUITECTURA.md")
    @"
ROMs incluidas
--------------
48.rom: ROM del Sinclair ZX Spectrum 48K, (c) Amstrad plc. Amstrad autoriza su
distribución junto con emuladores, sin cargo y manteniendo este aviso de copyright.

c64/: Open ROMs (KERNAL, BASIC y juego de caracteres libres para el Commodore 64),
(c) Paul Gardner-Stephen y Roman Standzikowski, licencia GNU LGPL 3 (ver los textos
de la licencia en la misma carpeta). Si tenés las ROMs originales del C64 (kernal.rom,
basic.rom, chargen.rom), ponelas en esa carpeta y el emulador las prefiere.

Los juegos no se incluyen.
"@ | Set-Content (Join-Path $out "roms/LEEME-ROMS.txt") -Encoding utf8

    if ($rid.StartsWith("win")) {
        $zip = Join-Path $dist "$name.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path $out -DestinationPath $zip
        Write-Host "   -> $zip"
    }
    else {
        # tar.gz keeping the executable bit (needed on Linux and macOS).
        $tgz = Join-Path $dist "$name.tar.gz"
        python -I (Join-Path $PSScriptRoot "make_tar.py") $out $tgz
        if ($LASTEXITCODE -ne 0) { throw "no se pudo crear $tgz" }
        Write-Host "   -> $tgz"
    }
}
