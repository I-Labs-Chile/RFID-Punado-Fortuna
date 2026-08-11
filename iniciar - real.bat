@echo off
cd /d "%~dp0src\PunadoFortuna"
echo ==========================================
echo   Punado de Fortuna - MODO REAL
echo ==========================================
taskkill /f /im PunadoFortuna.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo.
echo Abri http://localhost:5085 en el navegador
echo Presiona Ctrl+C para salir
echo.
start "" http://localhost:5085
dotnet run -- --no-sim
pause
