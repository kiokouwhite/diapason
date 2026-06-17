@echo off
REM ============================================================
REM  Lance Diapason (en admin, pour piloter HidHide).
REM  A lancer a chaque session de jeu/tournoi.
REM ============================================================
powershell -NoProfile -Command "Start-Process -FilePath '%~dp0Diapason.exe' -Verb RunAs"
