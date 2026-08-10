# LLRP — Low Level Reader Protocol

Implementación del protocolo LLRP v1.1 para comunicación directa con el Zebra FX9600.

**Archivo:** `Services/LLRP/LlrpClient.cs` — cliente LLRP completo, sin dependencias externas.

---

## ¿Qué es LLRP?

LLRP (Low Level Reader Protocol) es el protocolo estándar EPCglobal que usan los lectores RFID UHF para comunicarse por TCP. El FX9600 lo expone en el puerto **5084**.

Es un protocolo binario con mensajes TLV (Type-Length-Value). Cada mensaje tiene:

```
Header (10 bytes): PackedType(2) + Length(4) + MessageID(4)
Parameters (TLV):  Reserved(2) + Type(2) + Length(2) + Value(N)
```

---

## Formato binario del header

```
Byte 0: [Rsvd:2][Version:3][TypeHi:3]  — Version=1 para LLRP v1.1
Byte 1: [TypeLo:7][Rsvd:1]
Byte 2-5: MessageLength (uint32 big-endian, incluye los 10 bytes del header)
Byte 6-9: MessageID (uint32 big-endian)
```

**Ejemplo:** GET_READER_CAPABILITIES (type=1, ID=1)
```
08 02 00 00 00 10 00 00 00 01 [payload...]
```
- `08 02` → Type=1, Version=1
- `00 00 00 10` → Length=16 (10 header + 6 payload)
- `00 00 00 01` → MessageID=1

---

## Tipos de mensaje usados

### Requests

| Type | Nombre | Propósito |
|---|---|---|
| 1 | GET_READER_CAPABILITIES | Descubrir capacidades y antenas |
| 20 | ADD_ROSPEC | Configurar y agregar un ROSpec |
| 24 | ENABLE_ROSPEC | Habilitar el ROSpec |
| 22 | START_ROSPEC | Iniciar el inventario |
| 23 | STOP_ROSPEC | Detener el inventario |

### Unsolicited (del reader al cliente)

| Type | Nombre | Propósito |
|---|---|---|
| 61 | RO_ACCESS_REPORT | Tags leídos (EPC, antena, RSSI, etc.) |
| 63 | READER_EVENT_NOTIFICATION | Keepalive y eventos del reader |

---

## Flujo de conexión

```
1. TCP Connect → 192.168.100.114:5084

2. GET_READER_CAPABILITIES
   → Respuesta con GeneralDeviceCapabilities
   → Parseamos AntennaConfiguration para obtener AntennaIDs

3. ADD_ROSPEC
   Payload: ROSpec {
     ROSpecID: 1
     Priority: 0
     CurrentState: Disabled (0)
     ROBoundarySpec {
       StartTrigger: Immediate (1)
       StopTrigger: Null (0) — nunca para
     }
     AISpec {
       AntennaID: [1] (puerto de antena)
       StopTrigger: Null (0)
       InventoryParameterSpec {
         InventoryParameterSpecID: 1
         ProtocolID: 1 (EPC Gen2)
       }
     }
     ROReportSpec {
       Trigger: Upon_N_Tags_Or_End_Of_AISpec (1)
       N: 1 (reportear cada tag)
     }
   }

4. ENABLE_ROSPEC (ROSpecID=1)
   → Habilita el ROSpec

5. START_ROSPEC (ROSpecID=1)
   → Inicia la lectura continua

6. RO_ACCESS_REPORT (asincrónico, continuo)
   → Cada ciclo de inventario envía TagReportData
```

---

## TagReportData — Estructura de un tag leído

Cada RO_ACCESS_REPORT contiene uno o más TagReportData (TLV type 240).

Dentro de cada TagReportData:

| Parámetro | Type | Formato | Ejemplo |
|---|---|---|---|
| EPC-96 | TV 11 | 12 bytes hex | `300833B2DDD9014000000000` |
| AntennaID | TV 1 | uint16 | 1 |
| PeakRSSI | TV 5 | int8 (signed) | -45 |
| ChannelIndex | TV 6 | uint16 | 5 |
| TagSeenCount | TV 4 | uint16 | 127 |
| FirstSeenTimestampUTC | TV 2 | 8 bytes (µs) | - |
| LastSeenTimestampUTC | TV 3 | 8 bytes (µs) | - |

---

## Mapeo al modelo `TagRead`

```csharp
public class TagRead
{
    public string Epc { get; set; }         // ← EPC-96 (hex)
    public short AntennaId { get; set; }     // ← AntennaID
    public short PeakRssi { get; set; }      // ← PeakRSSI
    public int SeenCount { get; set; }       // ← TagSeenCount
    public short Phase { get; set; }         // (no disponible en LLRP estándar)
    public short ChannelIndex { get; set; }  // ← ChannelIndex
    public DateTimeOffset Timestamp { get; set; }  // ← UTC now
}
```

Nota: `Phase` no está disponible en LLRP estándar. Se deja en 0.

---

## Parámetros TV comunes

TV (Type-Value) es un parámetro compacto: 1 byte de tipo seguido de N bytes de valor.

| Type (byte>>1) | Nombre | Value size | Descripción |
|---|---|---|---|
| 1 | AntennaID / ROSpecID / otros IDs | 2 bytes (uint16) | IDs genéricos |
| 2 | FirstSeenTimestampUTC | 8 bytes | Microsegundos UTC |
| 3 | LastSeenTimestampUTC / ProtocolID | 2 bytes | |
| 4 | TagSeenCount | 2 bytes | |
| 5 | PeakRSSI | 1 byte (signed) | dBm |
| 6 | ChannelIndex | 2 bytes | Canal RF |
| 7 | ROSpecStartTriggerType / otros | 1 byte | Enumeraciones |
| 9 | ROSpecID / ROSpecParameter | 5 bytes | ID de ROSpec |
| 11 | EPC-96 | 12 bytes | EPC de 96 bits |

---

## Diagnóstico

### Modo simulación (default)

```powershell
dotnet run                           # simulación, sin hardware
```

### Modo real

```powershell
dotnet run -- --no-sim               # conexión LLRP real al FX9600
```

### Ver logs de tags

Los tags leídos se loguean en `logs/session_*.log` con formato:
```
[TIMESTAMP] TAG | EPC:300833B2DDD9014000000000 ANT:1 RSSI:-45 COUNT:127 PHASE:0 CH:5
```

### Datos crudos LLRP

En modo `--no-sim`, el `SessionLogger` guarda los bytes crudos de cada RO_ACCESS_REPORT:
```
[TIMESTAMP] LLRP_RAW | Hex: 043D000000...
```

Esto permite capturar tráfico real para análisis y simulación futura.
