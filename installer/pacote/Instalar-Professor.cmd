@echo off
rem Instala ou atualiza o MidviewMaster no PC do professor, para o usuario
rem atual (nao precisa de administrador). O trabalho e feito por
rem programa\scripts\Instalar-Professor.ps1.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0programa\scripts\Instalar-Professor.ps1" %*
echo.
pause
