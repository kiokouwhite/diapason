#Requires -Version 5.1
# ============================================================================
#  Desactiver-demarrage-auto.ps1
#  Supprime la tache planifiee de demarrage auto de Diapason.
#  (Clic droit > Executer avec PowerShell. Il passe admin tout seul.)
# ============================================================================

$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        Start-Process powershell.exe -Verb RunAs `
            -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    } catch { Write-Host "UAC refuse." -ForegroundColor Red; Start-Sleep 4 }
    exit
}

try {
    Unregister-ScheduledTask -TaskName 'Diapason' -Confirm:$false
    Write-Host "Demarrage auto DESACTIVE." -ForegroundColor Green
}
catch {
    Write-Host "Aucune tache 'Diapason' trouvee (deja desactive ?)." -ForegroundColor DarkYellow
}
Read-Host "Entree pour fermer"
