# ARCHITECTURE — Librería de conexión

Arquitectura del módulo de descubrimiento y conexión al FX9600 (Sprint 0).

---

## Diagrama de capas

```
┌─────────────────────────────────────────────────────┐
│  Program.cs                                         │
│  ┌───────────────────────────────────────────────┐  │
│  │ Configuración (appsettings.json)              │  │
│  │   Fx9600.IpAddress = "auto" | "192.168.x.x"  │  │
│  └──────────────────┬────────────────────────────┘  │
│                     │                                │
│  ┌──────────────────▼────────────────────────────┐  │
│  │ DeviceDiscoveryService.DiscoverAsync()         │  │
│  │                                                │  │
│  │  ┌──────────┐  ┌──────────┐  ┌─────────────┐  │  │
│  │  │ Ping     │  │ mDNS     │  │ TCP Probe   │  │  │
│  │  │ Sweep    │  │ Zeroconf │  │ :5084, :80  │  │  │
│  │  └────┬─────┘  └────┬─────┘  └──────┬──────┘  │  │
│  │       │              │               │         │  │
│  │       └──────────────┼───────────────┘         │  │
│  │                      ▼                          │  │
│  │           DeviceDiscoveryResult                 │  │
│  │           ├── IpAddress: string?                │  │
│  │           ├── Port: int                         │  │
│  │           ├── LLRPReachable: bool               │  │
│  │           ├── HttpReachable: bool               │  │
│  │           ├── DiscoveryMethod: string            │  │
│  │           └── Diagnostics: List<string>          │  │
│  └──────────────────┬────────────────────────────┘  │
│                     │ registrado como Singleton      │
│  ┌──────────────────▼────────────────────────────┐  │
│  │ RfidBackgroundService                         │  │
│  │   ├── Lee DeviceDiscoveryResult.IpAddress     │  │
│  │   ├── Si null → retry loop cada 10s           │  │
│  │   └── Si OK  → RfidReaderService.ConnectAsync │  │
│  └───────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

---

## DeviceDiscoveryService

**Namespace:** `PunadoFortuna.Services`  
**Archivo:** `Services/DeviceDiscoveryService.cs`

Servicio stateless. Responsable de encontrar el FX9600 en la red.

### Métodos principales

| Método | Descripción |
|---|---|
| `DiscoverAsync(staticIp?, port, ct)` | Pipeline completo. Si `staticIp` != null/auto, saltea discovery y solo verifica conectividad. |
| `GetActiveNetworkInterfaces()` | Devuelve adaptadores de red activos (filtra virtuales, loopback) |
| `PingSweepAsync(subnet, broadcast, ct)` | Ping a todas las IPs del rango. Concurrencia limitada. |
| `MdnsDiscoveryAsync(ct)` | Zeroconf browse + resolve. Busca "FX9600" o "zebra". |
| `TcpProbeAsync(ip, port, ct)` | Intenta TCP connect. True si el puerto acepta conexión. |
| `HttpProbeAsync(ip, ct)` | HTTP GET a `http://<ip>` y `https://<ip>`. |

### Algoritmo

```
DiscoverAsync(staticIp: null, port: 5084):
  interfaces = GetActiveNetworkInterfaces()
  
  candidates = {}
  for each interface:
    candidates += PingSweep(interface.subnet)
  
  candidates += await MdnsDiscovery()  // paralelo
  
  for each ip in candidates:
    if TcpProbe(ip, 5084):      // LLRP
      return DeviceDiscoveryResult(
        IpAddress = ip,
        Port = 5084,
        LLRPReachable = true,
        DiscoveryMethod = "auto_ping_sweep"
      )
  
  return DeviceDiscoveryResult.NotFound
```

### Dependencias externas

| Dependencia | Versión | Propósito |
|---|---|---|
| `Zeroconf` | 3.7.16 | mDNS discovery |
| `System.Net.NetworkInformation` | built-in | Ping, interfaces de red |
| `System.Net.Sockets` | built-in | TCP probe |

---

## DeviceDiscoveryResult

**Namespace:** `PunadoFortuna.Models`  
**Archivo:** `Models/DeviceDiscoveryResult.cs`

DTO inmutable con el resultado del descubrimiento.

### Propiedades

| Propiedad | Tipo | Descripción |
|---|---|---|
| `IpAddress` | `string?` | IP del FX9600. null si no encontrado. |
| `Port` | `int` | Puerto LLRP (default 5084) |
| `Hostname` | `string?` | Hostname (reservado para mDNS) |
| `DiscoveryMethod` | `string` | Cómo se encontró: `"static_config"`, `"auto_ping_sweep"`, `"auto_mdns"`, `"none"` |
| `LLRPReachable` | `bool` | El puerto LLRP aceptó TCP |
| `HttpReachable` | `bool` | La web UI respondió HTTP |
| `Diagnostics` | `List<string>` | Log detallado del proceso |
| `DiscoveredAt` | `DateTimeOffset` | Timestamp del descubrimiento |

### Métodos de fábrica

| Método | Uso |
|---|---|
| `DeviceDiscoveryResult.NotFound` | Cuando no se encuentra el dispositivo |
| `DeviceDiscoveryResult.FromConfig(ip, port)` | Cuando se usa IP fija del config |

---

## RfidBackgroundService

**Namespace:** `PunadoFortuna` (en `Program.cs`)  
**Archivo:** `Program.cs`

BackgroundService que gestiona la conexión al reader.

### Flujo

```
ExecuteAsync:
  host = discoveryResult.IpAddress
  
  if host == null:
    loop cada 10s:
      retry DiscoveryService.DiscoverAsync()
      if found: break
  
  if host != null:
    RfidReaderService.ConnectAsync(host, port)
  
  idle loop (mantiene el servicio vivo)
  
  finally:
    RfidReaderService.DisconnectAsync()
```

---

## Configuración (appsettings.json)

### Sección `Fx9600`

```json
{
  "Fx9600": {
    "IpAddress": "auto",
    "Port": 5084,
    "DiscoveryTimeoutMs": 10000,
    "PingTimeoutMs": 500,
    "TcpTimeoutMs": 2000,
    "MaxPingConcurrency": 50
  }
}
```

| Campo | Default | Descripción |
|---|---|---|
| `IpAddress` | `"auto"` | IP del FX9600. `"auto"` = descubrimiento automático. |
| `Port` | `5084` | Puerto LLRP estándar |
| `PingTimeoutMs` | `500` | Timeout por ping individual |
| `TcpTimeoutMs` | `2000` | Timeout para TCP connect |
| `MaxPingConcurrency` | `50` | Máximo de pings simultáneos |

### Persistencia

Cuando se descubre una IP automáticamente, se escribe en `appsettings.json` reemplazando `"auto"` por la IP concreta. Así la próxima ejecución es instantánea (no repite el scan).

---

## CLI: modo `--discover`

```powershell
dotnet run -- discover              # descubrimiento normal
dotnet run -- discover 192.168.1.50 # probar IP específica
```

Salida detallada con todos los pasos del pipeline y resultado final. No levanta servidor web ni SignalR.

---

## Modelos relacionados

### NetworkInterfaceInfo

```csharp
public class NetworkInterfaceInfo
{
    public string Name { get; init; }          // "Ethernet"
    public string Description { get; init; }    // "Intel(R) Ethernet Connection..."
    public string IpAddress { get; init; }      // "169.254.100.25"
    public int PrefixLength { get; init; }      // 16 (APIPA) o 24 (LAN típica)
    public string Gateway { get; init; }        // "" en conexión directa
}
```

### TagRead (existente, usado en RfidReaderService)

```csharp
public class TagRead
{
    public string Epc { get; set; }
    public short AntennaId { get; set; }
    public short PeakRssi { get; set; }
    public int SeenCount { get; set; }
    public short Phase { get; set; }
    public short ChannelIndex { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
```

---

## Lo que NO está en Sprint 0

| Componente | Sprint |
|---|---|
| SDK Zebra real (`Symbol.RFID3.dll`) | Sprint 1 |
| Eventos de lectura de tags reales | Sprint 1 |
| Configuración de antenas | Sprint 1 |
| Heartbeat / keepalive del reader | Sprint 1 |
| `RfidReaderService.ConnectAsync` modo real | Sprint 1 |
| `RfidReaderService.ReconnectAsync` con SDK | Sprint 1 |

En Sprint 0, `RfidReaderService` solo opera en modo simulación (`IsSimulationMode = true`), que es el default. El modo real se activa con `--no-sim` y será implementado con el SDK Zebra en Sprint 1.
