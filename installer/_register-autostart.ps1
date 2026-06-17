# Cree la tache planifiee de demarrage auto pointant vers $Exe. Lance par l'installeur (admin).
# -UserName : compte pour lequel la tache se declenche au logon. On prend l'utilisateur
# INTERACTIF passe par l'installeur ({username}) plutot que l'identite ELEVEE (qui peut etre
# un autre admin via UAC) -> la tache se declenche bien pour le compte qui joue.
param([Parameter(Mandatory = $true)][string]$Exe, [string]$UserName)

if ([string]::IsNullOrWhiteSpace($UserName)) {
    $UserName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
}

$action  = New-ScheduledTaskAction  -Execute $Exe
$trigger = New-ScheduledTaskTrigger -AtLogOn
# Petit delai : laisse les pilotes noyau (ViGEmBus/HidHide) se charger au boot avant Diapason.
try { $trigger.Delay = 'PT20S' } catch { }
$set     = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                                        -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)

function Register($userId) {
    $princ = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Highest
    Register-ScheduledTask -TaskName 'Diapason' -Action $action -Trigger $trigger `
                           -Principal $princ -Settings $set -Force | Out-Null
}

try {
    Register $UserName
} catch {
    # Si le nom passe ne se resout pas, repli sur l'identite courante.
    Register ([Security.Principal.WindowsIdentity]::GetCurrent().Name)
}
exit 0
