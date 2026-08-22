# NETWORK — Configuración de red

Guía de referencia sobre cómo funciona la conectividad de red entre la notebook y el FX9600.

---

## Escenarios de conexión

### A. Conexión directa (Ethernet notebook ↔ FX9600)

```
┌──────────┐     cable ethernet     ┌─────────┐
│ Notebook │ ─────────────────────── │ FX9600  │
│ (DHCP?)  │                        │ (DHCP?) │
└──────────┘                        └─────────┘
```

**Comportamiento esperado:**

1. Ninguno de los dos tiene servidor DHCP.
2. Ambos auto-asignan IPs en el rango APIPA: `169.254.0.0/16`.
3. La notebook obtiene algo como `169.254.x.x`. Verificar con:
   ```powershell
   ipconfig
   ```
   Buscar el adaptador "Ethernet" → `Autoconfiguración IPv4: 169.254.x.x`
4. El FX9600 obtiene otra IP en `169.254.x.x`.
5. La app hace **ping sweep** en esa subnet y encuentra al FX9600.
6. También intenta **mDNS** (requiere Bonjour instalado en Windows).

**Ventaja:** Plug & play real. Cero configuración.

**Desventaja:** El descubrimiento toma ~5-10s la primera vez.

---

### B. Misma red local (LAN)

```
┌──────────┐      ┌─────────┐      ┌─────────┐
│ Notebook │ ──── │ Router  │ ──── │ FX9600  │
│ DHCP OK  │      │  DHCP   │      │ DHCP OK │
└──────────┘      └─────────┘      └─────────┘
```

**Comportamiento esperado:**

1. Ambos obtienen IP del router (ej: `192.168.1.x`).
2. La app hace ping sweep en la subnet del adaptador activo.
3. Como el sweep cubre toda la `/24`, encuentra al FX9600.
4. mDNS también funciona si Bonjour está en la red.

---

### C. IP fija (manual)

Si ninguna de las anteriores funciona, forzar IP en `appsettings.json`:

```json
{
  "Fx9600": {
    "IpAddress": "169.254.100.50",
    "Port": 5084
  }
}
```

Cómo saber la IP del FX9600:
- Conectar monitor y teclado al FX9600 (tiene salida VGA y USB).
- La IP se muestra en la consola de arranque.
- Alternativa: la web UI muestra la IP si se conecta por el mismo método que antes funcionó.
- Alternativa: usar herramienta **123RFID Desktop** de Zebra que tiene su propio discovery.

---

## Protocolos y puertos

| Protocolo | Puerto | Propósito | Usado en |
|---|---|---|---|
| LLRP | TCP 5084 | Lectura/escritura de tags RFID | Conexión real (Sprint 1+) |
| HTTP | TCP 80 | Web UI del FX9600 | Diagnóstico, configuración |
| HTTPS | TCP 443 | Web UI segura del FX9600 | Configuración |
| mDNS | UDP 5353 | Descubrimiento Zeroconf/Bonjour | Auto-descubrimiento |
| ICMP | - | Ping | Ping sweep de descubrimiento |

---

## Pipeline de descubrimiento

El `DeviceDiscoveryService` ejecuta este pipeline en orden:

```
0. Subnets comunes prioritarias (192.168.100.x, 192.168.1.x, 192.168.0.x)
   └── TCP probe directo sobre :5084 (LLRP) en TODA la /24
   └── No depende de ICMP (firewalls suelen bloquear ping)
   └── Si encuentra LLRP OK → DEVICE FOUND (sale temprano)
   └── Concurrencia limitada, timeout ≤ 1s por conexión

1. GetActiveNetworkInterfaces()
   └── Enumera adaptadores de red activos (excluye virtuales, loopback, Hyper-V)

2. Para cada adaptador:
   └── PingSweepAsync(subnet)
       └── Ping a cada IP de la /24 (o /16 si APIPA)
       └── Concurrencia limitada a 50 pings simultáneos
       └── Timeout: 500ms por ping

3. En paralelo:
   └── MdnsDiscoveryAsync()
       └── Zeroconf browse + resolve
       └── Busca servicios que contengan "FX9600" o "zebra"
       └── Timeout: 5 segundos

4. Para cada IP candidata:
   └── TcpProbeAsync(ip, 5084) → ¿LLRP responde?
   └── HttpProbeAsync(ip)      → ¿Web UI responde?
   └── Si LLRP OK → DEVICE FOUND
```

> **Nota:** el paso 0 se ejecuta primero porque el FX9600 suele vivir en una de esas
> tres subnets (IP de fábrica `192.168.100.114`, o redes típicas de router).
> El ping sweep de adaptadores queda como respaldo para conexión directa (APIPA).

---

## APIPA (Automatic Private IP Addressing)

Cuando no hay servidor DHCP, Windows asigna una IP en `169.254.0.0/16`.

Esto es **lo esperado** en conexión directa notebook↔FX9600.

```
169.254.0.0 ────────────────── 169.254.255.255
     │                                  │
     └── Subnet mask: 255.255.0.0 (/16)
```

El ping sweep cubre esta subnet completa, por eso encuentra al FX9600 sin importar qué IP APIPA le tocó.

### Verificar APIPA

```powershell
ipconfig | Select-String "169.254"
```

---

## Detección de antenas conectadas

### Método actual (SDK)

```csharp
// Obtener todas las antenas del hardware
ushort[] availableAntennas = reader.Config.Antennas.AvailableAntennas;

// Verificar conexión física de cada puerto
foreach (ushort antId in availableAntennas)
{
    var physicalProps = reader.Config.Antennas[antId].GetPhysicalProperties();
    if (physicalProps.IsConnected)
    {
        Console.WriteLine($"Antena {antId}: CONECTADA (gain={physicalProps.AntennaGain}dB)");
    }
}
```

### Comportamiento

- **FX9600 con 4 puertos**: `AvailableAntennas` retorna `[1, 2, 3, 4]`
- **PhysicalProperties.IsConnected**: indica si hay antena físicamente enchufada
- **Configuración automática**: solo se configuran puertos con antena conectada
- **Fallback**: si no detecta ninguna, configura todas (por si el SDK falla)

### Logs esperados

```
SDK: Antena 1: CONECTADA (gain=6dB)
SDK: Antena 2: CONECTADA (gain=6dB)
SDK: Antena 3: desconectada
SDK: Antena 4: CONECTADA (gain=6dB)
SDK: Configurando 3 antena(s) conectada(s): [1, 2, 4]
```

Si aparece, la notebook ya está en modo APIPA. El FX9600 debería estarlo también.

### Forzar APIPA en el FX9600

Si el FX9600 está configurado con IP fija y querés que use APIPA:
1. Entrar a la web UI: `http://<ip_actual>`
2. Ir a Network Settings
3. Cambiar a DHCP
4. Al no encontrar servidor DHCP, caerá en APIPA automáticamente.

---

## mDNS / Bonjour en Windows

El descubrimiento mDNS requiere que el servicio Bonjour esté instalado. Windows no lo incluye por defecto.

**Instalar Bonjour:**

```powershell
# Opción A: instalar iTunes (incluye Bonjour)
winget install Apple.iTunes

# Opción B: instalar Bonjour Print Services (más liviano)
# Descargar de: https://support.apple.com/bonjour
```

Si no se instala Bonjour, el descubrimiento por mDNS falla silenciosamente y solo se usa ping sweep (que es más que suficiente).

---

## Troubleshooting de red

| Síntoma | Causa probable | Solución |
|---|---|---|
| `ipconfig` no muestra IP en el adaptador Ethernet | Cable suelto o driver faltante | Verificar cable, reinstalar driver |
| El log muestra barrido en `169.254.x.x` sin conexión ethernet | Interfaz VPN activa (ej. Tailscale usa `169.254.x.x`) | El discovery ya excluye adaptadores VPN/tunnel (Tailscale, WireGuard, OpenVPN, etc.) |
| Ping sweep encuentra 0 IPs | FX9600 apagado o en otra subnet | Verificar que el FX9600 esté encendido y el LED de link esté verde |
| Ping sweep encuentra IPs pero LLRP falla | LLRP desactivado en el FX9600 | Entrar a la web UI del FX9600 y activar LLRP |
| TCP 5084 rechaza conexión | Firewall bloqueando | Agregar regla de firewall o desactivar temporalmente |
| mDNS no encuentra nada | Bonjour no instalado | Ignorar (ping sweep es suficiente) o instalar Bonjour |
