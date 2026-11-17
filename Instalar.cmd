@echo off
rem Instala (ou atualiza) o Native. Basta dar dois cliques.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
if errorlevel 1 pause
