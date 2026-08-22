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

Cada pecera contiene **10 fichas RFID**. Cada ficha tiene un chip UHF con identidad única y un puntaje secreto (de 1 a 10).

| El jugador... | El sistema detecta... |
|---|---|
| Mete la mano y saca un puñado | Qué fichas **dejaron de leerse** |
| Sigue eligiendo fichas | Cambios en tiempo real del puntaje |
| Termina de sacar | Las fichas que se fueron → **puntaje final** |

> **No hay un botón de "listo".** El sistema sabe cuándo terminaste porque las lecturas se estabilizan. La IA del juego decide el momento justo.

---

## 🎮 La experiencia

```
          STANDBY               ACTIVE              RESULT
     ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
     │  Esperando   │ →  │  ¡Manos a la │ →  │  ¡GANADOR!   │
     │  jugadores   │    │   obra!      │    │  42 puntos   │
     └──────────────┘    └──────────────┘    └──────────────┘
```

- **Sin intervención del operador**: el ciclo STANDBY → ACTIVE → RESULT → STANDBY es completamente automático
- **Dos jugadores simultáneos**: cada pecera es independiente, con su propia antena direccional
- **Autocorrección en vivo**: ¿devolviste una ficha? El puntaje se ajusta al instante
- **Modo kiosco**: pantalla grande, cero clicks, pura inmersión

---

## 🔬 ¿Por qué RFID?

Esto va mucho más allá de un juego de feria. Es una **demostración viva** del potencial de RFID UHF:

| Capacidad demostrada | Aplicación en el mundo real |
|---|---|
| Lectura simultánea de múltiples tags | Inventarios masivos en segundos |
| Detección por ausencia (lo que NO está) | Control de activos, anti-robo |
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

# 2. Bajar SignalR client (única dependencia externa)
npm install @microsoft/signalr
cp node_modules/@microsoft/signalr/dist/browser/signalr.min.js src/PunadoFortuna/wwwroot/js/

# 3. Correr en modo simulación (sin hardware)
cd src/PunadoFortuna
dotnet run

# 4. Abrir http://localhost:5085
```

---

## 🎛 Atajos para el operador

| Tecla | Acción |
|---|---|
| `F1` | Reiniciar ambas peceras |
| `F2` | Reiniciar Pecera 1 |
| `F3` | Reiniciar Pecera 2 |

---

## 📂 Estructura

```
src/PunadoFortuna/
├── Program.cs              # Entrada, inyección de dependencias
├── Models/                 # Estado del juego, lecturas de tags
├── Services/
│   ├── RfidReaderService   # Capa de abstracción del lector + stub
│   ├── GameEngine          # Máquina de estados, scoring, detección
│   └── SessionLogger       # Registro crudo de eventos por sesión
├── Hubs/
│   └── GameHub             # SignalR — push en tiempo real al frontend
└── wwwroot/                # Interfaz del juego
data/
└── mapeo-fichas.json       # EPC → puntaje secreto
```

---

<div align="center">

**I-Labs Chile** · Transformamos la educación con tecnología

[github.com/I-Labs-Chile](https://github.com/I-Labs-Chile) · [i-labs.cl](https://i-labs.cl)

</div>
