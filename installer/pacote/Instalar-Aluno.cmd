@echo off
rem Instala ou atualiza o Midview nesta maquina de aluno.
rem Pede permissao de administrador; o trabalho e feito por
rem programa\scripts\Instalar-Aluno.ps1.
setlocal
fltmc >nul 2>&1
if not errorlevel 1 goto admin
if "%~1"=="/elevado" (
    echo Este instalador precisa de permissao de administrador.
    pause
    exit /b 1
)
powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '/elevado %*' -Verb RunAs"
if errorlevel 1 pause
exit /b

:admin
if "%~1"=="/elevado" shift
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0programa\scripts\Instalar-Aluno.ps1" %1 %2 %3 %4
echo.
pause
