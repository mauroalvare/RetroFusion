# Updates the same local installation; never creates a separate app for a feature.
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PackageFolder = '',
    [string]$Destination = '',
    [string]$ShortcutPath = ''
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'La actualización local y el acceso directo son para Windows.' }
$emuRoot = Split-Path -Parent $PSScriptRoot
$workspace = Split-Path -Parent (Split-Path -Parent $emuRoot)
if (!$PackageFolder) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $emuRoot 'src/Retro.App/Retro.App.csproj') -Raw
    $version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $PackageFolder = Join-Path $emuRoot "dist/RetroEmu-$version-win-x64"
}
if (!$ShortcutPath) { $ShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'RetroEmu.lnk' }
if (!$Destination) {
    $Destination = Join-Path $workspace 'Programa'
    # Follow the one existing installation without changing its location.
    if (Test-Path -LiteralPath $ShortcutPath -PathType Leaf) {
        $shell = New-Object -ComObject WScript.Shell
        $existing = $null
        try {
            $existing = $shell.CreateShortcut([IO.Path]::GetFullPath($ShortcutPath))
            if ($existing.TargetPath -and (Test-Path -LiteralPath $existing.TargetPath -PathType Leaf)) {
                $installed = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($existing.TargetPath))
                if ([IO.Path]::GetFileName($existing.TargetPath) -eq 'RetroEmu.exe' -and
                    [IO.Path]::GetDirectoryName($installed) -eq $workspace -and
                    [IO.Path]::GetFileName($installed) -match '^(Programa|RetroEmu-\d+\.\d+\.\d+-win-x64)$') {
                    $Destination = $installed
                }
            }
        }
        finally {
            if ($existing) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($existing) | Out-Null }
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
        }
    }
}
$source = [IO.Path]::GetFullPath($PackageFolder).TrimEnd('\')
$target = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ($source -eq $target -or $source.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $target.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or $target -eq [IO.Path]::GetPathRoot($target).TrimEnd('\')) {
    throw 'El paquete y la instalación deben estar en carpetas separadas; no se admite la raíz de una unidad.'
}
foreach ($folder in @($source, $target)) {
    if ((Test-Path -LiteralPath $folder) -and ((Get-Item -LiteralPath $folder).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "No se actualiza a través de un enlace: $folder"
    }
}
foreach ($file in 'RetroEmu.exe', 'RetroEmu.dll', 'retro-cli.exe') {
    if (!(Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)) { throw "Paquete incompleto: falta $file" }
}
$running = Get-Process -Name RetroEmu,retro-cli -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)
}
if ($running) { throw 'Cerrá RetroEmu antes de actualizar la instalación.' }
$entries = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'El paquete contiene enlaces.' }
if ((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {
    throw 'La instalación contiene enlaces. No se modificó.'
}
if (!$PSCmdlet.ShouldProcess($target, 'Actualizar RetroEmu y su acceso directo, conservando los datos')) { return }
$backup = Join-Path (Split-Path -Parent $target) ('Respaldo/actualizacion-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$plan = @($entries | Where-Object { !$_.PSIsContainer } | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($source, $_.FullName)
    $first = $relative.Split([IO.Path]::DirectorySeparatorChar)[0]
    if ($first -in @('ROMs', 'Juegos fusionados', 'Juegos creados')) {
        # Only add package instructions; never replace any player file.
        if ($relative -ne (Join-Path $first 'LEEME.txt') -or (Test-Path -LiteralPath (Join-Path $target $relative))) { return }
    }
    if ($relative -eq 'ARQUITECTURA.md') { $relative = 'docs/ARQUITECTURA.md' }
    $dest = Join-Path $target $relative
    if ($first -eq 'firmware' -and (Test-Path -LiteralPath $dest)) { return }
    [pscustomobject]@{Source=$_.FullName;Destination=$dest;Relative=$relative;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
foreach ($item in $plan) {
    if (Test-Path -LiteralPath $item.Destination) {
        $saved = Join-Path $backup $item.Relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
        Copy-Item -LiteralPath $item.Destination -Destination $saved
    }
}
foreach ($item in $plan) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $item.Destination) -Force | Out-Null
    Copy-Item -LiteralPath $item.Source -Destination $item.Destination -Force
    if ((Get-FileHash -LiteralPath $item.Destination).Hash -ne $item.Sha256) { throw "No coincide el archivo instalado: $($item.Relative)" }
}
# Retire old debug files and the former root-level technical document, with a backup.
foreach ($old in @(Get-ChildItem -LiteralPath $target -Filter '*.pdb' -File) + @(Get-Item -LiteralPath (Join-Path $target 'ARQUITECTURA.md') -ErrorAction SilentlyContinue)) {
    $saved = Join-Path $backup ('retirados/' + $old.Name)
    New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
    Move-Item -LiteralPath $old.FullName -Destination $saved
}
New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($ShortcutPath))) -Force | Out-Null
if (Test-Path -LiteralPath $ShortcutPath) { Copy-Item -LiteralPath $ShortcutPath -Destination (Join-Path $backup 'acceso-anterior.lnk') }
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut([IO.Path]::GetFullPath($ShortcutPath))
$shortcut.TargetPath = Join-Path $target 'RetroEmu.exe'
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'RetroEmu: jugar, fusionar, crear y modificar'
$shortcut.IconLocation = $shortcut.TargetPath + ',0'
$shortcut.Save()
$plan | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $backup 'archivos-actualizados.json') -Encoding utf8
Write-Output "Programa actualizado: $target"
Write-Output "Acceso: $ShortcutPath"
Write-Output "Respaldo: $backup"
