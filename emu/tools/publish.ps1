# Builds self-contained packages (no .NET install needed on the target PC).
#   pwsh tools/publish.ps1                       -> Windows x64 + Linux x64
#   pwsh tools/publish.ps1 -Runtimes osx-arm64   -> other targets
param(
    [ValidatePattern('^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$')]
    [string[]]$Runtimes = @("win-x64", "linux-x64"),
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "0.3.2"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = [IO.Path]::GetFullPath((Join-Path $root "dist"))
if ((Test-Path -LiteralPath $dist) -and ((Get-Item -LiteralPath $dist).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "La carpeta dist no puede ser un enlace."
}
New-Item -ItemType Directory -Force $dist | Out-Null

foreach ($rid in $Runtimes) {
    $name = "RetroEmu-$Version-$rid"
    $out = [IO.Path]::GetFullPath((Join-Path $dist $name))
    if ([IO.Directory]::GetParent($out).FullName -ne $dist) { throw "Destino fuera de dist: $out" }
    if (Test-Path -LiteralPath $out) {
        if ((Get-Item -LiteralPath $out).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "El destino no puede ser un enlace: $out"
        }
        Remove-Item -LiteralPath $out -Recurse -Force
    }
    Write-Host "== $name"
    foreach ($proj in "src/Retro.App", "src/Retro.Cli") {
        dotnet publish (Join-Path $root $proj) -c Release -r $rid --self-contained true -o $out -m:1 `
            -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló para $proj ($rid)" }
    }
    # system firmware in its own folder: on Windows "roms" and the games folder "ROMs" would be the same folder
    New-Item -ItemType Directory -Force (Join-Path $out "firmware") | Out-Null
    Copy-Item (Join-Path $root "roms/48.rom") (Join-Path $out "firmware/48.rom")
    # free C64 firmware (Open ROMs, LGPL-3) with its license
    Copy-Item -Recurse -Force (Join-Path $root "roms/c64") (Join-Path $out "firmware/c64")
    foreach ($folder in 'ROMs', 'Juegos fusionados', 'Juegos creados') {
        $library = Join-Path $out $folder
        New-Item -ItemType Directory -Force $library | Out-Null
        Copy-Item -LiteralPath (Join-Path $root "docs/paquete/$folder.txt") -Destination (Join-Path $library 'LEEME.txt')
    }
    $pcFirmware = Join-Path $out 'firmware/pcxt'
    New-Item -ItemType Directory -Force $pcFirmware | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'docs/paquete/PCXT.txt') -Destination (Join-Path $pcFirmware 'LEEME.txt')
    Copy-Item -LiteralPath (Join-Path $root 'docs/paquete/ACTUALIZAR.txt') -Destination (Join-Path $out 'ACTUALIZAR.txt')
    Copy-Item (Join-Path $root "README.md") (Join-Path $out "README.md")
    $playerDocs = Join-Path $out "docs"
    New-Item -ItemType Directory -Force $playerDocs | Out-Null
    Copy-Item (Join-Path $root "docs/MODIFICACIONES.md") (Join-Path $playerDocs "MODIFICACIONES.md")
    Copy-Item (Join-Path $root "docs/SONIDO.md") (Join-Path $playerDocs "SONIDO.md")
    Copy-Item (Join-Path $root "docs/NOVEDADES-0.3.2.md") (Join-Path $playerDocs "NOVEDADES-0.3.2.md")
    Copy-Item (Join-Path $root "docs/PC-DOS.md") (Join-Path $playerDocs "PC-DOS.md")
    Copy-Item (Join-Path $root "docs/PCXT.md") (Join-Path $playerDocs "PCXT.md")
    Copy-Item (Join-Path $root "docs/I8088.md") (Join-Path $playerDocs "I8088.md")
    Copy-Item (Join-Path $root "docs/PIT8253.md") (Join-Path $playerDocs "PIT8253.md")
    Copy-Item (Join-Path $root "docs/CONECTAR-IA.md") (Join-Path $playerDocs "CONECTAR-IA.md")
    $playerImages = Join-Path $playerDocs "img/interfaz"
    New-Item -ItemType Directory -Force $playerImages | Out-Null
    foreach ($image in "biblioteca.png", "biblioteca-compacta.png", "biblioteca-pc.png", "modificaciones.png", "favoritos.png") {
        Copy-Item (Join-Path $root "docs/img/interfaz/$image") (Join-Path $playerImages $image)
    }
    Copy-Item (Join-Path $root "docs/ARQUITECTURA.md") (Join-Path $playerDocs "ARQUITECTURA.md")
    @"
Firmware incluido
-----------------
48.rom: ROM del Sinclair ZX Spectrum 48K, (c) Amstrad plc. Amstrad autoriza su
distribución junto con emuladores, sin cargo y manteniendo este aviso de copyright.

c64/: Open ROMs (KERNAL, BASIC y juego de caracteres libres para el Commodore 64),
(c) Paul Gardner-Stephen y Roman Standzikowski, licencia GNU LGPL 3 (ver los textos
de la licencia en la misma carpeta). Si tenés las ROMs originales del C64 (kernal.rom,
basic.rom, chargen.rom), ponelas en esa carpeta y el emulador las prefiere.

PC/XT: incluye GLaBIOS y FreeDOS en system/pcxt, con licencias y fuentes.
Para usar tu BIOS, agregalo como pcxt/pcxt-bios.rom. Ver docs/PC-DOS.md.

Los juegos no se incluyen.
"@ | Set-Content (Join-Path $out "firmware/LEEME.txt") -Encoding utf8

    python -I (Join-Path $PSScriptRoot 'make_update_manifest.py') $out $Version $rid
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar el registro de actualización.' }
    if ($rid.StartsWith("win")) {
        $zip = Join-Path $dist "$name.zip"
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
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
