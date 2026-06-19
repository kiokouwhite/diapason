@echo off
REM ============================================================================
REM  Diagnostic Diapason - a lancer APRES un plantage pour voir la cause.
REM  Ouvre le journal de Diapason + liste les erreurs Windows recentes le
REM  concernant (utile pour un crash NATIF qui n'apparait pas dans le journal).
REM ============================================================================
title Diagnostic Diapason
chcp 65001 >nul

echo Ouverture du journal Diapason...
if exist "%LOCALAPPDATA%\Diapason\diapason.log" (
  start "" notepad "%LOCALAPPDATA%\Diapason\diapason.log"
) else if exist "%~dp0diapason.log" (
  start "" notepad "%~dp0diapason.log"
) else (
  echo   ^(aucun journal Diapason trouve^)
)

echo.
echo Recherche d'erreurs Windows recentes mentionnant Diapason ^(crash natif eventuel^)...
echo ----------------------------------------------------------------------------
powershell -NoProfile -Command "try { Get-WinEvent -FilterHashtable @{LogName='Application'; Level=1,2} -MaxEvents 80 -ErrorAction Stop | Where-Object { $_.Message -match 'Diapason' } | Select-Object -First 5 | Format-List TimeCreated, ProviderName, Message } catch { Write-Host '  Aucune erreur Windows trouvee mentionnant Diapason.' }"
echo ----------------------------------------------------------------------------
echo.
echo Astuce : envoie le contenu du journal (Bloc-notes) si tu veux de l'aide.
pause
