/* ==========================================================================
   Test Harness — Puñado de Fortuna
   Mock de SignalR que reemplaza al cliente real ANTES de cargar game.js.
   El game.js de producción corre sin modificaciones: probamos el código real.
   Replica la máquina de estados de Services/GameEngine.cs.
   ========================================================================== */
(function () {
  'use strict';

  /* ---------------- Máquina de estados (espejo de GameEngine.cs) --------- */

  var PHASES = ['WAITING', 'REVEAL_COUNT', 'GUESS_COLORS', 'REVEAL_COLORS'];
  var TOTAL_CHIPS = 105;

  var machine = {
    phase: 'WAITING',
    tagCount: 0,
    breakdown: {},
    isStable: false,

    advance: function () {
      switch (this.phase) {
        case 'WAITING': this.phase = 'REVEAL_COUNT'; break;
        case 'REVEAL_COUNT': this.phase = 'GUESS_COLORS'; break;
        case 'GUESS_COLORS': this.phase = 'REVEAL_COLORS'; break;
        case 'REVEAL_COLORS': this.reset(); return;
      }
      emitState();
    },

    reset: function () {
      this.phase = 'WAITING';
      this.tagCount = 0;
      this.breakdown = {};
      this.isStable = false;
      emitState();
    },

    load: function (s) {
      this.phase = typeof s.phase === 'string' ? s.phase : 'WAITING';
      this.tagCount = typeof s.tagCount === 'number' ? s.tagCount : 0;
      this.breakdown = s.colorBreakdown && typeof s.colorBreakdown === 'object'
        ? JSON.parse(JSON.stringify(s.colorBreakdown)) : {};
      this.isStable = !!s.isStable;
      emitState();
    },

    addPrize: function () {
      if (this.phase === 'WAITING') this.phase = 'GUESS_COLORS';
      this.breakdown = JSON.parse(JSON.stringify(this.breakdown));
      this.breakdown.premio = (this.breakdown.premio || 0) + 1;
      this.tagCount += 1;
      emitState();
    },

    snapshot: function () {
      var epcs = [];
      for (var i = 0; i < Math.min(this.tagCount, TOTAL_CHIPS); i++) {
        epcs.push('E28069150000TEST0000' + ('00' + i.toString(16).toUpperCase()).slice(-2));
      }
      return {
        Phase: this.phase,
        TagCount: this.tagCount,
        ColorBreakdown: JSON.parse(JSON.stringify(this.breakdown)),
        PresentChips: this.tagCount,
        TotalChips: TOTAL_CHIPS,
        PresentEpcs: epcs,
        IsStable: this.isStable,
        Timestamp: new Date().toISOString()
      };
    }
  };

  /* ---------------- Mock de SignalR -------------------------------------- */

  var handlers = { state: [], connChanged: [], reconnecting: [], reconnected: [], close: [] };

  function emitEvent(list, payload) {
    var arr = list.slice();
    for (var i = 0; i < arr.length; i++) {
      try { arr[i](payload); } catch (e) { console.error('[mock] handler error', e); }
    }
  }

  function emitState() {
    var snap = machine.snapshot();
    emitEvent(handlers.state, snap);
    harness.log('emit', 'GameStateUpdate → ' + brief(snap));
  }

  function brief(state) {
    var colors = Object.keys(state.ColorBreakdown || {}).map(function (k) {
      return k + '=' + state.ColorBreakdown[k];
    }).join(',');
    return '{phase:' + state.Phase + ', count:' + state.TagCount +
      ', stable:' + state.IsStable + (colors ? ', [' + colors + ']' : '') + '}';
  }

  function MockConnection() {}

  MockConnection.prototype.on = function (name, cb) {
    if (name === 'GameStateUpdate') handlers.state.push(cb);
    else if (name === 'ConnectionChanged') handlers.connChanged.push(cb);
    return this;
  };
  MockConnection.prototype.onreconnecting = function (cb) { handlers.reconnecting.push(cb); return this; };
  MockConnection.prototype.onreconnected = function (cb) { handlers.reconnected.push(cb); return this; };
  MockConnection.prototype.onclose = function (cb) { handlers.close.push(cb); return this; };

  MockConnection.prototype.start = function () {
    var self = this;
    return new Promise(function (resolve) {
      setTimeout(function () {
        harness.log('info', 'SignalR mock conectado a /gamehub');
        emitEvent(handlers.state, machine.snapshot());
        emitEvent(handlers.connChanged, true);
        self._started = true;
        resolve();
      }, 150);
    });
  };

  MockConnection.prototype.invoke = function (method) {
    var self = this;
    return new Promise(function (resolve, reject) {
      setTimeout(function () {
        if (!self._started) { reject(new Error('conexión no iniciada')); return; }
        harness.log('invoke', method + '()');
        if (method === 'AdvancePhase') machine.advance();
        else if (method === 'Reset') machine.reset();
        else harness.log('warn', 'Método desconocido: ' + method);
        resolve();
      }, 60);
    });
  };

  function MockBuilder() {}
  MockBuilder.prototype.withUrl = function () { return this; };
  MockBuilder.prototype.withAutomaticReconnect = function () { return this; };
  MockBuilder.prototype.configureLogging = function () { return this; };
  MockBuilder.prototype.build = function () { return new MockConnection(); };

  // Debe existir ANTES de que game.js se ejecute (orden de <script>)
  window.signalR = {
    LogLevel: { Trace: 5, Debug: 4, Information: 3, Warning: 2, Error: 1, Critical: 0, None: -1 },
    HubConnectionBuilder: MockBuilder
  };

  /* ---------------- Consola del panel ------------------------------------ */

  var logBox = null;
  var LOG_CAP = 250;

  var harness = {
    log: function (type, text) {
      if (!logBox) return;
      var time = new Date().toTimeString().slice(0, 8) + '.' +
        String(Date.now() % 1000).padStart(3, '0');
      var line = document.createElement('div');
      line.className = 't-' + type;
      var stamp = document.createElement('time');
      stamp.textContent = time;
      line.appendChild(stamp);
      line.appendChild(document.createTextNode(text));
      logBox.appendChild(line);
      while (logBox.children.length > LOG_CAP) logBox.removeChild(logBox.firstChild);
      logBox.scrollTop = logBox.scrollHeight;
    }
  };

  /* ---------------- Escenarios ------------------------------------------- */

  var SCENARIOS = {
    waitingUnstable: function () {
      machine.load({ phase: 'WAITING', tagCount: 34, colorBreakdown: { verde: 12, azul: 14, naranja: 8 }, isStable: false });
    },
    waitingStable: function () {
      machine.load({ phase: 'WAITING', tagCount: 87, colorBreakdown: { verde: 18, azul: 20, naranja: 19, rosado: 15, celeste: 15 }, isStable: true });
    },
    revealCount: function () {
      machine.load({ phase: 'REVEAL_COUNT', tagCount: 87, colorBreakdown: {}, isStable: true });
    },
    guessColors: function () {
      machine.load({ phase: 'GUESS_COLORS', tagCount: 87, colorBreakdown: {}, isStable: true });
    },
    revealColors: function () {
      machine.load({ phase: 'REVEAL_COLORS', tagCount: 87, colorBreakdown: { verde: 18, azul: 20, naranja: 19, rosado: 15, celeste: 15 }, isStable: true });
    },
    premioRound: function () {
      if (machine.phase === 'WAITING') machine.phase = 'GUESS_COLORS';
      machine.addPrize();
      assertSoon(function () { return overlayShown(); },
        'PREMIO tras ronda → overlay visible',
        'PREMIO tras ronda NO disparó el overlay');
    },
    premioWaiting: function () {
      // Limpiar latch previo con un WAITING sin premio (como tras un reset)
      machine.load({ phase: 'WAITING', tagCount: 87, colorBreakdown: {}, isStable: true });
      setTimeout(function () {
        machine.load({ phase: 'WAITING', tagCount: 88, colorBreakdown: { premio: 1 }, isStable: true });
        assertSoon(function () { return !overlayShown(); },
          'PREMIO en WAITING → correctamente NO gana',
          'FALLO: ganó estando en WAITING (debe exigir ronda iniciada)');
      }, 150);
    }
  };

  /* ---------------- Utilidades -------------------------------------------- */

  function overlayShown() {
    var el = document.getElementById('winOverlay');
    return el && !el.classList.contains('hidden');
  }

  function assertSoon(conditionFn, passMsg, failMsg) {
    setTimeout(function () {
      if (conditionFn()) harness.log('pass', '✔ ' + passMsg);
      else harness.log('fail', '✘ ' + failMsg);
    }, 250);
  }

  function pressKey(key) {
    harness.log('key', 'Tecla ' + key + ' despachada (evento real)');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: key, bubbles: true }));
  }

  function sleep(ms) {
    return new Promise(function (r) { setTimeout(r, ms); });
  }

  /* ---------------- Secuencia automática ---------------------------------- */

  var seqRunning = false;

  function runSequence(btn) {
    if (seqRunning) return;
    seqRunning = true;
    btn.disabled = true;

    var steps = [
      function () { harness.log('info', '── SECUENCIA: reinicio inicial ──'); pressKey('F1'); return sleep(700); },
      function () { harness.log('info', 'Paso 1/6: fichas entrando (inestable)'); SCENARIOS.waitingUnstable(); return sleep(1300); },
      function () { harness.log('info', 'Paso 2/6: lectura estable (LISTO)'); SCENARIOS.waitingStable(); return sleep(1600); },
      function () { harness.log('info', 'Paso 3/6: ENTER → revelar cantidad'); pressKey('Enter'); return sleep(1600); },
      function () { harness.log('info', 'Paso 4/6: ENTER → adivinar colores'); pressKey('Enter'); return sleep(1400); },
      function () { harness.log('info', 'Paso 5/6: ENTER → revelar colores'); pressKey('Enter'); return sleep(2200); },
      function () { harness.log('info', 'Paso 6/6: ¡aparece ficha PREMIO en lecturas!'); machine.addPrize();
        return sleep(600).then(function () {
          assertSoon(overlayShown,
            'Secuencia: overlay de victoria mostrado',
            'Secuencia: el overlay NO apareció con premio activo'); }); },
      function () { harness.log('info', 'Cierre: tecla F1 para reiniciar'); pressKey('F1'); return sleep(500).then(function () {
          assertSoon(function () { return !overlayShown() && machine.phase === 'WAITING'; },
            'Secuencia: reset limpio, juego en WAITING',
            'Secuencia: el estado no volvió a WAITING tras F1'); }); },
      function () { harness.log('info', '── SECUENCIA COMPLETA ──'); }
    ];

    var p = Promise.resolve();
    steps.forEach(function (s) { p = p.then(s); });
    p.catch(function (e) { harness.log('fail', 'Error en secuencia: ' + e.message); })
     .then(function () { seqRunning = false; btn.disabled = false; });
  }

  /* ---------------- Conexión simulada -------------------------------------- */

  var CONN_ACTIONS = {
    reconnecting: function () {
      harness.log('warn', 'onreconnecting disparado');
      emitEvent(handlers.reconnecting);
    },
    disconnected: function () {
      harness.log('warn', 'onclose + ConnectionChanged(false)');
      emitEvent(handlers.connChanged, false);
      emitEvent(handlers.close);
    },
    connected: function () {
      harness.log('pass', 'Conexión restaurada');
      emitEvent(handlers.connChanged, true);
      emitEvent(handlers.reconnected);
    }
  };

  /* ---------------- Wiring del panel --------------------------------------- */

  document.addEventListener('DOMContentLoaded', function () {
    logBox = document.getElementById('testLog');

    var scenarioBtns = document.querySelectorAll('[data-scenario]');
    for (var i = 0; i < scenarioBtns.length; i++) {
      (function (btn) {
        btn.addEventListener('click', function () {
          harness.log('info', '── Escenario: ' + btn.dataset.scenario + ' ──');
          SCENARIOS[btn.dataset.scenario]();
        });
      })(scenarioBtns[i]);
    }

    var connBtns = document.querySelectorAll('[data-conn]');
    for (var j = 0; j < connBtns.length; j++) {
      (function (btn) {
        btn.addEventListener('click', function () {
          CONN_ACTIONS[btn.dataset.conn]();
        });
      })(connBtns[j]);
    }

    document.getElementById('btnAutoSeq').addEventListener('click', function () {
      runSequence(this);
    });

    document.getElementById('btnKeyF1').addEventListener('click', function () {
      pressKey('F1');
    });

    document.getElementById('btnEmitJson').addEventListener('click', function () {
      var raw = document.getElementById('jsonInput').value;
      var parsed;
      try {
        parsed = JSON.parse(raw);
      } catch (e) {
        harness.log('fail', 'JSON inválido: ' + e.message);
        return;
      }
      if (!parsed || typeof parsed.phase !== 'string' ||
          PHASES.indexOf(parsed.phase) === -1) {
        harness.log('fail', "Se requiere \"phase\" con uno de: " + PHASES.join(', '));
        return;
      }
      harness.log('info', 'Inyección manual aceptada');
      machine.load(parsed);
    });

    harness.log('info', 'Harness listo. El mock signalR está activo.');
  });
})();
