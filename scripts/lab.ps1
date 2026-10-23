param(
    [ValidateSet('build', 'learn', 'test', 'content', 'tutorial', 'config', 'server')]
    [string]$Mode = 'learn',
    [ValidateSet('all', 'serialization', 'transport', 'session', 'messages')]
    [string]$Stage = 'all',
    [string]$ClientGameData = $env:ALBION_GAMEDATA,
    [string]$Only,
    [string]$ArtifactsPath,
    [string]$ConfigDir,
    [switch]$OpeningTutorial,
    [switch]$Trace
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$ConfigDir) { $ConfigDir = Join-Path $repoRoot 'config' }
$ConfigDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ConfigDir)
Push-Location $repoRoot
try {
    if ($OpeningTutorial -and $Mode -ne 'server') { throw '-OpeningTutorial se usa con -Mode server.' }
    if (!$ClientGameData -and (Test-Path -LiteralPath 'lab.local.json')) {
        $ClientGameData = (Get-Content -LiteralPath 'lab.local.json' -Raw | ConvertFrom-Json).clientGameData
    }
    if ($ClientGameData) { $ClientGameData = [IO.Path]::GetFullPath($ClientGameData) }
    if ($OpeningTutorial -and !$ArtifactsPath) { $ArtifactsPath = 'data/lab/onboarding-build' }
    if ($ArtifactsPath) { $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath) }
    if ($Mode -eq 'build') {
        $buildArgs = @('build', 'AlbionLocal.slnx', '--nologo', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all')
        if ($ArtifactsPath) { $buildArgs += '--artifacts-path', $ArtifactsPath }
        & dotnet @buildArgs
        exit $LASTEXITCODE
    }
    # A prior isolated build does not update src/.../bin. Always compile the selected server output
    # before launching, so the familiar -Mode server command cannot silently run an older assembly.
    if ($Mode -in 'server', 'config') {
        $serverBuildArgs = @('build', 'src/AlbionLocal.Server/AlbionLocal.Server.csproj', '--nologo', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all')
        if ($ArtifactsPath) { $serverBuildArgs += '--artifacts-path', $ArtifactsPath }
        Write-Host 'Compilando el servidor que se va a ejecutar...'
        & dotnet @serverBuildArgs
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo actualizar el servidor. Cierra el emulador anterior y revisa los errores de compilacion.' }
    }
    $project = if ($Mode -in 'learn', 'test') { 'tests/AlbionLocal.SelfTest' } else { 'src/AlbionLocal.Server' }
    $assembly = if ($Mode -in 'learn', 'test') { 'AlbionLocal.SelfTest' } else { 'AlbionLocal.Server' }
    $binary = Join-Path $repoRoot "$project/bin/Debug/net10.0/$assembly.dll"
    if ($ArtifactsPath) { $binary = Join-Path $ArtifactsPath "bin/$assembly/debug/$assembly.dll" }
    if (!(Test-Path -LiteralPath $binary)) { throw 'Primero ejecuta: ./scripts/lab.ps1 -Mode build' }
    $runArgs = @()
    if ($Mode -in 'learn', 'test') {
        if ($ClientGameData) { $runArgs += "--client-gamedata=$ClientGameData" }
        if ($Mode -eq 'learn') { $runArgs += "--lab=$Stage" }
        if ($Only) { $runArgs += "--only=$Only" }
        $runArgs += "--report=data/lab/$Mode-$Stage.json"
    } else {
        $runArgs += "--config-dir=$ConfigDir"
        if ($ClientGameData) {
            if ($Mode -ne 'config' -and !(Test-Path -LiteralPath $ClientGameData -PathType Container)) { throw 'No existe la carpeta GameData indicada.' }
            $runArgs += "--Paths:ClientGameData=$ClientGameData"
        }
        if ($Mode -eq 'config') { $runArgs += '--check-config' }
        elseif ($Mode -eq 'content') { $runArgs += '--check-content' }
        elseif ($Mode -eq 'tutorial') { $runArgs += '--check-tutorial' }
        elseif ($OpeningTutorial) {
            $runArgs += '--Tutorial:Opening=true', "--Paths:Saves=$(Join-Path $repoRoot 'data/lab/tutorial-server')", "--Log:File=$(Join-Path $repoRoot 'data/lab/tutorial-server/server.log')"
            Write-Host 'Tutorial de laboratorio: personajes nuevos; guardados separados en data/lab/tutorial-server.'
            Write-Host 'El recorrido implementado permite completar la isla y viajar al continente.'
        }
        else {
            $runArgs += "--Paths:Saves=$(Join-Path $repoRoot 'data/lab/server')", "--Log:File=$(Join-Path $repoRoot 'data/lab/server/server.log')"
            Write-Host 'Laboratorio habitual: el preset y el tutorial se leen de los archivos de configuracion.'
        }
        Write-Host "Directorio de configuracion: $ConfigDir"
        Write-Host "Opciones explicitas: $($runArgs -join ' ')"
    }
    if ($Trace) { $runArgs += '--protocol-trace' }
    if ($Mode -eq 'server') { Write-Host "Compilacion seleccionada: $binary" }
    & dotnet $binary @runArgs
    exit $LASTEXITCODE
} finally { Pop-Location }
