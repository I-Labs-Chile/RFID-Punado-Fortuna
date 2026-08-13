# AGENTS.md — Puñado de Fortuna

## Stack

- **Hardware**: Zebra FX9600, 2x antena AN480 (una por pecera/jugador)
- **OS**: Windows 10/11 x86-64
- **Runtime**: .NET 8 con ASP.NET Core minimal API + SignalR
- **SDK RFID**: Zebra RFID FXSeries Host .NET SDK v1.2 (`Symbol.RFID3.Host.dll`) via referencia directa
- **Frontend**: HTML/CSS/JS vanilla servido desde `wwwroot/`. SignalR client local, cero dependencias externas/CDN
- **Supervisor**: NSSM (Non-Sucking Service Manager) — reinicio automático si crashea
- **TargetFramework**: `net8.0-windows` (requiere Windows por SDK nativo y Symbol.RFID3.Host.dll)
- **NuGet Packages**:
  - `Zeroconf` 3.7.16 — descubrimiento mDNS
- **Referencias directas**:
  - `Symbol.RFID3.Host.dll` v1.2 — SDK oficial Zebra RFID FXSeries
  - `RFIDAPI32PC.dll` — DLL nativa x64 (copiada al output)

## Arquitectura

Proceso único .NET que aloja:
1. **DeviceDiscoveryService** — auto-descubre el FX9600 en la red (ping sweep + mDNS + TCP probe)
2. Lógica del reader (SDK Zebra)
3. Servidor web liviano (ASP.NET Core minimal API + SignalR)
4. Static files del frontend desde `wwwroot/`

Frontend en navegador modo kiosco, suscripto vía SignalR al estado de juego resuelto:

```json
{ "phase": "WAITING|REVEAL_COUNT|GUESS_COLORS|REVEAL_COLORS", "tagCount": 42, "colorBreakdown": { "verde": 20 }, "isStable": true }
```

## Configuración de red — Plug & Play

La app descubre el FX9600 automáticamente:

```
appsettings.json: Fx9600.IpAddress = "auto"
  → DeviceDiscoveryService.DiscoverAsync()
    → Ping sweep en subnet del adaptador activo
    → mDNS (Zeroconf) busca "FX9600"
    → TCP probe :5084 (LLRP) + :80 (HTTP)
    → Si encuentra → guarda IP en appsettings.json
    → Si no → retry loop en RfidBackgroundService
```

**Escenarios soportados:**
- Conexión directa notebook↔FX9600 (APIPA 169.254.x.x)
- Misma LAN (DHCP)
- IP fija en appsettings.json

## Mecánica del juego (Sprint 5 — "solo lectura")

- Cada pecera tiene un set de fichas RFID (EPC único + color)
- El lector reporta qué fichas **están presentes** (en el campo de lectura) con su `AntennaId`
- El juego **no** calcula scoring por ausencia: solo revela **cantidad** y **colores** de lo presente
- Máquina de estados global: `WAITING → REVEAL_COUNT → GUESS_COLORS → REVEAL_COLORS` (avanza con ENTER, reset con F1)
- La estabilidad se determina por ventana de tiempo sin cambios (`_stabilityWindow = 3s`)

## Fuente de verdad de datos

- **`data/mapeo-colores.json`** es la fuente de verdad: EPC → color (`verde`, `azul`, `naranja`, `rosado`, `celeste`, `premio`). 105 fichas.
- **`data/mapeo-fichas.json`** se deriva de colores: EPC → `valor` (1), `zona_id` (1), `descripcion`. Se usa para `TotalChips` y el modo simulación.
- Si agregás/quitas fichas, editá `mapeo-colores.json` y regenerá `mapeo-fichas.json` manteniendo el mismo set de EPCs.

## Prioridad #1: Estabilidad

- **Reconexión con backoff** si se cae la conexión al reader
- **Supervisión con NSSM**: reinicio automático si crashea
- **Recuperación limpia**: reconciliar contra inventario real del reader al arrancar
- **Reset manual**: F1 fuerza `Reset()` en vivo
- **Logging de eventos crudos** por sesión para diagnóstico post-evento (`logs/session_*.log`)

## Reglas para el frontend

- HTML, CSS, JS vanilla — sin frameworks, sin bundler, sin build toolchain
- Cero dependencias externas/CDN — `signalr.min.js` empaquetado localmente en `wwwroot/`
- Sin memory leaks: animaciones/timers limpiados en cada transición de estado
- Indicador visual "reconectando" si SignalR pierde conexión
- Sostenerse horas sin intervención
- **Importante**: el mapa `COLORS` de `game.js` debe contener TODOS los valores de `color` de `mapeo-colores.json` (verde, azul, naranja, rosado, celeste, premio). Si falta uno, ese color se muestra como "desconocido".

## Antenas

- El reader se auto-descubre en red. Las **antenas** también se auto-descubren: `RfidReaderService.ConfigureAvailableAntennas()` recorre `_reader.Config.Antennas.AvailableAntennas` y configura cada una (potencia, RF mode, singulación).
- Cada `TagRead.AntennaId` reporta qué antena física leyó el tag.

## Comandos CLI

```powershell
dotnet run                  # app normal con auto-discovery (modo simulación)
dotnet run -- --no-sim      # modo real con SDK Zebra
dotnet run -- discover      # modo diagnóstico (solo descubrimiento, sin web)
dotnet run -- discover <ip> # probar IP específica
```

### Configuración (appsettings.json)

```json
{
  "Fx9600": {
    "IpAddress": "auto",
    "Port": 5084,
    "PingTimeoutMs": 500,
    "TcpTimeoutMs": 2000,
    "MaxPingConcurrency": 50
  }
}
```

- `"auto"` = pipeline automático. Al encontrar la IP, se persiste reemplazando `"auto"` por la IP concreta.
- IP fija = se usa directamente sin scan.

## Decisiones tomadas

- Windows + .NET (camino primario)
- AN480 muy dirigidas → cross-reading entre peceras improbable
- SDK Zebra oficial (no LLRP propio) — la implementación LLRP de Sprint 2 fue reemplazada en Sprint 4
- Auto-discovery con ping sweep + mDNS + TCP probe — plug & play sin configuración manual
- IP descubierta se persiste en `appsettings.json` para arranques posteriores instantáneos
- `mapeo-colores.json` como única fuente de verdad de EPCs

## Estructura

```
RFID-Punado-Fortuna/
├── AGENTS.md
├── README.md
├── data/
│   ├── mapeo-colores.json      ← fuente de verdad (EPC → color)
│   └── mapeo-fichas.json       ← derivado (EPC → valor/zona/descripcion)
├── docs/
│   ├── SETUP.md
│   ├── NETWORK.md
│   ├── ARCHITECTURE.md
│   ├── SDK.md
│   └── LLRP.md
└── src/PunadoFortuna/
    ├── appsettings.json
    ├── Program.cs
    ├── PunadoFortuna.csproj
    ├── Hubs/
    │   └── GameHub.cs
    ├── Models/
    │   ├── ChipMapping.cs
    │   ├── DeviceDiscoveryResult.cs
    │   ├── GameState.cs
    │   └── TagRead.cs
    ├── Services/
    │   ├── DeviceDiscoveryService.cs
    │   ├── GameEngine.cs
    │   ├── RfidReaderService.cs
    │   └── SessionLogger.cs
    └── wwwroot/
        ├── index.html
        ├── css/game.css
        └── js/
            ├── game.js
            └── signalr.min.js
```

### Flujo de lectura

```
RfidReaderService
    ├── Modo simulación (default): Timer → SimulateInventoryCycle
    └── Modo real (--no-sim): RFIDReader → TCP → FX9600:5084
            │
            ├── ConnectAsync → RFIDReader.Connect()
            ├── ConfigureAvailableAntennas() → auto-descubre antenas
            ├── Inventory.Perform() → lectura continua
            ├── ReadNotify → GetReadTags(100) → TagRead[] → TagsRead event
            └── DisconnectAsync → Inventory.Stop() + Disconnect()
```

### Dependencias

| Componente | Versión | Propósito |
|---|---|---|
| Symbol.RFID3.Host.dll | 1.2.0.0 | SDK gestionado (.NET) |
| RFIDAPI32PC.dll | x64 | DLL nativa de comunicación |
| Zeroconf | 3.7.16 | mDNS discovery |
