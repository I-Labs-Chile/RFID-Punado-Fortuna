<div align="center">

![RFID Game](https://img.shields.io/badge/Zebra-FX9600-blue?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8-512bd4?style=flat-square)
![License](https://img.shields.io/badge/license-proprietary-red?style=flat-square)

</div>

# 🎯 Puñado de Fortuna

**Un juego de feria potenciado por RFID invisible.**

Dos peceras, fichas con chip, y un lector que detecta en tiempo real qué sacaste. Sin cámaras, sin botones, sin trampas — pura tecnología que funciona como magia.

---

## ¿Cómo funciona?

Cada pecera contiene fichas RFID UHF con identidad única y un color asociado.

| El jugador... | El sistema detecta... |
|---|---|
| Mete la mano y saca un puñado | Cuántas fichas **se leen** (presentes) |
| Se estabilizan las lecturas | El puñado queda "listo" para revelar |
| Avanza con ENTER | La **cantidad** y luego los **colores** del puñado |

> **Sin scoring por ausencia.** El juego es "solo lectura": revela qué está presente (cantidad y colores), no calcula puntaje por las fichas que se fueron.

---

## 🎮 La experiencia

```
   WAITING          REVEAL_COUNT       GUESS_COLORS       REVEAL_COLORS
┌────────────┐  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐
│  Esperando │→ │  ¡HAY N       │→ │  ¿De qué      │→ │  COLORES      │
│  piezas    │  │   PIEZAS!     │  │  colores son? │  │  (verde, azul…)│
└────────────┘  └───────────────┘  └───────────────┘  └───────────────┘
```

- **ENTER** avanza de fase; **F1** reinicia
- **Antenas auto-descubiertas**: cada antena direccional se configura sola al conectar
- **Estabilidad automática**: el sistema marca el puñado como "listo" cuando las lecturas se estabilizan
- **Modo kiosco**: pantalla grande, sin mouse, pura inmersión

---

## 🔬 ¿Por qué RFID?

Esto va mucho más allá de un juego de feria. Es una **demostración viva** del potencial de RFID UHF:

| Capacidad demostrada | Aplicación en el mundo real |
|---|---|
| Lectura simultánea de múltiples tags | Inventarios masivos en segundos |
| Discriminación de tags por identidad y color | Clasificación y trazabilidad de inventario |
| Discriminación por antena (zona) | Portales RFID, seguimiento de mercadería |
| Resiliencia ante desconexión | Operación 24/7 en entornos industriales |
| Inferencia de estado en tiempo real | Logística inteligente, smart shelving |

El mismo lector Zebra FX9600 que usamos acá se despliega en centros de distribución, hospitales y líneas de producción en todo el mundo.

---

## 🛠 Stack tecnológico

| Capa | Tecnología |
|---|---|
| Lector RFID | Zebra FX9600 + 2 antenas AN480 |
| Conectividad | LLRP sobre TCP/IP (puerto 5084) |
| Backend | .NET 8 + ASP.NET Core + SignalR |
| Frontend | HTML/CSS/JS vanilla (sin frameworks, sin CDN) |
| Supervisión | NSSM — reinicio automático si falla |
| OS | Windows 10/11 x86-64 |

> Proceso único: el mismo servicio .NET habla con el reader Zebra, corre la lógica de juego, y sirve la interfaz web. Menos piezas, menos fallas.

---

## 🚀 Levantar el proyecto

```bash
# 1. Clonar
git clone https://github.com/I-Labs-Chile/RFID-Punado-Fortuna.git
cd RFID-Punado-Fortuna

# 2. Correr en modo simulación (sin hardware)
cd src/PunadoFortuna
dotnet run

# 3. Abrir http://localhost:5085
```

> `signalr.min.js` ya está empaquetado en `wwwroot/js/` (cero dependencias externas). El modo real se activa con `dotnet run -- --no-sim`.

---

## 🎛 Atajos para el operador

| Tecla | Acción |
|---|---|
| `F1` | Reiniciar (reset) |
| `ENTER` | Avanzar de fase |

---

## 📂 Estructura

```
src/PunadoFortuna/
├── Program.cs              # Entrada, inyección de dependencias
├── Models/                 # Estado del juego, lecturas de tags
├── Services/
│   ├── RfidReaderService   # Capa de abstracción del lector (real + simulación)
│   ├── DeviceDiscoveryService # Auto-descubrimiento del FX9600
│   ├── GameEngine          # Máquina de estados (revelar cantidad/colores)
│   └── SessionLogger       # Registro crudo de eventos por sesión
├── Hubs/
│   └── GameHub             # SignalR — push en tiempo real al frontend
└── wwwroot/                # Interfaz del juego
data/
├── mapeo-colores.json      # Fuente de verdad: EPC → color
└── mapeo-fichas.json       # Derivado: EPC → valor/zona/descripcion
```

---

<div align="center">

**I-Labs Chile** · Transformamos la educación con tecnología

[github.com/I-Labs-Chile](https://github.com/I-Labs-Chile) · [i-labs.cl](https://i-labs.cl)

</div>
