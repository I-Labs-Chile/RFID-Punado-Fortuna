@echo off
cd /d "%~dp0src\PunadoFortuna"
echo ==========================================
echo   Punado de Fortuna - SIMULACION
echo ==========================================
taskkill /f /im PunadoFortuna.exe >nul 2>&1
timeout /t 1 /nobreak >nul
start "" http://localhost:5085
dotnet run
pause
