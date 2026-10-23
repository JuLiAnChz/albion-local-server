param(
    [string]$GameDir = $env:ALBION_GAME_DIR,
    [switch]$ExpandQuestTracker
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$GameDir) {
    $localSettings = Join-Path $repoRoot 'lab.local.json'
    if (Test-Path -LiteralPath $localSettings) {
        $gameDataDir = (Get-Content -LiteralPath $localSettings -Raw | ConvertFrom-Json).clientGameData
        if ($gameDataDir) {
            $GameDir = Split-Path (Split-Path (Split-Path $gameDataDir -Parent) -Parent) -Parent
        }
    }
}
if (!$GameDir) { throw 'Indica -GameDir con la carpeta que contiene Albion-Online_BE.exe.' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$launcher = Join-Path $GameDir 'Albion-Online_BE.exe'
if (!(Test-Path -LiteralPath $launcher -PathType Leaf)) { throw "No existe el launcher: $launcher" }
$versionFile = Join-Path $GameDir 'version.txt'
if (!(Test-Path -LiteralPath $versionFile)) { throw 'No se encontro version.txt en la carpeta del cliente.' }
$version = (Get-Content -LiteralPath $versionFile -First 1).Trim()
if ($version -ne 'albiononline-win32-full-1.32.030.345319') {
    throw "Este procedimiento se investigo para 1.32.030.345319; la instalacion declara $version."
}
if (Get-Process -Name 'Albion-Online' -ErrorAction SilentlyContinue) {
    throw 'Cierra las ventanas de Albion antes de iniciar otra prueba.'
}
$logDir = Join-Path $repoRoot 'data/lab'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
if ($ExpandQuestTracker) {
    # The client stores this preference outside the character database. A tutorial reset
    # may leave it collapsed again. Only identify a profile automatically when unambiguous.
    $profilesDir = Join-Path $env:USERPROFILE 'AppData/LocalLow/Sandbox Interactive GmbH/Albion Online Client/Profiles'
    $profiles = @(Get-ChildItem -LiteralPath $profilesDir -File -Filter '*.profile' -ErrorAction SilentlyContinue)
    if ($profiles.Count -eq 1) {
        $profilePath = $profiles[0].FullName
        $profileText = [IO.File]::ReadAllText($profilePath)
        $profileValues = ($profileText | ConvertFrom-Json).Values
        $collapsePattern = '"TrackerCollapse"\s*:\s*true\b'
        if ($profileValues.TrackerCollapse -eq $true -and [regex]::Matches($profileText, $collapsePattern).Count -eq 1) {
            $backupDir = Join-Path $logDir 'client-profile-backups'
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
            $backupPath = Join-Path $backupDir ("tracker-{0}.profile" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
            Copy-Item -LiteralPath $profilePath -Destination $backupPath
            $profileBytes = [IO.File]::ReadAllBytes($profilePath)
            $hasBom = $profileBytes.Length -ge 3 -and $profileBytes[0] -eq 239 -and $profileBytes[1] -eq 187 -and $profileBytes[2] -eq 191
            $updatedProfile = [regex]::Replace($profileText, $collapsePattern, '"TrackerCollapse":false')
            [IO.File]::WriteAllText($profilePath, $updatedProfile, (New-Object System.Text.UTF8Encoding -ArgumentList $hasBom))
            if (([IO.File]::ReadAllText($profilePath) | ConvertFrom-Json).Values.TrackerCollapse -ne $false) {
                throw 'No se pudo expandir el seguimiento de misiones en el perfil.'
            }
            Write-Host 'Seguimiento de misiones expandido; copia del perfil guardada en data/lab/client-profile-backups.'
        }
    } else {
        Write-Warning 'No hay un perfil unico para ajustar el seguimiento. Desactiva Contraer seguimiento en Configuracion > Interfaz.'
    }
}
$clientLog = Join-Path $logDir ("client-dev-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
# In this client build, dev contains the built-in Local (USA/Europe/Asia) entries at 127.0.0.1:5055.
# An unrecognized environment such as "local" instead produces a Custom entry.
$clientArgs = '0 1 +server 127.0.0.1 +serverenvironment dev -logFile "' + $clientLog + '"'
Write-Host 'Abriendo el cliente con BattlEye y el entorno dev (127.0.0.1:5055).'
Write-Host "Registro del cliente: $clientLog"
Start-Process -FilePath $launcher -WorkingDirectory $GameDir -ArgumentList $clientArgs -WindowStyle Normal | Out-Null
