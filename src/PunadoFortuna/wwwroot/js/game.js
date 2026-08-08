(function () {
  'use strict';

  var zoneElements = {
    1: { el: document.getElementById('zone1'), badge: document.getElementById('badge1'), score: document.getElementById('score1'), chips: document.getElementById('chips1'), chipCount: document.getElementById('chipCount1'), winnerScore: document.getElementById('winnerScore1') },
    2: { el: document.getElementById('zone2'), badge: document.getElementById('badge2'), score: document.getElementById('score2'), chips: document.getElementById('chips2'), chipCount: document.getElementById('chipCount2'), winnerScore: document.getElementById('winnerScore2') }
  };

  var connDot = document.getElementById('connDot');
  var connLabel = document.getElementById('connLabel');
  var reconnectBanner = document.getElementById('reconnectBanner');

  var bumpTimers = { 1: null, 2: null };
  var connection = null;
  var connected = false;

  function showBanner(text, cls) {
    reconnectBanner.textContent = text;
    reconnectBanner.className = 'reconnect-banner visible ' + (cls || '');
  }

  function hideBanner() {
    reconnectBanner.className = 'reconnect-banner';
  }

  function setConnectionStatus(status) {
    connDot.className = 'conn-dot';
    if (status === 'connected') {
      connDot.className += ' connected';
      connLabel.textContent = 'CONECTADO';
      connLabel.style.color = '#22dd66';
      hideBanner();
      connected = true;
    } else if (status === 'reconnecting') {
      connDot.className += ' reconnecting';
      connLabel.textContent = 'RECONECTANDO...';
      connLabel.style.color = '#ffd700';
      showBanner('RECONECTANDO...', 'reconnecting');
      connected = false;
    } else {
      connDot.className += ' disconnected';
      connLabel.textContent = 'DESCONECTADO';
      connLabel.style.color = '#ff4444';
      if (connection && connection.state === 0) {
        showBanner('SIN CONEXIÓN', '');
      }
      connected = false;
    }
  }

  function updateZone(state) {
    var z = zoneElements[state.zonaId];
    if (!z) return;

    z.el.className = 'zone zona-' + state.zonaId;
    z.el.classList.add(state.matchState.toLowerCase());

    z.badge.textContent = state.matchState;
    z.badge.className = 'state-badge ' + state.matchState.toLowerCase();

    var scoreStr = String(state.score).padStart(2, '0');
    if (z.score.textContent !== scoreStr) {
      z.score.textContent = scoreStr;
      z.score.classList.add('bump');
      if (bumpTimers[state.zonaId]) clearTimeout(bumpTimers[state.zonaId]);
      bumpTimers[state.zonaId] = setTimeout(function () {
        z.score.classList.remove('bump');
      }, 200);
    }

    var chipsHtml = '';
    for (var i = 0; i < state.totalChips; i++) {
      var absent = i >= state.presentChips;
      chipsHtml += '<span class="chip-dot' + (absent ? ' absent' : '') + '"></span>';
    }
    z.chips.innerHTML = chipsHtml;

    z.chipCount.textContent = state.presentChips + '/' + state.totalChips + ' fichas';

    if (state.matchState === 'RESULT') {
      z.winnerScore.textContent = 'Puntaje: ' + state.score + ' - ' + (state.winner || '');
    }

    if (state.matchState === 'STANDBY') {
      z.score.textContent = '00';
    }
  }

  function onGameStateInit(states) {
    if (!states) return;
    for (var i = 0; i < states.length; i++) {
      updateZone(states[i]);
    }
  }

  function onGameStateUpdate(state) {
    updateZone(state);
  }

  function onConnectionChanged(isConnected) {
    setConnectionStatus(isConnected ? 'connected' : 'disconnected');
  }

  function forceReset(zonaId) {
    if (connection && connected) {
      connection.invoke('ForceReset', zonaId).catch(function (err) {
        console.error('ForceReset error:', err);
      });
    }
  }

  function forceResetAll() {
    if (connection && connected) {
      connection.invoke('ForceResetAll').catch(function (err) {
        console.error('ForceResetAll error:', err);
      });
    }
  }

  function initSignalR() {
    if (typeof signalR === 'undefined') {
      console.warn('signalr.min.js no encontrado — usando modo offline');
      connLabel.textContent = 'SIGNALR NO DISPONIBLE';
      connLabel.style.color = '#ffd700';
      return;
    }

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/gamehub')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('GameStateInit', onGameStateInit);
    connection.on('GameStateUpdate', onGameStateUpdate);
    connection.on('ConnectionChanged', onConnectionChanged);

    connection.onreconnecting(function () {
      setConnectionStatus('reconnecting');
    });

    connection.onreconnected(function () {
      setConnectionStatus('connected');
    });

    connection.onclose(function () {
      setConnectionStatus('disconnected');
      setTimeout(function () {
        if (connection) {
          connection.start().catch(function () {});
        }
      }, 5000);
    });

    connection.start().then(function () {
      setConnectionStatus('connected');
    }).catch(function (err) {
      console.error('SignalR connection failed:', err);
      setConnectionStatus('disconnected');
      setTimeout(function () {
        if (connection) {
          connection.start().catch(function () {});
        }
      }, 5000);
    });
  }

  document.getElementById('btnResetAll').addEventListener('click', forceResetAll);
  document.getElementById('btnReset1').addEventListener('click', function () { forceReset(1); });
  document.getElementById('btnReset2').addEventListener('click', function () { forceReset(2); });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'F1') { e.preventDefault(); forceResetAll(); }
    if (e.key === 'F2') { e.preventDefault(); forceReset(1); }
    if (e.key === 'F3') { e.preventDefault(); forceReset(2); }
  });

  setConnectionStatus('disconnected');
  initSignalR();

})();
