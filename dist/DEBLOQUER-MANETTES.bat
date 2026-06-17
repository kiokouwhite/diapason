@echo off
REM ============================================================================
REM  DEBLOQUER-MANETTES : a lancer SEULEMENT si tes manettes semblent "mortes"
REM  (invisibles dans Windows / les jeux) apres un plantage de Diapason.
REM  Ca desactive le masquage HidHide reste actif. Demande les droits admin (UAC).
REM ============================================================================
powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%ProgramFiles%\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe' -ArgumentList '--cloak-off'"
echo.
echo Si tu as accepte la fenetre bleue (UAC) : c'est fait.
echo Tes manettes physiques sont de nouveau visibles partout.
echo (Relance Diapason quand tu veux pour reactiver la normalisation.)
echo.
pause
