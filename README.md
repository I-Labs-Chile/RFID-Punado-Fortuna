# Puñado de Fortuna

Juego de feria con lector RFID Zebra FX9600 y 2 antenas AN480.

## Requisitos

- **Windows 10/11 x86-64**
- **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0
- **Zebra RFID SDK for Windows** (descargar del portal de soporte Zebra)
- **NSSM** (Non-Sucking Service Manager) para supervisión en producción

## Setup

```bash
# 1. Instalar dependencia SignalR client JS
#    Descargar desde https://www.npmjs.com/package/@microsoft/signalr
#    o: npm install @microsoft/signalr
#    Copiar node_modules/@microsoft/signalr/dist/browser/signalr.min.js
#    a src/PunadoFortuna/wwwroot/js/signalr.min.js

# 2. Correr en modo desarrollo (simulación sin hardware)
cd src/PunadoFortuna
dotnet run

# 3. Abrir http://localhost:5085 en el navegador

# 4. Para correr sin simulación (necesita reader físico)
dotnet run -- --no-sim
```

## Estructura

```
src/PunadoFortuna/
├── Program.cs              # Entry point, DI, wiring
├── Models/                 # GameState, TagRead, ChipMapping
├── Services/
│   ├── RfidReaderService.cs  # Wrapper SDK Zebra + stub simulación
│   ├── GameEngine.cs         # Máquina de estados por zona
│   └── SessionLogger.cs      # Logging de eventos crudos
├── Hubs/
│   └── GameHub.cs            # SignalR hub (push en tiempo real)
└── wwwroot/                  # Frontend vanilla HTML/CSS/JS
    ├── index.html
    ├── css/game.css
    └── js/
        ├── signalr.min.js    # Mover acá después de descargar
        └── game.js

data/
└── mapeo-fichas.json         # EPC → valor secreto por zona

logs/                         # Sesiones de juego (gitignored)
```

## Estados del juego

```
STANDBY → ACTIVE → RESULT → STANDBY
```

- **STANDBY**: Pecera llena, esperando jugador
- **ACTIVE**: Se detectaron fichas ausentes (jugador sacando puñado)
- **RESULT**: Tiempo de quietud cumplido, se muestra puntaje
- Vuelve a STANDBY tras display o botón de reset

## Atajos de teclado

| Tecla | Acción |
|-------|--------|
| F1 | Reiniciar todo (forzar STANDBY) |
| F2 | Reiniciar Pecera 1 |
| F3 | Reiniciar Pecera 2 |

## Producción

Instalar como servicio Windows con NSSM:

```powershell
nssm install PunadoFortuna
# Path: dotnet.exe
# Arguments: run --project C:\...\src\PunadoFortuna
# Working directory: C:\...\src\PunadoFortuna
nssm set PunadoFortuna AppExit Default Restart
nssm start PunadoFortuna
```

## Fase de descubrimiento

Ver `AGENTS.md` para los 8 pasos de la fase de descubrimiento con hardware.
