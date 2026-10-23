# Native UI smoke test with a private, generated tape/profile. No user games or AI requests.
param([ValidateRange(1, 10)][int]$Seconds = 3)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Esta prueba de ventana oculta se ejecuta en Windows.' }
$emuRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $emuRoot 'src/Retro.App/bin/Release/net10.0/RetroEmu.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Compilá RetroEmu en Release antes de esta prueba.' }
$testRoot = Join-Path $emuRoot 'out/ui-modifications'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$tapePath = Join-Path $testRoot 'own-test.tap'
$tape = [byte[]]@(3, 0, 255, 0, 255)
[IO.File]::WriteAllBytes($tapePath, $tape)
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($tape)).ToLowerInvariant()
$profile = @{
    id = 'own-ui-test'; name = 'Prueba propia de modificaciones'; machine = 'zx48'
    match = @{ sha256 = @($digest) }
    variables = @{
        counter = @{ address = '8000'; encoding = 'uint8'; min = 0; max = 255; description = 'Valor de prueba editable.' }
        observed = @{ address = '8001'; encoding = 'uint8'; min = 0; max = 255; readOnly = $true; description = 'Dato de solo lectura.' }
    }
}
$profile | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $testRoot 'own-ui-test.json') -Encoding utf8
$picture = Join-Path $testRoot 'panel.png'
$pictureStamp = if (Test-Path -LiteralPath $picture) { (Get-Item -LiteralPath $picture).LastWriteTimeUtc } else { [DateTime]::MinValue }
$start = [Diagnostics.ProcessStartInfo]::new($appPath)
$start.WorkingDirectory = $emuRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['RETROEMU_PROFILES'] = $testRoot
foreach ($arg in @('--hidden', '--mute', '--no-organize', '--no-update', '--mods-window', '--autotest', "$Seconds", 'out/ui-modifications/panel.png', $tapePath))
    { $start.ArgumentList.Add($arg) }
$process = [Diagnostics.Process]::Start($start)
try {
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit(25000)
    if ($timedOut) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    $output.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $testRoot 'stdout.log') -Encoding utf8
    $errors.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $testRoot 'stderr.log') -Encoding utf8
    if ($timedOut) { throw 'La prueba de interfaz se detuvo más de 25 segundos. Se cerró solo su proceso; revisá out/ui-modifications/*.log.' }
    if ($process.ExitCode -ne 0) { throw "La app terminó con código $($process.ExitCode). Revisá out/ui-modifications/*.log." }
    if (-not (Test-Path -LiteralPath $picture) -or (Get-Item -LiteralPath $picture).LastWriteTimeUtc -le $pictureStamp)
        { throw 'La app no guardó una captura nueva.' }
    $after = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($tapePath))).ToLowerInvariant()
    if ($after -ne $digest) { throw 'La prueba alteró su archivo de entrada.' }
    Write-Output "PASS: panel con variables editables y de solo lectura, entrada intacta. Captura: $picture"
}
finally { $process.Dispose() }
