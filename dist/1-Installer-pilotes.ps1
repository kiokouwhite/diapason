#Requires -Version 5.1
# ============================================================================
#  1-Installer-pilotes.ps1  --  A LANCER UNE FOIS PAR PC.
#  Installe les 2 drivers necessaires a Diapason : ViGEmBus + HidHide (+ VC++).
#  Diapason.exe est autonome -> PAS besoin d'installer .NET.
#  (Clic droit > Executer avec PowerShell. Il passe admin tout seul.)
# ============================================================================

$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        Start-Process powershell.exe -Verb RunAs `
            -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    } catch { Write-Host "UAC refuse. Relance et accepte l'elevation." -ForegroundColor Red; Start-Sleep 4 }
    exit
}

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Host "winget introuvable. Installe 'App Installer' depuis le Microsoft Store puis relance." -ForegroundColor Red
    Read-Host "Entree pour quitter"; exit 1
}

function Inst($id, $label) {
    Write-Host "`n=== $label ===" -ForegroundColor Cyan
    winget install --id $id -e --source winget --silent --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -eq 0) { Write-Host "OK : $label" -ForegroundColor Green }
    else { Write-Host "$label : code $LASTEXITCODE (souvent = deja installe)" -ForegroundColor DarkYellow }
}

Inst 'ViGEm.ViGEmBus'                'ViGEmBus (manettes virtuelles)'
Inst 'Nefarius.HidHide'              'HidHide (masquage des manettes)'
Inst 'Microsoft.VCRedist.2015+.x64'  'Visual C++ Redistributable'

Write-Host "`nTermine. >> REDEMARRE le PC, puis lance 2-Lancer-Diapason.bat <<" -ForegroundColor Green
Read-Host "Entree pour fermer"
