param(
    [ValidateSet('build', 'learn', 'test', 'content', 'tutorial', 'server')]
    [string]$Mode = 'learn',
    [ValidateSet('all', 'serialization', 'transport', 'session', 'messages')]
    [string]$Stage = 'all',
    [string]$ClientGameData = $env:ALBION_GAMEDATA,
    [string]$Only,
    [string]$ArtifactsPath,
    [switch]$Trace
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    if (!$ClientGameData -and (Test-Path -LiteralPath 'lab.local.json')) {
        $ClientGameData = (Get-Content -LiteralPath 'lab.local.json' -Raw | ConvertFrom-Json).clientGameData
    }
    if ($ClientGameData) { $ClientGameData = [IO.Path]::GetFullPath($ClientGameData) }
    if ($ArtifactsPath) { $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath) }
    if ($Mode -eq 'build') {
        $buildArgs = @('build', 'AlbionLocal.slnx', '--nologo', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all')
        if ($ArtifactsPath) { $buildArgs += '--artifacts-path', $ArtifactsPath }
        & dotnet @buildArgs
        exit $LASTEXITCODE
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
        if (!$ClientGameData) { throw 'Indica -ClientGameData, ALBION_GAMEDATA o lab.local.json.' }
        if (!(Test-Path -LiteralPath $ClientGameData -PathType Container)) { throw 'No existe la carpeta GameData indicada.' }
        $runArgs += "--Paths:ClientGameData=$ClientGameData"
        if ($Mode -eq 'content') { $runArgs += '--check-content' }
        elseif ($Mode -eq 'tutorial') { $runArgs += '--check-tutorial' }
        else { $runArgs += '--Paths:Saves=data/lab/server', '--Log:File=data/lab/server/server.log' }
    }
    if ($Trace) { $runArgs += '--protocol-trace' }
    & dotnet $binary @runArgs
    exit $LASTEXITCODE
} finally { Pop-Location }
