# SDK — Zebra RFID FXSeries Host .NET SDK

Integración del SDK oficial de Zebra para lectores RFID FXSeries (FX9600, FX7500, etc).

**Instalación:** `winget install "Zebra RFID FXSeries Host .NET SDK"` o descargar de zebra.com/support.

---

## Dependencias

| Archivo | Ubicación | Propósito |
|---|---|---|
| `Symbol.RFID3.Host.dll` | `C:\Program Files\Zebra RFID FXSeries Host .NET SDK\SDK\` | Assembly gestionado (.NET) |
| `RFIDAPI32PC.dll` | `C:\Program Files\Zebra RFID FXSeries Host .NET SDK\Driver\64bits\` | DLL nativa (x64) |

**Referencia en .csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Symbol.RFID3.Host">
      <HintPath>C:\Program Files\Zebra RFID FXSeries Host .NET SDK\SDK\Symbol.RFID3.Host.dll</HintPath>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <None Update="RFIDAPI32PC.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

---

## API — Flujo básico

### Conectar y leer tags

```csharp
using Symbol.RFID3;

var reader = new RFIDReader("192.168.100.114", 5084, 5000);
reader.Connect();

reader.Events.ReadNotify += (sender, e) =>
{
    var tags = reader.Actions.GetReadTags(100);
    foreach (var tag in tags)
    {
        Console.WriteLine($"{tag.TagID} ANT:{tag.AntennaID} RSSI:{tag.PeakRSSI}");
    }
};

reader.Actions.Inventory.Perform();  // inicia lectura continua

// ... cuando termina ...
reader.Actions.Inventory.Stop();
reader.Disconnect();
```

### Mapeo TagData → TagRead

| SDK (`TagData`) | Modelo (`TagRead`) | Tipo |
|---|---|---|
| `TagID` | `Epc` | `string` |
| `AntennaID` | `AntennaId` | `ushort → short` |
| `PeakRSSI` | `PeakRssi` | `short` |
| `TagSeenCount` | `SeenCount` | `uint → int` |
| `ChannelIndex` | `ChannelIndex` | `ushort → short` |
| - | `Phase` | `short` (no disponible en SDK v1.2) |
| - | `Timestamp` | `DateTimeOffset.UtcNow` |

---

## Implementación en RfidReaderService

**Archivo:** `Services/RfidReaderService.cs`

El servicio tiene dos modos:

### Modo simulación (default)

```powershell
dotnet run
```

Usa un `Timer` que genera tags simulados cada 100ms basados en `mapeo-fichas.json`.

### Modo real

```powershell
dotnet run -- --no-sim
```

1. Crea `RFIDReader` con host, port, timeout
2. Llama `Connect()`
3. Suscribe `Events.ReadNotify`
4. Llama `Actions.Inventory.Perform()`
5. Cada evento `ReadNotify` → `GetReadTags(100)` → `TagRead[]` → `TagsRead` event
6. `DisconnectAsync` → `Inventory.Stop()` + `Disconnect()`

---

## API — Detección de antenas conectadas

### Propiedades físicas por antena

```csharp
// Obtener antenas disponibles (todas las que soporta el hardware)
ushort[] availableAntennas = reader.Config.Antennas.AvailableAntennas;

foreach (ushort antId in availableAntennas)
{
    // Obtener propiedades físicas de cada puerto
    Antennas.AntennaProperties physicalProps = reader.Config.Antennas[antId].GetPhysicalProperties();

    if (physicalProps.IsConnected)
    {
        Console.WriteLine($"Antena {antId}: CONECTADA (gain={physicalProps.AntennaGain}dB)");
    }
    else
    {
        Console.WriteLine($"Antena {antId}: desconectada");
    }
}
```

### Configuración RF por antena

```csharp
// Obtener configuración actual
Antennas.Config config = reader.Config.Antennas[antId].GetConfig();

// Modificar y aplicar
config.TransmitPowerIndex = 70;  // potencia TX
config.ReceiveSensitivityIndex = 5;  // sensibilidad RX
reader.Config.Antennas[antId].SetConfig(config);
```

### Persistencia LLRP

```csharp
// Guardar configuración en el reader (sobrevive reinicios)
reader.Config.SaveLlrpConfig(IntPtr.Zero);
```

---

## Notas

- El SDK es síncrono. Las operaciones se ejecutan en `Task.Run()` para no bloquear el event loop de ASP.NET.
- `AttachTagDataWithReadEvent = false` (default) — usamos `GetReadTags()` explícitamente.
- La DLL nativa `RFIDAPI32PC.dll` debe estar en el output directory. El .csproj la copia automáticamente con `CopyToOutputDirectory`.
- Solo compatible con **Windows**. El SDK requiere .NET Framework y DLLs nativas Win32.
- Puerto LLRP estándar: **5084**. Puerto seguro (SSL): **5085**.
- `PhysicalProperties.IsConnected` indica si hay antena físicamente conectada al puerto.
