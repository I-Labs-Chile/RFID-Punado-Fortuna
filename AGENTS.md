# AGENTS.md — Puñado de Fortuna

## Stack

- **Hardware**: Zebra FX9600, 2x antena AN480 (una por pecera/jugador)
- **OS**: Windows 10/11 x86-64
- **Runtime**: .NET 8 con ASP.NET Core minimal API + SignalR
- **SDK RFID**: Zebra Host RFID SDK para Windows (binding .NET/C#, nunca C) — Sprint 1
- **Frontend**: HTML/CSS/JS vanilla servido desde `wwwroot/`. SignalR client local, cero dependencias externas/CDN
- **Supervisor**: NSSM (Non-Sucking Service Manager) — reinicio automático si crashea
- **NuGet Packages**:
  - `Zeroconf` 3.7.16 — descubrimiento mDNS del FX9600
  - `System.Reactive` 5.0.0 — dependencia transitiva de Zeroconf

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

## Fase de descubrimiento (bloqueante — no escribir código de producción hasta completar)

1. ~~Conexión física y de red — confirmar IP del FX9600, validar ping/web UI~~ → Sprint 0
2. Validación sin código con 123RFID Desktop — confirmar que ambas antenas leen
3. ~~Linux/Java~~ — descartado, Windows/.NET es el camino primario
4. Primer contacto programático — suscribirse a eventos de lectura con ambas antenas y fichas reales → Sprint 1
5. Inspección de payload — documentar campos reales: EPC, antenna port, RSSI, timestamp, cadencia → Sprint 1
6. Medición de ausencia — GRACE_WINDOW con datos reales (cuántos ciclos falla un tag quieto) → Sprint 2
7. Prueba de resiliencia — desconexión real y cómo lo reporta el SDK → Sprint 1
8. Con payloads documentados → diseñar contrato de eventos, motor de estados, y capa de resiliencia → Sprint 2

## Decisiones tomadas

- Windows + .NET (camino primario, más documentado)
- Linux/Java descartado como plan B explícito (solo si Windows falla)
- AN480 son muy dirigidas → cross-reading entre peceras es improbable pero se verificará en fase 2
- Proyecto se estructura con stub del reader para iterar sin hardware físico
- Auto-discovery con ping sweep + mDNS + TCP probe — plug & play sin configuración manual
- IP descubierta se persiste en `appsettings.json` para arranques posteriores instantáneos

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
│   └── ARCHITECTURE.md
└── src/PunadoFortuna/
    ├── appsettings.json
    ├── Program.cs
    ├── PunadoFortuna.csproj
    ├── Hubs/
    │   └── GameHub.cs
    ├── Models/
    │   ├── ChipMapping.cs
    │   ├── DeviceDiscoveryResult.cs    ← NUEVO Sprint 0
    │   ├── GameState.cs
    │   └── TagRead.cs
    ├── Services/
    │   ├── DeviceDiscoveryService.cs   ← NUEVO Sprint 0
    │   ├── GameEngine.cs
    │   ├── RfidReaderService.cs
    │   └── SessionLogger.cs
    └── wwwroot/
        ├── index.html
        ├── css/
        │   └── game.css
        └── js/
            └── game.js
```
