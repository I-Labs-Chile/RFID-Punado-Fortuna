# SETUP — Puñado de Fortuna

Guía de instalación completa para un PC Windows 10 nuevo. Seguí estos pasos en orden.

---

## Requisitos mínimos

| Componente | Requerido |
|---|---|
| OS | Windows 10 x86-64 (o Windows 11) |
| RAM | 4 GB mínimo (8 GB recomendado) |
| Disco | 500 MB libres |
| Red | Puerto Ethernet disponible (conexión directa al FX9600 o misma LAN) |
| .NET | .NET 8 SDK + Runtime (instalado en paso 2) |

---

## 1. Conectar el hardware

### Conexión directa (recomendada)

```
Notebook/PC ──(cable ethernet)──> FX9600
```

1. Conectá el cable Ethernet directamente del puerto Ethernet de la notebook al puerto Ethernet del FX9600.
2. Encendé el FX9600 y esperá ~60 segundos a que bootee.
3. La notebook va a auto-asignarse una IP (APIPA `169.254.x.x`). El FX9600 también.
4. **No hace falta configurar nada.** La app lo descubre automáticamente.

### Conexión por LAN (alternativa)

Si el FX9600 y la notebook están en la misma red local:
- El FX9600 obtiene IP por DHCP del router.
- La app hace ping sweep en la subnet para encontrarlo.
- Si falla, usar `dotnet run -- discover` para diagnóstico.

---

## 2. Instalar .NET 8 SDK

```powershell
# Opción A: vía winget (recomendado, ya viene en Windows 10/11)
winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements

# Opción B: descargar manualmente de https://dotnet.microsoft.com/download/dotnet/8.0
```

Verificar instalación:

```powershell
dotnet --version
# Debe mostrar: 8.0.x
```

---

## 3. Clonar el proyecto

```powershell
cd C:\Users\<usuario>\Proyectos
git clone https://github.com/I-Labs-Chile/RFID-Punado-Fortuna.git
cd RFID-Punado-Fortuna
```

---

## 4. Restaurar dependencias

```powershell
cd src\PunadoFortuna
dotnet restore
```

Esto baja los paquetes NuGet:
- `Zeroconf` — descubrimiento mDNS del FX9600
- `Microsoft.AspNetCore.SignalR` — comunicación en tiempo real con el frontend

---

## 5. Descubrir el FX9600

### Modo automático (recomendado)

Simplemente ejecutá la app. Detecta el FX9600 sola:

```powershell
dotnet run
```

La primera vez tarda ~10-15 segundos escaneando la red. Una vez encontrado, guarda la IP en `appsettings.json` y las siguientes veces es instantáneo.

### Modo diagnóstico

Si querés ver qué encuentra sin levantar el servidor:

```powershell
dotnet run -- discover
```

Salida de ejemplo:

```
==========================================
  MODO DESCUBRIMIENTO - FX9600
==========================================

--- RESULTADO ---
IP:          169.254.100.50
Puerto LLRP: 5084
Método:      auto_ping_sweep
LLRP (5084): OK
HTTP (80):   OK
```

---

## 6. Abrir el juego

Una vez que la app muestra:

```
> FX9600 encontrado en 169.254.100.50:5084
```

Abrí en el navegador: **http://localhost:5085**

---

## 7. Instalar como servicio (producción)

Para que la app arranque automáticamente con Windows y se reinicie si crashea:

```powershell
# Instalar NSSM
winget install NSSM.NSSM --accept-source-agreements

# Crear el servicio
nssm install PunadoFortuna "dotnet" "run --project C:\Users\<usuario>\Proyectos\RFID-Punado-Fortuna\src\PunadoFortuna"
nssm set PunadoFortuna AppDirectory "C:\Users\<usuario>\Proyectos\RFID-Punado-Fortuna\src\PunadoFortuna"
nssm set PunadoFortuna Start SERVICE_AUTO_START
nssm start PunadoFortuna
```

---

## Troubleshooting

| Problema | Solución |
|---|---|
| "No se encontró ningún dispositivo FX9600" | Verificar que el FX9600 esté encendido y el cable ethernet conectado. Ejecutar `dotnet run -- discover` para diagnóstico. |
| "No .NET SDKs were found" | Instalar .NET 8 SDK (paso 2) |
| El ping sweep no encuentra nada | Si es conexión directa, verificar que ambos lados tengan IPs APIPA (`169.254.x.x`). Probar `ipconfig` para confirmar. |
| LLRP no responde | El FX9600 debe tener el servicio LLRP activado. Entrar a la web UI del FX9600 (`http://<ip>`) y verificar. |
| mDNS no funciona en Windows | Requiere Bonjour/mDNS. Si no está, instalar iTunes o Bonjour Print Services de Apple. Alternativa: usar IP fija en `appsettings.json`. |

---

## Configuración manual de IP fija

Si el descubrimiento automático no funciona, editar `src/PunadoFortuna/appsettings.json`:

```json
{
  "Fx9600": {
    "IpAddress": "192.168.1.100",
    "Port": 5084
  }
}
```

Luego ejecutar `dotnet run` normalmente.
