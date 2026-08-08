# AGENTS.md — Puñado de Fortuna

## Stack

- **Hardware**: Zebra FX9600, 2x antena AN480 (una por pecera/jugador)
- **OS**: Windows 10/11 x86-64
- **Runtime**: .NET 8 con ASP.NET Core minimal API + SignalR
- **SDK RFID**: Zebra Host RFID SDK para Windows (binding .NET/C#, nunca C)
- **Frontend**: HTML/CSS/JS vanilla servido desde `wwwroot/`. SignalR client local, cero dependencias externas/CDN
- **Supervisor**: NSSM (Non-Sucking Service Manager) — reinicio automático si crashea

## Arquitectura

Proceso único .NET que aloja:
1. Lógica del reader (SDK Zebra)
2. Servidor web liviano (ASP.NET Core minimal API + SignalR)
3. Static files del frontend desde `wwwroot/`

Frontend en navegador modo kiosco, suscripto vía SignalR al estado de juego resuelto:

```json
{ "zona_id": 1, "score": 42, "match_state": "STANDBY|ACTIVE|RESULT", "winner": null }
```

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

## Fase de descubrimiento (bloqueante — no escribir código de producción hasta completar)

1. Conexión física y de red — confirmar IP del FX9600, validar ping/web UI
2. Validación sin código con 123RFID Desktop — confirmar que ambas antenas leen
3. ~~Linux/Java~~ — descartado, Windows/.NET es el camino primario
4. Primer contacto programático — suscribirse a eventos de lectura con ambas antenas y fichas reales
5. Inspección de payload — documentar campos reales: EPC, antenna port, RSSI, timestamp, cadencia
6. Medición de ausencia — GRACE_WINDOW con datos reales (cuántos ciclos falla un tag quieto)
7. Prueba de resiliencia — desconexión real y cómo lo reporta el SDK
8. Con payloads documentados → diseñar contrato de eventos, motor de estados, y capa de resiliencia

## Decisiones tomadas

- Windows + .NET (camino primario, más documentado)
- Linux/Java descartado como plan B explícito (solo si Windows falla)
- AN480 son muy dirigidas → cross-reading entre peceras es improbable pero se verificará en fase 2
- Proyecto se estructura con stub del reader para iterar sin hardware físico
