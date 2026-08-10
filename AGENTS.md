# AGENTS.md — Puñado de Fortuna

## Stack

- **Hardware**: Zebra FX9600, 2x antena AN480 (una por pecera/jugador)
- **OS**: Windows 10/11 x86-64
- **Runtime**: .NET 8 con ASP.NET Core minimal API + SignalR
- **SDK RFID**: Zebra RFID FXSeries Host .NET SDK v1.2 (Symbol.RFID3.Host.dll) via referencia directa
- **Frontend**: HTML/CSS/JS vanilla servido desde `wwwroot/`. SignalR client local, cero dependencias externas/CDN
- **Supervisor**: NSSM (Non-Sucking Service Manager) — reinicio automático si crashea
- **TargetFramework**: `net8.0-windows` (requiere Windows por SDK nativo y Symbol.RFID3.Host.dll)
- **NuGet Packages**:
  - `Zeroconf` 3.7.16 — descubrimiento mDNS (Sprint 0)
- **Referencias directas**:
  - `Symbol.RFID3.Host.dll` v1.2 — SDK oficial Zebra RFID FXSeries (Sprint 1)
  - `RFIDAPI32PC.dll` — DLL nativa x64 (copiada al output)

## Arquitectura

Proceso único .NET que aloja:
1. **DeviceDiscoveryService** — auto-descubre el FX9600 en la red (ping sweep + mDNS + TCP probe)
2. Lógica del reader (SDK Zebra — Sprint 1, actualmente solo stub/simulación)
3. Servidor web liviano (ASP.NET Core minimal API + SignalR)
4. Static files del frontend desde `wwwroot/`

Frontend en navegador modo kiosco, suscripto vía SignalR al estado de juego resuelto:

```json
{ "zona_id": 1, "score": 42, "match_state": "STANDBY|ACTIVE|RESULT", "winner": null }
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
    → Si no → retry loop cada 10s en RfidBackgroundService
```

**Escenarios soportados:**
- Conexión directa notebook↔FX9600 (APIPA 169.254.x.x)
- Misma LAN (DHCP)
- IP fija en appsettings.json

**Docs de referencia para otros agentes:**
- `docs/SETUP.md` — guía de instalación completa
- `docs/NETWORK.md` — configuración de red, troubleshooting
- `docs/ARCHITECTURE.md` — arquitectura de la librería de conexión

## Mecánica del juego

- Cada pecera tiene un set fijo de fichas RFID (EPC único, valor numérico oculto 1..n)
- Jugador saca un puñado → sistema infiere fichas ausentes por **ausencia de lectura**
- `score(t) = Σ valor(epc)` de fichas ausentes en ese instante (no acumulado, se autocorrige si devuelven)
- Máquina de estados por zona: `STANDBY → ACTIVE → RESULT → STANDBY` (al rellenar)
- Parámetros a calibrar con datos reales: `GRACE_WINDOW`, `QUIET_TIMEOUT`

## Prioridad #1: Estabilidad

- **Reconexión con backoff** si se cae la conexión al reader
- **Distinguir silencio real de falla**: heartbeat/keepalive del SDK para diferenciar `QUIET_TIMEOUT` de desconexión
- **Supervisión con NSSM**: reinicio automático si crashea
- **Recuperación limpia**: reconciliar contra inventario real del reader al arrancar
- **Reset manual**: botón/atajo para forzar STANDBY en vivo
- **Logging de eventos crudos** por sesión para diagnóstico post-evento

## Reglas para el frontend

- HTML, CSS, JS vanilla — sin frameworks, sin bundler, sin build toolchain
- Cero dependencias externas/CDN — signalr.js empaquetado localmente en `wwwroot/`
- Sin memory leaks: animaciones/timers limpiados en cada transición de estado
- Indicador visual "reconectando" si SignalR pierde conexión
- Sostenerse horas sin intervención

## Sprint 0 — Completado: Auto-Discovery

### Archivos nuevos / modificados

| Archivo | Tipo | Descripción |
|---|---|---|
| `Models/DeviceDiscoveryResult.cs` | Nuevo | DTO con resultado del descubrimiento |
| `Services/DeviceDiscoveryService.cs` | Nuevo | Pipeline de discovery: ping sweep, mDNS, TCP probe |
| `Program.cs` | Modificado | Discovery automático al inicio, flag `--discover` |
| `appsettings.json` | Modificado | Sección `Fx9600` con IpAddress, port, timeouts |
| `PunadoFortuna.csproj` | Modificado | Agregado `Zeroconf` 3.7.16 |
| `docs/SETUP.md` | Nuevo | Guía de instalación paso a paso |
| `docs/NETWORK.md` | Nuevo | Configuración de red y troubleshooting |
| `docs/ARCHITECTURE.md` | Nuevo | Arquitectura del módulo de conexión |

### Comandos CLI

```powershell
dotnet run                  # app normal con auto-discovery
dotnet run -- discover      # modo diagnóstico (solo descubrimiento, sin web)
dotnet run -- discover <ip> # probar IP específica
dotnet run -- --no-sim      # forzar modo real (Sprint 1, actualmente sin SDK)
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

## Fase de descubrimiento

1. ~~Conexión física y de red~~ → Sprint 0
2. ~~Validación sin código con 123RFID Desktop~~ → OK, lee 5 tags
3. ~~Linux/Java~~ → descartado
4. ~~Primer contacto programático~~ → Sprint 1 (LLRP cliente propio)
5. ~~Inspección de payload~~ → Sprint 1 (documentado en docs/LLRP.md)
6. Medición de ausencia — GRACE_WINDOW con datos reales → Sprint 2
7. ~~Prueba de resiliencia~~ → Sprint 1 (reconexión con backoff)
8. Con payloads documentados → diseñar contrato de eventos → Sprint 2

## Decisiones tomadas

- Windows + .NET (camino primario, más documentado)
- Linux/Java descartado como plan B explícito (solo si Windows falla)
- AN480 son muy dirigidas → cross-reading entre peceras es improbable pero se verificará en fase 2
- Proyecto se estructura con stub del reader para iterar sin hardware físico
- Auto-discovery con ping sweep + mDNS + TCP probe — plug & play sin configuración manual
- IP descubierta se persiste en `appsettings.json` para arranques posteriores instantáneos
- LLRP nativo (sin SDK Zebra) — implementación propia del protocolo estándar, control total del payload
- Datos crudos LLRP capturados en session log para replay y simulación

## Estructura actualizada

```
RFID-Punado-Fortuna/
├── .gitignore
├── AGENTS.md
├── README.md
├── data/
│   └── mapeo-fichas.json
├── docs/
│   ├── SETUP.md
│   ├── NETWORK.md
│   ├── ARCHITECTURE.md
│   └── SDK.md                     ← NUEVO Sprint 1
└── src/PunadoFortuna/
    ├── appsettings.json
    ├── Program.cs
    ├── PunadoFortuna.csproj
    ├── Hubs/
    │   └── GameHub.cs
    ├── Models/
    │   ├── ChipMapping.cs
    │   ├── DeviceDiscoveryResult.cs    ← Sprint 0
    │   ├── GameState.cs
    │   └── TagRead.cs
    ├── Services/
    │   ├── DeviceDiscoveryService.cs   ← Sprint 0
    │   ├── GameEngine.cs
    │   ├── RfidReaderService.cs        ← MODIFICADO Sprint 1 (SDK)
    │   └── SessionLogger.cs
    └── wwwroot/
        ├── index.html
        ├── css/
        │   └── game.css
        └── js/
            └── game.js
```

## Sprint 1 — Completado: Conexión + Lectura de Tags vía SDK

### Resumen

Integración del SDK oficial Zebra RFID FXSeries Host .NET SDK (Symbol.RFID3 v1.2). El cliente usa `RFIDReader` para conectar al FX9600 vía LLRP en puerto 5084.

| Feature | Estado |
|---|---|
| Conexión al FX9600 | OK |
| Inventario continuo (`Actions.Inventory.Perform`) | OK |
| Eventos de lectura (`Events.ReadNotify` + `GetReadTags`) | OK |
| Parseo de TagData → TagRead (EPC, antenna, RSSI, count, channel) | OK |
| Stop/Disconnect | OK |
| Reconexión automática | OK |
| Modo simulación (stub) | OK |

### Arquitectura

```
RfidReaderService
    ├── Modo simulación (default): Timer → SimulateInventoryCycle
    └── Modo real (--no-sim): RFIDReader → TCP → FX9600:5084
            │
            ├── ConnectAsync → RFIDReader.Connect()
            ├── Inventory.Perform() → lectura continua
            ├── ReadNotify → GetReadTags(100) → TagRead[] → TagsRead event
            └── DisconnectAsync → Inventory.Stop() + Disconnect()
```

### Flujo

```
RFIDReader(host, port, timeout) → Connect() → Inventory.Perform()
    → ReadNotify (continuo, asincrónico) → GetReadTags() → TagsRead event
```

### Dependencias

| Componente | Versión | Propósito |
|---|---|---|
| Symbol.RFID3.Host.dll | 1.2.0.0 | SDK gestionado (.NET) |
| RFIDAPI32PC.dll | x64 | DLL nativa de comunicación |
| Zeroconf | 3.7.16 | mDNS discovery (Sprint 0) |
