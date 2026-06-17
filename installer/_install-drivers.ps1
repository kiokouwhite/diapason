# Lance par l'installeur APRES les pilotes embarques (vc_redist + ViGEmBus + HidHide).
# Role : VERIFIER que les 2 pilotes critiques sont bien presents. Repli winget seulement si
# un pilote manque encore (paquet embarque defaillant) ET si winget/internet dispo.
# Avertit par MessageBox en cas d'echec total (au lieu d'un faux "succes" silencieux).
$ErrorActionPreference = 'Continue'

$log = Join-Path $env:LOCALAPPDATA 'Diapason\install.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
function Log($m) { (((Get-Date).ToString('s')) + '  ' + $m) | Out-File -FilePath $log -Append -Encoding utf8 }
function HasSvc($n) { [bool](Get-Service -Name $n -ErrorAction SilentlyContinue) }

Log '=== Verification des pilotes ==='
$vigem   = HasSvc 'ViGEmBus'
$hidhide = (HasSvc 'HidHide') -or (HasSvc 'nefarius_HidHide')
Log "Apres paquets embarques : ViGEmBus=$vigem  HidHide=$hidhide"

# Repli winget UNIQUEMENT si un pilote manque encore.
if (-not $vigem -or -not $hidhide) {
    $wg = (Get-Command winget.exe -ErrorAction SilentlyContinue).Source
    if (-not $wg) {
        $pkg = Get-AppxPackage -AllUsers Microsoft.DesktopAppInstaller -ErrorAction SilentlyContinue |
               Sort-Object Version -Descending | Select-Object -First 1
        if ($pkg) { $c = Join-Path $pkg.InstallLocation 'winget.exe'; if (Test-Path $c) { $wg = $c } }
    }
    if ($wg) {
        Log "Repli winget = $wg"
        if (-not $vigem) {
            & $wg install --id ViGEm.ViGEmBus -e --source winget --silent --accept-source-agreements --accept-package-agreements | Out-Null
            Log "winget ViGEmBus -> code $LASTEXITCODE"
        }
        if (-not $hidhide) {
            & $wg install --id Nefarius.HidHide -e --source winget --silent --accept-source-agreements --accept-package-agreements | Out-Null
            Log "winget HidHide -> code $LASTEXITCODE"
        }
    } else {
        Log 'winget indisponible -> pas de repli possible.'
    }
    $vigem   = HasSvc 'ViGEmBus'
    $hidhide = (HasSvc 'HidHide') -or (HasSvc 'nefarius_HidHide')
    Log "Apres repli : ViGEmBus=$vigem  HidHide=$hidhide"
}

if (-not $vigem -or -not $hidhide) {
    $msg = "Les pilotes de Diapason n'ont pas pu etre installes.`n`n"
    if (-not $vigem)   { $msg += "- ViGEmBus MANQUANT (indispensable : sans lui, Diapason ne demarre pas).`n" }
    if (-not $hidhide) { $msg += "- HidHide MANQUANT (sans lui : double input en jeu).`n" }
    $msg += "`nRelance l'installeur, ou installe a la main : clic droit sur 1-Installer-pilotes.ps1 > Executer avec PowerShell.`n`n"
    $msg += "Journal detaille : $log"
    try {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show($msg, 'Diapason - pilotes manquants', 'OK', 'Warning') | Out-Null
    } catch { Log "MessageBox indisponible : $_" }
} else {
    Log 'OK : les deux pilotes sont presents.'
}

exit 0
