#Requires -Version 5.1
# ============================================================================
#  3-Activer-demarrage-auto.ps1
#  Cree une tache planifiee : Diapason se lance EN ADMIN automatiquement a chaque
#  ouverture de session (avec son icone dans la barre des taches). A lancer 1 fois.
#  (Clic droit > Executer avec PowerShell. Il passe admin tout seul.)
#
#  IMPORTANT : ne deplace pas ce dossier apres coup -> la tache pointe vers CE
#  Diapason.exe. Si tu le deplaces, relance ce script depuis le nouvel emplacement.
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

$exe = Join-Path $PSScriptRoot 'Diapason.exe'
if (-not (Test-Path $exe)) {
    Write-Host "Diapason.exe introuvable a cote de ce script." -ForegroundColor Red
    Read-Host "Entree pour quitter"; exit 1
}

$userId  = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action  = New-ScheduledTaskAction  -Execute $exe
$trigger = New-ScheduledTaskTrigger -AtLogOn
$princ   = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Highest
$set     = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                                        -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName 'Diapason' -Action $action -Trigger $trigger `
                       -Principal $princ -Settings $set -Force | Out-Null

Write-Host ""
Write-Host "OK ! Demarrage auto ACTIVE." -ForegroundColor Green
Write-Host "Diapason se lancera tout seul (en admin, icone barre des taches) a chaque session."
Write-Host "Pour desactiver : lance 'Desactiver-demarrage-auto.ps1'."
Read-Host "Entree pour fermer"
