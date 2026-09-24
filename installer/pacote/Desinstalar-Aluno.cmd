@echo off
rem Remove o Midview desta maquina de aluno, desfazendo antes qualquer
rem bloqueio de internet/sites e reativando teclado e mouse.
rem   Desinstalar-Aluno.cmd               mantem a identidade da maquina
rem   Desinstalar-Aluno.cmd -RemoverDados apaga tambem C:\ProgramData\Midview
rem Pede permissao de administrador; o trabalho e feito por
rem programa\scripts\Desinstalar-Aluno.ps1.
setlocal
fltmc >nul 2>&1
if not errorlevel 1 goto admin
if "%~1"=="/elevado" (
    echo Este desinstalador precisa de permissao de administrador.
    pause
    exit /b 1
)
powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '/elevado %*' -Verb RunAs"
if errorlevel 1 pause
exit /b

:admin
if "%~1"=="/elevado" shift
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0programa\scripts\Desinstalar-Aluno.ps1" %1 %2 %3 %4
echo.
pause
