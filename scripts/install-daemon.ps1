# Installation / mise a jour du daemon ORION.
#
# A REJOUER a chaque fois que le code du daemon change. Le publish seul NE SUFFIT PAS :
# voir l etape 2, qui transporte des fichiers que dotnet publish laisse derriere lui.
#
# Aucune elevation requise : tout vit dans le profil utilisateur, et le lancement passe par le
# dossier Demarrage (la creation d une tache planifiee, elle, exige un administrateur).

$ErrorActionPreference = "Stop"

$projet     = Join-Path $PSScriptRoot "..\daemon"
$install    = "$env:LOCALAPPDATA\Orion\daemon"
$demarrage  = [Environment]::GetFolderPath("Startup")
$buildCache = Join-Path $projet "Orion.Daemon\bin\Release\net9.0\win-x64"

Write-Host "[1/5] Arret de l instance en cours..."
Get-Process Orion.Daemon -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "[2/5] Publication (autonome, aucune dependance runtime)..."
dotnet publish (Join-Path $projet "Orion.Daemon\Orion.Daemon.csproj") -c Release -r win-x64 --self-contained -o $install -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "Echec de la publication" }

# ---------------------------------------------------------------------------------------
# ETAPE INDISPENSABLE, ET FACILE A OUBLIER.
#
# KokoroSharp telecharge ses voix et espeak-ng A L EXECUTION, dans le repertoire de travail.
# `dotnet publish` ne les connait pas et ne les emporte donc PAS. Sans elles, le daemon demarre,
# se connecte, parait sain... et reste MUET : GetVoice leve DirectoryNotFoundException et seule
# la voix Windows de secours repond. On les recopie depuis la sortie de build, ou on laisse
# Kokoro les retelecharger (~47 Mo) si elle n y sont pas.
# ---------------------------------------------------------------------------------------
Write-Host "[3/5] Transport des voix Kokoro et d espeak..."
foreach ($dossier in @("voices", "espeak", "Voicemodels")) {
    $source = Join-Path $buildCache $dossier
    if (Test-Path $source) {
        Copy-Item $source -Destination $install -Recurse -Force
        Write-Host "      $dossier : copie"
    } else {
        Write-Warning "      $dossier introuvable dans la sortie de build - Kokoro le retelechargera au premier demarrage"
    }
}

# Le jeton ne doit JAMAIS etre en dur ici ni dans un fichier suivi par git. On conserve la
# configuration de production deja installee ; au premier passage seulement, on la derive de la
# configuration de developpement.
Write-Host "[4/5] Configuration de production..."
$confProd = Join-Path $install "appsettings.Production.json"
if (-not (Test-Path $confProd)) {
    $confDev = Join-Path $projet "Orion.Daemon\appsettings.Development.json"
    if (-not (Test-Path $confDev)) { throw "Aucune configuration source : impossible de recuperer le jeton" }
    $dev = Get-Content $confDev -Raw | ConvertFrom-Json
    @{ Daemon = @{
        RenderWsUrl        = "wss://orion.shift-star.app/daemon"
        Token              = $dev.Daemon.Token
        MachineName        = $env:COMPUTERNAME
        ReconnectDelayMs   = 5000
        MaxReconnectDelayMs= 60000
        ReconnectMultiplier= 2
    }} | ConvertTo-Json -Depth 5 | Set-Content $confProd -Encoding utf8
    Write-Host "      creee (jeton repris de la configuration de developpement)"
} else {
    Write-Host "      conservee (le jeton en place n est pas ecrase)"
}
# La copie de la config de developpement n a rien a faire ici : jamais lue en Production, elle
# ne ferait que dupliquer le secret dans un fichier mort.
Remove-Item (Join-Path $install "appsettings.Development.json") -ErrorAction SilentlyContinue

Write-Host "[5/5] Lancement a l ouverture de session..."
Copy-Item (Join-Path $install "demarrer-orion.vbs") -Destination (Join-Path $demarrage "ORION.vbs") -Force
wscript.exe (Join-Path $demarrage "ORION.vbs")
Start-Sleep -Seconds 8

$p = Get-Process Orion.Daemon -ErrorAction SilentlyContinue
if ($p) {
    Write-Host ""
    Write-Host "ORION est demarre (PID $($p.Id)), sans fenetre, et redemarrera a chaque ouverture de session." -ForegroundColor Green
} else {
    throw "Le daemon ne s est pas lance - verifier $install"
}