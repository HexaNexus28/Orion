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
        AllowedRoots       = @()
        AllowedWriteRoots  = @()
    }} | ConvertTo-Json -Depth 5 | Set-Content $confProd -Encoding utf8
    Write-Host "      creee (jeton repris de la configuration de developpement)"
    Write-Warning "      perimetre disque VIDE - a renseigner, voir l avertissement final"
} else {
    Write-Host "      conservee (le jeton en place n est pas ecrase)"
}

# ---------------------------------------------------------------------------------------
# LE PERIMETRE DISQUE EST FAIL-CLOSED (audit du 2026-08-27, constats C1 et C2).
#
# Sans "AllowedRoots", read_file / list_files / write_file refusent TOUT. C est le defaut
# voulu, pas une panne. Mais ce script CONSERVE la configuration de production existante :
# une machine installee avant l audit recoit le nouveau code, demarre, se connecte, parait
# saine... et trois outils sont morts sans le moindre message.
#
# On ne peut pas deviner les racines a la place de l utilisateur : ce fichier decide de ce
# qui a le droit de SORTIR de la machine. On le SIGNALE donc, plutot que de laisser une
# panne se deguiser en succes.
# ---------------------------------------------------------------------------------------
$conf = Get-Content $confProd -Raw | ConvertFrom-Json
$racines = if ($conf.Daemon.PSObject.Properties.Name -contains "AllowedRoots") { @($conf.Daemon.AllowedRoots) } else { @() }
$perimetreAbsent = $racines.Count -eq 0
if (-not $perimetreAbsent) {
    Write-Host "      lecture : $($racines -join ', ')"
    $ecriture = @($conf.Daemon.AllowedWriteRoots)
    if ($ecriture.Count -eq 0) { $ecriture = $racines }
    Write-Host "      ecriture : $($ecriture -join ', ')"
    $mortes = $racines | Where-Object { -not (Test-Path $_) }
    if ($mortes) { Write-Warning "      racines DECLAREES mais INEXISTANTES : $($mortes -join ', ')" }
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
    if ($perimetreAbsent) {
        Write-Host ""
        Write-Host "ATTENTION : aucune racine disque n est declaree." -ForegroundColor Red
        Write-Host "read_file, list_files et write_file REFUSERONT TOUT (fail-closed, c est voulu)." -ForegroundColor Red
        Write-Host "Renseigner Daemon:AllowedRoots dans $confProd puis relancer ce script." -ForegroundColor Red
    }
} else {
    throw "Le daemon ne s est pas lance - verifier $install"
}